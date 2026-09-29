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
        public Camera camera;
        public ComputeBuffer cells;
        public ComputeBuffer lod;
        public ComputeBuffer weights;
        public ComputeBuffer fade;
        public bool reset = true;
        public bool hasInput;
        public bool hzb;
        public string hzbReason;
        public bool allowFade;
        public TreeLodFadeMode mode;
        public float3 lastOrigin;
        public float4x4 lastProjection;
        public int advancedFrame = -1;

        public void Initialize(Camera viewCamera, in int instanceCount, in int cellCount)
        {
            camera = viewCamera;
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

    internal class TreeVisibilityGpu
    {
        const int HzbWidth = 64;
        const int HzbHeight = 32;
        const int ScanWidth = 128;

        private ComputeShader m_Shader;
        private int m_BuildHzb;
        private int m_BuildHzbMip;
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
        private ComputeBuffer m_Hzb;
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
        private ComputeBuffer m_DummyWeight;
        private List<ComputeBuffer> m_Offsets;
        private List<ComputeBuffer> m_Sums;
        private List<ComputeBuffer> m_Reductions;
        private List<int> m_ScanLengths;
        private Dictionary<int, TreeGpuView> m_Views;
        private Dictionary<ulong, TreeGpuView> m_ShadowViews;
        private List<int> m_DeadViews;
        private List<ulong> m_DeadShadowViews;
        private TreeGpuView m_ActiveView;
        private int m_InstanceCount;
        private int m_CellCount;
        private int m_ChunkCount;
        private int m_LodCount;
        private int m_HeightRes;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
        private bool m_Ready;
        private bool m_HasHzb;
        private string m_FailureReason;
        private string m_HzbReason;
        private Vector4[] m_ShadowPlanes = new Vector4[6];

        public bool IsReady { get { return m_Ready; } }
        public bool HasHzb { get { return m_HasHzb; } }
        public string FailureReason { get { return m_FailureReason; } }
        public string HzbReason { get { return m_HzbReason; } }

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
            m_BuildHzb = OptionalKernel("BuildHzb");
            m_BuildHzbMip = OptionalKernel("BuildHzbMip");
            m_InstanceCount = bounds.Length;
            m_CellCount = cellBounds.Length;
            m_ChunkCount = (m_InstanceCount + 63) / 64;
            m_LodCount = lodScreenSizes.Length;
            m_HeightRes = heights.IsCreated && heightRes > 1 && heights.Length >= heightRes * heightRes ? heightRes : 0;
            m_TerrainPos = terrainPos;
            m_TerrainSize = terrainSize;
            try
            {
                m_Bounds = new ComputeBuffer(m_InstanceCount, Marshal.SizeOf(typeof(GpuAabb)));
                m_InstanceCell = new ComputeBuffer(m_InstanceCount, sizeof(int));
                m_CellBounds = new ComputeBuffer(m_CellCount, Marshal.SizeOf(typeof(GpuAabb)));
                m_LodSizes = new ComputeBuffer(m_LodCount, sizeof(float));
                m_LodDither = new ComputeBuffer(m_LodCount, sizeof(uint));
                m_Heights = new ComputeBuffer(m_HeightRes > 0 ? heights.Length : 1, sizeof(float));
                m_Planes = new ComputeBuffer(6, sizeof(float) * 4);
                m_Masks = new ComputeBuffer(m_ChunkCount, sizeof(uint) * 2);
                m_Counts = new ComputeBuffer(m_ChunkCount, sizeof(uint));
                m_Changes = new ComputeBuffer(m_ChunkCount, sizeof(uint));
                m_DummyWeight = new ComputeBuffer(1, sizeof(float));
                m_DummyHzb = new ComputeBuffer(1, sizeof(float));
                m_Offsets = new List<ComputeBuffer>();
                m_Sums = new List<ComputeBuffer>();
                m_Reductions = new List<ComputeBuffer>();
                m_ScanLengths = new List<int>();
                int length = m_ChunkCount;
                do
                {
                    int groups = (length + ScanWidth - 1) / ScanWidth;
                    m_ScanLengths.Add(length);
                    m_Offsets.Add(new ComputeBuffer(length, sizeof(uint)));
                    m_Sums.Add(new ComputeBuffer(groups, sizeof(uint)));
                    m_Reductions.Add(new ComputeBuffer(groups, sizeof(uint)));
                    if (groups == 1) { break; }
                    length = groups;
                }
                while (true);
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
                m_Views = new Dictionary<int, TreeGpuView>(4);
                m_ShadowViews = new Dictionary<ulong, TreeGpuView>(4);
                m_DeadViews = new List<int>(4);
                m_DeadShadowViews = new List<ulong>(4);
                m_Ready = true;
                m_HzbReason = "HZB has not been requested for this view.";
                return true;
            }
            catch (Exception error)
            {
                Release();
                m_FailureReason = "Tree GPU allocation failed: " + error.Message;
                return false;
            }
        }

        int OptionalKernel(string name)
        {
            if (!m_Shader.HasKernel(name)) { return -1; }
            int kernel = m_Shader.FindKernel(name);
            return m_Shader.IsSupported(kernel) ? kernel : -1;
        }

        static GpuAabb Pack(Aabb bounds)
        {
            GpuAabb packed = default;
            packed.center = bounds.center;
            packed.extents = bounds.extents;
            return packed;
        }

        void PruneViews()
        {
            m_DeadViews.Clear();
            foreach (var pair in m_Views)
            {
                if (pair.Value.camera == null) { m_DeadViews.Add(pair.Key); }
            }
            for (int i = 0; i < m_DeadViews.Count; ++i)
            {
                m_Views[m_DeadViews[i]].Dispose();
                m_Views.Remove(m_DeadViews[i]);
            }
            m_DeadShadowViews.Clear();
            foreach (var pair in m_ShadowViews)
            {
                if (pair.Value.camera == null) { m_DeadShadowViews.Add(pair.Key); }
            }
            for (int i = 0; i < m_DeadShadowViews.Count; ++i)
            {
                m_ShadowViews[m_DeadShadowViews[i]].Dispose();
                m_ShadowViews.Remove(m_DeadShadowViews[i]);
            }
        }

        TreeGpuView GetView(Camera camera, in int cascade)
        {
            PruneViews();
            int cameraId = camera.GetEntityId().GetHashCode();
            TreeGpuView view;
            if (cascade < 0)
            {
                if (m_Views.TryGetValue(cameraId, out view))
                {
                    if (view.camera == camera) { return view; }
                    view.Dispose();
                }
                view = new TreeGpuView();
                view.Initialize(camera, m_InstanceCount, m_CellCount);
                m_Views[cameraId] = view;
            }
            else
            {
                ulong key = ((ulong)(uint)cameraId << 32) | (uint)cascade;
                if (m_ShadowViews.TryGetValue(key, out view))
                {
                    if (view.camera == camera) { return view; }
                    view.Dispose();
                }
                view = new TreeGpuView();
                view.Initialize(camera, m_InstanceCount, m_CellCount);
                m_ShadowViews[key] = view;
            }
            return view;
        }

        bool TryGetView(Camera camera, in int cascade, out TreeGpuView view)
        {
            try
            {
                view = GetView(camera, cascade);
                return true;
            }
            catch (Exception error)
            {
                Fail("Tree GPU view allocation failed: " + error.Message);
                view = null;
                return false;
            }
        }

        public void Fail(string reason)
        {
            m_Ready = false;
            m_HasHzb = false;
            m_FailureReason = reason;
            m_HzbReason = reason;
        }

        public ComputeBuffer GetFadeState(Camera camera)
        {
            if (!m_Ready || camera == null || !m_Views.TryGetValue(camera.GetEntityId().GetHashCode(), out TreeGpuView view)) { return null; }
            return view.fade;
        }

        public string HzbStatus(Camera camera)
        {
            if (camera != null && m_Views != null && m_Views.TryGetValue(camera.GetEntityId().GetHashCode(), out TreeGpuView view))
            {
                return view.hzb ? "active" : view.hzbReason;
            }
            return m_HasHzb ? "active" : m_HzbReason;
        }

        public void ResetViews()
        {
            m_HasHzb = false;
            m_ActiveView = null;
            m_HzbReason = "View state was reset.";
            if (m_Views != null)
            {
                foreach (TreeGpuView view in m_Views.Values)
                {
                    view.reset = true;
                    view.hzb = false;
                    view.hzbReason = m_HzbReason;
                }
            }
            if (m_ShadowViews != null) { foreach (TreeGpuView view in m_ShadowViews.Values) { view.reset = true; } }
        }

        public void DispatchColor(CommandBuffer cmdBuffer, Camera camera, Vector4[] planes,
            in float drawDistance, in float4x4 lodProjection, in TreeOcclusionMode occlusion,
            in bool allowFade, in TreeLodFadeMode mode, in float fadeWidth, in float hysteresis,
            in float duration, in float deltaTime, RTHandle depth, in Vector4 zParams,
            in Matrix4x4 depthViewProj, List<TreeLodBatch> batches)
        {
            if (camera == null || cmdBuffer == null) { return; }
            if (!m_Ready || !TryGetView(camera, -1, out TreeGpuView view))
            {
                for (int lod = 0; lod < batches.Count; ++lod)
                {
                    TreeLodBatch batch = batches[lod];
                    for (int bucket = 0; bucket < 3; ++bucket)
                    {
                        batch.uploadedCount[bucket] = 0;
                        batch.gpuArgs[bucket] = false;
                    }
                    for (int args = 0; args < batch.argsBuffers.Length; ++args)
                    {
                        batch.argsValues[args][1] = 0;
                        cmdBuffer.SetBufferData(batch.argsBuffers[args], batch.argsValues[args]);
                    }
                }
                return;
            }
            m_ActiveView = view;
            BuildHzb(cmdBuffer, camera, depth, zParams, depthViewProj, (occlusion & TreeOcclusionMode.Hzb) != 0);
            view.hzb = m_HasHzb;
            view.hzbReason = m_HzbReason;
            Prepare(cmdBuffer, camera, view, planes, drawDistance, lodProjection, occlusion, allowFade,
                mode, fadeWidth, hysteresis, duration, deltaTime, false);
            for (int lod = 0; lod < batches.Count; ++lod)
            {
                TreeLodBatch batch = batches[lod];
                for (int bucket = 0; bucket < 3; ++bucket)
                {
                    bool weighted = mode == TreeLodFadeMode.Distance && allowFade && bucket != 0;
                    ComputeBuffer weights = weighted ? batch.weightBuffers[bucket - 1] : m_DummyWeight;
                    Emit(cmdBuffer, view, lod, bucket, batch.indexBuffers[bucket], weights, weighted);
                    for (int submesh = 0; submesh < batch.sectionIndexs.Length; ++submesh)
                    {
                        int argsIndex = (bucket * batch.sectionIndexs.Length) + submesh;
                        cmdBuffer.SetBufferData(batch.argsBuffers[argsIndex], batch.argsValues[argsIndex]);
                        WriteCount(cmdBuffer, batch.argsBuffers[argsIndex]);
                    }
                    batch.gpuArgs[bucket] = true;
                    batch.uploadedCount[bucket] = m_InstanceCount;
                }
            }
        }

        public void DispatchShadow(CommandBuffer cmdBuffer, Camera camera, in int cascadeIndex,
            Plane[] planes, in float hysteresis, TreeShadowView shadowView, List<TreeLodBatch> batches)
        {
            if (!m_Ready || camera == null || cmdBuffer == null || cascadeIndex < 0 || cascadeIndex >= 4) { return; }
            if (!TryGetView(camera, cascadeIndex, out TreeGpuView view)) { return; }
            for (int i = 0; i < 6; ++i)
            {
                Vector3 normal = planes[i].normal;
                m_ShadowPlanes[i] = new Vector4(normal.x, normal.y, normal.z, planes[i].distance);
            }
            float4x4 projection = Geometry.GetLodProjectionMatrix(camera);
            Prepare(cmdBuffer, camera, view, m_ShadowPlanes, float.MaxValue, projection, TreeOcclusionMode.None,
                false, TreeLodFadeMode.Temporal, 0.2f, hysteresis, 0.5f, 0f, true);
            for (int lod = 0; lod < batches.Count; ++lod)
            {
                TreeShadowBatch shadow = shadowView.cascades[cascadeIndex][lod];
                Emit(cmdBuffer, view, lod, 0, shadow.indexBuffer, m_DummyWeight, false);
                for (int submesh = 0; submesh < shadow.argsBuffers.Length; ++submesh)
                {
                    WriteCount(cmdBuffer, shadow.argsBuffers[submesh]);
                }
            }
        }

        void Prepare(CommandBuffer cmdBuffer, Camera camera, TreeGpuView view, Vector4[] planes,
            in float drawDistance, in float4x4 projection, in TreeOcclusionMode occlusion,
            in bool allowFade, in TreeLodFadeMode mode, in float fadeWidth, in float hysteresis,
            in float duration, in float deltaTime, in bool shadow)
        {
            float3 origin = camera.transform.position;
            bool reset = view.reset || !view.hasInput || view.allowFade != allowFade || view.mode != mode ||
                FoliageLogic.ViewDiscontinuous(view.lastOrigin, origin, view.lastProjection, projection, drawDistance);
            bool advance = view.advancedFrame != Time.frameCount;
            cmdBuffer.SetBufferData(m_Planes, planes, 0, 0, 6);
            cmdBuffer.SetComputeIntParam(m_Shader, "_InstanceCount", m_InstanceCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_ChunkCount", m_ChunkCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_CellCount", m_CellCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_LodCount", m_LodCount);
            cmdBuffer.SetComputeIntParam(m_Shader, "_HeightRes", m_HeightRes);
            cmdBuffer.SetComputeIntParam(m_Shader, "_EnableTerrain", !shadow && (occlusion & TreeOcclusionMode.Terrain) != 0 ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_EnableHzb", !shadow && m_HasHzb ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Mode", mode == TreeLodFadeMode.Distance ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_AllowFade", allowFade ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Reset", reset ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Advance", advance ? 1 : 0);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Shadow", shadow ? 1 : 0);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_ProjectionY", projection.c1.y);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Orthographic", projection.c3.w > 0.5f ? 1f : 0f);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_MaxDistance", drawDistance);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_FadeWidth", FoliageLogic.ClampFadeWidth(fadeWidth));
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Hysteresis", reset ? 0f : math.clamp(hysteresis, 0f, 0.49f));
            cmdBuffer.SetComputeFloatParam(m_Shader, "_Duration", duration);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_DeltaTime", deltaTime);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
            cmdBuffer.SetComputeVectorParam(m_Shader, "_TerrainPos", new Vector4(m_TerrainPos.x, m_TerrainPos.y, m_TerrainPos.z, 0));
            cmdBuffer.SetComputeVectorParam(m_Shader, "_TerrainSize", new Vector4(m_TerrainSize.x, m_TerrainSize.y, m_TerrainSize.z, 0));
            Bind(cmdBuffer, m_PrepareView, "_FadeState", view.fade);
            cmdBuffer.DispatchCompute(m_Shader, m_PrepareView, 1, 1, 1);
            Bind(cmdBuffer, m_CullCells, "_CellBounds", m_CellBounds);
            Bind(cmdBuffer, m_CullCells, "_CellVisible", view.cells);
            Bind(cmdBuffer, m_CullCells, "_Planes", m_Planes);
            Bind(cmdBuffer, m_CullCells, "_Heights", m_Heights);
            Bind(cmdBuffer, m_CullCells, "_Hzb", m_Hzb ?? m_DummyHzb);
            cmdBuffer.DispatchCompute(m_Shader, m_CullCells, (m_CellCount + 63) / 64, 1, 1);
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
            Bind(cmdBuffer, m_CullInstances, "_Hzb", m_Hzb ?? m_DummyHzb);
            cmdBuffer.DispatchCompute(m_Shader, m_CullInstances, m_ChunkCount, 1, 1);
            ComputeBuffer input = m_Changes;
            for (int i = 0; i < m_ScanLengths.Count; ++i)
            {
                cmdBuffer.SetComputeIntParam(m_Shader, "_ScanLength", m_ScanLengths[i]);
                Bind(cmdBuffer, m_ReduceChanges, "_ScanInput", input);
                Bind(cmdBuffer, m_ReduceChanges, "_ScanSums", m_Reductions[i]);
                cmdBuffer.DispatchCompute(m_Shader, m_ReduceChanges, (m_ScanLengths[i] + ScanWidth - 1) / ScanWidth, 1, 1);
                input = m_Reductions[i];
            }
            Bind(cmdBuffer, m_AdvanceFade, "_ScanInput", input);
            Bind(cmdBuffer, m_AdvanceFade, "_FadeState", view.fade);
            cmdBuffer.DispatchCompute(m_Shader, m_AdvanceFade, 1, 1, 1);
            Bind(cmdBuffer, m_CommitLod, "_FadeState", view.fade);
            Bind(cmdBuffer, m_CommitLod, "_LodState", view.lod);
            cmdBuffer.DispatchCompute(m_Shader, m_CommitLod, m_ChunkCount, 1, 1);
            view.reset = false;
            view.hasInput = true;
            view.lastOrigin = origin;
            view.lastProjection = projection;
            view.mode = mode;
            view.allowFade = allowFade;
            view.advancedFrame = Time.frameCount;
        }

        void Emit(CommandBuffer cmdBuffer, TreeGpuView view, in int lod, in int bucket,
            ComputeBuffer indices, ComputeBuffer weights, in bool weighted)
        {
            cmdBuffer.SetComputeIntParam(m_Shader, "_Lod", lod);
            cmdBuffer.SetComputeIntParam(m_Shader, "_Bucket", bucket);
            cmdBuffer.SetComputeIntParam(m_Shader, "_WriteWeight", weighted ? 1 : 0);
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
                cmdBuffer.DispatchCompute(m_Shader, m_ScanBlocks, (m_ScanLengths[i] + ScanWidth - 1) / ScanWidth, 1, 1);
            }
            for (int i = m_ScanLengths.Count - 2; i >= 0; --i)
            {
                cmdBuffer.SetComputeIntParam(m_Shader, "_ScanLength", m_ScanLengths[i]);
                Bind(cmdBuffer, m_AddOffsets, "_ScanOffsets", m_Offsets[i]);
                Bind(cmdBuffer, m_AddOffsets, "_ParentOffsets", m_Offsets[i + 1]);
                cmdBuffer.DispatchCompute(m_Shader, m_AddOffsets, (m_ScanLengths[i] + ScanWidth - 1) / ScanWidth, 1, 1);
            }
            Bind(cmdBuffer, m_Compact, "_Masks", m_Masks);
            Bind(cmdBuffer, m_Compact, "_Offsets", m_Offsets[0]);
            Bind(cmdBuffer, m_Compact, "_Indices", indices);
            Bind(cmdBuffer, m_Compact, "_Weights", weights);
            Bind(cmdBuffer, m_Compact, "_InstanceWeights", view.weights);
            cmdBuffer.DispatchCompute(m_Shader, m_Compact, m_ChunkCount, 1, 1);
        }

        void WriteCount(CommandBuffer cmdBuffer, ComputeBuffer args)
        {
            Bind(cmdBuffer, m_CopyArgs, "_Offsets", m_Offsets[0]);
            Bind(cmdBuffer, m_CopyArgs, "_Counts", m_Counts);
            Bind(cmdBuffer, m_CopyArgs, "_ArgsDst", args);
            cmdBuffer.DispatchCompute(m_Shader, m_CopyArgs, 1, 1, 1);
        }

        void Bind(CommandBuffer cmdBuffer, in int kernel, string name, ComputeBuffer buffer)
        {
            cmdBuffer.SetComputeBufferParam(m_Shader, kernel, name, buffer);
        }

        void BuildHzb(CommandBuffer cmdBuffer, Camera camera, RTHandle depth, in Vector4 zParams,
            in Matrix4x4 depthViewProj, in bool enabled)
        {
            m_HasHzb = false;
            if (!enabled) { m_HzbReason = "HZB is disabled."; return; }
            if (m_BuildHzb < 0 || m_BuildHzbMip < 0) { m_HzbReason = "HZB kernels are unavailable."; return; }
            if (camera.orthographic) { m_HzbReason = "Orthographic depth uses conservative visibility without HZB."; return; }
            if (depth == null || depth.rt == null || zParams == Vector4.zero) { m_HzbReason = "Current camera depth is unavailable."; return; }
            if (depth.rt.antiAliasing > 1) { m_HzbReason = "Multisample depth is not supported by HZB."; return; }
            if (m_Hzb == null)
            {
                try
                {
                    m_Hzb = new ComputeBuffer((HzbWidth * HzbHeight) + ((HzbWidth / 2) * (HzbHeight / 2)), sizeof(float));
                }
                catch (Exception error)
                {
                    m_HzbReason = "HZB allocation failed: " + error.Message;
                    return;
                }
            }
            cmdBuffer.SetComputeIntParam(m_Shader, "_HzbWidth", HzbWidth);
            cmdBuffer.SetComputeIntParam(m_Shader, "_HzbHeight", HzbHeight);
            cmdBuffer.SetComputeFloatParam(m_Shader, "_NearClip", camera.nearClipPlane);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_ZBufferParams", zParams);
            cmdBuffer.SetComputeVectorParam(m_Shader, "_HzbSize", new Vector4(HzbWidth, HzbHeight, depth.rt.width, depth.rt.height));
            cmdBuffer.SetComputeMatrixParam(m_Shader, "_ViewProj", depthViewProj);
            cmdBuffer.SetComputeTextureParam(m_Shader, m_BuildHzb, "_CameraDepthTexture", depth);
            Bind(cmdBuffer, m_BuildHzb, "_Hzb", m_Hzb);
            cmdBuffer.DispatchCompute(m_Shader, m_BuildHzb, (HzbWidth + 7) / 8, (HzbHeight + 7) / 8, 1);
            Bind(cmdBuffer, m_BuildHzbMip, "_Hzb", m_Hzb);
            cmdBuffer.DispatchCompute(m_Shader, m_BuildHzbMip, ((HzbWidth / 2) + 7) / 8, ((HzbHeight / 2) + 7) / 8, 1);
            m_HasHzb = true;
            m_HzbReason = null;
        }

