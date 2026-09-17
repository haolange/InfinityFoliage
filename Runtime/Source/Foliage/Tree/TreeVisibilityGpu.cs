using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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
        private ComputeBuffer m_Planes;
        private GpuVisibilityChunk[] m_ChunkScratch;
        private bool m_Ready;
        private bool m_HasHzb;

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

            m_BuildHzb = m_Shader.FindKernel("BuildHzb");
            m_BuildHzbMip = m_Shader.FindKernel("BuildHzbMip");
            m_ExpandMask = m_Shader.FindKernel("ExpandMaskToIndex");
            m_ExpandRun = m_Shader.FindKernel("ExpandRunToIndex");
            m_FilterIndex = m_Shader.FindKernel("FilterIndexHzb");
            m_CullInstances = m_Shader.FindKernel("CullInstances");
            m_CopyArgs = m_Shader.FindKernel("CopyArgsCount");

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
            m_Planes = new ComputeBuffer(6, sizeof(float) * 4);
            m_ChunkScratch = new GpuVisibilityChunk[chunks];
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

        public void BuildHzb(Camera camera)
        {
            m_HasHzb = false;
            if (!m_Ready || camera == null) { return; }

            Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            Vector4 zParams = Shader.GetGlobalVector("_ZBufferParams");
            if (depth == null || zParams == Vector4.zero) { return; }

            Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix;
            m_Shader.SetInt("_EnableHzb", 1);
            m_Shader.SetInt("_HzbWidth", HzbWidth);
            m_Shader.SetInt("_HzbHeight", HzbHeight);
            m_Shader.SetVector("_ZBufferParams", zParams);
            m_Shader.SetVector("_HzbSize", new Vector4(HzbWidth, HzbHeight, depth.width, depth.height));
            m_Shader.SetMatrix("_ViewProj", viewProj);
            m_Shader.SetTexture(m_BuildHzb, "_CameraDepthTexture", depth);
            m_Shader.SetBuffer(m_BuildHzb, "_Hzb", m_Hzb);
            m_Shader.Dispatch(m_BuildHzb, (HzbWidth + 7) / 8, (HzbHeight + 7) / 8, 1);
            m_Shader.SetBuffer(m_BuildHzbMip, "_Hzb", m_Hzb);
            m_Shader.Dispatch(m_BuildHzbMip, ((HzbWidth / 2) + 7) / 8, ((HzbHeight / 2) + 7) / 8, 1);
            m_HasHzb = true;
        }

        public void BindCommon(ComputeBuffer indexBuffer, ComputeBuffer argsBuffer, Camera camera, in float maxDistance)
        {
            if (!m_Ready) { return; }
            int enable = m_HasHzb ? 1 : 0;
            float3 origin = camera != null ? (float3)camera.transform.position : float3.zero;
            m_Shader.SetInt("_EnableHzb", enable);
            m_Shader.SetInt("_HzbWidth", HzbWidth);
            m_Shader.SetInt("_HzbHeight", HzbHeight);
            m_Shader.SetFloat("_MaxDistance", maxDistance);
            m_Shader.SetVector("_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
            if (camera != null)
            {
                m_Shader.SetMatrix("_ViewProj", GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix);
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
                m_Chunks.SetData(m_ChunkScratch, 0, 0, chunkCount);
            }
            else
            {
                source = m_RwChunks;
            }
            m_Shader.SetInt("_ChunkCount", chunkCount);
            m_Shader.SetBuffer(m_ExpandMask, "_Chunks", source);
            m_Shader.SetBuffer(m_ExpandMask, "_Bounds", m_Bounds);
            m_Shader.SetBuffer(m_ExpandMask, "_Indices", indexBuffer);
            m_Shader.SetBuffer(m_ExpandMask, "_Args", argsBuffer);
            m_Shader.Dispatch(m_ExpandMask, math.max(chunkCount, 1), 1, 1);
        }

        public void ExpandRun(VisibilityRun[] runs, int runCount, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            if (!m_Ready || runCount <= 0) { return; }
            m_Runs.SetData(runs, 0, 0, runCount);
            m_Shader.SetInt("_RunCount", runCount);
            m_Shader.SetBuffer(m_ExpandRun, "_Runs", m_Runs);
            m_Shader.SetBuffer(m_ExpandRun, "_Bounds", m_Bounds);
            m_Shader.SetBuffer(m_ExpandRun, "_Indices", indexBuffer);
            m_Shader.SetBuffer(m_ExpandRun, "_Args", argsBuffer);
            m_Shader.Dispatch(m_ExpandRun, runCount, 1, 1);
        }

        public void FilterIndex(int[] indices, int count, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            if (!m_Ready || count <= 0) { return; }
            m_SrcIndex.SetData(indices, 0, 0, count);
            m_Shader.SetInt("_SrcCount", count);
            m_Shader.SetBuffer(m_FilterIndex, "_SrcIndices", m_SrcIndex);
            m_Shader.SetBuffer(m_FilterIndex, "_Bounds", m_Bounds);
            m_Shader.SetBuffer(m_FilterIndex, "_Indices", indexBuffer);
            m_Shader.SetBuffer(m_FilterIndex, "_Args", argsBuffer);
            m_Shader.Dispatch(m_FilterIndex, (count + 63) / 64, 1, 1);
        }

        public void CullChunks(ulong[] masks, int chunkCount, int instanceCount, NativeArray<byte> cellVisible, Vector4[] planes, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            if (!m_Ready) { return; }
            PackChunks(masks, chunkCount, instanceCount);
            m_RwChunks.SetData(m_ChunkScratch, 0, 0, chunkCount);

            uint[] cells = new uint[cellVisible.Length];
            for (int i = 0; i < cellVisible.Length; ++i)
            {
                cells[i] = cellVisible[i];
            }
            m_CellVisible.SetData(cells);
            m_Planes.SetData(planes);

            m_Shader.SetInt("_ChunkCount", chunkCount);
            m_Shader.SetBuffer(m_CullInstances, "_RwChunks", m_RwChunks);
            m_Shader.SetBuffer(m_CullInstances, "_Bounds", m_Bounds);
            m_Shader.SetBuffer(m_CullInstances, "_InstanceCell", m_InstanceCell);
            m_Shader.SetBuffer(m_CullInstances, "_CellVisible", m_CellVisible);
            m_Shader.SetBuffer(m_CullInstances, "_Planes", m_Planes);
            m_Shader.SetBuffer(m_CullInstances, "_Hzb", m_Hzb);
            m_Shader.SetBuffer(m_CullInstances, "_Indices", indexBuffer);
            m_Shader.SetBuffer(m_CullInstances, "_Args", argsBuffer);
            m_Shader.Dispatch(m_CullInstances, math.max(chunkCount, 1), 1, 1);
        }

        public void CopyArgsInstanceCount(ComputeBuffer src, ComputeBuffer dst)
        {
            if (!m_Ready) { return; }
            m_Shader.SetBuffer(m_CopyArgs, "_Args", src);
            m_Shader.SetBuffer(m_CopyArgs, "_ArgsDst", dst);
            m_Shader.Dispatch(m_CopyArgs, 1, 1, 1);
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
            if (m_Planes != null) { m_Planes.Dispose(); }
            m_Ready = false;
            m_HasHzb = false;
        }

        void BindHzb(int kernel, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            m_Shader.SetBuffer(kernel, "_Hzb", m_Hzb);
            m_Shader.SetBuffer(kernel, "_Bounds", m_Bounds);
            m_Shader.SetBuffer(kernel, "_Indices", indexBuffer);
            m_Shader.SetBuffer(kernel, "_Args", argsBuffer);
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
