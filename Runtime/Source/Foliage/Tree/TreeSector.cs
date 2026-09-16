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
        public NativeList<int> stable;
        public NativeList<int> fadeOut;
        public NativeList<int> fadeIn;

        public void Initialize(in int instanceCount)
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
            stable = new NativeList<int>(instanceCount, Allocator.Persistent);
            fadeOut = new NativeList<int>(instanceCount, Allocator.Persistent);
            fadeIn = new NativeList<int>(instanceCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            for (int i = 0; i < indexBuffers.Length; ++i) { indexBuffers[i].Dispose(); }
            for (int i = 0; i < argsBuffers.Length; ++i) { argsBuffers[i].Dispose(); }
            stable.Dispose();
            fadeOut.Dispose();
            fadeIn.Dispose();
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
        private NativeArray<int> m_CellOffset;
        private NativeArray<int> m_CellCount;
        private NativeArray<float> m_LodScreenSizes;
        private NativeArray<int> m_InstanceVisible;
        private NativeArray<int> m_LodNow;
        private List<TreeLodBatch> m_Batches;
        private Dictionary<int, TreeCameraFade> m_FadeViews;
        private TreeCameraFade m_ActiveFade;
        private uint[] m_ArgsScratch;
        private bool m_DitherEnabled;
        private int m_InstanceCount;
        private bool m_PendingCompact;

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
            Aabb localBound = tree.boundBox;

            Aabb[] worldBounds = new Aabb[n];
            for (int i = 0; i < n; ++i)
            {
                InstanceTransform transform = transforms[i];
                float4x4 matrixWorld = float4x4.TRS(transform.position, quaternion.EulerXYZ(transform.rotation), transform.scale);
                worldBounds[i] = Geometry.CaculateWorldBound(localBound, matrixWorld);
                float3 local = transform.position - terrainPosition;
                cellOf[i] = FoliageLogic.CellFromLocal(local.x, local.z, sectionWorld, numSection);
                counts[cellOf[i]]++;
            }

            int[] offsets = new int[cellCount];
            FoliageLogic.PrefixOffsets(counts, offsets);
            Aabb[] packedBounds = new Aabb[n];
            int[] cursor = (int[])offsets.Clone();
            for (int i = 0; i < n; ++i)
            {
                packedBounds[cursor[cellOf[i]]++] = worldBounds[i];
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

        void BuildSoA(in int numSection, in int sectorSize, in float3 terrainPosition)
        {
            int n = transforms.Count;
            int cellCount = numSection * numSection;
            float sectionWorld = sectorSize / (float)math.max(numSection, 1);
            Aabb localBound = tree.boundBox;

            int[] counts = new int[cellCount];
            int[] cellOf = new int[n];
            float4x4[] worldMatrices = new float4x4[n];
            Aabb[] worldBounds = new Aabb[n];
            for (int i = 0; i < n; ++i)
            {
                InstanceTransform transform = transforms[i];
                float4x4 matrixWorld = float4x4.TRS(transform.position, quaternion.EulerXYZ(transform.rotation), transform.scale);
                worldMatrices[i] = matrixWorld;
                worldBounds[i] = Geometry.CaculateWorldBound(localBound, matrixWorld);
                float3 local = transform.position - terrainPosition;
                cellOf[i] = FoliageLogic.CellFromLocal(local.x, local.z, sectionWorld, numSection);
                counts[cellOf[i]]++;
            }

            int[] offsets = new int[cellCount];
            FoliageLogic.PrefixOffsets(counts, offsets);
            m_Matrices = new NativeArray<float4x4>(n, Allocator.Persistent);
            m_Bounds = new NativeArray<Aabb>(n, Allocator.Persistent);
            int[] cursor = (int[])offsets.Clone();
            for (int i = 0; i < n; ++i)
            {
                int dest = cursor[cellOf[i]]++;
                m_Matrices[dest] = worldMatrices[i];
                m_Bounds[dest] = worldBounds[i];
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
            m_CellOffset = new NativeArray<int>(cells.Length, Allocator.Persistent);
            m_CellCount = new NativeArray<int>(cells.Length, Allocator.Persistent);
            for (int i = 0; i < cells.Length; ++i)
            {
                m_CellOffset[i] = cells[i].offset;
                m_CellCount[i] = cells[i].count;
            }

            m_LodScreenSizes = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
            m_InstanceVisible = new NativeArray<int>(m_InstanceCount, Allocator.Persistent);
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
                batch.Initialize(m_InstanceCount);
                m_Batches.Add(batch);
            }

            m_FadeViews = new Dictionary<int, TreeCameraFade>(4);
            m_ArgsScratch = new uint[5];
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

            m_ActiveFade = GetFade(camera);
            int writeLod = m_ActiveFade.fading ? 0 : 1;
            var cullLodJob = new TreeCullLodJob();
            {
                cullLodJob.writeLod = writeLod;
                cullLodJob.maxDistance = cullDistance;
                cullLodJob.viewOrigin = viewOrigin;
                cullLodJob.matrixProj = matrixProj;
                cullLodJob.cellVisible = boundSector.visibleMap;
                cullLodJob.cellOffset = m_CellOffset;
                cullLodJob.cellCount = m_CellCount;
                cullLodJob.bounds = m_Bounds;
                cullLodJob.lodScreenSizes = m_LodScreenSizes;
                cullLodJob.planes = planes;
                cullLodJob.instanceVisible = m_InstanceVisible;
                cullLodJob.lodNow = m_LodNow;
            }
            JobHandle cullHandle = cullLodJob.Schedule(cells.Length, 8);
            taskHandles.Add(cullHandle);
            m_PendingCompact = true;
            m_ActiveFade.lastOrigin = viewOrigin;
            m_ActiveFade.lastProj = matrixProj;
            m_ActiveFade.hasLast = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushPendingUploads(in float duration, in float deltaTime)
        {
            if (!m_PendingCompact || m_InstanceCount <= 0 || m_ActiveFade == null) { return; }
            UpdateFade(duration, deltaTime);

            JobHandle compactHandle = default;
            bool scheduled = false;
            int dither = (m_DitherEnabled && m_ActiveFade.fading) ? 1 : 0;
            for (int i = 0; i < m_Batches.Count; ++i)
            {
                TreeLodBatch batch = m_Batches[i];
                batch.stable.Clear();
                batch.fadeOut.Clear();
                batch.fadeIn.Clear();
                var compactJob = new TreeCompactLodJob();
                {
                    compactJob.meshIndex = batch.meshIndex;
                    compactJob.ditherEnabled = dither;
                    compactJob.instanceVisible = m_InstanceVisible;
                    compactJob.lodHold = m_ActiveFade.lodHold;
                    compactJob.lodNow = m_LodNow;
                    compactJob.stable = batch.stable;
                    compactJob.fadeOut = batch.fadeOut;
                    compactJob.fadeIn = batch.fadeIn;
                }
                JobHandle handle = compactJob.Schedule();
                compactHandle = scheduled ? JobHandle.CombineDependencies(compactHandle, handle) : handle;
                scheduled = true;
            }
            if (scheduled) { compactHandle.Complete(); }
            m_PendingCompact = false;
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
            NativeList<int> indices = batch.stable;
            if (bucket == (int)LodBucket.FadeOut) { indices = batch.fadeOut; }
            else if (bucket == (int)LodBucket.FadeIn) { indices = batch.fadeIn; }
            int count = indices.Length;
            if (count <= 0) { return; }

            int argsIndex = bucket == (int)LodBucket.FadeOut ? 1 : bucket == (int)LodBucket.FadeIn ? 2 : 0;
            ComputeBuffer indexBuffer = batch.indexBuffers[argsIndex];
            indexBuffer.SetData(indices.AsArray(), 0, 0, count);

            for (int s = 0; s < batch.sectionIndexs.Length; ++s)
            {
                int submesh = batch.sectionIndexs[s];
                Material material = tree.materials[batch.materialIndexs[s]];
                ComputeBuffer argsBuffer = batch.argsBuffers[(argsIndex * batch.sectionIndexs.Length) + s];
                m_ArgsScratch[0] = mesh.GetIndexCount(submesh);
                m_ArgsScratch[1] = (uint)count;
                m_ArgsScratch[2] = mesh.GetIndexStart(submesh);
                m_ArgsScratch[3] = mesh.GetBaseVertex(submesh);
                m_ArgsScratch[4] = 0;
                argsBuffer.SetData(m_ArgsScratch);

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
            if (m_CellOffset.IsCreated) { m_CellOffset.Dispose(); }
            if (m_CellCount.IsCreated) { m_CellCount.Dispose(); }
            if (m_LodScreenSizes.IsCreated) { m_LodScreenSizes.Dispose(); }
            if (m_InstanceVisible.IsCreated) { m_InstanceVisible.Dispose(); }
            if (m_LodNow.IsCreated) { m_LodNow.Dispose(); }
            if (m_MatrixBuffer != null) { m_MatrixBuffer.Dispose(); }
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
                if (m_InstanceVisible.IsCreated && m_InstanceVisible[i] == 0) { continue; }
                int lod = m_LodNow.IsCreated ? math.max(m_LodNow[i], 0) : 0;
                Color color = Geometry.LODColors[math.min(lod, Geometry.LODColors.Length - 1)];
                Geometry.DrawBound(m_Bounds[i], lodColorState ? color : Color.blue);
            }
        }
#endif
    }
}
