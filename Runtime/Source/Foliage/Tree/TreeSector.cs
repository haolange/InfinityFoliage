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
    internal struct TreeViewSettings
    {
        public float cullDistance;
        public float lodHysteresis;
        public float fadeWidth;
        public float fadeDuration;
        public float deltaTime;
        public bool allowFade;
        public TreeLodFadeMode fadeMode;
        public TreeOcclusionMode occlusion;
    }

    internal class TreeLodBatch
    {
        public int meshIndex;
        public int[] sectionIndexs;
        public int[] materialIndexs;
        public int[] forwardPasses;
        public int[] depthPasses;
        public int[] shadowPasses;
        public bool[] alphaClips;
        public uint[] argsTemplate;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PassIndex(in FoliageDrawPass pass, in int submesh, in int bucket)
        {
            if (pass == FoliageDrawPass.Forward) { return forwardPasses[submesh]; }
            if (pass == FoliageDrawPass.Shadow) { return shadowPasses[submesh]; }
            return alphaClips[submesh] || bucket != 0 ? depthPasses[submesh] : -1;
        }
    }

    internal class TreeLodPack
    {
        public int bucketCount;
        public int submeshCount;
        public int capacity;
        public int[] counts;
        public uint[] args;
        public ComputeBuffer argsBuffer;
        public ComputeBuffer[] indexBuffers;
        public ComputeBuffer[] weightBuffers;

        public void Initialize(TreeLodBatch batch, in int buckets)
        {
            bucketCount = buckets;
            submeshCount = batch.sectionIndexs.Length;
            counts = new int[bucketCount];
            indexBuffers = new ComputeBuffer[bucketCount];
            weightBuffers = new ComputeBuffer[bucketCount - 1];
            args = new uint[5 * bucketCount * submeshCount];
            for (int bucket = 0; bucket < bucketCount; ++bucket)
            {
                for (int submesh = 0; submesh < submeshCount; ++submesh)
                {
                    Array.Copy(batch.argsTemplate, submesh * 5, args, ((bucket * submeshCount) + submesh) * 5, 5);
                }
            }
            argsBuffer = new ComputeBuffer(args.Length, sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);
            EnsureCapacity(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ArgsOffset(in int bucket, in int submesh)
        {
            return ((bucket * submeshCount) + submesh) * 5 * sizeof(uint);
        }

        public void EnsureCapacity(in int required)
        {
            if (capacity >= required) { return; }
            int nextCapacity = math.max(64, math.ceilpow2(required));
            for (int i = 0; i < indexBuffers.Length; ++i)
            {
                ComputeBuffer next = new ComputeBuffer(nextCapacity, sizeof(int));
                indexBuffers[i]?.Dispose();
                indexBuffers[i] = next;
            }
            for (int i = 0; i < weightBuffers.Length; ++i)
            {
                ComputeBuffer next = new ComputeBuffer(nextCapacity, sizeof(float));
                weightBuffers[i]?.Dispose();
                weightBuffers[i] = next;
            }
            capacity = nextCapacity;
        }

        public void WriteCounts(CommandBuffer cmdBuffer)
        {
            for (int bucket = 0; bucket < bucketCount; ++bucket)
            {
                for (int submesh = 0; submesh < submeshCount; ++submesh)
                {
                    args[(((bucket * submeshCount) + submesh) * 5) + 1] = (uint)counts[bucket];
                }
            }
            cmdBuffer.SetBufferData(argsBuffer, args);
        }

        public void Dispose()
        {
            argsBuffer?.Dispose();
            argsBuffer = null;
            if (indexBuffers != null)
            {
                for (int i = 0; i < indexBuffers.Length; ++i) { indexBuffers[i]?.Dispose(); }
            }
            if (weightBuffers != null)
            {
                for (int i = 0; i < weightBuffers.Length; ++i) { weightBuffers[i]?.Dispose(); }
            }
            capacity = 0;
        }
    }

    internal class TreeView
    {
        public Camera camera;
        public int cascade;
        public TreeLodPack[] packs;
        public TreeGpuView gpu;
        public NativeArray<int> lodHold;
        public NativeArray<int> lodStable;
        public NativeArray<float> fadeState;
        public float3 holdOrigin;
        public float3 nowOrigin;
        public float3 lastOrigin;
        public float4x4 holdProj;
        public float4x4 nowProj;
        public float4x4 lastProj;
        public TreeLodFadeMode fadeMode;
        public bool allowFade;
        public bool hasMode;
        public bool hasLast;
        public bool distanceReset;
        public int advancedFrame = -1;
        public int usedFrame;
        public int culledFrame = -1;
        public int packedFrame = -1;

        public bool isShadow
        {
            get { return cascade >= 0; }
        }

        public void Initialize(Camera viewCamera, in int viewCascade, List<TreeLodBatch> batches)
        {
            camera = viewCamera;
            cascade = viewCascade;
            int bucketCount = isShadow ? 1 : FoliageLogic.LodBucketCount;
            packs = new TreeLodPack[batches.Count];
            try
            {
                for (int i = 0; i < packs.Length; ++i)
                {
                    packs[i] = new TreeLodPack();
                    packs[i].Initialize(batches[i], bucketCount);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void EnsureCpu(in int instanceCount)
        {
            if (lodStable.IsCreated) { return; }
            lodStable = new NativeArray<int>(instanceCount, Allocator.Persistent);
            if (!isShadow)
            {
                lodHold = new NativeArray<int>(instanceCount, Allocator.Persistent);
                fadeState = new NativeArray<float>(3, Allocator.Persistent);
                fadeState[0] = 1f;
            }
            ResetLods();
        }

        public void EnsureGpu(in int instanceCount, in int cellCount)
        {
            if (gpu != null) { return; }
            TreeGpuView next = new TreeGpuView();
            next.Initialize(instanceCount, cellCount);
            gpu = next;
        }

        public void ResetLods()
        {
            for (int i = 0; i < lodStable.Length; ++i) { lodStable[i] = -1; }
            if (!lodHold.IsCreated) { return; }
            for (int i = 0; i < lodHold.Length; ++i) { lodHold[i] = -1; }
        }

        public void Dispose()
        {
            if (packs != null)
            {
                for (int i = 0; i < packs.Length; ++i) { packs[i]?.Dispose(); }
                packs = null;
            }
            gpu?.Dispose();
            gpu = null;
            if (lodHold.IsCreated) { lodHold.Dispose(); }
            if (lodStable.IsCreated) { lodStable.Dispose(); }
            if (fadeState.IsCreated) { fadeState.Dispose(); }
        }
    }

#if UNITY_EDITOR
    internal class TreeVisibilitySnapshot
    {
        internal class Bucket
        {
            internal int lod;
            internal int bucket;
            internal int argsOffset;
            internal bool weighted;
            internal uint[] args;
            internal int[] indices;
            internal float[] weights;

            internal int Count()
            {
                return math.min((int)args[argsOffset + 1], indices.Length);
            }
        }

        internal Camera camera;
        internal int frame;
        internal bool hzb;
        internal TreeVisibilityBackend requestedBackend;
        internal TreeVisibilityBackend actualBackend;
        internal float4[] fadeState;
        internal int4[] gpuLods;
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
        internal int weightPairs;
        internal int invalidWeights;

        internal void FinishRequest()
        {
            --pending;
            if (pending > 0) { return; }
            complete = true;
            if (failed) { return; }
            if (actualBackend == TreeVisibilityBackend.GPU)
            {
                if (gpuCells == null || gpuLods == null || fadeState == null) { failed = true; return; }
                for (int i = 0; i < coarseCells.Length; ++i)
                {
                    coarseCells[i] = (byte)((gpuCells[i] & 1) != 0 ? 1 : 0);
                    terrainCells[i] = (byte)((gpuCells[i] & 2) != 0 ? 1 : 0);
                }
                for (int i = 0; i < lods.Length; ++i) { lods[i] = gpuLods[i].y; }
            }
            for (int b = 0; b < buckets.Count; ++b)
            {
                Bucket bucket = buckets[b];
                if (bucket.args == null || bucket.indices == null || bucket.weighted && bucket.weights == null)
                {
                    failed = true;
                    return;
                }
                int count = bucket.Count();
                if (bucket.weighted && bucket.weights.Length < count) { failed = true; return; }
                for (int i = 0; i < count; ++i)
                {
                    int index = bucket.indices[i];
                    if (index >= 0 && index < finalVisible.Length) { finalVisible[index] = true; }
                    if (!bucket.weighted) { continue; }
                    ++weightPairs;
                    float weight = bucket.weights[i];
                    if (float.IsNaN(weight) || weight < 0f || weight > 1f) { ++invalidWeights; }
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

    [Serializable]
    public class TreeSector
    {
        const int ViewRetireFrames = 120;

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
        private NativeArray<TreeCell> m_Cells;
        private NativeArray<float> m_LodScreenSizes;
        private NativeArray<byte> m_LodDither;
        private NativeArray<float> m_Heights;
        private NativeArray<byte> m_CellFlags;
        private NativeArray<ulong> m_ChunkMasks;
        private NativeArray<int> m_LodNow;
        private NativeArray<float> m_LodScreenSqr;
        private NativeArray<float> m_LodWeight;
        private NativeArray<ulong> m_BucketMasks;
        private NativeArray<int> m_BucketCounts;
        private NativeArray<int2> m_Ranges;
        private NativeArray<int> m_IndexScratch;
        private NativeArray<float> m_WeightScratch;
        private List<TreeLodBatch> m_Batches;
        private List<TreeView> m_Views;
        private TreeView m_ScratchView;
        private TreeVisibilityGpu m_Gpu;
        private Camera m_ActiveCamera;
        private Vector4[] m_PlaneScratch;
        private TreeVisibilityBackend m_RequestedBackend;
        private TreeVisibilityBackend m_ActualBackend = TreeVisibilityBackend.CPU;
        private string m_BackendReason = "CPU selected";
        private string m_LastBackendWarning;
        private TreeOcclusionMode m_OcclusionMode = TreeOcclusionMode.TerrainAndHzb;
        private bool m_DitherEnabled;
        private int m_InstanceCount;
        private int m_ChunkCount;
        private int m_HeightRes;
        private int m_PrunedFrame = -1;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
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
            m_ChunkCount = FoliageLogic.VisibilityChunkCount(n);
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
            m_LodScreenSizes = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
            m_LodDither = new NativeArray<byte>(tree.lODInfos.Length, Allocator.Persistent);

            m_Batches = new List<TreeLodBatch>(tree.lODInfos.Length);
            m_DitherEnabled = false;
            for (int i = 0; i < tree.lODInfos.Length; ++i)
            {
                Mesh mesh = tree.meshes[i];
                ref MeshLodInfo meshLodInfo = ref tree.lODInfos[i];
                m_LodScreenSizes[i] = meshLodInfo.screenSize;
                m_LodDither[i] = 1;

                int submeshCount = mesh.subMeshCount;
                TreeLodBatch batch = new TreeLodBatch();
                batch.meshIndex = i;
                batch.sectionIndexs = new int[submeshCount];
                batch.materialIndexs = new int[submeshCount];
                batch.forwardPasses = new int[submeshCount];
                batch.depthPasses = new int[submeshCount];
                batch.shadowPasses = new int[submeshCount];
                batch.alphaClips = new bool[submeshCount];
                batch.argsTemplate = new uint[5 * submeshCount];
                for (int j = 0; j < submeshCount; ++j)
                {
                    Material material = tree.materials[meshLodInfo.materialSlot[j]];
                    batch.sectionIndexs[j] = j;
                    batch.materialIndexs[j] = meshLodInfo.materialSlot[j];
                    batch.forwardPasses[j] = material.FindPass("ForwardLit-Instance");
                    batch.depthPasses[j] = material.FindPass("FoliageDepth");
                    batch.shadowPasses[j] = material.FindPass("FoliageShadow");
                    batch.alphaClips[j] = material.GetTag("RenderType", false) == "TransparentCutout";
                    batch.argsTemplate[(j * 5) + 0] = mesh.GetIndexCount(j);
                    batch.argsTemplate[(j * 5) + 2] = mesh.GetIndexStart(j);
                    batch.argsTemplate[(j * 5) + 3] = mesh.GetBaseVertex(j);
                    if (batch.forwardPasses[j] < 0) { m_LodDither[i] = 0; }
                }
                if (m_LodDither[i] != 0) { m_DitherEnabled = true; }
                m_Batches.Add(batch);
            }

            m_Views = new List<TreeView>(8);
            m_Cells = new NativeArray<TreeCell>(cells, Allocator.Persistent);
            m_PlaneScratch = new Vector4[TreeVisibilityGpu.MaxPlanes];
            if (!m_Heights.IsCreated)
            {
                m_Heights = new NativeArray<float>(1, Allocator.Persistent);
                m_HeightRes = 0;
            }
        }

        void EnsureCpu()
        {
            if (m_ChunkMasks.IsCreated) { return; }
            int slotCount = m_Batches.Count * FoliageLogic.LodBucketCount;
            m_CellFlags = new NativeArray<byte>(cells.Length, Allocator.Persistent);
            m_ChunkMasks = new NativeArray<ulong>(m_ChunkCount, Allocator.Persistent);
            m_LodNow = new NativeArray<int>(m_InstanceCount, Allocator.Persistent);
            m_LodScreenSqr = new NativeArray<float>(m_InstanceCount, Allocator.Persistent);
            m_LodWeight = new NativeArray<float>(m_InstanceCount, Allocator.Persistent);
            m_BucketMasks = new NativeArray<ulong>(slotCount * m_ChunkCount, Allocator.Persistent);
            m_BucketCounts = new NativeArray<int>(slotCount, Allocator.Persistent);
            m_Ranges = new NativeArray<int2>(slotCount, Allocator.Persistent);
            m_IndexScratch = new NativeArray<int>(m_InstanceCount * 2, Allocator.Persistent);
            m_WeightScratch = new NativeArray<float>(m_InstanceCount * 2, Allocator.Persistent);
            for (int i = 0; i < m_InstanceCount; ++i) { m_LodNow[i] = -1; }
        }

        public void SetBackend(in TreeVisibilityBackend backend)
        {
            m_RequestedBackend = backend;
            TreeVisibilityBackend actual = TreeVisibilityBackend.CPU;
            string reason = "CPU selected";
            if (backend != TreeVisibilityBackend.CPU && m_InstanceCount > 0 && m_Batches != null)
            {
                if (m_Gpu == null)
                {
                    m_Gpu = new TreeVisibilityGpu();
                    m_Gpu.Initialize(m_Bounds, m_InstanceCell, boundSector.nativeSections, m_LodScreenSizes,
                        m_LodDither, m_Heights, m_HeightRes, m_TerrainPos, m_TerrainSize);
                }
                if (m_Gpu.IsReady) { actual = TreeVisibilityBackend.GPU; reason = "Compute available"; }
                else { reason = m_Gpu.FailureReason; }
            }
            if (actual != m_ActualBackend)
            {
                m_CullHandle.Complete();
                m_ActualBackend = actual;
                DisposeViews();
#if UNITY_EDITOR
                m_DebugSnapshot = null;
#endif
            }
            m_BackendReason = reason;
            string warning = backend == TreeVisibilityBackend.GPU && actual != TreeVisibilityBackend.GPU ? reason : null;
            if (warning != m_LastBackendWarning && warning != null)
            {
                Debug.LogWarning("Tree GPU backend unavailable; using CPU. " + warning);
            }
            m_LastBackendWarning = warning;
        }

        public TreeVisibilityBackend actualBackend
        {
            get { return m_ActualBackend; }
        }

        public string BackendStatus(Camera camera)
        {
            string hzb = "disabled";
            if ((m_OcclusionMode & TreeOcclusionMode.Hzb) != 0)
            {
                TreeView view = FindView(camera, -1);
                hzb = m_ActualBackend == TreeVisibilityBackend.CPU ? "requires GPU backend" :
                    view == null || view.gpu == null ? "view has not been culled" : view.gpu.hzb ? "active" : view.gpu.hzbReason;
            }
            return "Requested " + m_RequestedBackend + ", actual " + m_ActualBackend + ": " + m_BackendReason + ". HZB: " + hzb + ".";
        }

        TreeView FindView(Camera camera, in int cascade)
        {
            if (m_Views == null) { return null; }
            for (int i = 0; i < m_Views.Count; ++i)
            {
                TreeView view = m_Views[i];
                if (view.camera == camera && view.cascade == cascade) { return view; }
            }
            return null;
        }

        TreeView GetView(Camera camera, in int cascade)
        {
            int frame = Time.frameCount;
            if (m_PrunedFrame != frame)
            {
                m_PrunedFrame = frame;
                for (int i = m_Views.Count - 1; i >= 0; --i)
                {
                    TreeView stale = m_Views[i];
                    if (stale.camera != null && frame - stale.usedFrame <= ViewRetireFrames) { continue; }
                    if (m_ScratchView == stale) { m_ScratchView = null; }
                    stale.Dispose();
                    m_Views.RemoveAt(i);
                }
            }
            TreeView view = FindView(camera, cascade);
            if (view == null)
            {
                view = new TreeView();
                view.Initialize(camera, cascade, m_Batches);
                m_Views.Add(view);
            }
            view.usedFrame = frame;
            return view;
        }

        void DisposeViews()
        {
            m_ScratchView = null;
            if (m_Views == null) { return; }
            for (int i = 0; i < m_Views.Count; ++i) { m_Views[i].Dispose(); }
            m_Views.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe void Cull(CommandBuffer cmdBuffer, in FoliageView view, in TreeViewSettings settings, in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0 || m_Batches == null || view.camera == null) { return; }
            if (!view.isShadow)
            {
                m_ActiveCamera = view.camera;
                m_OcclusionMode = settings.occlusion;
            }

            if (m_ActualBackend == TreeVisibilityBackend.GPU)
            {
                TreeView gpuView;
                try
                {
                    gpuView = GetView(view.camera, view.cascade);
                    gpuView.EnsureGpu(m_InstanceCount, cells.Length);
                }
                catch (Exception error)
                {
                    m_Gpu.Fail("Tree GPU view allocation failed: " + error.Message);
                    return;
                }
                CullGpu(cmdBuffer, gpuView, view, settings);
                return;
            }

            EnsureCpu();
            TreeView treeView = GetView(view.camera, view.cascade);
            treeView.EnsureCpu(m_InstanceCount);
            m_CullHandle = view.isShadow ? ScheduleShadowCull(treeView, view, settings) : ScheduleColorCull(treeView, view, settings);
            m_ScratchView = treeView;
            treeView.culledFrame = Time.frameCount;
            taskHandles.Add(m_CullHandle);
        }

        unsafe JobHandle ScheduleColorCull(TreeView treeView, in FoliageView view, in TreeViewSettings settings)
        {
            bool distanceMode = settings.fadeMode == TreeLodFadeMode.Distance;
            bool firstMode = !treeView.hasMode;
            bool fadeChanged = treeView.hasMode && treeView.allowFade != settings.allowFade;
            bool modeChanged = treeView.hasMode && treeView.fadeMode != settings.fadeMode;
            treeView.allowFade = settings.allowFade;
            treeView.fadeMode = settings.fadeMode;
            treeView.hasMode = true;
            treeView.distanceReset = firstMode || fadeChanged || modeChanged;
            if (treeView.distanceReset)
            {
                treeView.ResetLods();
#if UNITY_EDITOR
                m_DebugSnapshot = null;
#endif
            }

            bool discontinuous = !treeView.hasLast ||
                FoliageLogic.ViewDiscontinuous(treeView.lastOrigin, view.origin, treeView.lastProj, view.lodProjection, settings.cullDistance);
            bool hardCut = false;
            if (distanceMode)
            {
                treeView.holdOrigin = view.origin;
                treeView.nowOrigin = view.origin;
                treeView.holdProj = view.lodProjection;
                treeView.nowProj = view.lodProjection;
            }
            else
            {
                hardCut = !settings.allowFade || fadeChanged || discontinuous || modeChanged;
                if (hardCut)
                {
                    treeView.holdOrigin = view.origin;
                    treeView.nowOrigin = view.origin;
                    treeView.holdProj = view.lodProjection;
                    treeView.nowProj = view.lodProjection;
                }
                else if (treeView.fadeState[1] == 0f)
                {
                    treeView.holdOrigin = treeView.nowOrigin;
                    treeView.holdProj = treeView.nowProj;
                    treeView.nowOrigin = view.origin;
                    treeView.nowProj = view.lodProjection;
                }
            }
            treeView.lastOrigin = view.origin;
            treeView.lastProj = view.lodProjection;
            treeView.hasLast = true;

            JobHandle handle = boundSector.Cull(settings.cullDistance, new float4(view.origin, 1), view.planes);

            var cellTerrainJob = new TreeCellTerrainJob();
            {
                cellTerrainJob.heightRes = (settings.occlusion & TreeOcclusionMode.Terrain) != 0 ? m_HeightRes : 0;
                cellTerrainJob.viewOrigin = view.origin;
                cellTerrainJob.terrainPos = m_TerrainPos;
                cellTerrainJob.terrainSize = m_TerrainSize;
                cellTerrainJob.cellVisible = boundSector.visibleMap;
                cellTerrainJob.cellBounds = boundSector.nativeSections;
                cellTerrainJob.heights = m_Heights;
                cellTerrainJob.cellFlags = m_CellFlags;
            }
            handle = cellTerrainJob.Schedule(m_CellFlags.Length, 16, handle);

            var cullLodJob = new TreeCullLodJob();
            {
                cullLodJob.maxDistance = settings.cullDistance;
                cullLodJob.viewOrigin = view.origin;
                cullLodJob.lodHoldOrigin = treeView.holdOrigin;
                cullLodJob.lodNowOrigin = treeView.nowOrigin;
                cullLodJob.lodHoldProj = treeView.holdProj;
                cullLodJob.lodNowProj = treeView.nowProj;
                cullLodJob.lodHysteresis = distanceMode || discontinuous ? 0f : math.clamp(settings.lodHysteresis, 0f, 0.5f);
                cullLodJob.distanceMode = distanceMode ? 1 : 0;
                cullLodJob.cellFlags = m_CellFlags;
                cullLodJob.instanceCell = m_InstanceCell;
                cullLodJob.bounds = m_Bounds;
                cullLodJob.lodScreenSizes = m_LodScreenSizes;
                cullLodJob.planes = view.planes;
                cullLodJob.chunkMasks = m_ChunkMasks;
                cullLodJob.lodHold = treeView.lodHold;
                cullLodJob.lodStable = treeView.lodStable;
                cullLodJob.lodNow = m_LodNow;
                cullLodJob.lodScreenSqr = m_LodScreenSqr;
            }
            handle = cullLodJob.Schedule(m_ChunkMasks.Length, 4, handle);

            int dither = settings.allowFade && m_DitherEnabled ? 1 : 0;
            if (distanceMode)
            {
                var emitJob = new TreeEmitDistanceMasksJob();
                {
                    emitJob.ditherEnabled = treeView.distanceReset ? 0 : dither;
                    emitJob.instanceCount = m_InstanceCount;
                    emitJob.fadeWidth = FoliageLogic.ClampFadeWidth(settings.fadeWidth);
                    emitJob.chunkMasks = m_ChunkMasks;
                    emitJob.lodNow = m_LodNow;
                    emitJob.screenSqr = m_LodScreenSqr;
                    emitJob.lodScreenSizes = m_LodScreenSizes;
                    emitJob.lodDither = m_LodDither;
                    emitJob.bucketMasks = m_BucketMasks;
                    emitJob.bucketCounts = m_BucketCounts;
                    emitJob.lodWeight = m_LodWeight;
                }
                handle = emitJob.Schedule(handle);
            }
            else
            {
                int frame = Time.frameCount;
                var advanceJob = new TreeAdvanceFadeJob();
                {
                    advanceJob.hardCut = hardCut ? 1 : 0;
                    advanceJob.advance = treeView.advancedFrame != frame ? 1 : 0;
                    advanceJob.ditherEnabled = dither;
                    advanceJob.step = settings.deltaTime / math.max(settings.fadeDuration, 0.0001f);
                    advanceJob.lodHold = treeView.lodHold;
                    advanceJob.lodNow = m_LodNow;
                    advanceJob.lodDither = m_LodDither;
                    advanceJob.lodStable = treeView.lodStable;
                    advanceJob.fadeState = treeView.fadeState;
                }
                handle = advanceJob.Schedule(handle);
                treeView.advancedFrame = frame;

                var emitJob = new TreeEmitLodMasksJob();
                {
                    emitJob.allowDither = dither;
                    emitJob.instanceCount = m_InstanceCount;
                    emitJob.chunkMasks = m_ChunkMasks;
                    emitJob.lodHold = treeView.lodHold;
                    emitJob.lodNow = m_LodNow;
                    emitJob.lodDither = m_LodDither;
                    emitJob.fadeState = treeView.fadeState;
                    emitJob.bucketMasks = m_BucketMasks;
                    emitJob.bucketCounts = m_BucketCounts;
                }
                handle = emitJob.Schedule(handle);
            }
            return ScheduleCompact(distanceMode, handle);
        }

        unsafe JobHandle ScheduleShadowCull(TreeView treeView, in FoliageView view, in TreeViewSettings settings)
        {
            var shadowCullJob = new TreeShadowCullJob();
            {
                shadowCullJob.planeCount = view.planeCount;
                shadowCullJob.origin = view.origin;
                shadowCullJob.projection = view.lodProjection;
                shadowCullJob.hysteresis = math.clamp(settings.lodHysteresis, 0f, 0.5f);
                shadowCullJob.planes = view.planes;
                shadowCullJob.sections = boundSector.nativeSections;
                shadowCullJob.cells = m_Cells;
                shadowCullJob.bounds = m_Bounds;
                shadowCullJob.lodScreenSizes = m_LodScreenSizes;
                shadowCullJob.lodStable = treeView.lodStable;
                shadowCullJob.bucketMasks = m_BucketMasks;
            }
            return ScheduleCompact(false, shadowCullJob.Schedule());
        }

        JobHandle ScheduleCompact(in bool weighted, in JobHandle dependsOn)
        {
            var compactJob = new TreeCompactJob();
            {
                compactJob.instanceCount = m_InstanceCount;
                compactJob.weighted = weighted ? 1 : 0;
                compactJob.bucketMasks = m_BucketMasks;
                compactJob.candidateWeights = m_LodWeight;
                compactJob.indices = m_IndexScratch;
                compactJob.weights = m_WeightScratch;
                compactJob.ranges = m_Ranges;
            }
            return compactJob.Schedule(dependsOn);
        }

        unsafe void CullGpu(CommandBuffer cmdBuffer, TreeView treeView, in FoliageView view, in TreeViewSettings settings)
        {
            treeView.allowFade = !view.isShadow && settings.allowFade;
            treeView.fadeMode = view.isShadow ? TreeLodFadeMode.Temporal : settings.fadeMode;
            int planeCount = math.min(view.planeCount, TreeVisibilityGpu.MaxPlanes);
            for (int i = 0; i < planeCount; ++i)
            {
                float4 plane = view.planes[i].normalDist;
                m_PlaneScratch[i] = new Vector4(plane.x, plane.y, plane.z, plane.w);
            }
            TreeHzb hzb = null;
            string hzbReason = "HZB is disabled.";
            if (!view.isShadow && (settings.occlusion & TreeOcclusionMode.Hzb) != 0)
            {
                hzb = TreeHzb.Previous(view.camera, view.origin, view.lodProjection, settings.cullDistance, out hzbReason);
            }
            m_Gpu.Cull(cmdBuffer, treeView.gpu, m_PlaneScratch, planeCount, view.origin, view.lodProjection, settings, hzb, view.isShadow);
            treeView.gpu.hzb = hzb != null;
            treeView.gpu.hzbReason = hzbReason;
            treeView.culledFrame = Time.frameCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Pack(CommandBuffer cmdBuffer, in FoliageView view, in TreeViewSettings settings)
        {
            if (m_InstanceCount <= 0 || m_Batches == null) { return; }
            TreeView treeView = FindView(view.camera, view.cascade);
            int frame = Time.frameCount;
            if (treeView == null || treeView.culledFrame != frame) { return; }

            if (m_ActualBackend == TreeVisibilityBackend.GPU)
            {
                if (!m_Gpu.IsReady) { return; }
                try
                {
                    for (int lod = 0; lod < treeView.packs.Length; ++lod) { treeView.packs[lod].EnsureCapacity(m_InstanceCount); }
                }
                catch (Exception error)
                {
                    m_Gpu.Fail("Tree GPU index allocation failed: " + error.Message);
                    return;
                }
                m_Gpu.Pack(cmdBuffer, treeView.gpu, treeView.packs, settings.fadeMode, settings.allowFade, view.isShadow);
                treeView.packedFrame = frame;
                return;
            }

            if (m_ScratchView != treeView) { return; }
            if (!view.isShadow && treeView.fadeState[2] != 0f)
            {
                treeView.holdOrigin = treeView.nowOrigin;
                treeView.holdProj = treeView.nowProj;
            }
            bool weighted = !view.isShadow && treeView.fadeMode == TreeLodFadeMode.Distance;
            for (int lod = 0; lod < treeView.packs.Length; ++lod)
            {
                TreeLodPack pack = treeView.packs[lod];
                int required = 0;
                for (int bucket = 0; bucket < pack.bucketCount; ++bucket)
                {
                    required = math.max(required, m_Ranges[(lod * FoliageLogic.LodBucketCount) + bucket].y);
                }
                pack.EnsureCapacity(required);
                for (int bucket = 0; bucket < pack.bucketCount; ++bucket)
                {
                    int2 range = m_Ranges[(lod * FoliageLogic.LodBucketCount) + bucket];
                    pack.counts[bucket] = range.y;
                    if (range.y <= 0) { continue; }
                    cmdBuffer.SetBufferData(pack.indexBuffers[bucket], m_IndexScratch, range.x, 0, range.y);
                    if (weighted && bucket > 0) { cmdBuffer.SetBufferData(pack.weightBuffers[bucket - 1], m_WeightScratch, range.x, 0, range.y); }
                }
                pack.WriteCounts(cmdBuffer);
            }
            treeView.packedFrame = frame;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Draw(CommandBuffer cmdBuffer, in FoliageView view, in FoliageDrawPass pass, MaterialPropertyBlock propertyBlock)
        {
            if (m_InstanceCount <= 0 || m_Batches == null) { return; }
            TreeView treeView = FindView(view.camera, view.cascade);
            if (treeView == null || treeView.packedFrame != Time.frameCount) { return; }

            bool gpu = m_ActualBackend == TreeVisibilityBackend.GPU;
            bool fadeBuckets = pass != FoliageDrawPass.Shadow && treeView.allowFade && m_DitherEnabled;
            for (int lod = 0; lod < m_Batches.Count; ++lod)
            {
                TreeLodBatch batch = m_Batches[lod];
                TreeLodPack pack = treeView.packs[lod];
                Mesh mesh = tree.meshes[batch.meshIndex];
                DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 0, 0f, 0f, null, null, gpu);
                if (!fadeBuckets) { continue; }
                if (treeView.fadeMode == TreeLodFadeMode.Distance)
                {
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 1, 1f, 1f, pack.weightBuffers[0], null, gpu);
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 2, -1f, 1f, pack.weightBuffers[1], null, gpu);
                }
                else if (gpu)
                {
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 1, 1f, 1f, null, treeView.gpu.fade, gpu);
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 2, -1f, 1f, null, treeView.gpu.fade, gpu);
                }
                else if (treeView.fadeState[1] != 0f)
                {
                    float alpha = treeView.fadeState[0];
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 1, 1f - alpha, 1f, null, null, gpu);
                    DrawBucket(cmdBuffer, pass, propertyBlock, batch, pack, mesh, 2, alpha - 1f, 1f, null, null, gpu);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DrawBucket(CommandBuffer cmdBuffer, in FoliageDrawPass pass, MaterialPropertyBlock propertyBlock, TreeLodBatch batch, TreeLodPack pack, Mesh mesh,
            in int bucket, in float lodFactor, in float fadeEnable, ComputeBuffer weightBuffer, ComputeBuffer fadeState, in bool gpu)
        {
            if (!gpu && pack.counts[bucket] <= 0) { return; }
            if (weightBuffer != null) { cmdBuffer.EnableShaderKeyword("FOLIAGE_LOD_WEIGHT"); }
            if (fadeState != null) { cmdBuffer.EnableShaderKeyword("FOLIAGE_GPU_TEMPORAL"); }
            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int passIndex = batch.PassIndex(pass, s, bucket);
                if (passIndex < 0) { continue; }
                Material material = tree.materials[batch.materialIndexs[s]];
                propertyBlock.Clear();
                if (pass == FoliageDrawPass.Forward) { FoliageAmbientSH.Bind(propertyBlock); }
                propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, pack.indexBuffers[bucket]);
                propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, m_MatrixBuffer);
                propertyBlock.SetFloat(TreeShaderID.LodFactor, lodFactor);
                propertyBlock.SetFloat(TreeShaderID.LodFadeEnable, fadeEnable);
                if (weightBuffer != null) { propertyBlock.SetBuffer(TreeShaderID.LodWeightBuffer, weightBuffer); }
                if (fadeState != null) { propertyBlock.SetBuffer(TreeShaderID.FadeStateBuffer, fadeState); }
                cmdBuffer.DrawMeshInstancedIndirect(mesh, batch.sectionIndexs[s], material, passIndex, pack.argsBuffer, pack.ArgsOffset(bucket, s), propertyBlock);
            }
            if (weightBuffer != null) { cmdBuffer.DisableShaderKeyword("FOLIAGE_LOD_WEIGHT"); }
            if (fadeState != null) { cmdBuffer.DisableShaderKeyword("FOLIAGE_GPU_TEMPORAL"); }
        }

        internal bool UsesHzb(in TreeOcclusionMode occlusion)
        {
            return m_InstanceCount > 0 && m_ActualBackend == TreeVisibilityBackend.GPU && (occlusion & TreeOcclusionMode.Hzb) != 0;
        }

#if UNITY_EDITOR
        public void CaptureVisibilitySnapshot(CommandBuffer cmdBuffer, Camera camera)
        {
            TreeView treeView = FindView(camera, -1);
            if (m_InstanceCount <= 0 || treeView == null || treeView.packedFrame != Time.frameCount) { return; }
            bool gpu = m_ActualBackend == TreeVisibilityBackend.GPU;
            TreeVisibilitySnapshot snapshot = new TreeVisibilitySnapshot();
            snapshot.camera = camera;
            snapshot.frame = Time.frameCount;
            snapshot.requestedBackend = m_RequestedBackend;
            snapshot.actualBackend = m_ActualBackend;
            snapshot.hzb = gpu && treeView.gpu.hzb;
            snapshot.coarseCells = new byte[cells.Length];
            snapshot.terrainCells = new byte[cells.Length];
            snapshot.lods = new int[m_InstanceCount];
            snapshot.finalVisible = new bool[m_InstanceCount];
            if (!gpu)
            {
                snapshot.coarseCells = boundSector.visibleMap.ToArray();
                snapshot.cpuMasks = m_ChunkMasks.ToArray();
                snapshot.lods = m_LodNow.ToArray();
                for (int i = 0; i < cells.Length; ++i)
                {
                    snapshot.terrainCells[i] = (byte)((m_CellFlags[i] & FoliageLogic.CellTerrain) != 0 ? 1 : 0);
                }
            }
            else
            {
                snapshot.pending += 3;
                cmdBuffer.RequestAsyncReadback(treeView.gpu.cells, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.gpuCells = request.GetData<uint>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                });
                cmdBuffer.RequestAsyncReadback(treeView.gpu.lod, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.gpuLods = request.GetData<int4>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                });
                cmdBuffer.RequestAsyncReadback(treeView.gpu.fade, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.fadeState = request.GetData<float4>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                });
            }

            for (int lod = 0; lod < treeView.packs.Length; ++lod)
            {
                TreeLodPack pack = treeView.packs[lod];
                for (int bucketIndex = 0; bucketIndex < pack.bucketCount; ++bucketIndex)
                {
                    int upperBound = gpu ? m_InstanceCount : pack.counts[bucketIndex];
                    if (upperBound <= 0) { continue; }
                    TreeVisibilitySnapshot.Bucket captured = new TreeVisibilitySnapshot.Bucket();
                    captured.lod = m_Batches[lod].meshIndex;
                    captured.bucket = bucketIndex;
                    captured.argsOffset = pack.ArgsOffset(bucketIndex, 0) / sizeof(uint);
                    captured.weighted = treeView.fadeMode == TreeLodFadeMode.Distance && treeView.allowFade && bucketIndex != 0;
                    snapshot.buckets.Add(captured);
                    snapshot.pending += 2;
                    cmdBuffer.RequestAsyncReadback(pack.argsBuffer, request =>
                    {
                        try
                        {
                            if (request.hasError) { snapshot.failed = true; }
                            else { captured.args = request.GetData<uint>().ToArray(); }
                        }
                        catch (Exception) { snapshot.failed = true; }
                        finally { snapshot.FinishRequest(); }
                    });
                    cmdBuffer.RequestAsyncReadback(pack.indexBuffers[bucketIndex], upperBound * sizeof(int), 0, request =>
                    {
                        try
                        {
                            if (request.hasError) { snapshot.failed = true; }
                            else { captured.indices = request.GetData<int>().ToArray(); }
                        }
                        catch (Exception) { snapshot.failed = true; }
                        finally { snapshot.FinishRequest(); }
                    });
                    if (captured.weighted)
                    {
                        ++snapshot.pending;
                        cmdBuffer.RequestAsyncReadback(pack.weightBuffers[bucketIndex - 1], upperBound * sizeof(float), 0, request =>
                        {
                            try
                            {
                                if (request.hasError) { snapshot.failed = true; }
                                else { captured.weights = request.GetData<float>().ToArray(); }
                            }
                            catch (Exception) { snapshot.failed = true; }
                            finally { snapshot.FinishRequest(); }
                        });
                    }
                }
            }
            if (snapshot.pending == 0) { snapshot.pending = 1; snapshot.FinishRequest(); }
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
            if (snapshot.cpuMasks != null)
            {
                for (int i = 0; i < snapshot.cpuMasks.Length; ++i) { cpuCount += FoliageLogic.PopCount(snapshot.cpuMasks[i]); }
            }
            else
            {
                for (int i = 0; i < snapshot.gpuLods.Length; ++i) { if (snapshot.gpuLods[i].w != 0) { ++cpuCount; } }
            }
            string weights = snapshot.weightPairs > 0 ? ", distance fade pairs " + snapshot.weightPairs +
                ", invalid weights " + snapshot.invalidWeights : "";
            string backend = " requested " + snapshot.requestedBackend + ", actual " + snapshot.actualBackend;
            string stage = snapshot.actualBackend == TreeVisibilityBackend.CPU ? "CPU candidates " : "GPU instance survivors ";
            string alpha = snapshot.fadeState == null ? "" : ", alpha " + snapshot.fadeState[0].x.ToString("F3");
            return source + " frame " + snapshot.frame + backend + ": " + stage + cpuCount +
                ", final indices " + snapshot.FinalCount() + ", HZB " + (snapshot.hzb ? "on" : "off") + weights + alpha + ".";
        }

        public string VisibilitySnapshotCandidate(in int index)
        {
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot;
            if (snapshot == null || !snapshot.complete || snapshot.failed) { return "Candidate snapshot is unavailable."; }
            if (index < 0 || index >= snapshot.finalVisible.Length) { return "Candidate index is outside this tree stream."; }
            int cell = m_InstanceCell[index];
            bool cpu = snapshot.cpuMasks != null ? (snapshot.cpuMasks[index >> 6] & (1UL << (index & 63))) != 0 : snapshot.gpuLods[index].w != 0;
            string gpuCell = snapshot.gpuCells == null ? "unknown" : ((snapshot.gpuCells[cell] & 4) != 0 ? "visible" : "rejected");
            var pairs = new System.Text.StringBuilder();
            for (int b = 0; b < snapshot.buckets.Count; ++b)
            {
                TreeVisibilitySnapshot.Bucket bucket = snapshot.buckets[b];
                if (!bucket.weighted) { continue; }
                int count = bucket.Count();
                for (int i = 0; i < count; ++i)
                {
                    if (bucket.indices[i] != index) { continue; }
                    pairs.Append(" LOD ").Append(bucket.lod).Append(" bucket ").Append(bucket.bucket)
                        .Append(" x ").Append(bucket.weights[i].ToString("F3"));
                }
            }
            return "Candidate " + index + ", cell " + cell + ": coarse " + snapshot.coarseCells[cell] +
                ", terrain " + snapshot.terrainCells[cell] + ", " + snapshot.actualBackend + " instance " + (cpu ? "visible" : "rejected") +
                ", GPU cell " + gpuCell + ", final index " + (snapshot.finalVisible[index] ? "present" : "absent") +
                ", LOD " + snapshot.lods[index] + pairs + ".";
        }

        public string VisibilitySnapshotGpuRejections(in int limit)
        {
            TreeVisibilitySnapshot snapshot = m_DebugSnapshot;
            if (snapshot == null || !snapshot.complete || snapshot.failed) { return "GPU rejections unavailable."; }
            if (snapshot.cpuMasks == null) { return "GPU backend: inspect cell and instance stages in candidate snapshot."; }
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

        public void Release()
        {
            m_CullHandle.Complete();
            transforms = null;
            DisposeViews();
            m_Views = null;
            if (boundSector != null) { boundSector.ReleaseNativeCollection(); }
            if (m_Matrices.IsCreated) { m_Matrices.Dispose(); }
            if (m_Bounds.IsCreated) { m_Bounds.Dispose(); }
            if (m_InstanceCell.IsCreated) { m_InstanceCell.Dispose(); }
            if (m_Cells.IsCreated) { m_Cells.Dispose(); }
            if (m_LodScreenSizes.IsCreated) { m_LodScreenSizes.Dispose(); }
            if (m_LodDither.IsCreated) { m_LodDither.Dispose(); }
            if (m_Heights.IsCreated) { m_Heights.Dispose(); }
            if (m_CellFlags.IsCreated) { m_CellFlags.Dispose(); }
            if (m_ChunkMasks.IsCreated) { m_ChunkMasks.Dispose(); }
            if (m_LodNow.IsCreated) { m_LodNow.Dispose(); }
            if (m_LodScreenSqr.IsCreated) { m_LodScreenSqr.Dispose(); }
            if (m_LodWeight.IsCreated) { m_LodWeight.Dispose(); }
            if (m_BucketMasks.IsCreated) { m_BucketMasks.Dispose(); }
            if (m_BucketCounts.IsCreated) { m_BucketCounts.Dispose(); }
            if (m_Ranges.IsCreated) { m_Ranges.Dispose(); }
            if (m_IndexScratch.IsCreated) { m_IndexScratch.Dispose(); }
            if (m_WeightScratch.IsCreated) { m_WeightScratch.Dispose(); }
            if (m_MatrixBuffer != null) { m_MatrixBuffer.Dispose(); m_MatrixBuffer = null; }
            if (m_Gpu != null) { m_Gpu.Release(); m_Gpu = null; }
            m_Batches = null;
            m_ActiveCamera = null;
            m_PlaneScratch = null;
            m_InstanceCount = 0;
            m_ChunkCount = 0;
            m_PrunedFrame = -1;
            m_ActualBackend = TreeVisibilityBackend.CPU;
            m_LastBackendWarning = null;
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
                            snapshot.gpuCells != null && (snapshot.gpuCells[i] & 4) == 0 ? Color.cyan : Color.green;
                    }
                    else if (m_ActualBackend == TreeVisibilityBackend.GPU || !m_CellFlags.IsCreated)
                    {
                        color = Color.gray;
                    }
                    else
                    {
                        byte flags = m_CellFlags[i];
                        color = flags == FoliageLogic.CellVisible ? Color.green : flags == FoliageLogic.CellFrustum ? Color.magenta : Color.red;
                    }
                    Geometry.DrawBound(boundSector.sections[i].boundBox, color);
                }
                return;
            }
            for (int i = 0; i < m_Bounds.Length; ++i)
            {
                int chunk = i >> 6;
                int bit = i & 63;
                if (snapshot == null && m_ActualBackend == TreeVisibilityBackend.GPU)
                {
                    Geometry.DrawBound(m_Bounds[i], Color.gray);
                    continue;
                }
                bool visible = snapshot == null ? m_ChunkMasks.IsCreated && (m_ChunkMasks[chunk] & (1UL << bit)) != 0 :
                    snapshot.cpuMasks != null ? (snapshot.cpuMasks[chunk] & (1UL << bit)) != 0 : snapshot.gpuLods[i].w != 0;
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
