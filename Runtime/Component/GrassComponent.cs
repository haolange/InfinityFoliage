using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage/Grass Component")]
    public unsafe class GrassComponent : FoliageComponent
    {
        [Header("Setting")]
        public int numSection = 16;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
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

        private int m_Counter;
        private int m_UploadNeed;
        private int m_LastUploadFrame;
        private bool m_BuildScheduled;
        private bool m_BuildCompleted;
        private JobHandle m_BuildHandle;
        private float3 m_ViewOrigin;
        private float m_DrawDistance;
        private MaterialPropertyBlock m_PropertyBlock;
        private int[] m_Offsets;
        private int[] m_Counts;
        private int[] m_TypeCounts;
        private int[] m_Picked;
        private float[] m_PivotX;
        private float[] m_PivotZ;
        private byte[] m_Visible;
        private byte[] m_Uploaded;
        private DrawRun[] m_Runs;

        protected override void OnRegiste()
        {
            m_Counter = 0;
            m_LastUploadFrame = -1;
            m_BuildScheduled = false;
            m_BuildCompleted = false;
            m_BuildHandle = default;
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Grass;
            terrainData = terrain.terrainData;
            m_DrawDistance = terrain.detailObjectDistance;
            terrain.detailObjectDistance = 0;

            boundSector.BuildNativeCollection();
            int sectionCount = boundSector.sections.Length;
            m_Offsets = new int[sectionCount];
            m_Counts = new int[sectionCount];
            m_TypeCounts = new int[sectionCount];
            m_Picked = new int[sectionCount];
            m_PivotX = new float[sectionCount];
            m_PivotZ = new float[sectionCount];
            m_Visible = new byte[sectionCount];
            m_Uploaded = new byte[sectionCount];
            m_Runs = new DrawRun[sectionCount];
            m_UploadNeed = 0;
            m_ViewOrigin = float3.zero;

            m_PropertyBlock = new MaterialPropertyBlock();
            m_PropertyBlock.SetInt(GrassShaderID.TerrainSize, sectorSize);
            m_PropertyBlock.SetTexture(GrassShaderID.TerrainNormalmap, terrain.normalmapTexture);
            m_PropertyBlock.SetTexture(GrassShaderID.TerrainHeightmap, terrainData.heightmapTexture);
            m_PropertyBlock.SetVector(GrassShaderID.TerrainPivotScaleY, new float4(transform.position, terrainScaleY));

            if (grassSectors != null)
            {
                foreach (GrassSector grassSector in grassSectors)
                {
                    grassSector.Init(terrainData);
                    grassSector.BindPivots(boundSector.sections);
                }
            }

            CollectCellCounts();
            if (Application.isPlaying)
            {
                ScheduleBuild();
            }
        }

        protected override void UnRegiste()
        {
            if (m_BuildScheduled && !m_BuildCompleted)
            {
                m_BuildHandle.Complete();
                m_BuildCompleted = true;
            }

            boundSector.ReleaseNativeCollection();
            terrain.detailObjectDistance = m_DrawDistance;
            if (grassSectors == null) { return; }
            foreach (GrassSector grassSector in grassSectors)
            {
                grassSector.Release();
            }
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

            Geometry.DrawBound(boundSector.bound, Color.white);

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
                    Geometry.DrawBound(boundSector.sections[i].boundBox, boundSector.visibleMap[i] == 1 ? Color.green : Color.red);
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
            m_ViewOrigin = viewOrigin;
            taskHandles.Add(boundSector.InitView(m_DrawDistance, new float4(viewOrigin, 1), planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollectCellCounts()
        {
            m_UploadNeed = 0;
            int sectionCount = boundSector.sections.Length;
            for (int i = 0; i < sectionCount; ++i)
            {
                int count = 0;
                if (grassSectors != null)
                {
                    for (int j = 0; j < grassSectors.Length; ++j)
                    {
                        count += grassSectors[j].sections[i].count;
                    }
                }
                m_Counts[i] = count;
                m_PivotX[i] = boundSector.sections[i].pivotPosition.x;
                m_PivotZ[i] = boundSector.sections[i].pivotPosition.y;
                if (count > 0) { ++m_UploadNeed; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ScheduleBuild()
        {
            if (grassSectors == null || m_BuildScheduled) { return; }

            float densityScale = terrain.detailObjectDensity;
            JobHandle combined = default;
            bool any = false;
            for (int i = 0; i < grassSectors.Length; ++i)
            {
                JobHandle handle = grassSectors[i].ScheduleBuild(sectionSize, densityScale);
                if (handle.Equals(default(JobHandle))) { continue; }
                if (!any)
                {
                    combined = handle;
                    any = true;
                }
                else
                {
                    combined = JobHandle.CombineDependencies(combined, handle);
                }
            }

            m_BuildHandle = combined;
            m_BuildScheduled = true;
            m_BuildCompleted = !any;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void FlushPendingUploads()
        {
            if (grassSectors == null || !m_BuildScheduled) { return; }
            if (!m_BuildCompleted)
            {
                if (!m_BuildHandle.IsCompleted) { return; }
                m_BuildHandle.Complete();
                m_BuildCompleted = true;
            }

            int frame = Time.frameCount;
            if (frame == m_LastUploadFrame) { return; }
            if (m_Counter >= m_UploadNeed) { return; }

            int sectionCount = boundSector.sections.Length;
            for (int i = 0; i < sectionCount; ++i)
            {
                m_Visible[i] = boundSector.visibleMap[i];
            }

            int pickedCount = FoliageLogic.PickUploadCells(m_Uploaded, m_Visible, m_Counts, m_PivotX, m_PivotZ, m_ViewOrigin.x, m_ViewOrigin.z, FoliageLogic.GrassSetupBatch, m_Picked);
            if (pickedCount <= 0) { return; }

            for (int i = 0; i < grassSectors.Length; ++i)
            {
                grassSectors[i].CollectDraw(m_Offsets, m_TypeCounts);
                int runCount = FoliageLogic.MergeUploadRuns(m_Picked, pickedCount, m_Offsets, m_TypeCounts, m_Runs);
                grassSectors[i].FlushUploadRuns(m_Runs, runCount);
            }

            for (int i = 0; i < pickedCount; ++i)
            {
                m_Uploaded[m_Picked[i]] = 1;
            }
            m_Counter += pickedCount;
            m_LastUploadFrame = frame;
            if (m_Counter >= m_UploadNeed)
            {
                for (int i = 0; i < grassSectors.Length; ++i)
                {
                    grassSectors[i].ReleaseScatterScratch();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            if (grassSectors == null) { return; }
            int sectionCount = boundSector.sections.Length;
            for (int i = 0; i < sectionCount; ++i)
            {
                m_Visible[i] = (m_Uploaded[i] != 0) ? boundSector.visibleMap[i] : (byte)0;
            }

            for (int j = 0; j < grassSectors.Length; ++j)
            {
                GrassSector grassSector = grassSectors[j];
                grassSector.CollectDraw(m_Offsets, m_TypeCounts);
                int runCount = FoliageLogic.MergeVisibleRuns(m_Offsets, m_TypeCounts, m_Visible, numSection, m_Runs);
                grassSector.DrawRuns(cmdBuffer, m_PropertyBlock, m_Runs, runCount, passIndex);
            }
        }
    }
}
