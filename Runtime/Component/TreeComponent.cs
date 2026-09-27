using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage/Tree Component")]
    public unsafe class TreeComponent : FoliageComponent
    {
        [Header("Setting")]
        public int numSection = 16;
        [HideInInspector]
        public string assetKey;
        [Range(0.05f, 2f)]
        public float fadeDuration = 0.5f;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif
        [HideInInspector]
        public TreeSector[] treeSectors;

        private float drawDistance;
        private MaterialPropertyBlock m_PropertyBlock;
        private FrustumPlane* m_Planes;
        private bool m_RuntimeReady;
        private bool m_Resident;
        private bool[] m_SectorReady;
        private int m_Generation;
        private float[] m_OcclusionHeights;
        private Matrix4x4 m_DepthViewProj;

        internal int sectorSize
        {
            get
            {
                return terrainData.heightmapResolution - 1;
            }
        }

        float[] SampleOcclusionHeight()
        {
            const int res = 64;
            float[] heights = new float[res * res];
            float[,] samples = new float[res, res];
            float step = 1f / (res - 1);
            terrainData.GetInterpolatedHeights(samples, 0, 0, 0f, 0f, res, res, step, step);
            float heightOffset = transform.position.y;
            for (int z = 0; z < res; ++z)
            {
                for (int x = 0; x < res; ++x)
                {
                    heights[(z * res) + x] = heightOffset + samples[z, x];
                }
            }
            return heights;
        }

        protected override void OnRegiste()
        {
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Tree;
            terrainData = terrain.terrainData;
            if (!Application.isPlaying) { return; }
            if (string.IsNullOrEmpty(assetKey) || boundSector == null)
            {
                Debug.LogError("Tree Terrain has no current streamed asset. Rebake this Terrain.", this);
                return;
            }
            drawDistance = terrain.treeDistance;
            terrain.treeDistance = 0;
            m_PropertyBlock = new MaterialPropertyBlock();
            m_SectorReady = new bool[treeSectors == null ? 0 : treeSectors.Length];
            m_RuntimeReady = true;
            FoliageResidency.Register(terrain, this);
        }

        protected override void UnRegiste()
        {
            if (!m_RuntimeReady) { return; }
            FoliageResidency.Unregister(terrain, this);
            terrain.treeDistance = drawDistance;
            m_RuntimeReady = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetResident(in bool resident)
        {
            if (!m_RuntimeReady || m_Resident == resident) { return; }
            m_Resident = resident;
            ++m_Generation;
            if (treeSectors == null) { return; }
            if (!resident)
            {
                m_OcclusionHeights = null;
                for (int i = 0; i < treeSectors.Length; ++i)
                {
                    m_SectorReady[i] = false;
                    treeSectors[i].Release();
                }
                return;
            }
            int generation = m_Generation;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                TreeSector sector = treeSectors[i];
                if (sector == null) { continue; }
                int index = i;
                string path = FoliageAssetCodec.TreePath(assetKey, sector.treeIndex);
                ResourceRequest request = Resources.LoadAsync<TextAsset>(path);
                request.completed += operation =>
                {
                    TextAsset asset = request.asset as TextAsset;
                    if (generation != m_Generation || !m_Resident)
                    {
                        if (asset != null) { Resources.UnloadAsset(asset); }
                        return;
                    }
                    if (asset == null)
                    {
                        Debug.LogError("Missing foliage tree asset " + path + ". Rebake this Terrain.", this);
                        return;
                    }
                    try
                    {
                        sector.LoadCandidates(FoliageAssetCodec.DecodeTree(asset.bytes, sector.treeIndex));
                        float3 terrainPosition = transform.position;
                        sector.Initialize(numSection, sectorSize, terrainPosition, terrainData.bounds);
                        sector.BuildRuntimeData();
                        if (m_OcclusionHeights == null) { m_OcclusionHeights = SampleOcclusionHeight(); }
                        sector.SetHeightField(m_OcclusionHeights, 64, terrainPosition, terrainData.size);
                        m_SectorReady[index] = true;
                        EncapsulateComponentBound();
                    }
                    catch (Exception exception)
                    {
                        sector.Release();
                        Debug.LogException(exception, this);
                    }
                    finally
                    {
                        Resources.UnloadAsset(asset);
                    }
                };
            }
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            if (Application.isPlaying) { return; }
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            int size = terrainData.heightmapResolution - 1;
            boundSector = new BoundSector(0, size, 0, transform.position, terrainData.bounds, false);
            if (treeSectors == null) { return; }
            int expected = numSection * numSection;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (treeSectors[i] == null) { continue; }
                if (treeSectors[i].boundSector != null && treeSectors[i].boundSector.sections != null && treeSectors[i].boundSector.sections.Length == expected)
                {
                    continue;
                }
                treeSectors[i].RebuildSpatialGrid(numSection, size, transform.position, terrainData.bounds);
            }
        }

        public void BakeAfterTransforms()
        {
            if (treeSectors == null) { return; }
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            int size = terrainData.heightmapResolution - 1;
            Aabb terrainBound = terrainData.bounds;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                treeSectors[i].BakeCells(numSection, size, transform.position, terrainBound);
            }
            EncapsulateComponentBound();
        }

        private void DrawBounds(in bool color = false)
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.DrawBounds(color);
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif

        public void EncapsulateComponentBound()
        {
            if (terrainData == null) { return; }
            if (boundSector == null)
            {
                int size = terrainData.heightmapResolution - 1;
                boundSector = new BoundSector(0, size, 0, transform.position, terrainData.bounds, false);
            }
            Aabb bound = boundSector.bound;
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                TreeSector sector = treeSectors[i];
                if (sector == null || !sector.hasPackedBound) { continue; }
                bound.Encapsulate(sector.packedBound);
            }
            boundSector.bound = bound;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            m_Planes = planes;
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].InitView(drawDistance, viewOrigin, matrixProj, planes, taskHandles); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].DispatchSetup(camera, drawDistance, viewOrigin, matrixProj, m_Planes, taskHandles); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetDepthViewProjection(in Matrix4x4 depthViewProj)
        {
            m_DepthViewProj = depthViewProj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void FlushPendingUploads(CommandBuffer cmdBuffer, RTHandle cameraDepth, in Vector4 zParams)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            if (treeSectors == null) { return; }
            float dt = Time.deltaTime;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].FlushPendingUploads(cmdBuffer, cameraDepth, zParams, m_DepthViewProj, fadeDuration, dt); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].DispatchDraw(cmdBuffer, passIndex, m_PropertyBlock); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DispatchShadow(CommandBuffer cmdBuffer, Camera camera, in int cascadeIndex, Plane[] planes)
        {
            if (!m_RuntimeReady || !m_Resident || treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].DispatchShadow(cmdBuffer, camera, cascadeIndex, planes, m_PropertyBlock); }
            }
        }
    }
}
