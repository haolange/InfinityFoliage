using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
#if UNITY_EDITOR
    public enum TreeBoundsMode
    {
        Component,
        Cells,
        Instances
    }
#endif

    [AddComponentMenu("HG/Foliage/Tree Component")]
    public unsafe class TreeComponent : FoliageComponent
    {
        [Header("Setting")]
        public int numSection = 16;
        [HideInInspector]
        public string assetKey;
        [Range(0.05f, 2f)]
        public float fadeDuration = 0.5f;
        [Header("Rendering Overrides")]
        public bool overrideDrawDistance;
        [Min(0f)] public float drawDistanceOverride;
        public bool overrideOcclusion;
        public TreeOcclusionMode occlusionOverride = TreeOcclusionMode.TerrainAndHzb;
        public bool overrideLodFade;
        public bool lodFadeOverride = true;
        public bool overrideLodHysteresis;
        [Range(0f, 0.5f)] public float lodHysteresisOverride = 0.08f;
        public bool overrideMainShadows;
        public bool castMainShadowsOverride = true;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
        public TreeBoundsMode boundsMode = TreeBoundsMode.Instances;
        public Camera debugCamera;
        public int debugTreeIndex = -1;
        public int debugCandidateIndex = -1;
        private bool m_SnapshotRequested;
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
        private FoliageRenderSettings m_RenderSettings;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetRenderSettings(FoliageRenderSettings settings)
        {
            m_RenderSettings = settings;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float ResolveDrawDistance(FoliageRenderSettings settings)
        {
            return overrideDrawDistance ? Mathf.Max(0f, drawDistanceOverride) :
                drawDistance * Mathf.Max(0f, settings.treeDistanceScale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TreeOcclusionMode ResolveOcclusion(FoliageRenderSettings settings)
        {
            return overrideOcclusion ? occlusionOverride : settings.treeOcclusion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool CastMainShadows(FoliageRenderSettings settings)
        {
            return overrideMainShadows ? castMainShadowsOverride : settings.castTreeMainShadows;
        }

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

        private void DrawBounds()
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;
            if (boundSector == null || treeSectors == null) { return; }
            Geometry.DrawBound(boundSector.bound, Color.white);
            if (boundsMode == TreeBoundsMode.Component) { return; }
            foreach (TreeSector treeSector in treeSectors)
            {
                if (treeSector != null) { treeSector.DrawBounds(boundsMode, debugCamera); }
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds();
        }

        public void RequestVisibilitySnapshot()
        {
            m_SnapshotRequested = true;
        }

        public void ClearVisibilitySnapshot()
        {
            m_SnapshotRequested = false;
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i) { treeSectors[i]?.ClearVisibilitySnapshot(); }
        }

        public void CaptureVisibilitySnapshot(CommandBuffer cmdBuffer, Camera camera)
        {
            if (!m_SnapshotRequested || !m_RuntimeReady || !m_Resident || camera == null) { return; }
            Camera targetCamera = debugCamera != null ? debugCamera : Camera.main;
            if (targetCamera != null && targetCamera != camera) { return; }
            if (targetCamera == null && camera.cameraType != CameraType.Game) { return; }
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].CaptureVisibilitySnapshot(cmdBuffer, camera); }
            }
            m_SnapshotRequested = false;
        }

        public string VisibilitySnapshotSummary()
        {
            if (treeSectors == null) { return "No loaded tree sectors."; }
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (!m_RuntimeReady || m_SectorReady == null || !m_SectorReady[i]) { continue; }
                summary.Append("Tree ").Append(treeSectors[i].treeIndex).Append(": ")
                    .Append(treeSectors[i].VisibilitySnapshotSummary()).Append('\n');
                if (treeSectors[i].treeIndex == debugTreeIndex && debugCandidateIndex >= 0)
                {
                    summary.Append(treeSectors[i].VisibilitySnapshotCandidate(debugCandidateIndex)).Append('\n');
                    summary.Append(treeSectors[i].VisibilitySnapshotGpuRejections(12)).Append('\n');
                }
            }
            return summary.Length == 0 ? "Tree assets are loading." : summary.ToString();
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
                if (m_SectorReady[i]) { treeSectors[i].InitView(ResolveDrawDistance(m_RenderSettings), viewOrigin, matrixProj, planes, taskHandles); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            if (!m_RuntimeReady || !m_Resident) { return; }
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i])
                {
                    TreeOcclusionMode mode = ResolveOcclusion(m_RenderSettings);
                    bool lodFade = overrideLodFade ? lodFadeOverride : m_RenderSettings.treeLodFade;
                    float hysteresis = overrideLodHysteresis ? lodHysteresisOverride : m_RenderSettings.treeLodHysteresis;
                    treeSectors[i].DispatchSetup(camera, ResolveDrawDistance(m_RenderSettings), viewOrigin, matrixProj,
                        m_Planes, mode, lodFade, hysteresis, taskHandles);
                }
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
                if (m_SectorReady[i])
                {
                    bool hzb = (ResolveOcclusion(m_RenderSettings) & TreeOcclusionMode.Hzb) != 0;
                    treeSectors[i].FlushPendingUploads(cmdBuffer, cameraDepth, zParams, m_DepthViewProj, hzb, fadeDuration, dt);
                }
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
            float hysteresis = overrideLodHysteresis ? lodHysteresisOverride : m_RenderSettings.treeLodHysteresis;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (m_SectorReady[i]) { treeSectors[i].DispatchShadow(cmdBuffer, camera, cascadeIndex, planes, hysteresis, m_PropertyBlock); }
            }
        }
    }
}