#if UNITY_EDITOR
        public bool RequestCellReadback(CommandBuffer cmdBuffer, Action<AsyncGPUReadbackRequest> callback)
        {
            if (!m_Ready || m_ActiveView == null) { return false; }
            cmdBuffer.RequestAsyncReadback(m_ActiveView.cells, callback);
            return true;
        }

        public bool RequestCellReadback(CommandBuffer cmdBuffer, Camera camera, Action<AsyncGPUReadbackRequest> callback)
        {
            if (!m_Ready || camera == null || !m_Views.TryGetValue(camera.GetEntityId().GetHashCode(), out TreeGpuView view)) { return false; }
            cmdBuffer.RequestAsyncReadback(view.cells, callback);
            return true;
        }

        public bool RequestStateReadback(CommandBuffer cmdBuffer, Camera camera, Action<AsyncGPUReadbackRequest> callback)
        {
            ComputeBuffer state = GetFadeState(camera);
            if (state == null) { return false; }
            cmdBuffer.RequestAsyncReadback(state, callback);
            return true;
        }

        public bool RequestLodReadback(CommandBuffer cmdBuffer, Camera camera, Action<AsyncGPUReadbackRequest> callback)
        {
            if (!m_Ready || camera == null || !m_Views.TryGetValue(camera.GetEntityId().GetHashCode(), out TreeGpuView view)) { return false; }
            cmdBuffer.RequestAsyncReadback(view.lod, callback);
            return true;
        }
#endif

        public void Release()
        {
            m_Ready = false;
            m_HasHzb = false;
            m_ActiveView = null;
            if (m_Views != null) { foreach (TreeGpuView view in m_Views.Values) { view.Dispose(); } m_Views.Clear(); }
            if (m_ShadowViews != null) { foreach (TreeGpuView view in m_ShadowViews.Values) { view.Dispose(); } m_ShadowViews.Clear(); }
            m_Hzb?.Dispose(); m_Hzb = null;
            m_DummyHzb?.Dispose(); m_DummyHzb = null;
            m_DeadViews?.Clear();
            m_DeadShadowViews?.Clear();
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
