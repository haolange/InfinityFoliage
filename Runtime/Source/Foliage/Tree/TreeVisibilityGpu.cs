using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.FoliagePipeline
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuVisibilityChunk
    {
        public int candidateBase;
        public int count;
        public uint maskLo;
        public uint maskHi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuAabb
    {
        public float centerX;
        public float centerY;
        public float centerZ;
        public float pad0;
        public float extentsX;
        public float extentsY;
        public float extentsZ;
        public float pad1;
    }

    internal class TreeVisibilityGpu
    {
        const int HzbWidth = 64;
        const int HzbHeight = 32;

        private ComputeShader m_Shader;
        private int m_BuildHzb;
        private int m_BuildHzbMip;
        private int m_CullCells;
        private int m_ExpandMask;
        private int m_ExpandRun;
        private int m_FilterIndex;
        private int m_CullInstances;
        private int m_CopyArgs;
        private ComputeBuffer m_Hzb;
        private ComputeBuffer m_Chunks;
        private ComputeBuffer m_RwChunks;
        private ComputeBuffer m_Runs;
        private ComputeBuffer m_SrcIndex;
        private ComputeBuffer m_Bounds;
        private ComputeBuffer m_InstanceCell;
        private ComputeBuffer m_CellVisible;
        private ComputeBuffer m_CellBounds;
        private ComputeBuffer m_Planes;
        private GpuVisibilityChunk[] m_ChunkScratch;
        private uint[] m_CellScratch;
        private CommandBuffer m_CmdBuffer;
        private bool m_Ready;
        private bool m_HasHzb;
        private Matrix4x4 m_DepthViewProj;
        private static bool s_WarnedUnsupported;

        public bool IsReady
        {
            get { return m_Ready; }
        }

        public bool HasHzb
        {
            get { return m_HasHzb; }
        }

        public void Initialize(in int instanceCount, in int chunkCount, in int cellCount)
        {
            m_Shader = Resources.Load<ComputeShader>("TreeVisibility");
            if (m_Shader == null)
            {
                m_Ready = false;
                return;
            }

            if (!m_Shader.HasKernel("BuildHzb") || !m_Shader.HasKernel("BuildHzbMip") ||
                !m_Shader.HasKernel("CullCells") || !m_Shader.HasKernel("CullInstances") ||
                !m_Shader.HasKernel("ExpandMaskToIndex") || !m_Shader.HasKernel("ExpandRunToIndex") ||
                !m_Shader.HasKernel("FilterIndexHzb") || !m_Shader.HasKernel("CopyArgsCount"))
            {
                m_Ready = false;
                return;
            }

            m_BuildHzb = m_Shader.FindKernel("BuildHzb");
            m_BuildHzbMip = m_Shader.FindKernel("BuildHzbMip");
            m_CullCells = m_Shader.FindKernel("CullCells");
            m_ExpandMask = m_Shader.FindKernel("ExpandMaskToIndex");
            m_ExpandRun = m_Shader.FindKernel("ExpandRunToIndex");
            m_FilterIndex = m_Shader.FindKernel("FilterIndexHzb");
            m_CullInstances = m_Shader.FindKernel("CullInstances");
            m_CopyArgs = m_Shader.FindKernel("CopyArgsCount");
            if (!SystemInfo.supportsComputeShaders ||
                !m_Shader.IsSupported(m_BuildHzb) || !m_Shader.IsSupported(m_BuildHzbMip) ||
                !m_Shader.IsSupported(m_CullCells) || !m_Shader.IsSupported(m_CullInstances) ||
                !m_Shader.IsSupported(m_ExpandMask) || !m_Shader.IsSupported(m_ExpandRun) ||
                !m_Shader.IsSupported(m_FilterIndex) || !m_Shader.IsSupported(m_CopyArgs))
            {
                if (!s_WarnedUnsupported)
                {
                    Debug.LogWarning("TreeVisibility compute is unavailable; tree visibility is using the CPU path.");
                    s_WarnedUnsupported = true;
                }
                m_Ready = false;
                return;
            }

            int n = math.max(instanceCount, 1);
            int chunks = math.max(chunkCount, 1);
            int cells = math.max(cellCount, 1);
            m_Hzb = new ComputeBuffer((HzbWidth * HzbHeight) + ((HzbWidth / 2) * (HzbHeight / 2)), sizeof(float));
            m_Chunks = new ComputeBuffer(chunks, Marshal.SizeOf(typeof(GpuVisibilityChunk)));
            m_RwChunks = new ComputeBuffer(chunks, Marshal.SizeOf(typeof(GpuVisibilityChunk)));
            m_Runs = new ComputeBuffer(n, Marshal.SizeOf(typeof(VisibilityRun)));
            m_SrcIndex = new ComputeBuffer(n, sizeof(uint));
            m_Bounds = new ComputeBuffer(n, Marshal.SizeOf(typeof(GpuAabb)));
            m_InstanceCell = new ComputeBuffer(n, sizeof(int));
            m_CellVisible = new ComputeBuffer(cells, sizeof(uint));
            m_CellBounds = new ComputeBuffer(cells, Marshal.SizeOf(typeof(GpuAabb)));
            m_Planes = new ComputeBuffer(6, sizeof(float) * 4);
            m_ChunkScratch = new GpuVisibilityChunk[chunks];
            m_CellScratch = new uint[cells];
            m_Ready = true;
            m_HasHzb = false;
        }

        public void UploadBounds(NativeArray<Aabb> bounds, NativeArray<int> instanceCell)
        {
            if (!m_Ready) { return; }
            int n = bounds.Length;
            GpuAabb[] packed = new GpuAabb[n];
            int[] cells = new int[n];
            for (int i = 0; i < n; ++i)
            {
                packed[i].centerX = bounds[i].center.x;
                packed[i].centerY = bounds[i].center.y;
                packed[i].centerZ = bounds[i].center.z;
                packed[i].extentsX = bounds[i].extents.x;
                packed[i].extentsY = bounds[i].extents.y;
                packed[i].extentsZ = bounds[i].extents.z;
                cells[i] = instanceCell[i];
            }
            m_Bounds.SetData(packed);
            m_InstanceCell.SetData(cells);
        }

        public void UploadCells(NativeArray<BoundSection> cells)
        {
            if (!m_Ready || cells.Length == 0) { return; }
            GpuAabb[] packed = new GpuAabb[cells.Length];
            for (int i = 0; i < cells.Length; ++i)
            {
                Aabb box = cells[i].boundBox;
                packed[i].centerX = box.center.x;
                packed[i].centerY = box.center.y;
                packed[i].centerZ = box.center.z;
                packed[i].extentsX = box.extents.x;
                packed[i].extentsY = box.extents.y;
                packed[i].extentsZ = box.extents.z;
            }
            m_CellBounds.SetData(packed);
        }

        public void BuildHzb(CommandBuffer cmdBuffer, Camera camera, RTHandle depth, in Vector4 zParams, in Matrix4x4 depthViewProj)
        {
            m_CmdBuffer = cmdBuffer;
            m_HasHzb = false;
            m_DepthViewProj = depthViewProj;
            if (!m_Ready || cmdBuffer == null || camera == null || camera.orthographic || depth == null || depth.rt == null || depth.rt.antiAliasing > 1 || zParams == Vector4.zero) { return; }

            cmdBuffer.SetComputeIntParam(m_Shader, "_EnableHzb", 1);
            cmdBuffer.SetComputeIntParam(m_Shader, "_HzbWidth", HzbWidth);
            cmdBuffer.SetComputeIntParam(m_Shader, "_HzbHeight", HzbHeight);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_NearClip", camera.nearClipPlane);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_ZBufferParams", zParams);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_HzbSize", new Vector4(HzbWidth, HzbHeight, depth.rt.width, depth.rt.height));
            cmdBuffer.SetComputeMatrixParam(m_Shader, "_ViewProj", m_DepthViewProj);
            cmdBuffer.SetComputeTextureParam(m_Shader, m_BuildHzb, "_CameraDepthTexture", depth);
            cmdBuffer.SetComputeBufferParam(m_Shader, m_BuildHzb, "_Hzb", m_Hzb);
            cmdBuffer.DispatchCompute(m_Shader, m_BuildHzb, (HzbWidth + 7) / 8, (HzbHeight + 7) / 8, 1);
            cmdBuffer.SetComputeBufferParam(m_Shader, m_BuildHzbMip, "_Hzb", m_Hzb);
            cmdBuffer.DispatchCompute(m_Shader, m_BuildHzbMip, ((HzbWidth / 2) + 7) / 8, ((HzbHeight / 2) + 7) / 8, 1);
            m_HasHzb = true;
        }

        public void BindCommon(ComputeBuffer indexBuffer, ComputeBuffer argsBuffer, Camera camera, in float maxDistance)
        {
            if (!m_Ready) { return; }
            int enable = m_HasHzb ? 1 : 0;
            float3 origin = camera != null ? (float3)camera.transform.position : float3.zero;
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_EnableHzb", enable);
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_HzbWidth", HzbWidth);
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_HzbHeight", HzbHeight);
            m_CmdBuffer.SetComputeFloatParam(m_Shader, "_MaxDistance", maxDistance);
            m_CmdBuffer.SetComputeVectorParam(m_Shader, "_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
            if (camera != null)
            {
                m_CmdBuffer.SetComputeMatrixParam(m_Shader, "_ViewProj", m_DepthViewProj);
            }
            BindHzb(m_ExpandMask, indexBuffer, argsBuffer);
            BindHzb(m_ExpandRun, indexBuffer, argsBuffer);
            BindHzb(m_FilterIndex, indexBuffer, argsBuffer);
            BindHzb(m_CullInstances, indexBuffer, argsBuffer);
        }

        public void ExpandMask(ulong[] masks, int chunkCount, int instanceCount, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer, in int useCulled)
        {
            if (!m_Ready) { return; }
            ComputeBuffer source = m_Chunks;
            if (useCulled == 0)
            {
                PackChunks(masks, chunkCount, instanceCount);
                m_CmdBuffer.SetBufferData(m_Chunks, m_ChunkScratch, 0, 0, chunkCount);
            }
            else
            {
                source = m_RwChunks;
            }
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_ChunkCount", chunkCount);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_ExpandMask, "_Chunks", source);
            m_CmdBuffer.DispatchCompute(m_Shader, m_ExpandMask, math.max(chunkCount, 1), 1, 1);
        }

        public void ExpandRun(VisibilityRun[] runs, int runCount, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            if (!m_Ready || runCount <= 0) { return; }
            m_CmdBuffer.SetBufferData(m_Runs, runs, 0, 0, runCount);
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_RunCount", runCount);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_ExpandRun, "_Runs", m_Runs);
            m_CmdBuffer.DispatchCompute(m_Shader, m_ExpandRun, runCount, 1, 1);
        }

        public void FilterIndex(int[] indices, int count, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            if (!m_Ready || count <= 0) { return; }
            m_CmdBuffer.SetBufferData(m_SrcIndex, indices, 0, 0, count);
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_SrcCount", count);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_FilterIndex, "_SrcIndices", m_SrcIndex);
            m_CmdBuffer.DispatchCompute(m_Shader, m_FilterIndex, (count + 63) / 64, 1, 1);
        }

        public void CullCells(NativeArray<byte> cellVisible, Vector4[] planes, Camera camera, in float maxDistance)
        {
            if (!m_Ready || m_CmdBuffer == null || cellVisible.Length == 0) { return; }
            for (int i = 0; i < cellVisible.Length; ++i) { m_CellScratch[i] = cellVisible[i]; }
            m_CmdBuffer.SetBufferData(m_CellVisible, m_CellScratch, 0, 0, cellVisible.Length);
            m_CmdBuffer.SetBufferData(m_Planes, planes);
            float3 origin = camera != null ? (float3)camera.transform.position : float3.zero;
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_EnableHzb", m_HasHzb ? 1 : 0);
            m_CmdBuffer.SetComputeIntParam(m_Shader, "_CellCount", cellVisible.Length);
            m_CmdBuffer.SetComputeFloatParam(m_Shader, "_MaxDistance", maxDistance);
            m_CmdBuffer.SetComputeVectorParam(m_Shader, "_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullCells, "_CellVisible", m_CellVisible);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullCells, "_CellBounds", m_CellBounds);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullCells, "_Planes", m_Planes);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullCells, "_Hzb", m_Hzb);
            m_CmdBuffer.DispatchCompute(m_Shader, m_CullCells, (cellVisible.Length + 63) / 64, 1, 1);
        }

        public void CullChunks(ulong[] masks, int chunkCount, int instanceCount)
        {
            if (!m_Ready) { return; }
            PackChunks(masks, chunkCount, instanceCount);
            m_CmdBuffer.SetBufferData(m_RwChunks, m_ChunkScratch, 0, 0, chunkCount);

            m_CmdBuffer.SetComputeIntParam(m_Shader, "_ChunkCount", chunkCount);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullInstances, "_RwChunks", m_RwChunks);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CullInstances, "_Planes", m_Planes);
            m_CmdBuffer.DispatchCompute(m_Shader, m_CullInstances, math.max(chunkCount, 1), 1, 1);
        }

        public void CopyArgsInstanceCount(ComputeBuffer src, ComputeBuffer dst)
        {
            if (!m_Ready) { return; }
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CopyArgs, "_Args", src);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, m_CopyArgs, "_ArgsDst", dst);
            m_CmdBuffer.DispatchCompute(m_Shader, m_CopyArgs, 1, 1, 1);
        }

        public void Release()
        {
            if (m_Hzb != null) { m_Hzb.Dispose(); }
            if (m_Chunks != null) { m_Chunks.Dispose(); }
            if (m_RwChunks != null) { m_RwChunks.Dispose(); }
            if (m_Runs != null) { m_Runs.Dispose(); }
            if (m_SrcIndex != null) { m_SrcIndex.Dispose(); }
            if (m_Bounds != null) { m_Bounds.Dispose(); }
            if (m_InstanceCell != null) { m_InstanceCell.Dispose(); }
            if (m_CellVisible != null) { m_CellVisible.Dispose(); }
            if (m_CellBounds != null) { m_CellBounds.Dispose(); }
            if (m_Planes != null) { m_Planes.Dispose(); }
            m_Ready = false;
            m_HasHzb = false;
            m_CmdBuffer = null;
        }

        void BindHzb(int kernel, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_Hzb", m_Hzb);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_Bounds", m_Bounds);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_InstanceCell", m_InstanceCell);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_CellVisible", m_CellVisible);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_Indices", indexBuffer);
            m_CmdBuffer.SetComputeBufferParam(m_Shader, kernel, "_Args", argsBuffer);
        }

        void PackChunks(ulong[] masks, int chunkCount, int instanceCount)
        {
            for (int i = 0; i < chunkCount; ++i)
            {
                ulong mask = masks[i];
                m_ChunkScratch[i].candidateBase = FoliageLogic.VisibilityChunkBase(i);
                m_ChunkScratch[i].count = FoliageLogic.VisibilityChunkSize(i, instanceCount);
                m_ChunkScratch[i].maskLo = (uint)mask;
                m_ChunkScratch[i].maskHi = (uint)(mask >> 32);
            }
        }
    }
}
