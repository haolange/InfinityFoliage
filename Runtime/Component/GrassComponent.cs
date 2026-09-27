using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
#if UNITY_EDITOR
    public enum GrassBoundsMode
    {
        Component,
        Cells,
        Pages
    }
#endif

    [AddComponentMenu("HG/Foliage/Grass Component")]
    public unsafe class GrassComponent : FoliageComponent
    {
        [Header("Setting")]
        public int numSection = 16;
        [HideInInspector]
        public string assetKey;
        [Header("Rendering")]
        public bool overrideDrawDistance;
        [Min(0f)] public float drawDistanceOverride;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
        public GrassBoundsMode boundsMode = GrassBoundsMode.Cells;
#endif

        internal int sectorSize
        {
            get
            {
                return terrainData.heightmapResolution - 1;
            }
        }

        public int sectionSize
        {
            get
            {
                return sectorSize / numSection;
            }
        }

        internal float terrainScaleY
        {
            get
            {
                return terrainData.size.y;
            }
        }

        [HideInInspector]
        public GrassSector[] grassSectors;

        private bool m_RuntimeReady;
        private bool m_Resident;
        private bool[] m_DetailResident;
        private int[] m_PageLastUsed;
        private int[] m_PageLastVisible;
        private byte[] m_Visible;
        private float m_DensityScale;
        private float m_DrawDistance;
        private FoliageRenderSettings m_RenderSettings;
        private MaterialPropertyBlock m_PropertyBlock;

        internal float drawDistance { get { return m_DrawDistance; } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetRenderSettings(FoliageRenderSettings settings)
        {
            m_RenderSettings = settings;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float ResolveDrawDistance(FoliageRenderSettings settings)
        {
            return overrideDrawDistance ? Mathf.Max(0f, drawDistanceOverride) :
                m_DrawDistance * Mathf.Max(0f, settings.grassDistanceScale);
        }

        protected override void OnRegiste()
        {
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Grass;
            terrainData = terrain.terrainData;
            if (!Application.isPlaying) { return; }
            if (string.IsNullOrEmpty(assetKey) || boundSector == null || boundSector.sections == null)
            {
                Debug.LogError("Grass Terrain has no current streamed asset. Rebake this Terrain.", this);
                return;
            }
            if (grassSectors != null)
            {
                DetailPrototype[] prototypes = terrainData.detailPrototypes;
                foreach (GrassSector grassSector in grassSectors)
                {
                    if (grassSector == null || grassSector.grassIndex < 0 || grassSector.grassIndex >= prototypes.Length ||
                        prototypes[grassSector.grassIndex].prototype == null)
                    {
                        Debug.LogError("Grass Terrain has an invalid detail prototype. Rebake this Terrain.", this);
                        return;
                    }
                }
            }
            m_DrawDistance = terrain.detailObjectDistance;
            try
            {
                terrain.detailObjectDistance = 0;
                boundSector.BuildNativeCollection();
                m_PropertyBlock = new MaterialPropertyBlock();
                m_PropertyBlock.SetInt(GrassShaderID.TerrainSize, sectorSize);
                m_PropertyBlock.SetTexture(GrassShaderID.TerrainNormalmap, terrain.normalmapTexture);
                m_PropertyBlock.SetTexture(GrassShaderID.TerrainHeightmap, terrainData.heightmapTexture);
                m_PropertyBlock.SetVector(GrassShaderID.TerrainPivotScaleY, new float4(transform.position, terrainScaleY));

                m_DensityScale = Mathf.Clamp01(terrain.detailObjectDensity);
                if (grassSectors != null)
                {
                    foreach (GrassSector grassSector in grassSectors)
                    {
                        grassSector.Init(terrainData, assetKey, numSection, transform.position);
                        grassSector.SetDensityScale(m_DensityScale);
                    }
                }
                m_DetailResident = new bool[(grassSectors == null ? 0 : grassSectors.Length) * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis];
                m_PageLastUsed = new int[m_DetailResident.Length];
                m_PageLastVisible = new int[m_DetailResident.Length];
                for (int i = 0; i < m_PageLastVisible.Length; ++i) { m_PageLastVisible[i] = -1; }
                m_Visible = new byte[boundSector.sections.Length];
                m_RuntimeReady = true;
                FoliageResidency.Register(terrain, this);
            }
            catch (System.Exception exception)
            {
                m_RuntimeReady = false;
                if (grassSectors != null)
                {
                    foreach (GrassSector grassSector in grassSectors) { grassSector?.Release(); }
                }
                boundSector.ReleaseNativeCollection();
                terrain.detailObjectDistance = m_DrawDistance;
                Debug.LogException(exception, this);
            }
        }

        protected override void UnRegiste()
        {
            if (!m_RuntimeReady) { return; }
            FoliageResidency.Unregister(terrain, this);
            boundSector.ReleaseNativeCollection();
            terrain.detailObjectDistance = m_DrawDistance;
            if (grassSectors != null)
            {
                foreach (GrassSector grassSector in grassSectors) { grassSector.Release(); }
            }
            m_RuntimeReady = false;
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            if (Application.isPlaying) { return; }
            terrain = GetComponent<Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;
            if (boundSector != null && boundSector.sections != null && boundSector.sections.Length == numSection * numSection)
            {
                return;
            }

            TerrainTexture heightTexture = new TerrainTexture(sectorSize);
            heightTexture.TerrainDataToHeightmap(terrainData);

            boundSector = new BoundSector(numSection, sectorSize, sectionSize, transform.position, terrainData.bounds);
            boundSector.BuildBounds(sectorSize, sectionSize, terrainScaleY, transform.position, heightTexture.HeightMap);

            heightTexture.Release();
        }

        private void DrawBounds()
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;
            if (boundSector == null) { return; }

            Geometry.DrawBound(boundSector.bound, Color.white);
            if (boundsMode == GrassBoundsMode.Component) { return; }

            if (boundsMode == GrassBoundsMode.Pages)
            {
                int axis = FoliageAssetCodec.PageAxis;
                for (int x = 0; x < axis; ++x)
                {
                    for (int y = 0; y < axis; ++y)
                    {
                        bool resident = false;
                        for (int s = 0; grassSectors != null && s < grassSectors.Length; ++s)
                        {
                            if (IsDetailResident(s, x, y)) { resident = true; break; }
                        }
                        Vector3 size = terrainData.size;
                        Vector3 pageSize = new Vector3(size.x / axis, boundSector.bound.size.y, size.z / axis);
                        Vector3 center = transform.position + new Vector3((x + 0.5f) * pageSize.x,
                            boundSector.bound.center.y - transform.position.y, (y + 0.5f) * pageSize.z);
                        Geometry.DrawBound(new Bounds(center, pageSize), resident ? Color.green : Color.yellow);
                    }
                }
                return;
            }

            for (int i = 0; i < boundSector.sections.Length; ++i)
            {
                int count = 0;
                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    GrassSector grassSector = grassSectors[j];
                    GrassSection grassSection = grassSector.sections[i];
                    count += grassSection.count;
                }

                if (count > 0)
                {
                    Color color = !boundSector.visibleMap.IsCreated ? Color.gray :
                        boundSector.visibleMap[i] == 1 ? Color.green : Color.red;
                    Geometry.DrawBound(boundSector.sections[i].boundBox, color);
                }
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds();
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            taskHandles.Add(boundSector.InitView(ResolveDrawDistance(m_RenderSettings), new float4(viewOrigin, 1), planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
        }

        public void SetDensityScale(in float scale)
        {
            if (terrain == null) { terrain = GetComponent<Terrain>(); }
            terrain.detailObjectDensity = Mathf.Clamp01(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetResident(in bool resident)
        {
            if (!m_RuntimeReady || m_Resident == resident) { return; }
            m_Resident = resident;
            if (grassSectors == null) { return; }
            for (int i = 0; i < grassSectors.Length; ++i) { grassSectors[i].SetResident(resident); }
            if (!resident && m_DetailResident != null) { System.Array.Clear(m_DetailResident, 0, m_DetailResident.Length); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsDetailResident(in int species, in int x, in int y)
        {
            int index = (species * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis) + x * FoliageAssetCodec.PageAxis + y;
            return m_DetailResident != null && m_DetailResident[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsDetailEmpty(in int species, in int x, in int y)
        {
            return grassSectors != null && grassSectors[species].IsDetailEmpty(x, y);
        }

        internal int ResidentDetailPageCount()
        {
            if (m_DetailResident == null) { return 0; }
            int count = 0;
            for (int i = 0; i < m_DetailResident.Length; ++i) { if (m_DetailResident[i]) { ++count; } }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int PageLastUsed(in int species, in int x, in int y)
        {
            int index = (species * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis) + x * FoliageAssetCodec.PageAxis + y;
            return m_PageLastUsed == null ? 0 : m_PageLastUsed[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int PageLastVisible(in int species, in int x, in int y)
        {
            int index = (species * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis) + x * FoliageAssetCodec.PageAxis + y;
            return m_PageLastVisible == null ? -1 : m_PageLastVisible[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkPageVisible(in int species, in int x, in int y, in int frame)
        {
            int index = (species * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis) + x * FoliageAssetCodec.PageAxis + y;
            m_PageLastVisible[index] = frame;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetDetailResident(in int species, in int x, in int y, in bool resident, in int frame)
        {
            if (!m_RuntimeReady || !m_Resident || grassSectors == null) { return; }
            int index = (species * FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis) + x * FoliageAssetCodec.PageAxis + y;
            bool changed = m_DetailResident[index] != resident;
            if (changed) { m_DetailResident[index] = resident; }
            if (changed || resident) { grassSectors[species].SetDetailResident(x, y, resident); }
            if (resident) { m_PageLastUsed[index] = frame; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void FlushPendingUploads(CommandBuffer cmdBuffer, RTHandle cameraDepth, in Vector4 zParams)
        {
            if (!m_RuntimeReady || !m_Resident || grassSectors == null) { return; }
            float scale = Mathf.Clamp01(terrain.detailObjectDensity);
            if (scale != m_DensityScale)
            {
                m_DensityScale = scale;
                for (int i = 0; i < grassSectors.Length; ++i) { grassSectors[i].SetDensityScale(scale); }
            }
            for (int i = 0; i < grassSectors.Length; ++i) { grassSectors[i].Flush(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            if (!m_RuntimeReady || !m_Resident || grassSectors == null) { return; }
            for (int i = 0; i < m_Visible.Length; ++i) { m_Visible[i] = boundSector.visibleMap[i]; }
            for (int j = 0; j < grassSectors.Length; ++j)
            {
                grassSectors[j].Draw(cmdBuffer, m_PropertyBlock, m_Visible, numSection, passIndex);
            }
        }
    }
}
