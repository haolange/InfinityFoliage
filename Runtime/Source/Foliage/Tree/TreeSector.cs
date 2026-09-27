using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.FoliagePipeline
{
    internal class TreeLodBatch
    {
        public int meshIndex;
        public int[] sectionIndexs;
        public int[] materialIndexs;
        public ComputeBuffer[] indexBuffers;
        public ComputeBuffer[] argsBuffers;
        public NativeArray<ulong> stableMask;
        public NativeArray<ulong> fadeOutMask;
        public NativeArray<ulong> fadeInMask;
        public NativeArray<int> bucketCounts;
        public int[] uploadedCount;
        public bool[] gpuArgs;

        public void Initialize(in int instanceCount, in int chunkCount)
        {
            indexBuffers = new ComputeBuffer[3];
            int argsCount = 3 * math.max(sectionIndexs.Length, 1);
            argsBuffers = new ComputeBuffer[argsCount];
            try
            {
                for (int i = 0; i < 3; ++i)
                {
                    indexBuffers[i] = new ComputeBuffer(math.max(instanceCount, 1), sizeof(int));
                }
                for (int i = 0; i < argsCount; ++i)
                {
                    argsBuffers[i] = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                }
                int chunks = math.max(chunkCount, 1);
                stableMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
                fadeOutMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
                fadeInMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
                bucketCounts = new NativeArray<int>(3, Allocator.Persistent);
                uploadedCount = new int[3];
                gpuArgs = new bool[3];
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (indexBuffers != null)
            {
                for (int i = 0; i < indexBuffers.Length; ++i) { indexBuffers[i]?.Dispose(); }
            }
            if (argsBuffers != null)
            {
                for (int i = 0; i < argsBuffers.Length; ++i) { argsBuffers[i]?.Dispose(); }
            }
            if (stableMask.IsCreated) { stableMask.Dispose(); }
            if (fadeOutMask.IsCreated) { fadeOutMask.Dispose(); }
            if (fadeInMask.IsCreated) { fadeInMask.Dispose(); }
            if (bucketCounts.IsCreated) { bucketCounts.Dispose(); }
        }
    }

    internal class TreeCameraFade
    {
        public Camera camera;
        public NativeArray<int> lodHold;
        public NativeArray<int> lodStable;
        public int lastAdvancedFrame;
        public float alpha;
        public bool fading;
        public bool hardCut;
        public float3 holdOrigin;
        public float3 nowOrigin;
        public float4x4 holdProj;
        public float4x4 nowProj;
        public float3 lastOrigin;
        public float4x4 lastProj;
        public bool hasLast;

        public void Initialize(Camera camera, in int instanceCount)
        {
            this.camera = camera;
            lodHold = new NativeArray<int>(instanceCount, Allocator.Persistent);
            lodStable = new NativeArray<int>(instanceCount, Allocator.Persistent);
            for (int i = 0; i < instanceCount; ++i) { lodHold[i] = -1; lodStable[i] = -1; }
            lastAdvancedFrame = -1;
            alpha = 1f;
            fading = false;
            hasLast = false;
        }

        public void Dispose()
        {
            lodHold.Dispose();
            lodStable.Dispose();
        }
    }

#if UNITY_EDITOR
    internal class TreeVisibilitySnapshot
    {
        internal class Bucket
        {
            internal uint[] args;
            internal int[] indices;
        }

        internal Camera camera;
        internal int frame;
        internal bool hzb;
        internal bool complete;
        internal bool failed;
        internal int pending;
        internal byte[] coarseCells;
        internal byte[] terrainCells;
        internal uint[] gpuCells;
        internal ulong[] cpuMasks;
        internal int[] lods;
        internal bool[] finalVisible;
        internal List<Bucket> buckets = new List<Bucket>();

        internal void FinishRequest()
        {
            --pending;
            if (pending > 0) { return; }
            complete = true;
            if (failed) { return; }
            for (int b = 0; b < buckets.Count; ++b)
            {
                Bucket bucket = buckets[b];
                if (bucket.args == null || bucket.indices == null) { failed = true; return; }
                int count = math.min((int)bucket.args[1], bucket.indices.Length);
                for (int i = 0; i < count; ++i)
                {
                    int index = bucket.indices[i];
                    if (index >= 0 && index < finalVisible.Length) { finalVisible[index] = true; }
                }
            }
        }

        internal int FinalCount()
        {
            if (!complete || failed) { return 0; }
            int count = 0;
            for (int i = 0; i < finalVisible.Length; ++i) { if (finalVisible[i]) { ++count; } }
            return count;
        }
    }
#endif

    internal class TreeShadowBatch
    {
        public ComputeBuffer indexBuffer;
        public ComputeBuffer[] argsBuffers;
        public int capacity;

        public void Initialize(in int submeshCount)
        {
            argsBuffers = new ComputeBuffer[submeshCount];
            try
            {
                for (int i = 0; i < submeshCount; ++i)
                {
                    argsBuffers[i] = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void EnsureCapacity(in int required)
        {
            if (capacity >= required) { return; }
            int nextCapacity = math.max(64, math.ceilpow2(required));
            ComputeBuffer next = new ComputeBuffer(nextCapacity, sizeof(int));
            if (indexBuffer != null) { indexBuffer.Dispose(); }
            indexBuffer = next;
            capacity = nextCapacity;
        }

        public void Dispose()
        {
            if (indexBuffer != null) { indexBuffer.Dispose(); indexBuffer = null; }
            if (argsBuffers == null) { return; }
            for (int i = 0; i < argsBuffers.Length; ++i) { argsBuffers[i]?.Dispose(); }
        }
    }

    internal class TreeShadowView
    {
        public TreeShadowBatch[][] cascades;
        public int[] lodStable;
        public Camera camera;

        public void Initialize(Camera viewCamera, List<TreeLodBatch> batches, in int instanceCount)
        {
            camera = viewCamera;
            lodStable = new int[instanceCount];
            for (int i = 0; i < instanceCount; ++i) { lodStable[i] = -1; }
            cascades = new TreeShadowBatch[4][];
            for (int cascade = 0; cascade < 4; ++cascade)
            {
                cascades[cascade] = new TreeShadowBatch[batches.Count];
            }
        }

        public void Dispose()
        {
            if (cascades == null) { return; }
            for (int cascade = 0; cascade < cascades.Length; ++cascade)
            {
                for (int lod = 0; lod < cascades[cascade].Length; ++lod)
                {
                    if (cascades[cascade][lod] != null) { cascades[cascade][lod].Dispose(); }
                }
            }
        }
    }

    [Serializable]
    public class TreeSector
    {
        public FoliageMesh tree;
        public int treeIndex;
        [NonSerialized]
        public List<InstanceTransform> transforms;
        public TreeCell[] cells;
        public BoundSector boundSector;

        [NonSerialized]
        public Aabb packedBound;
        [NonSerialized]
        public bool hasPackedBound;

        private ComputeBuffer m_MatrixBuffer;
        private NativeArray<float4x4> m_Matrices;
        private NativeArray<Aabb> m_Bounds;
        private NativeArray<int> m_InstanceCell;
        private NativeArray<float> m_LodScreenSizes;
        private NativeArray<byte> m_LodDither;
        private NativeArray<ulong> m_ChunkMasks;
        private NativeArray<int> m_LodNow;
        private NativeArray<float> m_Heights;
        private List<TreeLodBatch> m_Batches;
        private Dictionary<ulong, TreeCameraFade> m_FadeViews;
        private List<ulong> m_DeadFadeViews;
        private Dictionary<ulong, TreeShadowView> m_ShadowViews;
        private List<ulong> m_DeadShadowViews;
        private TreeCameraFade m_ActiveFade;
        private TreeVisibilityGpu m_Gpu;
        private Camera m_ActiveCamera;
        private uint[] m_ArgsScratch;
        private int[] m_IndexScratch;
        private int[][] m_ShadowIndexScratch;
        private int[] m_ShadowCounts;
        private ulong[] m_MaskScratch;
        private VisibilityRun[] m_RunScratch;
        private Vector4[] m_PlaneScratch;
        private bool m_DitherEnabled;
        private bool m_AllowFade = true;
        private TreeOcclusionMode m_OcclusionMode = TreeOcclusionMode.TerrainAndHzb;
        private int m_InstanceCount;
        private int m_HeightRes;
        private float m_DrawDistance;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
        private bool m_PendingVisibility;
        private JobHandle m_ViewHandle;
        private JobHandle m_CullHandle;
#if UNITY_EDITOR
        private TreeVisibilitySnapshot m_DebugSnapshot;
#endif

        public void LoadCandidates(List<InstanceTransform> candidates)
        {
            transforms = candidates;
        }

        public void RebuildSpatialGrid(in int numSection, in int sectorSize, in float3 terrainPosition, in Aabb terrainBound)
        {
            int sectionSize = math.max(sectorSize / math.max(numSection, 1), 1);
            boundSector = new BoundSector(numSection, sectorSize, sectionSize, terrainPosition, terrainBound, true);
            int cellCount = numSection * numSection;
            cells = new TreeCell[cellCount];
            for (int i = 0; i < cellCount; ++i)
            {
                cells[i].boundIndex = i;
                cells[i].offset = 0;
                cells[i].count = 0;
            }
        }

        public void BakeCells(in int numSection, in int sectorSize, in float3 terrainPosition, in Aabb terrainBound)
        {
            if (boundSector == null || boundSector.sections == null || boundSector.sections.Length != numSection * numSection)
            {
                RebuildSpatialGrid(numSection, sectorSize, terrainPosition, terrainBound);
            }
            if (transforms == null) { return; }

            int cellCount = numSection * numSection;
            int n = transforms.Count;
            float sectionWorld = sectorSize / (float)math.max(numSection, 1);
            int[] counts = new int[cellCount];
            int[] cellOf = new int[n];
            int[] order = new int[n];
            Aabb localBound = tree.boundBox;
            Aabb[] worldBounds = new Aabb[n];
            BuildMortonOrder(n, terrainPosition, sectorSize, order);

            for (int i = 0; i < n; ++i)
            {
                int src = order[i];
                InstanceTransform transform = transforms[src];
                float4x4 matrixWorld = float4x4.TRS(transform.position, quaternion.EulerXYZ(transform.rotation), transform.scale);
                worldBounds[src] = Geometry.CaculateWorldBound(localBound, matrixWorld);
                float3 local = transform.position - terrainPosition;
                cellOf[src] = FoliageLogic.CellFromLocal(local.x, local.z, sectionWorld, numSection);
                counts[cellOf[src]]++;
            }

            int[] offsets = new int[cellCount];
            FoliageLogic.PrefixOffsets(counts, offsets);
            Aabb[] packedBounds = new Aabb[n];
            int[] cursor = (int[])offsets.Clone();
            for (int i = 0; i < n; ++i)
            {
                int src = order[i];
                packedBounds[cursor[cellOf[src]]++] = worldBounds[src];
            }

            hasPackedBound = false;
            for (int c = 0; c < cellCount; ++c)
            {
                cells[c].boundIndex = c;
                cells[c].offset = offsets[c];
                cells[c].count = counts[c];
                if (counts[c] <= 0) { continue; }
                Aabb box = packedBounds[offsets[c]];
                for (int k = 1; k < counts[c]; ++k)
                {
                    box.Encapsulate(packedBounds[offsets[c] + k]);
                }
                boundSector.sections[c].boundBox = box;
                if (!hasPackedBound)
                {
                    packedBound = box;
                    hasPackedBound = true;
                }
                else
                {
                    packedBound.Encapsulate(box);
                }
            }
        }

        public void Initialize(in int numSection, in int sectorSize, in float3 terrainPosition, in Aabb terrainBound)
        {
            if (transforms == null || transforms.Count == 0)
            {
                m_InstanceCount = 0;
                return;
            }

            if (boundSector == null || boundSector.sections == null || boundSector.sections.Length != numSection * numSection)
            {
                RebuildSpatialGrid(numSection, sectorSize, terrainPosition, terrainBound);
            }

            BuildSoA(numSection, sectorSize, terrainPosition);
            if (Application.isPlaying)
            {
                transforms = null;
            }
        }

        public void SetHeightField(float[] heights, int res, float3 pos, float3 size)
        {
            if (heights == null || res <= 1) { return; }
            m_CullHandle.Complete();
            if (m_Heights.IsCreated) { m_Heights.Dispose(); }
            m_Heights = new NativeArray<float>(heights, Allocator.Persistent);
            m_HeightRes = res;
            m_TerrainPos = pos;
            m_TerrainSize = size;
        }

        void BuildMortonOrder(int n, in float3 terrainPosition, in float worldSize, int[] order)
        {
            uint[] keys = new uint[n];
            for (int i = 0; i < n; ++i)
            {
                float3 local = transforms[i].position - terrainPosition;
                keys[i] = FoliageLogic.MortonFromLocal(local.x, local.z, worldSize);
                order[i] = i;
            }
            FoliageLogic.SortIndicesByKey(keys, order, n);
        }

        void BuildSoA(in int numSection, in int sectorSize, in float3 terrainPosition)
        {
            int n = transforms.Count;
            int cellCount = numSection * numSection;
            float sectionWorld = sectorSize / (float)math.max(numSection, 1);
            Aabb localBound = tree.boundBox;

            int[] counts = new int[cellCount];
            int[] cellOf = new int[n];
            int[] order = new int[n];
            float4x4[] worldMatrices = new float4x4[n];
            Aabb[] worldBounds = new Aabb[n];
            BuildMortonOrder(n, terrainPosition, sectorSize, order);
            for (int i = 0; i < n; ++i)
            {
                int src = order[i];
                InstanceTransform transform = transforms[src];
                float4x4 matrixWorld = float4x4.TRS(transform.position, quaternion.EulerXYZ(transform.rotation), transform.scale);
                worldMatrices[src] = matrixWorld;
                worldBounds[src] = Geometry.CaculateWorldBound(localBound, matrixWorld);
                float3 local = transform.position - terrainPosition;
                cellOf[src] = FoliageLogic.CellFromLocal(local.x, local.z, sectionWorld, numSection);
                counts[cellOf[src]]++;
            }

            int[] offsets = new int[cellCount];
            FoliageLogic.PrefixOffsets(counts, offsets);
            m_Matrices = new NativeArray<float4x4>(n, Allocator.Persistent);
            m_Bounds = new NativeArray<Aabb>(n, Allocator.Persistent);
            m_InstanceCell = new NativeArray<int>(n, Allocator.Persistent);
            int[] cursor = (int[])offsets.Clone();
            for (int i = 0; i < n; ++i)
            {
                int src = order[i];
                int dest = cursor[cellOf[src]]++;
                m_Matrices[dest] = worldMatrices[src];
                m_Bounds[dest] = worldBounds[src];
                m_InstanceCell[dest] = cellOf[src];
            }

            if (cells == null || cells.Length != cellCount)
            {
                cells = new TreeCell[cellCount];
            }
            hasPackedBound = false;
            for (int c = 0; c < cellCount; ++c)
            {
                cells[c].boundIndex = c;
                cells[c].offset = offsets[c];
                cells[c].count = counts[c];
                if (counts[c] <= 0) { continue; }
                Aabb box = m_Bounds[offsets[c]];
                for (int k = 1; k < counts[c]; ++k)
                {
                    box.Encapsulate(m_Bounds[offsets[c] + k]);
                }
                boundSector.sections[c].boundBox = box;
                if (!hasPackedBound)
                {
                    packedBound = box;
                    hasPackedBound = true;
                }
                else
                {
                    packedBound.Encapsulate(box);
                }
            }

            m_MatrixBuffer = new ComputeBuffer(n, Marshal.SizeOf(typeof(float4x4)));
            m_MatrixBuffer.SetData(m_Matrices);
            m_InstanceCount = n;
        }

        public void BuildRuntimeData()
        {
            if (m_InstanceCount <= 0 || tree.lODInfos == null) { return; }
            if (tree.meshes == null || tree.materials == null || tree.meshes.Length != tree.lODInfos.Length || tree.meshes.Length == 0)
            {
                throw new InvalidOperationException("Tree LOD mesh layout is invalid. Rebake this Terrain.");
            }
            for (int i = 0; i < tree.meshes.Length; ++i)
            {
                Mesh mesh = tree.meshes[i];
                int[] slots = tree.lODInfos[i].materialSlot;
                if (mesh == null || mesh.subMeshCount <= 0 || slots == null || slots.Length < mesh.subMeshCount)
                {
                    throw new InvalidOperationException("Tree LOD submesh layout is invalid. Rebake this Terrain.");
                }
                for (int j = 0; j < mesh.subMeshCount; ++j)
                {
                    int slot = slots[j];
                    if (slot < 0 || slot >= tree.materials.Length || tree.materials[slot] == null)
                    {
                        throw new InvalidOperationException("Tree LOD material slot is invalid. Rebake this Terrain.");
                    }
                }
            }

            boundSector.BuildNativeCollection();
            int chunkCount = FoliageLogic.VisibilityChunkCount(m_InstanceCount);
            m_LodScreenSizes = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
            m_LodDither = new NativeArray<byte>(tree.lODInfos.Length, Allocator.Persistent);
            m_ChunkMasks = new NativeArray<ulong>(math.max(chunkCount, 1), Allocator.Persistent);
            m_LodNow = new NativeArray<int>(m_InstanceCount, Allocator.Persistent);
            for (int i = 0; i < m_InstanceCount; ++i) { m_LodNow[i] = -1; }

            m_Batches = new List<TreeLodBatch>(tree.lODInfos.Length);
            m_DitherEnabled = false;
            for (int i = 0; i < tree.lODInfos.Length; ++i)
            {
                Mesh mesh = tree.meshes[i];
                ref MeshLodInfo meshLodInfo = ref tree.lODInfos[i];
                m_LodScreenSizes[i] = meshLodInfo.screenSize;
                m_LodDither[i] = 1;

                TreeLodBatch batch = new TreeLodBatch();
                batch.meshIndex = i;
                batch.sectionIndexs = new int[mesh.subMeshCount];
                batch.materialIndexs = new int[mesh.subMeshCount];
                for (int j = 0; j < mesh.subMeshCount; ++j)
                {
                    batch.sectionIndexs[j] = j;
                    batch.materialIndexs[j] = meshLodInfo.materialSlot[j];
                    Material material = tree.materials[meshLodInfo.materialSlot[j]];
                    if (material == null || material.FindPass("ForwardLit-Instance") < 0) { m_LodDither[i] = 0; }
                }
                if (m_LodDither[i] != 0) { m_DitherEnabled = true; }
                batch.Initialize(m_InstanceCount, chunkCount);
                m_Batches.Add(batch);
            }

            m_FadeViews = new Dictionary<ulong, TreeCameraFade>(4);
            m_DeadFadeViews = new List<ulong>(4);
            m_ShadowViews = new Dictionary<ulong, TreeShadowView>(4);
            m_DeadShadowViews = new List<ulong>(4);
            m_ArgsScratch = new uint[5];
            m_IndexScratch = new int[m_InstanceCount];
            m_ShadowIndexScratch = new int[m_Batches.Count][];
            m_ShadowCounts = new int[m_Batches.Count];
            for (int i = 0; i < m_Batches.Count; ++i) { m_ShadowIndexScratch[i] = new int[m_InstanceCount]; }
            m_MaskScratch = new ulong[math.max(chunkCount, 1)];
            m_RunScratch = new VisibilityRun[m_InstanceCount];
            m_PlaneScratch = new Vector4[6];
            m_Gpu = new TreeVisibilityGpu();
            m_Gpu.Initialize(m_InstanceCount, chunkCount, cells.Length);
            if (m_Gpu.IsReady)
            {
                m_Gpu.UploadBounds(m_Bounds, m_InstanceCell);
                m_Gpu.UploadCells(boundSector.nativeSections);
            }
            if (!m_Heights.IsCreated)
            {
                m_Heights = new NativeArray<float>(1, Allocator.Persistent);
                m_HeightRes = 0;
            }
        }

        TreeCameraFade GetFade(Camera camera)
        {
            m_DeadFadeViews.Clear();
            foreach (var pair in m_FadeViews)
            {
                if (pair.Value.camera == null) { m_DeadFadeViews.Add(pair.Key); }
            }
            for (int i = 0; i < m_DeadFadeViews.Count; ++i)
            {
                ulong deadKey = m_DeadFadeViews[i];
                m_FadeViews[deadKey].Dispose();
                m_FadeViews.Remove(deadKey);
            }
            ulong key = camera != null ? EntityId.ToULong(camera.GetEntityId()) : 0;
            if (m_FadeViews.TryGetValue(key, out TreeCameraFade fade))
            {
                return fade;
            }
            fade = new TreeCameraFade();
            fade.Initialize(camera, m_InstanceCount);
            m_FadeViews.Add(key, fade);
            return fade;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void InitView(in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0) { return; }
            m_ViewHandle = boundSector.InitView(cullDistance, new float4(viewOrigin, 1), planes);
            taskHandles.Add(m_ViewHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void DispatchSetup(Camera camera, in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj,
            in FrustumPlane* planes, in TreeOcclusionMode occlusionMode, in bool allowFade, in float lodHysteresis,
            in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0) { return; }

            m_ActiveCamera = camera;
            m_DrawDistance = cullDistance;
            bool fadeModeChanged = m_AllowFade != allowFade;
            m_AllowFade = allowFade;
            m_OcclusionMode = occlusionMode;
            for (int i = 0; i < 6; ++i)
            {
                float4 plane = planes[i].normalDist;
                m_PlaneScratch[i] = new Vector4(plane.x, plane.y, plane.z, plane.w);
            }

            m_ActiveFade = GetFade(camera);
            TreeCameraFade fade = m_ActiveFade;
            bool discontinuous = !fade.hasLast ||
                FoliageLogic.ViewDiscontinuous(fade.lastOrigin, viewOrigin, fade.lastProj, matrixProj, cullDistance);
            fade.hardCut = !allowFade || fadeModeChanged || discontinuous;
            if (fade.hardCut)
            {
                fade.holdOrigin = viewOrigin;
                fade.nowOrigin = viewOrigin;
                fade.holdProj = matrixProj;
                fade.nowProj = matrixProj;
                fade.alpha = 1f;
                fade.fading = false;
            }
            else if (!fade.fading)
            {
                fade.holdOrigin = fade.nowOrigin;
                fade.holdProj = fade.nowProj;
                fade.nowOrigin = viewOrigin;
                fade.nowProj = matrixProj;
            }
            var cullLodJob = new TreeCullLodJob();
            {
                cullLodJob.heightRes = m_Heights.IsCreated && (occlusionMode & TreeOcclusionMode.Terrain) != 0 ? m_HeightRes : 0;
                cullLodJob.maxDistance = cullDistance;
                cullLodJob.viewOrigin = viewOrigin;
                cullLodJob.lodHoldOrigin = fade.holdOrigin;
                cullLodJob.lodNowOrigin = fade.nowOrigin;
                cullLodJob.lodHoldProj = fade.holdProj;
                cullLodJob.lodNowProj = fade.nowProj;
                cullLodJob.lodHysteresis = discontinuous ? 0f : math.clamp(lodHysteresis, 0f, 0.5f);
                cullLodJob.terrainPos = m_TerrainPos;
                cullLodJob.terrainSize = m_TerrainSize;
                cullLodJob.cellVisible = boundSector.visibleMap;
                cullLodJob.cellBounds = boundSector.nativeSections;
                cullLodJob.instanceCell = m_InstanceCell;
                cullLodJob.bounds = m_Bounds;
                cullLodJob.lodScreenSizes = m_LodScreenSizes;
                cullLodJob.heights = m_Heights;
                cullLodJob.planes = planes;
                cullLodJob.chunkMasks = m_ChunkMasks;
                cullLodJob.lodHold = fade.lodHold;
                cullLodJob.lodStable = fade.lodStable;
                cullLodJob.lodNow = m_LodNow;
            }
            m_CullHandle = cullLodJob.Schedule(m_ChunkMasks.Length, 4);
            taskHandles.Add(m_CullHandle);
            m_PendingVisibility = true;
            fade.lastOrigin = viewOrigin;
            fade.lastProj = matrixProj;
            fade.hasLast = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushPendingUploads(CommandBuffer cmdBuffer, RTHandle cameraDepth, in Vector4 zParams, in Matrix4x4 depthViewProj,
            in bool enableHzb, in float duration, in float deltaTime)
        {
            if (!m_PendingVisibility || m_InstanceCount <= 0 || m_ActiveFade == null) { return; }
            UpdateFade(duration, deltaTime);

            JobHandle emitHandle = default;
            bool scheduled = false;
            int dither = (m_AllowFade && m_DitherEnabled && m_ActiveFade.fading) ? 1 : 0;
            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                var emitJob = new TreeEmitLodMasksJob();
                {
                    emitJob.meshIndex = batch.meshIndex;
                    emitJob.ditherEnabled = dither;
                    emitJob.instanceCount = m_InstanceCount;
                    emitJob.chunkMasks = m_ChunkMasks;
                    emitJob.lodHold = m_ActiveFade.lodHold;
                    emitJob.lodNow = m_LodNow;
                    emitJob.lodDither = m_LodDither;
                    emitJob.stableMask = batch.stableMask;
                    emitJob.fadeOutMask = batch.fadeOutMask;
                    emitJob.fadeInMask = batch.fadeInMask;
                    emitJob.bucketCounts = batch.bucketCounts;
                }
                JobHandle handle = emitJob.Schedule();
                emitHandle = scheduled ? JobHandle.CombineDependencies(emitHandle, handle) : handle;
                scheduled = true;
            }
            if (scheduled) { emitHandle.Complete(); }

            int gpuReady = (m_Gpu != null && m_Gpu.IsReady) ? 1 : 0;
            if (gpuReady != 0)
            {
                m_Gpu.BuildHzb(cmdBuffer, m_ActiveCamera, cameraDepth, zParams, depthViewProj, enableHzb);
                m_Gpu.CullCells(boundSector.visibleMap, m_PlaneScratch, m_ActiveCamera, m_DrawDistance);
            }

            int chunkCount = m_ChunkMasks.Length;
            for (int i = 0; i < m_Batches.Count; ++i)
            {
                LowerBatch(cmdBuffer, m_Batches[i], gpuReady, chunkCount);
            }
            m_PendingVisibility = false;
        }

        void LowerBatch(CommandBuffer cmdBuffer, TreeLodBatch batch, in int gpuReady, in int chunkCount)
        {
            LowerBucket(cmdBuffer, batch, 0, batch.stableMask, gpuReady, chunkCount);
            LowerBucket(cmdBuffer, batch, 1, batch.fadeOutMask, gpuReady, chunkCount);
            LowerBucket(cmdBuffer, batch, 2, batch.fadeInMask, gpuReady, chunkCount);
        }

        void LowerBucket(CommandBuffer cmdBuffer, TreeLodBatch batch, in int bucket, NativeArray<ulong> masks, in int gpuReady, in int chunkCount)
        {
            int visible = batch.bucketCounts[bucket];
            batch.uploadedCount[bucket] = visible;
            batch.gpuArgs[bucket] = false;
            if (visible <= 0) { return; }

            for (int i = 0; i < chunkCount; ++i)
            {
                m_MaskScratch[i] = masks[i];
            }

            int runCount = FoliageLogic.EncodeRunsFromMasks(m_MaskScratch, chunkCount, m_InstanceCount, m_RunScratch);
            int codec = FoliageLogic.PickVisibilityCodec(visible, chunkCount, runCount, gpuReady);
            ComputeBuffer indexBuffer = batch.indexBuffers[bucket];
            bool useGpu = gpuReady != 0;
            bool useHzb = useGpu && m_Gpu.HasHzb;

            if (!useGpu || (codec == (int)VisibilityCodec.CompactIndex && !useHzb))
            {
                int written = FoliageLogic.ExpandMasksToIndex(m_MaskScratch, chunkCount, m_InstanceCount, m_IndexScratch);
                cmdBuffer.SetBufferData(indexBuffer, m_IndexScratch, 0, 0, written);
                WriteCpuArgs(cmdBuffer, batch, bucket, written);
                return;
            }

            ResetGpuArgs(cmdBuffer, batch, bucket);
            m_Gpu.BindCommon(indexBuffer, FirstArgs(batch, bucket), m_ActiveCamera, m_DrawDistance);

            if (codec == (int)VisibilityCodec.BitMaskTransfer)
            {
                m_Gpu.CullChunks(m_MaskScratch, chunkCount, m_InstanceCount);
                m_Gpu.ExpandMask(m_MaskScratch, chunkCount, m_InstanceCount, indexBuffer, FirstArgs(batch, bucket), 1);
            }
            else if (codec == (int)VisibilityCodec.RunTransfer)
            {
                m_Gpu.ExpandRun(m_RunScratch, runCount, indexBuffer, FirstArgs(batch, bucket));
            }
            else
            {
                int written = FoliageLogic.ExpandMasksToIndex(m_MaskScratch, chunkCount, m_InstanceCount, m_IndexScratch);
                m_Gpu.FilterIndex(m_IndexScratch, written, indexBuffer, FirstArgs(batch, bucket));
            }

            CopyGpuArgs(batch, bucket);
            batch.gpuArgs[bucket] = true;
        }

        ComputeBuffer FirstArgs(TreeLodBatch batch, in int bucket)
        {
            return batch.argsBuffers[bucket * batch.sectionIndexs.Length];
        }

        void ResetGpuArgs(CommandBuffer cmdBuffer, TreeLodBatch batch, in int bucket)
        {
            Mesh mesh = tree.meshes[batch.meshIndex];
            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int submesh = batch.sectionIndexs[s];
                ComputeBuffer argsBuffer = batch.argsBuffers[(bucket * batch.sectionIndexs.Length) + s];
                m_ArgsScratch[0] = mesh.GetIndexCount(submesh);
                m_ArgsScratch[1] = 0;
                m_ArgsScratch[2] = mesh.GetIndexStart(submesh);
                m_ArgsScratch[3] = mesh.GetBaseVertex(submesh);
                m_ArgsScratch[4] = 0;
                cmdBuffer.SetBufferData(argsBuffer, m_ArgsScratch);
            }
        }

        void WriteCpuArgs(CommandBuffer cmdBuffer, TreeLodBatch batch, in int bucket, in int instanceCount)
        {
            Mesh mesh = tree.meshes[batch.meshIndex];
            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int submesh = batch.sectionIndexs[s];
                ComputeBuffer argsBuffer = batch.argsBuffers[(bucket * batch.sectionIndexs.Length) + s];
                m_ArgsScratch[0] = mesh.GetIndexCount(submesh);
                m_ArgsScratch[1] = (uint)instanceCount;
                m_ArgsScratch[2] = mesh.GetIndexStart(submesh);
                m_ArgsScratch[3] = mesh.GetBaseVertex(submesh);
                m_ArgsScratch[4] = 0;
                cmdBuffer.SetBufferData(argsBuffer, m_ArgsScratch);
            }
        }

        void CopyGpuArgs(TreeLodBatch batch, in int bucket)
        {
            ComputeBuffer src = FirstArgs(batch, bucket);
            for (int s = 1; s < batch.sectionIndexs.Length; ++s)
            {
                m_Gpu.CopyArgsInstanceCount(src, batch.argsBuffers[(bucket * batch.sectionIndexs.Length) + s]);
            }
        }

        void UpdateFade(float duration, float deltaTime)
        {
            TreeCameraFade fade = m_ActiveFade;
            if (fade.hardCut)
            {
                CommitLod(fade);
                fade.hardCut = false;
                fade.lastAdvancedFrame = Time.frameCount;
                return;
            }
            if (fade.lastAdvancedFrame == Time.frameCount) { return; }
            fade.lastAdvancedFrame = Time.frameCount;
            if (fade.fading)
            {
                fade.alpha = math.min(1f, fade.alpha + (deltaTime / math.max(duration, 0.0001f)));
                if (fade.alpha >= 1f)
                {
                    CommitLod(fade);
                }
                return;
            }

            bool startFade = false;
            for (int i = 0; i < m_InstanceCount; ++i)
            {
                int now = m_LodNow[i];
                if (now < 0) { continue; }
                int hold = fade.lodHold[i];
                if (hold < 0) { continue; }
                int delta = hold - now;
                if (delta < 0) { delta = -delta; }
                if (delta == 1 && m_DitherEnabled && m_LodDither[hold] != 0 && m_LodDither[now] != 0)
                {
                    startFade = true;
                    break;
                }
            }

            if (startFade)
            {
                fade.fading = true;
                fade.alpha = 0f;
            }
            else
            {
                CommitLod(fade);
            }
        }

        void CommitLod(TreeCameraFade fade)
        {
            for (int i = 0; i < m_InstanceCount; ++i)
            {
                if (m_LodNow[i] >= 0) { fade.lodStable[i] = m_LodNow[i]; }
            }
            fade.holdOrigin = fade.nowOrigin;
            fade.holdProj = fade.nowProj;
            fade.fading = false;
            fade.alpha = 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            if (m_InstanceCount <= 0 || m_Batches == null) { return; }

            Bounds worldBounds = hasPackedBound ? (Bounds)packedBound : tree.boundBox;
            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                Mesh mesh = tree.meshes[batch.meshIndex];
                DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.Stable, 0f, 0f, worldBounds);
                if (m_ActiveFade != null && m_ActiveFade.fading && m_DitherEnabled && m_AllowFade)
                {
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeOut, 1f - m_ActiveFade.alpha, 1f, worldBounds);
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeIn, m_ActiveFade.alpha - 1f, 1f, worldBounds);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DrawBucket(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock, TreeLodBatch batch, Mesh mesh, in int bucket, in float lodFactor, in float fadeEnable, Bounds worldBounds)
        {
            int argsIndex = bucket == (int)LodBucket.FadeOut ? 1 : bucket == (int)LodBucket.FadeIn ? 2 : 0;
            if (!batch.gpuArgs[argsIndex] && batch.uploadedCount[argsIndex] <= 0) { return; }

            ComputeBuffer indexBuffer = batch.indexBuffers[argsIndex];
            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int submesh = batch.sectionIndexs[s];
                Material material = tree.materials[batch.materialIndexs[s]];
                ComputeBuffer argsBuffer = batch.argsBuffers[(argsIndex * batch.sectionIndexs.Length) + s];
                propertyBlock.Clear();
                FoliageAmbientSH.Bind(propertyBlock);
                propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, indexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, m_MatrixBuffer);
                propertyBlock.SetFloat(TreeShaderID.LodFactor, lodFactor);
                propertyBlock.SetFloat(TreeShaderID.LodFadeEnable, fadeEnable);
                cmdBuffer.DrawMeshInstancedIndirect(mesh, submesh, material, passIndex, argsBuffer, 0, propertyBlock);
            }
        }

#if UNITY_EDITOR
        public void CaptureVisibilitySnapshot(CommandBuffer cmdBuffer, Camera camera)
        {
            if (m_InstanceCount <= 0 || m_ActiveCamera != camera || !m_ChunkMasks.IsCreated) { return; }
            TreeVisibilitySnapshot snapshot = new TreeVisibilitySnapshot();
            snapshot.camera = camera;
            snapshot.frame = Time.frameCount;
            snapshot.hzb = m_Gpu != null && m_Gpu.HasHzb;
            snapshot.coarseCells = boundSector.visibleMap.ToArray();
            snapshot.terrainCells = new byte[snapshot.coarseCells.Length];
            snapshot.cpuMasks = m_ChunkMasks.ToArray();
            snapshot.lods = m_LodNow.ToArray();
            snapshot.finalVisible = new bool[m_InstanceCount];

            var terrainTest = new TreeCullLodJob();
            {
                terrainTest.heightRes = (m_OcclusionMode & TreeOcclusionMode.Terrain) != 0 ? m_HeightRes : 0;
                terrainTest.heights = m_Heights;
                terrainTest.viewOrigin = camera.transform.position;
                terrainTest.terrainPos = m_TerrainPos;
                terrainTest.terrainSize = m_TerrainSize;
            }
            for (int i = 0; i < snapshot.terrainCells.Length; ++i)
            {
                if (snapshot.coarseCells[i] == 0) { continue; }
                bool terrainOccluded = terrainTest.heightRes > 1 &&
                    terrainTest.TerrainOccludes(boundSector.nativeSections[i].boundBox) != 0;
                snapshot.terrainCells[i] = terrainOccluded ? (byte)0 : (byte)1;
            }

            if (m_Gpu != null && m_Gpu.IsReady)
            {
                ++snapshot.pending;
                if (!m_Gpu.RequestCellReadback(cmdBuffer, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.gpuCells = request.GetData<uint>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                })) { --snapshot.pending; }
            }

            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                for (int bucketIndex = 0; bucketIndex < 3; ++bucketIndex)
                {
                    int upperBound = batch.uploadedCount[bucketIndex];
                    if (upperBound <= 0) { continue; }
                    TreeVisibilitySnapshot.Bucket captured = new TreeVisibilitySnapshot.Bucket();
                    snapshot.buckets.Add(captured);
                    snapshot.pending += 2;
                    cmdBuffer.RequestAsyncReadback(batch.argsBuffers[bucketIndex * batch.sectionIndexs.Length], request =>
                    {
                        try
                        {
                            if (request.hasError) { snapshot.failed = true; }
                            else { captured.args = request.GetData<uint>().ToArray(); }
                        }
                        catch (Exception) { snapshot.failed = true; }
                        finally { snapshot.FinishRequest(); }
                    });
                    cmdBuffer.RequestAsyncReadback(batch.indexBuffers[bucketIndex], upperBound * sizeof(int), 0, request =>
                    {
                        try
                        {
                            if (request.hasError) { snapshot.failed = true; }
                            else { captured.indices = request.GetData<int>().ToArray(); }
                        }
                        catch (Exception) { snapshot.failed = true; }
                        finally { snapshot.FinishRequest(); }
                    });
                }
            }
            if (snapshot.pending == 0) { snapshot.complete = true; }
            m_DebugSnapshot = snapshot;
        }

        public void ClearVisibilitySnapshot()
        {
            m_DebugSnapshot = null;
        }

        public string VisibilitySnapshotSummary()
        {
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot;
            if (snapshot == null) { return "No frozen GPU snapshot."; }
            string source = snapshot.camera == null ? "Destroyed camera" : snapshot.camera.name;
            if (!snapshot.complete) { return source + " frame " + snapshot.frame + ": GPU readback pending."; }
            if (snapshot.failed) { return source + " frame " + snapshot.frame + ": GPU readback failed."; }
            int cpuCount = 0;
            for (int i = 0; i < snapshot.cpuMasks.Length; ++i) { cpuCount += FoliageLogic.PopCount(snapshot.cpuMasks[i]); }
            return source + " frame " + snapshot.frame + ": CPU candidates " + cpuCount +
                ", final GPU indices " + snapshot.FinalCount() + ", HZB " + (snapshot.hzb ? "on" : "off") + ".";
        }

        public string VisibilitySnapshotCandidate(in int index)
        {
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot;
            if (snapshot == null || !snapshot.complete || snapshot.failed) { return "Candidate snapshot is unavailable."; }
            if (index < 0 || index >= snapshot.finalVisible.Length) { return "Candidate index is outside this tree stream."; }
            int cell = m_InstanceCell[index];
            bool cpu = (snapshot.cpuMasks[index >> 6] & (1UL << (index & 63))) != 0;
            string gpuCell = snapshot.gpuCells == null ? "unknown" : (snapshot.gpuCells[cell] != 0 ? "visible" : "rejected");
            return "Candidate " + index + ", cell " + cell + ": coarse " + snapshot.coarseCells[cell] +
                ", terrain " + snapshot.terrainCells[cell] + ", CPU instance " + (cpu ? "visible" : "rejected") +
                ", GPU cell " + gpuCell + ", final index " + (snapshot.finalVisible[index] ? "present" : "absent") +
                ", LOD " + snapshot.lods[index] + ".";
        }

        public string VisibilitySnapshotGpuRejections(in int limit)
        {
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot;
            if (snapshot == null || !snapshot.complete || snapshot.failed) { return "GPU rejections unavailable."; }
            var text = new System.Text.StringBuilder();
            int total = 0;
            for (int i = 0; i < snapshot.finalVisible.Length; ++i)
            {
                bool cpu = (snapshot.cpuMasks[i >> 6] & (1UL << (i & 63))) != 0;
                if (!cpu || snapshot.finalVisible[i]) { continue; }
                ++total;
                if (total > limit) { continue; }
                int cell = m_InstanceCell[i];
                float3 center = m_Bounds[i].center;
                text.Append(" #").Append(i).Append(" cell ").Append(cell)
                    .Append(" gpuCell ").Append(snapshot.gpuCells == null ? -1 : (int)snapshot.gpuCells[cell])
                    .Append(" at (").Append(center.x.ToString("F1")).Append(',')
                    .Append(center.y.ToString("F1")).Append(',').Append(center.z.ToString("F1")).Append(')');
            }
            return total + " CPU-visible candidates absent from final index:" + text;
        }
