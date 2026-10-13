using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.FoliagePipeline
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuAabb
    {
        public float3 center;
        public float pad0;
        public float3 extents;
        public float pad1;
    }

    internal class TreeGpuView
    {
        public ComputeBuffer cells;
        public ComputeBuffer lod;
        public ComputeBuffer weights;
        public ComputeBuffer fade;
        public bool hasInput;
        public bool hzb;
        public string hzbReason;
        public bool allowFade;
        public TreeLodFadeMode mode;
        public float3 lastOrigin;
        public float4x4 lastProjection;
        public int advancedFrame = -1;

        public void Initialize(in int instanceCount, in int cellCount)
        {
            try
            {
                cells = new ComputeBuffer(math.max(cellCount, 1), sizeof(uint));
                lod = new ComputeBuffer(math.max(instanceCount, 1), sizeof(int) * 4);
                weights = new ComputeBuffer(math.max(instanceCount, 1), sizeof(float));
                fade = new ComputeBuffer(4, sizeof(float) * 4);
                fade.SetData(new Vector4[4]);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            cells?.Dispose();
            lod?.Dispose();
            weights?.Dispose();
            fade?.Dispose();
            cells = null;
            lod = null;
            weights = null;
            fade = null;
        }
    }

    internal class TreeHzb
    {
        public const int Width = 64;
        public const int Height = 32;
        const int RetireFrames = 120;

        private static readonly List<TreeHzb> s_Views = new List<TreeHzb>(4);
        private static ComputeShader s_Shader;
        private static int s_BuildHzb = -1;
        private static int s_BuildHzbMip = -1;

        public Camera camera;
        public ComputeBuffer buffer;
        public Matrix4x4 viewProj;
        public float3 origin;
        public float4x4 projection;
        public float nearClip;
        public int frame = -1;
        public string reason = "HZB has not been built for this camera.";

        static bool EnsureShader()
        {
            if (s_Shader != null) { return s_BuildHzb >= 0 && s_BuildHzbMip >= 0; }
            s_Shader = Resources.Load<ComputeShader>("TreeVisibility");
            if (s_Shader == null) { return false; }
            s_BuildHzb = FindKernel("BuildHzb");
            s_BuildHzbMip = FindKernel("BuildHzbMip");
            return s_BuildHzb >= 0 && s_BuildHzbMip >= 0;
        }

        static int FindKernel(string name)
        {
            if (!s_Shader.HasKernel(name)) { return -1; }
            int kernel = s_Shader.FindKernel(name);
            return s_Shader.IsSupported(kernel) ? kernel : -1;
        }

        static TreeHzb Find(Camera camera, in bool create)
        {
            int now = Time.frameCount;
            for (int i = s_Views.Count - 1; i >= 0; --i)
            {
                TreeHzb stale = s_Views[i];
                if (stale.camera != null && (stale.frame < 0 || now - stale.frame <= RetireFrames)) { continue; }
                stale.buffer?.Dispose();
                s_Views.RemoveAt(i);
            }
            for (int i = 0; i < s_Views.Count; ++i)
            {
                if (s_Views[i].camera == camera) { return s_Views[i]; }
            }
            if (!create) { return null; }
            TreeHzb hzb = new TreeHzb();
            hzb.camera = camera;
            s_Views.Add(hzb);
            return hzb;
        }

        public static void Build(CommandBuffer cmdBuffer, Camera camera, RTHandle depth, in Vector4 zParams, in Matrix4x4 depthViewProj)
        {
            if (camera == null || cmdBuffer == null) { return; }
            TreeHzb hzb = Find(camera, true);
            hzb.frame = -1;
            if (!EnsureShader()) { hzb.reason = "HZB kernels are unavailable."; return; }
            if (camera.orthographic) { hzb.reason = "Orthographic depth uses conservative visibility without HZB."; return; }
            if (depth == null || depth.rt == null || zParams == Vector4.zero) { hzb.reason = "Camera depth is unavailable."; return; }
            if (depth.rt.antiAliasing > 1) { hzb.reason = "Multisample depth is not supported by HZB."; return; }
            if (hzb.buffer == null)
            {
                try
                {
                    hzb.buffer = new ComputeBuffer((Width * Height) + ((Width / 2) * (Height / 2)), sizeof(float));
                }
                catch (Exception error)
                {
                    hzb.reason = "HZB allocation failed: " + error.Message;
                    return;
                }
            }
            cmdBuffer.SetComputeIntParam(s_Shader, "_HzbWidth", Width);
            cmdBuffer.SetComputeIntParam(s_Shader, "_HzbHeight", Height);
            cmdBuffer.SetComputeVectorParam(s_Shader, "_ZBufferParams", zParams);
            cmdBuffer.SetComputeVectorParam(s_Shader, "_HzbSize", new Vector4(Width, Height, depth.rt.width, depth.rt.height));
            cmdBuffer.SetComputeTextureParam(s_Shader, s_BuildHzb, "_CameraDepthTexture", depth);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_BuildHzb, "_Hzb", hzb.buffer);
            cmdBuffer.DispatchCompute(s_Shader, s_BuildHzb, (Width + 7) / 8, (Height + 7) / 8, 1);
            cmdBuffer.SetComputeBufferParam(s_Shader, s_BuildHzbMip, "_Hzb", hzb.buffer);
            cmdBuffer.DispatchCompute(s_Shader, s_BuildHzbMip, ((Width / 2) + 7) / 8, ((Height / 2) + 7) / 8, 1);
            hzb.viewProj = depthViewProj;
            hzb.origin = camera.transform.position;
            hzb.projection = Geometry.GetLodProjectionMatrix(camera);
            hzb.nearClip = camera.nearClipPlane;
            hzb.frame = Time.frameCount;
            hzb.reason = null;
        }

        public static TreeHzb Previous(Camera camera, in float3 origin, in float4x4 projection, in float cullDistance, out string reason)
        {
            TreeHzb hzb = Find(camera, false);
            if (hzb == null) { reason = "HZB has not been built for this camera."; return null; }
            if (hzb.frame < 0) { reason = hzb.reason; return null; }
            if (Time.frameCount - hzb.frame > 1) { reason = "Previous-frame HZB is unavailable."; return null; }
            if (FoliageLogic.ViewDiscontinuous(hzb.origin, origin, hzb.projection, projection, cullDistance))
            {
                reason = "View is discontinuous; HZB skipped this frame.";
                return null;
            }
            reason = null;
            return hzb;
        }

        public static void Release()
        {
            for (int i = 0; i < s_Views.Count; ++i) { s_Views[i].buffer?.Dispose(); }
            s_Views.Clear();
        }
    }

    internal class TreeVisibilityGpu
    {
        public const int MaxPlanes = 16;
        const int ScanWidth = 128;
        const int ArgsWidth = 64;

        private ComputeShader m_Shader;
        private int m_PrepareView;
        private int m_CullCells;
        private int m_CullInstances;
        private int m_ReduceChanges;
        private int m_AdvanceFade;
        private int m_CommitLod;
        private int m_EmitMasks;
        private int m_ScanBlocks;
        private int m_AddOffsets;
        private int m_Compact;
        private int m_CopyArgs;
        private ComputeBuffer m_DummyHzb;
        private ComputeBuffer m_Bounds;
        private ComputeBuffer m_InstanceCell;
        private ComputeBuffer m_CellBounds;
        private ComputeBuffer m_LodSizes;
        private ComputeBuffer m_LodDither;
        private ComputeBuffer m_Heights;
        private ComputeBuffer m_Planes;
        private ComputeBuffer m_Masks;
        private ComputeBuffer m_Counts;
        private ComputeBuffer m_Changes;
        private ComputeBuffer m_DummyIndex;
        private ComputeBuffer m_DummyWeight;
        private List<ComputeBuffer> m_Offsets;
        private List<ComputeBuffer> m_Sums;
        private List<ComputeBuffer> m_Reductions;
        private List<int> m_ReduceLengths;
        private List<int> m_ScanLengths;
        private int m_InstanceCount;
        private int m_CellCount;
        private int m_ChunkCount;
        private int m_LodCount;
        private int m_HeightRes;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
        private bool m_Ready;
        private string m_FailureReason;

        public bool IsReady { get { return m_Ready; } }
        public string FailureReason { get { return m_FailureReason; } }

        public bool Initialize(NativeArray<Aabb> bounds, NativeArray<int> instanceCell,
            NativeArray<BoundSection> cellBounds, NativeArray<float> lodScreenSizes,
            NativeArray<byte> lodDither, NativeArray<float> heights, in int heightRes,
            in float3 terrainPos, in float3 terrainSize)
        {
            Release();
            m_FailureReason = null;
            if (!SystemInfo.supportsComputeShaders)
            {
                m_FailureReason = "Compute shaders are unavailable.";
                return false;
            }
            if (bounds.Length <= 0 || bounds.Length != instanceCell.Length || cellBounds.Length <= 0 ||
                lodScreenSizes.Length <= 0 || lodDither.Length != lodScreenSizes.Length)
            {
                m_FailureReason = "Tree GPU candidate data is incomplete.";
                return false;
            }
            m_Shader = Resources.Load<ComputeShader>("TreeVisibility");
            if (m_Shader == null)
            {
                m_FailureReason = "TreeVisibility compute shader is missing.";
                return false;
            }
            string[] names = { "PrepareView", "CullCells", "CullInstances", "ReduceChanges", "AdvanceFade",
                "CommitLod", "EmitMasks", "ScanBlocks", "AddOffsets", "Compact", "CopyArgsCount" };
            int[] kernels = new int[names.Length];
            for (int i = 0; i < names.Length; ++i)
            {
                if (!m_Shader.HasKernel(names[i]))
                {
                    m_FailureReason = "Tree GPU kernel is missing: " + names[i];
                    return false;
                }
                kernels[i] = m_Shader.FindKernel(names[i]);
                if (!m_Shader.IsSupported(kernels[i]))
                {
                    m_FailureReason = "Tree GPU kernel is unsupported: " + names[i];
                    return false;
                }
            }
            m_PrepareView = kernels[0];
            m_CullCells = kernels[1];
            m_CullInstances = kernels[2];
            m_ReduceChanges = kernels[3];
            m_AdvanceFade = kernels[4];
            m_CommitLod = kernels[5];
            m_EmitMasks = kernels[6];
            m_ScanBlocks = kernels[7];
            m_AddOffsets = kernels[8];
            m_Compact = kernels[9];
            m_CopyArgs = kernels[10];
            m_InstanceCount = bounds.Length;
            m_CellCount = cellBounds.Length;
            m_ChunkCount = FoliageLogic.VisibilityChunkCount(m_InstanceCount);
            m_LodCount = lodScreenSizes.Length;
            m_HeightRes = heights.IsCreated && heightRes > 1 && heights.Length >= heightRes * heightRes ? heightRes : 0;
            m_TerrainPos = terrainPos;
            m_TerrainSize = terrainSize;
            int bucketChunks = m_ChunkCount * FoliageLogic.LodBucketCount;
            try
            {
                m_Bounds = new ComputeBuffer(m_InstanceCount, Marshal.SizeOf(typeof(GpuAabb)));
                m_InstanceCell = new ComputeBuffer(m_InstanceCount, sizeof(int));
                m_CellBounds = new ComputeBuffer(m_CellCount, Marshal.SizeOf(typeof(GpuAabb)));
                m_LodSizes = new ComputeBuffer(m_LodCount, sizeof(float));
                m_LodDither = new ComputeBuffer(m_LodCount, sizeof(uint));
                m_Heights = new ComputeBuffer(m_HeightRes > 0 ? heights.Length : 1, sizeof(float));
                m_Planes = new ComputeBuffer(MaxPlanes, sizeof(float) * 4);
                m_Masks = new ComputeBuffer(bucketChunks, sizeof(uint) * 2);
                m_Counts = new ComputeBuffer(bucketChunks, sizeof(uint));
                m_Changes = new ComputeBuffer(m_ChunkCount, sizeof(uint));
                m_DummyIndex = new ComputeBuffer(1, sizeof(uint));
                m_DummyWeight = new ComputeBuffer(1, sizeof(float));
                m_DummyHzb = new ComputeBuffer(1, sizeof(float));
                m_Offsets = new List<ComputeBuffer>();
                m_Sums = new List<ComputeBuffer>();
                m_Reductions = new List<ComputeBuffer>();
                m_ReduceLengths = new List<int>();
                m_ScanLengths = new List<int>();
                BuildLevels(bucketChunks, m_ScanLengths);
                for (int i = 0; i < m_ScanLengths.Count; ++i)
                {
                    m_Offsets.Add(new ComputeBuffer(m_ScanLengths[i], sizeof(uint)));
                    m_Sums.Add(new ComputeBuffer(Groups(m_ScanLengths[i], ScanWidth), sizeof(uint)));
                }
                BuildLevels(m_ChunkCount, m_ReduceLengths);
                for (int i = 0; i < m_ReduceLengths.Count; ++i)
                {
                    m_Reductions.Add(new ComputeBuffer(Groups(m_ReduceLengths[i], ScanWidth), sizeof(uint)));
                }
                var packed = new GpuAabb[m_InstanceCount];
                for (int i = 0; i < packed.Length; ++i) { packed[i] = Pack(bounds[i]); }
                m_Bounds.SetData(packed);
                m_InstanceCell.SetData(instanceCell);
                packed = new GpuAabb[m_CellCount];
                for (int i = 0; i < packed.Length; ++i) { packed[i] = Pack(cellBounds[i].boundBox); }
                m_CellBounds.SetData(packed);
                m_LodSizes.SetData(lodScreenSizes);
                var dither = new uint[m_LodCount];
                for (int i = 0; i < dither.Length; ++i) { dither[i] = lodDither[i]; }
                m_LodDither.SetData(dither);
                if (m_HeightRes > 0) { m_Heights.SetData(heights); }
                else { m_Heights.SetData(new float[1]); }
                m_Ready = true;
                return true;
            }
            catch (Exception error)
            {
                Release();
                m_FailureReason = "Tree GPU allocation failed: " + error.Message;
                return false;
            }
        }

        static int Groups(in int length, in int width)
        {
            return (length + width - 1) / width;
        }

        static void BuildLevels(int length, List<int> levels)
        {
            levels.Clear();
            while (true)
            {
                levels.Add(length);
                int groups = Groups(length, ScanWidth);
                if (groups == 1) { break; }
                length = groups;
            }
        }

        static GpuAabb Pack(Aabb bounds)
        {
            GpuAabb packed = default;
            packed.center = bounds.center;
            packed.extents = bounds.extents;
            return packed;
        }

        public void Fail(string reason)
        {
            m_Ready = false;
            m_FailureReason = reason;
        }

        public void Cull(CommandBuffer cmdBuffer, TreeGpuView view, Vector4[] planes, in int planeCount,
            in float3 origin, in float4x4 projection, in TreeViewSettings settings, TreeHzb hzb, in bool shadow)
        {
            if (!m_Ready || cmdBuffer == null) { return; }
            bool allowFade = !shadow && settings.allowFade;
            TreeLodFadeMode mode = shadow ? TreeLodFadeMode.Temporal : settings.fadeMode;
            float drawDistance = shadow ? float.MaxValue : settings.cullDistance;
            bool reset = !view.hasInput || view.allowFade != allowFade || view.mode != mode ||
                FoliageLogic.ViewDiscontinuous(view.lastOrigin, origin, view.lastProjection, projection, drawDistance);
            bool advance = view.advancedFrame != Time.frameCount;
            cmdBuffer.SetBufferData(m_Planes, planes, 0, 0, planeCount);
            SetViewParams(cmdBuffer, mode, allowFade, shadow);
            cmdBuffer.SetComputeIntParam(m_Shader, "_CellCount", m_CellCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_PlaneCount", planeCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_HeightRes", m_HeightRes);
            cmdBuffer.SetComputeIntParam(m_Shader, "_EnableTerrain", !shadow && (settings.occlusion & TreeOcclusionMode.Terrain) != 0 ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_EnableHzb", hzb != null ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Reset", reset ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Advance", advance ? 1 : 0);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_ProjectionY", projection.c1.y);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Orthographic", projection.c3.w > 0.5f ? 1f : 0f);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_MaxDistance", drawDistance);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_FadeWidth", FoliageLogic.ClampFadeWidth(settings.fadeWidth));
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Hysteresis", reset ? 0f : math.clamp(settings.lodHysteresis, 0f, 0.49f));
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Duration", settings.fadeDuration);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_DeltaTime", settings.deltaTime);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
            cmdBuffer.SetComputeVectorParam(m_Shader, "_TerrainPos", new Vector4(m_TerrainPos.x, m_TerrainPos.y, m_TerrainPos.z, 0));
            cmdBuffer.SetComputeVectorParam(m_Shader, "_TerrainSize", new Vector4(m_TerrainSize.x, m_TerrainSize.y, m_TerrainSize.z, 0));
            ComputeBuffer hzbBuffer = m_DummyHzb;
            if (hzb != null)
            {
                hzbBuffer = hzb.buffer;
                cmdBuffer.SetComputeIntParam(m_Shader, "_HzbWidth", TreeHzb.Width);
                cmdBuffer.SetComputeIntParam(m_Shader, "_HzbHeight", TreeHzb.Height);
                cmdBuffer.SetComputeFloatParam(m_Shader, "_NearClip", hzb.nearClip);
                cmdBuffer.SetComputeMatrixParam(m_Shader, "_ViewProj", hzb.viewProj);
            }
            Bind(cmdBuffer, m_PrepareView, "_FadeState", view.fade);
            cmdBuffer.DispatchCompute(m_Shader, m_PrepareView, 1, 1, 1);
            Bind(cmdBuffer, m_CullCells, "_CellBounds", m_CellBounds);
            Bind(cmdBuffer, m_CullCells, "_CellVisible", view.cells);
            Bind(cmdBuffer, m_CullCells, "_Planes", m_Planes);
            Bind(cmdBuffer, m_CullCells, "_Heights", m_Heights);
            Bind(cmdBuffer, m_CullCells, "_Hzb", hzbBuffer);
            cmdBuffer.DispatchCompute(m_Shader, m_CullCells, Groups(m_CellCount, 64), 1, 1);
            Bind(cmdBuffer, m_CullInstances, "_Bounds", m_Bounds);
            Bind(cmdBuffer, m_CullInstances, "_InstanceCell", m_InstanceCell);
            Bind(cmdBuffer, m_CullInstances, "_CellVisible", view.cells);
            Bind(cmdBuffer, m_CullInstances, "_Planes", m_Planes);
            Bind(cmdBuffer, m_CullInstances, "_LodSizes", m_LodSizes);
            Bind(cmdBuffer, m_CullInstances, "_LodDither", m_LodDither);
            Bind(cmdBuffer, m_CullInstances, "_LodState", view.lod);
            Bind(cmdBuffer, m_CullInstances, "_InstanceWeights", view.weights);
            Bind(cmdBuffer, m_CullInstances, "_FadeState", view.fade);
            Bind(cmdBuffer, m_CullInstances, "_Changes", m_Changes);
            Bind(cmdBuffer, m_CullInstances, "_Hzb", hzbBuffer);
            cmdBuffer.DispatchCompute(m_Shader, m_CullInstances, m_ChunkCount, 1, 1);
            ComputeBuffer input = m_Changes;
            for (int i = 0; i < m_ReduceLengths.Count; ++i)
            {
                cmdBuffer.SetComputeIntParam(m_Shader, "_ScanLength", m_ReduceLengths[i]);
                Bind(cmdBuffer, m_ReduceChanges, "_ScanInput", input);
                Bind(cmdBuffer, m_ReduceChanges, "_ScanSums", m_Reductions[i]);
                cmdBuffer.DispatchCompute(m_Shader, m_ReduceChanges, Groups(m_ReduceLengths[i], ScanWidth), 1, 1);
                input = m_Reductions[i];
            }
            Bind(cmdBuffer, m_AdvanceFade, "_ScanInput", input);
            Bind(cmdBuffer, m_AdvanceFade, "_FadeState", view.fade);
            cmdBuffer.DispatchCompute(m_Shader, m_AdvanceFade, 1, 1, 1);
            Bind(cmdBuffer, m_CommitLod, "_FadeState", view.fade);
            Bind(cmdBuffer, m_CommitLod, "_LodState", view.lod);
            cmdBuffer.DispatchCompute(m_Shader, m_CommitLod, Groups(m_InstanceCount, 64), 1, 1);
            view.hasInput = true;
            view.lastOrigin = origin;
            view.lastProjection = projection;
            view.mode = mode;
            view.allowFade = allowFade;
            view.advancedFrame = Time.frameCount;
        }

        public void Pack(CommandBuffer cmdBuffer, TreeGpuView view, TreeLodPack[] packs, in TreeLodFadeMode fadeMode, in bool fadeAllowed, in bool shadow)
        {
            if (!m_Ready || cmdBuffer == null) { return; }
            bool allowFade = !shadow && fadeAllowed;
            TreeLodFadeMode mode = shadow ? TreeLodFadeMode.Temporal : fadeMode;
            int bucketCount = shadow ? 1 : FoliageLogic.LodBucketCount;
            int length = bucketCount * m_ChunkCount;
            bool weighted = mode == TreeLodFadeMode.Distance && allowFade;
            BuildLevels(length, m_ScanLengths);
            SetViewParams(cmdBuffer, mode, allowFade, shadow);
            cmdBuffer.SetComputeIntParam(m_Shader, "_BucketCount", bucketCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_WriteWeight", weighted ? 1 : 0);
            for (int lod = 0; lod < packs.Length; ++lod)
            {
                TreeLodPack pack = packs[lod];
                cmdBuffer.SetComputeIntParam(m_Shader, "_Lod", lod);
                cmdBuffer.SetComputeIntParam(m_Shader, "_SubmeshCount", pack.submeshCount);
                Bind(cmdBuffer, m_EmitMasks, "_LodState", view.lod);
                Bind(cmdBuffer, m_EmitMasks, "_FadeState", view.fade);
                Bind(cmdBuffer, m_EmitMasks, "_LodDither", m_LodDither);
                Bind(cmdBuffer, m_EmitMasks, "_Masks", m_Masks);
                Bind(cmdBuffer, m_EmitMasks, "_Counts", m_Counts);
                cmdBuffer.DispatchCompute(m_Shader, m_EmitMasks, m_ChunkCount, 1, 1);
                for (int i = 0; i < m_ScanLengths.Count; ++i)
                {
                    cmdBuffer.SetComputeIntParam(m_Shader, "_ScanLength", m_ScanLengths[i]);
                    Bind(cmdBuffer, m_ScanBlocks, "_ScanInput", i == 0 ? m_Counts : m_Sums[i - 1]);
                    Bind(cmdBuffer, m_ScanBlocks, "_ScanOffsets", m_Offsets[i]);
                    Bind(cmdBuffer, m_ScanBlocks, "_ScanSums", m_Sums[i]);
                    cmdBuffer.DispatchCompute(m_Shader, m_ScanBlocks, Groups(m_ScanLengths[i], ScanWidth), 1, 1);
                }
                for (int i = m_ScanLengths.Count - 2; i >= 0; --i)
                {
                    cmdBuffer.SetComputeIntParam(m_Shader, "_ScanLength", m_ScanLengths[i]);
                    Bind(cmdBuffer, m_AddOffsets, "_ScanOffsets", m_Offsets[i]);
                    Bind(cmdBuffer, m_AddOffsets, "_ParentOffsets", m_Offsets[i + 1]);
                    cmdBuffer.DispatchCompute(m_Shader, m_AddOffsets, Groups(m_ScanLengths[i], ScanWidth), 1, 1);
                }
                Bind(cmdBuffer, m_Compact, "_Masks", m_Masks);
                Bind(cmdBuffer, m_Compact, "_Offsets", m_Offsets[0]);
                Bind(cmdBuffer, m_Compact, "_Indices0", pack.indexBuffers[0]);
                Bind(cmdBuffer, m_Compact, "_Indices1", bucketCount > 1 ? pack.indexBuffers[1] : m_DummyIndex);
                Bind(cmdBuffer, m_Compact, "_Indices2", bucketCount > 2 ? pack.indexBuffers[2] : m_DummyIndex);
                Bind(cmdBuffer, m_Compact, "_Weights1", weighted ? pack.weightBuffers[0] : m_DummyWeight);
                Bind(cmdBuffer, m_Compact, "_Weights2", weighted ? pack.weightBuffers[1] : m_DummyWeight);
                Bind(cmdBuffer, m_Compact, "_InstanceWeights", view.weights);
                cmdBuffer.DispatchCompute(m_Shader, m_Compact, length, 1, 1);
                Bind(cmdBuffer, m_CopyArgs, "_Offsets", m_Offsets[0]);
                Bind(cmdBuffer, m_CopyArgs, "_Counts", m_Counts);
                Bind(cmdBuffer, m_CopyArgs, "_ArgsDst", pack.argsBuffer);
                cmdBuffer.DispatchCompute(m_Shader, m_CopyArgs, Groups(bucketCount * pack.submeshCount, ArgsWidth), 1, 1);
            }
        }

        void SetViewParams(CommandBuffer cmdBuffer, in TreeLodFadeMode mode, in bool allowFade, in bool shadow)
        {
            cmdBuffer.SetComputeIntParam(m_Shader, "_InstanceCount", m_InstanceCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_ChunkCount", m_ChunkCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_LodCount", m_LodCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Mode", mode == TreeLodFadeMode.Distance ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_AllowFade", allowFade ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Shadow", shadow ? 1 : 0);
        }

        void Bind(CommandBuffer cmdBuffer, in int kernel, string name, ComputeBuffer buffer)
        {
            cmdBuffer.SetComputeBufferParam(m_Shader, kernel, name, buffer);
        }

        public void Release()
        {
            m_Ready = false;
            m_DummyHzb?.Dispose(); m_DummyHzb = null;
            m_Bounds?.Dispose(); m_Bounds = null;
            m_InstanceCell?.Dispose(); m_InstanceCell = null;
            m_CellBounds?.Dispose(); m_CellBounds = null;
            m_LodSizes?.Dispose(); m_LodSizes = null;
            m_LodDither?.Dispose(); m_LodDither = null;
            m_Heights?.Dispose(); m_Heights = null;
            m_Planes?.Dispose(); m_Planes = null;
            m_Masks?.Dispose(); m_Masks = null;
            m_Counts?.Dispose(); m_Counts = null;
            m_Changes?.Dispose(); m_Changes = null;
            m_DummyIndex?.Dispose(); m_DummyIndex = null;
            m_DummyWeight?.Dispose(); m_DummyWeight = null;
            DisposeBuffers(m_Offsets);
            DisposeBuffers(m_Sums);
            DisposeBuffers(m_Reductions);
        }

        static void DisposeBuffers(List<ComputeBuffer> buffers)
        {
            if (buffers == null) { return; }
            for (int i = 0; i < buffers.Count; ++i) { buffers[i]?.Dispose(); }
            buffers.Clear();
        }
    }
}
