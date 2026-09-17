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

        public void Dispose()
        {
            for (int i = 0; i < indexBuffers.Length; ++i) { indexBuffers[i].Dispose(); }
            for (int i = 0; i < argsBuffers.Length; ++i) { argsBuffers[i].Dispose(); }
            if (stableMask.IsCreated) { stableMask.Dispose(); }
            if (fadeOutMask.IsCreated) { fadeOutMask.Dispose(); }
            if (fadeInMask.IsCreated) { fadeInMask.Dispose(); }
            if (bucketCounts.IsCreated) { bucketCounts.Dispose(); }
        }
    }

    internal class TreeCameraFade
    {
        public NativeArray<int> lodHold;
        public float alpha;
        public bool fading;
        public float3 lastOrigin;
        public float4x4 lastProj;
        public bool hasLast;

        public void Initialize(in int instanceCount)
        {
            lodHold = new NativeArray<int>(instanceCount, Allocator.Persistent);
            for (int i = 0; i < instanceCount; ++i) { lodHold[i] = -1; }
            alpha = 1f;
            fading = false;
            hasLast = false;
        }

        public void Dispose()
        {
            lodHold.Dispose();
        }
    }

    [Serializable]
    public class TreeSector
    {
        public FoliageMesh tree;
        public int treeIndex;
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
        private NativeArray<ulong> m_ChunkMasks;
        private NativeArray<int> m_LodNow;
        private NativeArray<float> m_Heights;
        private List<TreeLodBatch> m_Batches;
        private Dictionary<int, TreeCameraFade> m_FadeViews;
        private TreeCameraFade m_ActiveFade;
        private TreeVisibilityGpu m_Gpu;
        private Camera m_ActiveCamera;
        private uint[] m_ArgsScratch;
        private int[] m_IndexScratch;
        private ulong[] m_MaskScratch;
        private VisibilityRun[] m_RunScratch;
        private Vector4[] m_PlaneScratch;
        private bool m_DitherEnabled;
        private int m_InstanceCount;
        private int m_HeightRes;
        private float m_DrawDistance;
        private float3 m_TerrainPos;
        private float3 m_TerrainSize;
        private bool m_PendingVisibility;

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

            boundSector.BuildNativeCollection();
            int chunkCount = FoliageLogic.VisibilityChunkCount(m_InstanceCount);
            m_LodScreenSizes = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
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

                TreeLodBatch batch = new TreeLodBatch();
                batch.meshIndex = i;
                batch.sectionIndexs = new int[mesh.subMeshCount];
                batch.materialIndexs = new int[mesh.subMeshCount];
                for (int j = 0; j < mesh.subMeshCount; ++j)
                {
                    batch.sectionIndexs[j] = j;
                    batch.materialIndexs[j] = meshLodInfo.materialSlot[j];
                    Material material = tree.materials[meshLodInfo.materialSlot[j]];
                    if (material != null && material.IsKeywordEnabled("LOD_FADE_CROSSFADE"))
                    {
                        m_DitherEnabled = true;
                    }
                }
                batch.Initialize(m_InstanceCount, chunkCount);
                m_Batches.Add(batch);
            }

            m_FadeViews = new Dictionary<int, TreeCameraFade>(4);
            m_ArgsScratch = new uint[5];
            m_IndexScratch = new int[m_InstanceCount];
            m_MaskScratch = new ulong[math.max(chunkCount, 1)];
            m_RunScratch = new VisibilityRun[m_InstanceCount];
            m_PlaneScratch = new Vector4[6];
            m_Gpu = new TreeVisibilityGpu();
            m_Gpu.Initialize(m_InstanceCount, chunkCount, cells.Length);
            if (m_Gpu.IsReady)
            {
                m_Gpu.UploadBounds(m_Bounds, m_InstanceCell);
            }
            if (!m_Heights.IsCreated)
            {
                m_Heights = new NativeArray<float>(1, Allocator.Persistent);
                m_HeightRes = 0;
            }
        }

        TreeCameraFade GetFade(Camera camera)
        {
            int key = camera != null ? camera.GetInstanceID() : 0;
            if (m_FadeViews.TryGetValue(key, out TreeCameraFade fade))
            {
                return fade;
            }
            fade = new TreeCameraFade();
            fade.Initialize(m_InstanceCount);
            m_FadeViews.Add(key, fade);
            return fade;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void InitView(in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0) { return; }
            taskHandles.Add(boundSector.InitView(cullDistance, new float4(viewOrigin, 1), planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void DispatchSetup(Camera camera, in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            if (m_InstanceCount <= 0) { return; }

            m_ActiveCamera = camera;
            m_DrawDistance = cullDistance;
            for (int i = 0; i < 6; ++i)
            {
                float4 plane = planes[i].normalDist;
                m_PlaneScratch[i] = new Vector4(plane.x, plane.y, plane.z, plane.w);
            }

            m_ActiveFade = GetFade(camera);
            int writeLod = m_ActiveFade.fading ? 0 : 1;
            var cullLodJob = new TreeCullLodJob();
            {
                cullLodJob.writeLod = writeLod;
                cullLodJob.heightRes = m_Heights.IsCreated ? m_HeightRes : 0;
                cullLodJob.maxDistance = cullDistance;
                cullLodJob.viewOrigin = viewOrigin;
                cullLodJob.matrixProj = matrixProj;
                cullLodJob.terrainPos = m_TerrainPos;
                cullLodJob.terrainSize = m_TerrainSize;
                cullLodJob.cellVisible = boundSector.visibleMap;
                cullLodJob.instanceCell = m_InstanceCell;
                cullLodJob.bounds = m_Bounds;
                cullLodJob.lodScreenSizes = m_LodScreenSizes;
                cullLodJob.heights = m_Heights;
                cullLodJob.planes = planes;
                cullLodJob.chunkMasks = m_ChunkMasks;
                cullLodJob.lodNow = m_LodNow;
            }
            taskHandles.Add(cullLodJob.Schedule(m_ChunkMasks.Length, 4));
            m_PendingVisibility = true;
            m_ActiveFade.lastOrigin = viewOrigin;
            m_ActiveFade.lastProj = matrixProj;
            m_ActiveFade.hasLast = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushPendingUploads(in float duration, in float deltaTime)
        {
            if (!m_PendingVisibility || m_InstanceCount <= 0 || m_ActiveFade == null) { return; }
            UpdateFade(duration, deltaTime);

            JobHandle emitHandle = default;
            bool scheduled = false;
            int dither = (m_DitherEnabled && m_ActiveFade.fading) ? 1 : 0;
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
                m_Gpu.BuildHzb(m_ActiveCamera);
            }

            int chunkCount = m_ChunkMasks.Length;
            for (int i = 0; i < m_Batches.Count; ++i)
            {
                LowerBatch(m_Batches[i], gpuReady, chunkCount);
            }
            m_PendingVisibility = false;
        }

        void LowerBatch(TreeLodBatch batch, in int gpuReady, in int chunkCount)
        {
            LowerBucket(batch, 0, batch.stableMask, gpuReady, chunkCount);
            LowerBucket(batch, 1, batch.fadeOutMask, gpuReady, chunkCount);
            LowerBucket(batch, 2, batch.fadeInMask, gpuReady, chunkCount);
        }

        void LowerBucket(TreeLodBatch batch, in int bucket, NativeArray<ulong> masks, in int gpuReady, in int chunkCount)
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
                indexBuffer.SetData(m_IndexScratch, 0, 0, written);
                WriteCpuArgs(batch, bucket, written);
                return;
            }

            ResetGpuArgs(batch, bucket);
            m_Gpu.BindCommon(indexBuffer, FirstArgs(batch, bucket), m_ActiveCamera, m_DrawDistance);

            if (codec == (int)VisibilityCodec.BitMaskTransfer)
            {
                m_Gpu.CullChunks(m_MaskScratch, chunkCount, m_InstanceCount, boundSector.visibleMap, m_PlaneScratch, indexBuffer, FirstArgs(batch, bucket));
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

        void ResetGpuArgs(TreeLodBatch batch, in int bucket)
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
                argsBuffer.SetData(m_ArgsScratch);
            }
        }

        void WriteCpuArgs(TreeLodBatch batch, in int bucket, in int instanceCount)
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
                argsBuffer.SetData(m_ArgsScratch);
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
            if (fade.fading)
            {
                fade.alpha = math.min(1f, fade.alpha + (deltaTime / math.max(duration, 0.0001f)));
                if (fade.alpha >= 1f)
                {
                    for (int i = 0; i < m_InstanceCount; ++i)
                    {
                        if (m_LodNow[i] >= 0) { fade.lodHold[i] = m_LodNow[i]; }
                    }
                    fade.fading = false;
                    fade.alpha = 1f;
                }
                return;
            }

            bool startFade = false;
            for (int i = 0; i < m_InstanceCount; ++i)
            {
                int now = m_LodNow[i];
                if (now < 0) { continue; }
                int hold = fade.lodHold[i];
                if (hold < 0)
                {
                    fade.lodHold[i] = now;
                    continue;
                }
                int delta = hold - now;
                if (delta < 0) { delta = -delta; }
                if (delta > 1)
                {
                    fade.lodHold[i] = now;
                    continue;
                }
                if (delta == 1 && m_DitherEnabled)
                {
                    startFade = true;
                }
            }

            if (startFade)
            {
                fade.fading = true;
                fade.alpha = 0f;
            }
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
                if (m_ActiveFade != null && m_ActiveFade.fading && m_DitherEnabled)
                {
                    DrawBucket(cmdBuffer, passIndex, propertyBlock, batch, mesh, (int)LodBucket.FadeOut, m_ActiveFade.alpha, 1f, worldBounds);
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
                propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, indexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, m_MatrixBuffer);
                propertyBlock.SetFloat(TreeShaderID.LodFactor, lodFactor);
                propertyBlock.SetFloat(TreeShaderID.LodFadeEnable, fadeEnable);
                cmdBuffer.DrawMeshInstancedIndirect(mesh, submesh, material, passIndex, argsBuffer, 0, propertyBlock);
            }
        }

        public void Release()
        {
            if (boundSector != null) { boundSector.ReleaseNativeCollection(); }
            if (m_Matrices.IsCreated) { m_Matrices.Dispose(); }
            if (m_Bounds.IsCreated) { m_Bounds.Dispose(); }
            if (m_InstanceCell.IsCreated) { m_InstanceCell.Dispose(); }
            if (m_LodScreenSizes.IsCreated) { m_LodScreenSizes.Dispose(); }
            if (m_ChunkMasks.IsCreated) { m_ChunkMasks.Dispose(); }
            if (m_LodNow.IsCreated) { m_LodNow.Dispose(); }
            if (m_Heights.IsCreated) { m_Heights.Dispose(); }
            if (m_MatrixBuffer != null) { m_MatrixBuffer.Dispose(); }
            if (m_Gpu != null) { m_Gpu.Release(); }
            if (m_Batches != null)
            {
                for (int i = 0; i < m_Batches.Count; ++i) { m_Batches[i].Dispose(); }
            }
            if (m_FadeViews != null)
            {
                foreach (var pair in m_FadeViews) { pair.Value.Dispose(); }
                m_FadeViews.Clear();
            }
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool lodColorState = false)
        {
            if (Application.isPlaying == false || !m_Bounds.IsCreated) { return; }
            for (int i = 0; i < m_Bounds.Length; ++i)
            {
                int chunk = i >> 6;
                int bit = i & 63;
                if (m_ChunkMasks.IsCreated && (m_ChunkMasks[chunk] & (1UL << bit)) == 0) { continue; }
                int lod = m_LodNow.IsCreated ? math.max(m_LodNow[i], 0) : 0;
                Color color = Geometry.LODColors[math.min(lod, Geometry.LODColors.Length - 1)];
                Geometry.DrawBound(m_Bounds[i], lodColorState ? color : Color.blue);
            }
        }
#endif
    }
}
