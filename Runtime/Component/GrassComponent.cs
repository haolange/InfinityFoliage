using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
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
        public FGrassSector[] grassSectors;

        private int m_Counter;
        private float m_DrawDistance;
        private float m_LastNumSection;
        private MaterialPropertyBlock m_PropertyBlock;

        protected override void OnRegiste()
        {
            m_Counter = 0;
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Grass;
            terrainData = terrain.terrainData;
            m_DrawDistance = terrain.detailObjectDistance;
            terrain.detailObjectDistance = 0;

            boundSector.BuildNativeCollection();
            m_PropertyBlock = new MaterialPropertyBlock();
            m_PropertyBlock.SetInt(GrassShaderID.TerrainSize, sectorSize);
            m_PropertyBlock.SetTexture(GrassShaderID.TerrainNormalmap, terrain.normalmapTexture);
            m_PropertyBlock.SetTexture(GrassShaderID.TerrainHeightmap, terrainData.heightmapTexture);
            m_PropertyBlock.SetVector(GrassShaderID.TerrainPivotScaleY, new float4(transform.position, terrainScaleY));

            foreach(FGrassSector grassSector in grassSectors)
            {
                grassSector.Init(terrainData);
            }
        }

        protected override void UnRegiste()
        {
            boundSector.ReleaseNativeCollection();
            terrain.detailObjectDistance = m_DrawDistance;

            foreach (FGrassSector grassSector in grassSectors)
            {
                grassSector.Release();
            }
        }

        #if UNITY_EDITOR
        public void OnSave()
        {
            if(m_LastNumSection == numSection) { return; }
            m_LastNumSection = numSection;

            terrain = GetComponent<Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            TerrainTexture HeightTexture = new TerrainTexture(sectorSize);
            HeightTexture.TerrainDataToHeightmap(terrainData);

            boundSector = new FBoundSector(numSection, sectorSize, sectionSize, transform.position, terrainData.bounds);
            boundSector.BuildBounds(sectorSize, sectionSize, terrainScaleY, transform.position, HeightTexture.HeightMap);

            HeightTexture.Release();
        }

        private void DrawBounds()
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;

            Geometry.DrawBound(boundSector.bound, Color.white);

            for (int i = 0; i < boundSector.m_Sections.Length; ++i)
            {
                int count = -1;
                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    FGrassSection grassSection = grassSector.sections[i];
                    count += grassSection.instanceCount;
                }

                if(count > 0)
                {
                    Geometry.DrawBound(boundSector.m_Sections[i].boundBox, boundSector.visibleMap[i] == 1 ? Color.green : Color.red);
                }
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds();
        }
        #endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, in FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            taskHandles.Add(boundSector.InitView(m_DrawDistance, new float4(viewOrigin, 1), planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            if(m_Counter == boundSector.m_Sections.Length) { return; }

            int limit = 16;
            while (limit > 0 && m_Counter < boundSector.m_Sections.Length)
            {
                FBoundSection boundSection = boundSector.m_Sections[m_Counter];

                for (int i = 0; i < grassSectors.Length; ++i)
                {
                    FGrassSector grassSector = grassSectors[i];
                    taskHandles.Add(grassSector.sections[m_Counter].BuildInstance(sectionSize, UnityEngine.Random.Range(1, 16), terrainScaleY, terrain.detailObjectDensity, boundSection.pivotPosition, grassSector.widthScale));
                }
                JobHandle.CompleteAll(taskHandles);

                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    grassSector.sections[m_Counter].UploadGPUData();
                }

                --limit;
                ++m_Counter;
                taskHandles.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            for(int i = 0; i < boundSector.m_Sections.Length; ++i)
            {
                if (boundSector.visibleMap[i] == 0) { continue; }

                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    FGrassSection grassSection = grassSector.sections[i];
                    grassSection.DispatchDraw(cmdBuffer, grassSector.grass.meshes[0], grassSector.grass.materials[0], m_PropertyBlock, passIndex);
                }
            }
        }
    }
}
