using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.FoliagePipeline
{
    [BurstCompile]
    internal struct TreeCompactMaskJob : IJob
    {
        [ReadOnly] public NativeArray<ulong> masks;
        [ReadOnly] public NativeArray<float> candidateWeights;
        public NativeArray<int> indices;
        public NativeArray<float> weights;
        public NativeArray<int> counts;
        public int instanceCount;
        public int bucket;
        public int weighted;

        public void Execute()
        {
            int written = 0;
            for (int chunk = 0; chunk < masks.Length; ++chunk)
            {
                ulong mask = masks[chunk];
                while (mask != 0)
                {
                    int candidate = (chunk << 6) + math.tzcnt(mask);
                    mask &= mask - 1;
                    if (candidate >= instanceCount) { continue; }
                    indices[written] = candidate;
                    if (weighted != 0) { weights[written] = candidateWeights[candidate]; }
                    ++written;
                }
            }
            counts[bucket] = written;
        }
    }

    internal class TreeLodBatch
    {
        public int meshIndex;
        public int[] sectionIndexs;
        public int[] materialIndexs;
        public ComputeBuffer[] indexBuffers;
        public ComputeBuffer[] weightBuffers;
        public ComputeBuffer[] argsBuffers;
        public NativeArray<ulong> stableMask;
        public NativeArray<ulong> fadeOutMask;
        public NativeArray<ulong> fadeInMask;
        public NativeArray<int> bucketCounts;
        public int[] uploadedCount;
        public bool[] gpuArgs;
        public uint[][] argsValues;

        public void Initialize(in int instanceCount, in int chunkCount)
        {
            indexBuffers = new ComputeBuffer[3];
            int argsCount = 3 * math.max(sectionIndexs.Length, 1);
            argsBuffers = new ComputeBuffer[argsCount];
            argsValues = new uint[argsCount][];
            try
            {
                for (int i = 0; i < 3; ++i)
                {
                    indexBuffers[i] = new ComputeBuffer(math.max(instanceCount, 1), sizeof(int));
                }
                weightBuffers = new ComputeBuffer[2];
                for (int i = 0; i < 2; ++i)
                {
                    weightBuffers[i] = new ComputeBuffer(math.max(instanceCount, 1), sizeof(float));
                }
                for (int i = 0; i < argsCount; ++i)
                {
                    argsBuffers[i] = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                }
                uploadedCount = new int[3];
                gpuArgs = new bool[3];
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void EnsureCpu(in int chunkCount)
        {
            if (stableMask.IsCreated) { return; }
            int chunks = math.max(chunkCount, 1);
            stableMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
            fadeOutMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
            fadeInMask = new NativeArray<ulong>(chunks, Allocator.Persistent);
            bucketCounts = new NativeArray<int>(3, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (indexBuffers != null)
            {
                for (int i = 0; i < indexBuffers.Length; ++i) { indexBuffers[i]?.Dispose(); }
            }
            if (weightBuffers != null)
            {
                for (int i = 0; i < weightBuffers.Length; ++i) { weightBuffers[i]?.Dispose(); }
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
        public bool hasMode;
        public bool allowFade;
        public TreeLodFadeMode mode;

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
            internal int lod;
            internal int bucket;
            internal bool weighted;
            internal uint[] args;
            internal int[] indices;
            internal float[] weights;
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
                int count = math.min((int)bucket.args[1], bucket.indices.Length);
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
        public NativeArray<int>[] lodStable;
        public NativeArray<int> indices;
        public NativeArray<int> counts;
        public NativeArray<FrustumPlane> planes;
        public Camera camera;

        public void Initialize(Camera viewCamera, List<TreeLodBatch> batches, in int instanceCount)
        {
            camera = viewCamera;
            lodStable = new NativeArray<int>[4];
            cascades = new TreeShadowBatch[4][];
            for (int cascade = 0; cascade < 4; ++cascade)
            {
                cascades[cascade] = new TreeShadowBatch[batches.Count];
            }
        }

        public void EnsureCpu(in int instanceCount, in int lodCount)
        {
            if (indices.IsCreated) { return; }
            indices = new NativeArray<int>(instanceCount * lodCount, Allocator.Persistent);
            counts = new NativeArray<int>(lodCount, Allocator.Persistent);
            planes = new NativeArray<FrustumPlane>(6, Allocator.Persistent);
            for (int cascade = 0; cascade < 4; ++cascade)
            {
                lodStable[cascade] = new NativeArray<int>(instanceCount, Allocator.Persistent);
                for (int i = 0; i < instanceCount; ++i) { lodStable[cascade][i] = -1; }
            }
        }

        public void Dispose()
        {
            if (indices.IsCreated) { indices.Dispose(); }
            if (counts.IsCreated) { counts.Dispose(); }
            if (planes.IsCreated) { planes.Dispose(); }
            if (lodStable != null)
            {
                for (int cascade = 0; cascade < lodStable.Length; ++cascade)
                {
                    if (lodStable[cascade].IsCreated) { lodStable[cascade].Dispose(); }
                }
            }
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
        private NativeArray<TreeCell> m_Cells;
        private NativeArray<float> m_LodScreenSizes;
        private NativeArray<byte> m_LodDither;
        private NativeArray<ulong> m_ChunkMasks;
        private NativeArray<int> m_LodNow;
        private NativeArray<float> m_LodScreenSqr;
        private NativeArray<float> m_LodWeight;
        private NativeArray<float> m_Heights;
        private List<TreeLodBatch> m_Batches;
        private Dictionary<ulong, TreeCameraFade> m_FadeViews;
        private List<ulong> m_DeadFadeViews;
        private Dictionary<ulong, TreeShadowView> m_ShadowViews;
        private List<ulong> m_DeadShadowViews;
        private TreeCameraFade m_ActiveFade;
        private TreeVisibilityGpu m_Gpu;
        private Camera m_ActiveCamera;
        private NativeArray<int> m_IndexScratch;
        private NativeArray<float> m_WeightScratch;
        private TreeVisibilityBackend m_RequestedBackend;
        private TreeVisibilityBackend m_ActualBackend = TreeVisibilityBackend.CPU;
        private string m_BackendReason = "CPU selected";
        private string m_LastBackendWarning;
        private float4x4 m_ViewProjection;
        private float m_LodHysteresis;
        private Vector4[] m_PlaneScratch;
        private bool m_DitherEnabled;
        private bool m_AllowFade = true;
        private TreeLodFadeMode m_FadeMode;
        private float m_FadeWidth = 0.2f;
        private TreeOcclusionMode m_OcclusionMode = TreeOcclusionMode.TerrainAndHzb;
        private int m_InstanceCount;
        private int m_HeightRes;
        private float m_DrawDistance;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
        private bool m_PendingVisibility;
        private bool m_DistanceReset;
        private bool m_ResetArgs;
        private bool m_ResetShadowArgs;
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
                for (int bucket = 0; bucket < 3; ++bucket)
                {
                    for (int submesh = 0; submesh < batch.sectionIndexs.Length; ++submesh)
                    {
                        int section = batch.sectionIndexs[submesh];
                        uint[] args = new uint[5];
                        args[0] = mesh.GetIndexCount(section);
                        args[2] = mesh.GetIndexStart(section);
                        args[3] = mesh.GetBaseVertex(section);
                        batch.argsValues[bucket * batch.sectionIndexs.Length + submesh] = args;
                        batch.argsBuffers[bucket * batch.sectionIndexs.Length + submesh].SetData(args);
                    }
                }
                m_Batches.Add(batch);
            }

            m_FadeViews = new Dictionary<ulong, TreeCameraFade>(4);
            m_DeadFadeViews = new List<ulong>(4);
            m_ShadowViews = new Dictionary<ulong, TreeShadowView>(4);
            m_DeadShadowViews = new List<ulong>(4);
            m_Cells = new NativeArray<TreeCell>(cells, Allocator.Persistent);
            m_PlaneScratch = new Vector4[6];
            if (!m_Heights.IsCreated)
            {
                m_Heights = new NativeArray<float>(1, Allocator.Persistent);
                m_HeightRes = 0;
            }
        }

        void EnsureCpu()
        {
            if (m_ChunkMasks.IsCreated) { return; }
            int chunkCount = FoliageLogic.VisibilityChunkCount(m_InstanceCount);
            m_ChunkMasks = new NativeArray<ulong>(math.max(chunkCount, 1), Allocator.Persistent);
            m_LodNow = new NativeArray<int>(m_InstanceCount, Allocator.Persistent);
            m_LodScreenSqr = new NativeArray<float>(m_InstanceCount, Allocator.Persistent);
            m_LodWeight = new NativeArray<float>(m_InstanceCount, Allocator.Persistent);
            m_IndexScratch = new NativeArray<int>(m_InstanceCount, Allocator.Persistent);
            m_WeightScratch = new NativeArray<float>(m_InstanceCount, Allocator.Persistent);
            for (int i = 0; i < m_InstanceCount; ++i) { m_LodNow[i] = -1; }
            for (int i = 0; i < m_Batches.Count; ++i) { m_Batches[i].EnsureCpu(chunkCount); }
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
                m_ViewHandle.Complete();
                m_ActualBackend = actual;
                m_PendingVisibility = false;
                m_ResetArgs = true;
                m_ResetShadowArgs = true;
                m_ActiveFade = null;
                foreach (var pair in m_FadeViews)
                {
                    TreeCameraFade fade = pair.Value;
                    fade.hasLast = false;
                    fade.hasMode = false;
                    fade.fading = false;
                    fade.alpha = 1f;
                    for (int i = 0; i < fade.lodStable.Length; ++i) { fade.lodStable[i] = -1; fade.lodHold[i] = -1; }
                }
                foreach (var pair in m_ShadowViews)
                {
                    TreeShadowView view = pair.Value;
                    for (int c = 0; c < view.lodStable.Length; ++c)
                    {
                        if (!view.lodStable[c].IsCreated) { continue; }
                        for (int i = 0; i < view.lodStable[c].Length; ++i) { view.lodStable[c][i] = -1; }
                    }
                }
                m_Gpu?.ResetViews();
                for (int i = 0; i < m_Batches.Count; ++i)
                {
                    TreeLodBatch batch = m_Batches[i];
                    for (int b = 0; b < 3; ++b) { batch.uploadedCount[b] = 0; batch.gpuArgs[b] = false; }
                }
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
            string hzb = (m_OcclusionMode & TreeOcclusionMode.Hzb) == 0 ? "disabled" :
                m_ActualBackend == TreeVisibilityBackend.CPU ? "requires GPU backend" : m_Gpu.HzbStatus(camera);
            return "Requested " + m_RequestedBackend + ", actual " + m_ActualBackend + ": " + m_BackendReason + ". HZB: " + hzb + ".";
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
            if (m_InstanceCount <= 0 || m_ActualBackend == TreeVisibilityBackend.GPU) { return; }
            EnsureCpu();
            m_ViewHandle = boundSector.InitView(cullDistance, new float4(viewOrigin, 1), planes);
            taskHandles.Add(m_ViewHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void DispatchSetup(Camera camera, in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj,
            in FrustumPlane* planes, in TreeOcclusionMode occlusionMode, in bool allowFade, in float lodHysteresis,
            in TreeLodFadeMode fadeMode, in float fadeWidth, in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0) { return; }

            if (m_AllowFade != allowFade || m_FadeMode != fadeMode)
            {
                m_ResetArgs = true;
#if UNITY_EDITOR
                m_DebugSnapshot = null;
#endif
            }
            m_ActiveCamera = camera;
            m_DrawDistance = cullDistance;
            m_AllowFade = allowFade;
            m_FadeMode = fadeMode;
            m_FadeWidth = FoliageLogic.ClampFadeWidth(fadeWidth);
            m_OcclusionMode = occlusionMode;
            for (int i = 0; i < 6; ++i)
            {
                float4 plane = planes[i].normalDist;
                m_PlaneScratch[i] = new Vector4(plane.x, plane.y, plane.z, plane.w);
            }

            m_ViewProjection = matrixProj;
            m_LodHysteresis = math.clamp(lodHysteresis, 0f, 0.5f);
            if (m_ActualBackend == TreeVisibilityBackend.GPU)
            {
                m_PendingVisibility = true;
                m_ActiveFade = null;
                return;
            }
            EnsureCpu();
            m_ActiveFade = GetFade(camera);
            TreeCameraFade fade = m_ActiveFade;
            bool firstMode = !fade.hasMode;
            bool fadeModeChanged = fade.hasMode && fade.allowFade != allowFade;
            bool lodModeChanged = fade.hasMode && fade.mode != fadeMode;
            fade.allowFade = allowFade;
            fade.mode = fadeMode;
            fade.hasMode = true;
            m_DistanceReset = firstMode || fadeModeChanged || lodModeChanged;
            if (m_DistanceReset)
            {
                for (int i = 0; i < m_InstanceCount; ++i) { fade.lodStable[i] = -1; fade.lodHold[i] = -1; }
#if UNITY_EDITOR
                m_DebugSnapshot = null;
#endif
            }
            bool discontinuous = !fade.hasLast ||
                FoliageLogic.ViewDiscontinuous(fade.lastOrigin, viewOrigin, fade.lastProj, matrixProj, cullDistance);
            if (m_FadeMode == TreeLodFadeMode.Distance)
            {
                fade.holdOrigin = viewOrigin;
                fade.nowOrigin = viewOrigin;
                fade.holdProj = matrixProj;
                fade.nowProj = matrixProj;
                fade.alpha = 1f;
                fade.fading = false;
                fade.hardCut = false;
            }
            else
            {
                fade.hardCut = !allowFade || fadeModeChanged || discontinuous || lodModeChanged;
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
            }
            var cullLodJob = new TreeCullLodJob();
            {
                cullLodJob.heightRes = m_Heights.IsCreated && (occlusionMode & TreeOcclusionMode.Terrain) != 0 ? m_HeightRes : 0;
                cullLodJob.maxDistance = cullDistance;
                cullLodJob.viewOrigin = viewOrigin;
                cullLodJob.lodHoldOrigin = fade.holdOrigin;
                cullLodJob.lodNowOrigin = m_FadeMode == TreeLodFadeMode.Distance ? viewOrigin : fade.nowOrigin;
                cullLodJob.lodHoldProj = fade.holdProj;
                cullLodJob.lodNowProj = m_FadeMode == TreeLodFadeMode.Distance ? matrixProj : fade.nowProj;
                cullLodJob.lodHysteresis = m_FadeMode == TreeLodFadeMode.Distance || discontinuous ? 0f : math.clamp(lodHysteresis, 0f, 0.5f);
                cullLodJob.distanceMode = m_FadeMode == TreeLodFadeMode.Distance ? 1 : 0;
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
                cullLodJob.lodScreenSqr = m_LodScreenSqr;
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
            if (!m_PendingVisibility || m_InstanceCount <= 0) { return; }
            if (m_ResetArgs) { ClearDrawArgs(cmdBuffer); m_ResetArgs = false; }
            if (m_ActualBackend == TreeVisibilityBackend.GPU)
            {
                TreeOcclusionMode occlusion = enableHzb ? m_OcclusionMode : m_OcclusionMode & ~TreeOcclusionMode.Hzb;
                m_Gpu.DispatchColor(cmdBuffer, m_ActiveCamera, m_PlaneScratch, m_DrawDistance, m_ViewProjection,
                    occlusion, m_AllowFade, m_FadeMode, m_FadeWidth, m_LodHysteresis, duration, deltaTime,
                    cameraDepth, zParams, depthViewProj, m_Batches);
                m_PendingVisibility = false;
                return;
            }
            if (m_ActiveFade == null) { return; }

            JobHandle emitHandle = default;
            bool scheduled = false;
            if (m_FadeMode == TreeLodFadeMode.Distance)
            {
                int dither = (m_AllowFade && m_DitherEnabled && !m_DistanceReset) ? 1 : 0;
                for (int i = 0; i < m_Batches.Count; ++i)
                {
                    TreeLodBatch batch = m_Batches[i];
                    var emitJob = new TreeEmitDistanceMasksJob();
                    {
                        emitJob.meshIndex = batch.meshIndex;
                        emitJob.ditherEnabled = dither;
                        emitJob.instanceCount = m_InstanceCount;
                        emitJob.fadeWidth = m_FadeWidth;
                        emitJob.chunkMasks = m_ChunkMasks;
                        emitJob.lodNow = m_LodNow;
                        emitJob.screenSqr = m_LodScreenSqr;
                        emitJob.lodScreenSizes = m_LodScreenSizes;
                        emitJob.lodDither = m_LodDither;
                        emitJob.stableMask = batch.stableMask;
                        emitJob.fadeOutMask = batch.fadeOutMask;
                        emitJob.fadeInMask = batch.fadeInMask;
                        emitJob.bucketCounts = batch.bucketCounts;
                        emitJob.lodWeight = m_LodWeight;
                    }
                    emitHandle = emitJob.Schedule(emitHandle);
                    scheduled = true;
                }
            }
            else
            {
                UpdateFade(duration, deltaTime);
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
            }
            if (scheduled) { emitHandle.Complete(); }

            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                LowerBucket(cmdBuffer, batch, 0, batch.stableMask);
                LowerBucket(cmdBuffer, batch, 1, batch.fadeOutMask);
                LowerBucket(cmdBuffer, batch, 2, batch.fadeInMask);
            }
            m_PendingVisibility = false;
        }

        void LowerBucket(CommandBuffer cmdBuffer, TreeLodBatch batch, in int bucket, NativeArray<ulong> masks)
        {
            bool weighted = m_FadeMode == TreeLodFadeMode.Distance && bucket != 0;
            var compactJob = new TreeCompactMaskJob();
            {
                compactJob.masks = masks;
                compactJob.candidateWeights = m_LodWeight;
                compactJob.indices = m_IndexScratch;
                compactJob.weights = m_WeightScratch;
                compactJob.counts = batch.bucketCounts;
                compactJob.instanceCount = m_InstanceCount;
                compactJob.bucket = bucket;
                compactJob.weighted = weighted ? 1 : 0;
            }
            compactJob.Schedule().Complete();
            int written = batch.bucketCounts[bucket];
            batch.uploadedCount[bucket] = written;
            batch.gpuArgs[bucket] = false;
            if (written > 0)
            {
                cmdBuffer.SetBufferData(batch.indexBuffers[bucket], m_IndexScratch, 0, 0, written);
                if (weighted) { cmdBuffer.SetBufferData(batch.weightBuffers[bucket - 1], m_WeightScratch, 0, 0, written); }
            }
            WriteCpuArgs(cmdBuffer, batch, bucket, written);
        }

        void ClearDrawArgs(CommandBuffer cmdBuffer)
        {
            for (int lod = 0; lod < m_Batches.Count; ++lod)
            {
                TreeLodBatch batch = m_Batches[lod];
                for (int bucket = 0; bucket < 3; ++bucket)
                {
                    batch.uploadedCount[bucket] = 0;
                    batch.gpuArgs[bucket] = false;
                    WriteCpuArgs(cmdBuffer, batch, bucket, 0);
                }
            }
        }

        void ClearShadowArgs(CommandBuffer cmdBuffer)
        {
            foreach (var pair in m_ShadowViews)
            {
                TreeShadowView view = pair.Value;
                for (int cascade = 0; cascade < view.cascades.Length; ++cascade)
                {
                    for (int lod = 0; lod < m_Batches.Count; ++lod)
                    {
                        TreeShadowBatch shadow = view.cascades[cascade][lod];
                        if (shadow == null) { continue; }
                        TreeLodBatch batch = m_Batches[lod];
                        for (int submesh = 0; submesh < shadow.argsBuffers.Length; ++submesh)
                        {
                            uint[] args = batch.argsValues[submesh];
                            args[1] = 0;
                            cmdBuffer.SetBufferData(shadow.argsBuffers[submesh], args);
                        }
                    }
                }
            }
        }

        void WriteCpuArgs(CommandBuffer cmdBuffer, TreeLodBatch batch, in int bucket, in int instanceCount)
        {
            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int argsIndex = bucket * batch.sectionIndexs.Length + s;
                uint[] args = batch.argsValues[argsIndex];
                args[1] = (uint)instanceCount;
                cmdBuffer.SetBufferData(batch.argsBuffers[argsIndex], args);
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
                DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.Stable, 0f, 0f, null, worldBounds);
                if (m_FadeMode == TreeLodFadeMode.Distance && m_AllowFade)
                {
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeOut, 1f, 1f, batch.weightBuffers[0], worldBounds);
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeIn, -1f, 1f, batch.weightBuffers[1], worldBounds);
                }
                else if (m_ActualBackend == TreeVisibilityBackend.GPU && m_DitherEnabled && m_AllowFade)
                {
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeOut, 1f, 1f, null, worldBounds);
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeIn, -1f, 1f, null, worldBounds);
                }
                else if (m_ActiveFade != null && m_ActiveFade.fading && m_DitherEnabled && m_AllowFade)
                {
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeOut, 1f - m_ActiveFade.alpha, 1f, null, worldBounds);
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeIn, m_ActiveFade.alpha - 1f, 1f, null, worldBounds);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DrawBucket(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock, TreeLodBatch batch, Mesh mesh, in int bucket, in float lodFactor, in float fadeEnable, ComputeBuffer weightBuffer, Bounds worldBounds)
        {
            int argsIndex = bucket == (int)LodBucket.FadeOut ? 1 : bucket == (int)LodBucket.FadeIn ? 2 : 0;
            if (!batch.gpuArgs[argsIndex] && batch.uploadedCount[argsIndex] <= 0) { return; }

            ComputeBuffer indexBuffer = batch.indexBuffers[argsIndex];
            if (weightBuffer != null) { cmdBuffer.EnableShaderKeyword("FOLIAGE_LOD_WEIGHT"); }
            else { cmdBuffer.DisableShaderKeyword("FOLIAGE_LOD_WEIGHT"); }
            ComputeBuffer fadeState = m_ActualBackend == TreeVisibilityBackend.GPU &&
                m_FadeMode == TreeLodFadeMode.Temporal && bucket != (int)LodBucket.Stable ? m_Gpu.GetFadeState(m_ActiveCamera) : null;
            if (fadeState != null) { cmdBuffer.EnableShaderKeyword("FOLIAGE_GPU_TEMPORAL"); }
            else { cmdBuffer.DisableShaderKeyword("FOLIAGE_GPU_TEMPORAL"); }
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
                if (weightBuffer != null) { propertyBlock.SetBuffer(TreeShaderID.LodWeightBuffer, weightBuffer); }
                if (fadeState != null) { propertyBlock.SetBuffer(TreeShaderID.FadeStateBuffer, fadeState); }
                cmdBuffer.DrawMeshInstancedIndirect(mesh, submesh, material, passIndex, argsBuffer, 0, propertyBlock);
            }
            cmdBuffer.DisableShaderKeyword("FOLIAGE_LOD_WEIGHT");
            cmdBuffer.DisableShaderKeyword("FOLIAGE_GPU_TEMPORAL");
        }

#if UNITY_EDITOR
        public void CaptureVisibilitySnapshot(CommandBuffer cmdBuffer, Camera camera)
        {
            if (m_InstanceCount <= 0 || m_ActiveCamera != camera) { return; }
            TreeVisibilitySnapshot snapshot = new TreeVisibilitySnapshot();
            snapshot.camera = camera;
            snapshot.frame = Time.frameCount;
            snapshot.requestedBackend = m_RequestedBackend;
            snapshot.actualBackend = m_ActualBackend;
            snapshot.hzb = m_ActualBackend == TreeVisibilityBackend.GPU && m_Gpu.HasHzb;
            snapshot.coarseCells = new byte[cells.Length];
            snapshot.terrainCells = new byte[cells.Length];
            snapshot.lods = new int[m_InstanceCount];
            snapshot.finalVisible = new bool[m_InstanceCount];
            if (m_ActualBackend == TreeVisibilityBackend.CPU)
            {
                snapshot.coarseCells = boundSector.visibleMap.ToArray();
                snapshot.cpuMasks = m_ChunkMasks.ToArray();
                snapshot.lods = m_LodNow.ToArray();
                var terrainTest = new TreeCullLodJob();
                {
                    terrainTest.heightRes = (m_OcclusionMode & TreeOcclusionMode.Terrain) != 0 ? m_HeightRes : 0;
                    terrainTest.heights = m_Heights;
                    terrainTest.viewOrigin = camera.transform.position;
                    terrainTest.terrainPos = m_TerrainPos;
                    terrainTest.terrainSize = m_TerrainSize;
                }
                for (int i = 0; i < cells.Length; ++i)
                {
                    if (snapshot.coarseCells[i] == 0) { continue; }
                    bool occluded = terrainTest.heightRes > 1 && terrainTest.TerrainOccludes(boundSector.nativeSections[i].boundBox) != 0;
                    snapshot.terrainCells[i] = occluded ? (byte)0 : (byte)1;
                }
            }
            else
            {
                snapshot.pending += 3;
                if (!m_Gpu.RequestCellReadback(cmdBuffer, camera, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.gpuCells = request.GetData<uint>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                })) { snapshot.failed = true; --snapshot.pending; }
                if (!m_Gpu.RequestLodReadback(cmdBuffer, camera, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.gpuLods = request.GetData<int4>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                })) { snapshot.failed = true; --snapshot.pending; }
                if (!m_Gpu.RequestStateReadback(cmdBuffer, camera, request =>
                {
                    try
                    {
                        if (request.hasError) { snapshot.failed = true; }
                        else { snapshot.fadeState = request.GetData<float4>().ToArray(); }
                    }
                    catch (Exception) { snapshot.failed = true; }
                    finally { snapshot.FinishRequest(); }
                })) { snapshot.failed = true; --snapshot.pending; }
            }

            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                for (int bucketIndex = 0; bucketIndex < 3; ++bucketIndex)
                {
                    int upperBound = m_ActualBackend == TreeVisibilityBackend.GPU ? m_InstanceCount : batch.uploadedCount[bucketIndex];
                    if (upperBound <= 0) { continue; }
                    TreeVisibilitySnapshot.Bucket captured = new TreeVisibilitySnapshot.Bucket();
                    captured.lod = batch.meshIndex;
                    captured.bucket = bucketIndex;
                    captured.weighted = m_FadeMode == TreeLodFadeMode.Distance && m_AllowFade && bucketIndex != 0;
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
                    if (captured.weighted)
                    {
                        ++snapshot.pending;
                        cmdBuffer.RequestAsyncReadback(batch.weightBuffers[bucketIndex - 1], upperBound * sizeof(float), 0, request =>
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
                int count = math.min((int)bucket.args[1], bucket.indices.Length);
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
        public void DispatchShadow(CommandBuffer cmdBuffer, Camera camera, in int cascadeIndex, Plane[] planes,
            in float lodHysteresis, MaterialPropertyBlock propertyBlock)
        {
            if (m_InstanceCount <= 0 || m_Batches == null || m_ShadowViews == null || cascadeIndex < 0 || cascadeIndex >= 4) { return; }
            if (m_ResetShadowArgs) { ClearShadowArgs(cmdBuffer); m_ResetShadowArgs = false; }
            TreeShadowView view = GetShadowView(camera);
            bool gpu = m_ActualBackend == TreeVisibilityBackend.GPU;
            if (gpu && !m_Gpu.IsReady) { return; }
            if (!gpu)
            {
                view.EnsureCpu(m_InstanceCount, m_Batches.Count);
                for (int i = 0; i < 6; ++i)
                {
                    FrustumPlane plane = new FrustumPlane();
                    plane.normalDist = new float4((float3)planes[i].normal, planes[i].distance);
                    view.planes[i] = plane;
                }
                var cullJob = new TreeShadowCullJob();
                {
                    cullJob.sections = boundSector.nativeSections;
                    cullJob.cells = m_Cells;
                    cullJob.bounds = m_Bounds;
                    cullJob.planes = view.planes;
                    cullJob.lodScreenSizes = m_LodScreenSizes;
                    cullJob.lodStable = view.lodStable[cascadeIndex];
                    cullJob.indices = view.indices;
                    cullJob.counts = view.counts;
                    cullJob.origin = camera.transform.position;
                    cullJob.projection = Geometry.GetLodProjectionMatrix(camera);
                    cullJob.hysteresis = math.clamp(lodHysteresis, 0f, 0.5f);
                }
                cullJob.Schedule().Complete();
            }
            try
            {
                for (int lod = 0; lod < m_Batches.Count; ++lod)
                {
                    TreeLodBatch batch = m_Batches[lod];
                    TreeShadowBatch shadowBatch = view.cascades[cascadeIndex][lod];
                    if (shadowBatch == null)
                    {
                        shadowBatch = new TreeShadowBatch();
                        shadowBatch.Initialize(batch.sectionIndexs.Length);
                        view.cascades[cascadeIndex][lod] = shadowBatch;
                    }
                    int count = gpu ? m_InstanceCount : view.counts[lod];
                    shadowBatch.EnsureCapacity(math.max(count, 1));
                    if (!gpu && count > 0)
                    {
                        cmdBuffer.SetBufferData(shadowBatch.indexBuffer, view.indices, lod * m_InstanceCount, 0, count);
                    }
                    for (int submesh = 0; submesh < batch.sectionIndexs.Length; ++submesh)
                    {
                        uint[] args = batch.argsValues[submesh];
                        args[1] = gpu ? 0 : (uint)count;
                        cmdBuffer.SetBufferData(shadowBatch.argsBuffers[submesh], args);
                    }
                }
            }
            catch (Exception error)
            {
                if (!gpu) { throw; }
                m_Gpu.Fail("Tree GPU shadow allocation failed: " + error.Message);
                return;
            }
            if (gpu) { m_Gpu.DispatchShadow(cmdBuffer, camera, cascadeIndex, planes, lodHysteresis, view, m_Batches); }
            cmdBuffer.DisableShaderKeyword("FOLIAGE_LOD_WEIGHT");
            cmdBuffer.DisableShaderKeyword("FOLIAGE_GPU_TEMPORAL");
            for (int lod = 0; lod < m_Batches.Count; ++lod)
            {
                if (!gpu && view.counts[lod] == 0) { continue; }
                TreeLodBatch batch = m_Batches[lod];
                TreeShadowBatch shadowBatch = view.cascades[cascadeIndex][lod];
                Mesh mesh = tree.meshes[batch.meshIndex];
                for (int submeshIndex = 0; submeshIndex < batch.sectionIndexs.Length; ++submeshIndex)
                {
                    int submesh = batch.sectionIndexs[submeshIndex];
                    Material material = tree.materials[batch.materialIndexs[submeshIndex]];
                    int passIndex = material.FindPass("FoliageShadow");
                    if (passIndex < 0) { continue; }
                    propertyBlock.Clear();
                    propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, shadowBatch.indexBuffer);
                    propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, m_MatrixBuffer);
                    propertyBlock.SetFloat(TreeShaderID.LodFadeEnable, 0f);
                    cmdBuffer.DrawMeshInstancedIndirect(mesh, submesh, material, passIndex, shadowBatch.argsBuffers[submeshIndex], 0, propertyBlock);
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
            if (m_Cells.IsCreated) { m_Cells.Dispose(); }
            if (m_LodScreenSizes.IsCreated) { m_LodScreenSizes.Dispose(); }
            if (m_LodDither.IsCreated) { m_LodDither.Dispose(); }
            if (m_ChunkMasks.IsCreated) { m_ChunkMasks.Dispose(); }
            if (m_LodNow.IsCreated) { m_LodNow.Dispose(); }
            if (m_LodScreenSqr.IsCreated) { m_LodScreenSqr.Dispose(); }
            if (m_LodWeight.IsCreated) { m_LodWeight.Dispose(); }
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
            if (m_IndexScratch.IsCreated) { m_IndexScratch.Dispose(); }
            if (m_WeightScratch.IsCreated) { m_WeightScratch.Dispose(); }
            m_PlaneScratch = null;
            m_InstanceCount = 0;
            m_ActualBackend = TreeVisibilityBackend.CPU;
            m_LastBackendWarning = null;
            m_PendingVisibility = false;
            m_ResetArgs = false;
            m_ResetShadowArgs = false;
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
                    else
                    {
                        bool visible = boundSector.visibleMap.IsCreated && boundSector.visibleMap[i] != 0;
                        color = m_ActualBackend == TreeVisibilityBackend.GPU ? Color.gray : visible ? Color.green : Color.red;
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