#endif

        TreeShadowView GetShadowView(Camera camera)
        {
            m_DeadShadowViews.Clear();
            foreach (var pair in m_ShadowViews)
            {
                if (pair.Value.camera == null) { m_DeadShadowViews.Add(pair.Key); }
            }
            for (int i = 0; i < m_DeadShadowViews.Count; ++i)
            {
                ulong deadKey = m_DeadShadowViews[i];
                m_ShadowViews[deadKey].Dispose();
                m_ShadowViews.Remove(deadKey);
            }
            ulong key = camera != null ? EntityId.ToULong(camera.GetEntityId()) : 0;
            if (m_ShadowViews.TryGetValue(key, out TreeShadowView view)) { return view; }
            view = new TreeShadowView();
            view.Initialize(camera, m_Batches, m_InstanceCount);
            m_ShadowViews.Add(key, view);
            return view;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsShadowVisible(in Aabb box, Plane[] planes)
        {
            for (int i = 0; i < planes.Length; ++i)
            {
                float3 normal = planes[i].normal;
                float distance = math.dot(normal, box.center) + planes[i].distance;
                float radius = math.dot(math.abs(normal), box.extents);
                if (distance + radius < 0f) { return false; }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchShadow(CommandBuffer cmdBuffer, Camera camera, in int cascadeIndex, Plane[] planes,
            in float lodHysteresis, MaterialPropertyBlock propertyBlock)
        {
            if (m_InstanceCount <= 0 || m_Batches == null || m_ShadowViews == null || cascadeIndex < 0 || cascadeIndex >= 4) { return; }
            TreeShadowView view = GetShadowView(camera);
            Array.Clear(m_ShadowCounts, 0, m_ShadowCounts.Length);
            float3 cameraOrigin = camera.transform.position;
            float4x4 projection = Geometry.GetLodProjectionMatrix(camera);

            for (int cellIndex = 0; cellIndex < cells.Length; ++cellIndex)
            {
                TreeCell cell = cells[cellIndex];
                if (cell.count <= 0 || !IsShadowVisible(boundSector.sections[cellIndex].boundBox, planes)) { continue; }
                int end = cell.offset + cell.count;
                for (int candidate = cell.offset; candidate < end; ++candidate)
                {
                    Aabb box = m_Bounds[candidate];
                    if (!IsShadowVisible(box, planes)) { continue; }
                    float radius = math.cmax(math.abs(box.extents));
                    float screenRadiusSqr = Geometry.ComputeBoundsScreenRadiusSquared(radius, box.center, cameraOrigin, projection);
                    int lod = FoliageLogic.ComputeLodIndexHysteresis(screenRadiusSqr, m_LodScreenSizes,
                        view.lodStable[candidate], math.clamp(lodHysteresis, 0f, 0.5f));
                    view.lodStable[candidate] = lod;
                    m_ShadowIndexScratch[lod][m_ShadowCounts[lod]++] = candidate;
                }
            }

            for (int lod = 0; lod < m_Batches.Count; ++lod)
            {
                int count = m_ShadowCounts[lod];
                if (count <= 0) { continue; }
                TreeShadowBatch shadowBatch = view.cascades[cascadeIndex][lod];
                if (shadowBatch == null)
                {
                    shadowBatch = new TreeShadowBatch();
                    shadowBatch.Initialize(m_Batches[lod].sectionIndexs.Length);
                    view.cascades[cascadeIndex][lod] = shadowBatch;
                }
                shadowBatch.EnsureCapacity(count);
                cmdBuffer.SetBufferData(shadowBatch.indexBuffer, m_ShadowIndexScratch[lod], 0, 0, count);
                TreeLodBatch batch = m_Batches[lod];
                Mesh mesh = tree.meshes[batch.meshIndex];
                for (int submeshIndex = 0; submeshIndex < batch.sectionIndexs.Length; ++submeshIndex)
                {
                    int submesh = batch.sectionIndexs[submeshIndex];
                    Material material = tree.materials[batch.materialIndexs[submeshIndex]];
                    if (material == null) { continue; }
                    int passIndex = material.FindPass("FoliageShadow");
                    if (passIndex < 0) { continue; }
                    m_ArgsScratch[0] = mesh.GetIndexCount(submesh);
                    m_ArgsScratch[1] = (uint)count;
                    m_ArgsScratch[2] = mesh.GetIndexStart(submesh);
                    m_ArgsScratch[3] = mesh.GetBaseVertex(submesh);
                    m_ArgsScratch[4] = 0;
                    ComputeBuffer argsBuffer = shadowBatch.argsBuffers[submeshIndex];
                    cmdBuffer.SetBufferData(argsBuffer, m_ArgsScratch);
                    propertyBlock.Clear();
                    propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, shadowBatch.indexBuffer);
                    propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, m_MatrixBuffer);
                    propertyBlock.SetFloat(TreeShaderID.LodFadeEnable, 0f);
                    cmdBuffer.DrawMeshInstancedIndirect(mesh, submesh, material, passIndex, argsBuffer, 0, propertyBlock);
                }
            }
        }

        public void Release()
        {
            m_CullHandle.Complete();
            m_ViewHandle.Complete();
            transforms = null;
            if (boundSector != null) { boundSector.ReleaseNativeCollection(); }
            if (m_Matrices.IsCreated) { m_Matrices.Dispose(); }
            if (m_Bounds.IsCreated) { m_Bounds.Dispose(); }
            if (m_InstanceCell.IsCreated) { m_InstanceCell.Dispose(); }
            if (m_LodScreenSizes.IsCreated) { m_LodScreenSizes.Dispose(); }
            if (m_LodDither.IsCreated) { m_LodDither.Dispose(); }
            if (m_ChunkMasks.IsCreated) { m_ChunkMasks.Dispose(); }
            if (m_LodNow.IsCreated) { m_LodNow.Dispose(); }
            if (m_Heights.IsCreated) { m_Heights.Dispose(); }
            if (m_MatrixBuffer != null) { m_MatrixBuffer.Dispose(); m_MatrixBuffer = null; }
            if (m_Gpu != null) { m_Gpu.Release(); m_Gpu = null; }
            if (m_Batches != null)
            {
                for (int i = 0; i < m_Batches.Count; ++i) { m_Batches[i].Dispose(); }
                m_Batches = null;
            }
            if (m_FadeViews != null)
            {
                foreach (var pair in m_FadeViews) { pair.Value.Dispose(); }
                m_FadeViews.Clear();
                m_FadeViews = null;
            }
            m_DeadFadeViews = null;
            if (m_ShadowViews != null)
            {
                foreach (var pair in m_ShadowViews) { pair.Value.Dispose(); }
                m_ShadowViews.Clear();
                m_ShadowViews = null;
            }
            m_DeadShadowViews = null;
            m_ActiveFade = null;
            m_ActiveCamera = null;
            m_ArgsScratch = null;
            m_IndexScratch = null;
            m_ShadowIndexScratch = null;
            m_ShadowCounts = null;
            m_MaskScratch = null;
            m_RunScratch = null;
            m_PlaneScratch = null;
            m_InstanceCount = 0;
            m_PendingVisibility = false;
#if UNITY_EDITOR
            m_DebugSnapshot = null;
#endif
        }

#if UNITY_EDITOR
        public void DrawBounds(in TreeBoundsMode mode, Camera debugCamera)
        {
            if (Application.isPlaying == false || !m_Bounds.IsCreated) { return; }
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot != null && m_DebugSnapshot.complete && !m_DebugSnapshot.failed ? m_DebugSnapshot : null;
            if (debugCamera != null && (snapshot == null ? m_ActiveCamera != debugCamera : snapshot.camera != debugCamera)) { return; }
            if (mode == TreeBoundsMode.Cells)
            {
                if (boundSector == null || boundSector.sections == null) { return; }
                for (int i = 0; i < boundSector.sections.Length; ++i)
                {
                    if (cells == null || cells[i].count <= 0) { continue; }
                    Color color;
                    if (snapshot != null)
                    {
                        color = snapshot.coarseCells[i] == 0 ? Color.red :
                            snapshot.terrainCells[i] == 0 ? Color.magenta :
                            snapshot.gpuCells != null && snapshot.gpuCells[i] == 0 ? Color.cyan : Color.green;
                    }
                    else
                    {
                        bool visible = boundSector.visibleMap.IsCreated && boundSector.visibleMap[i] != 0;
                        color = visible ? Color.green : Color.red;
                    }
                    Geometry.DrawBound(boundSector.sections[i].boundBox, color);
                }
                return;
            }
            for (int i = 0; i < m_Bounds.Length; ++i)
            {
                int chunk = i >> 6;
                int bit = i & 63;
                bool visible = snapshot == null ? m_ChunkMasks.IsCreated && (m_ChunkMasks[chunk] & (1UL << bit)) != 0 :
                    (snapshot.cpuMasks[chunk] & (1UL << bit)) != 0;
                int lod = snapshot == null ? (m_LodNow.IsCreated ? math.max(m_LodNow[i], 0) : 0) : math.max(snapshot.lods[i], 0);
                int cell = m_InstanceCell[i];
                Color color = !visible ? snapshot != null && snapshot.coarseCells[cell] != 0 &&
                    snapshot.terrainCells[cell] == 0 ? Color.magenta : Color.red :
                    snapshot != null && !snapshot.finalVisible[i] ? Color.cyan :
                    Geometry.LODColors[math.min(lod, Geometry.LODColors.Length - 1)];
                Geometry.DrawBound(m_Bounds[i], color);
            }
        }
#endif
    }
}
