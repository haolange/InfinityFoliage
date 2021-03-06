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

        public int SectorSize
        {
            get
            {
                return terrainData.heightmapResolution - 1;
            }
        }
        public int SectionSize
        {
            get
            {
                return SectorSize / numSection;
            }
        }
        public float TerrainScaleY
        {
            get
            {
                return terrainData.size.y;
            }
        }
        [HideInInspector]
        public float drawDistance;
        [HideInInspector]
        public float lastNumSection;
        [HideInInspector]
        public FGrassSector[] grassSectors;

        private int m_Count;

        protected override void OnRegister()
        {
            m_Count = 0;
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Grass;
            terrainData = terrain.terrainData;
            drawDistance = terrain.detailObjectDistance;
            terrain.detailObjectDistance = 0;

            FGrassShaderProperty shaderProperty;
            shaderProperty.terrainSize = SectorSize;
            shaderProperty.terrainPivotScaleY = new float4(transform.position, TerrainScaleY);
            shaderProperty.heightmapTexture = terrainData.heightmapTexture;
            
            boundSector.BuildNativeCollection();
            foreach(FGrassSector grassSector in grassSectors)
            {
                grassSector.Init(boundSector, terrainData, shaderProperty);
            }
        }

        protected override void OnTransformChange()
        {

        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {

        }

        protected override void UnRegister()
        {
            boundSector.ReleaseNativeCollection();
            terrain.detailObjectDistance = drawDistance;

            foreach (FGrassSector grassSector in grassSectors)
            {
                grassSector.Release();
            }
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            if(lastNumSection == numSection) { return; }
            lastNumSection = numSection;

            terrain = GetComponent<Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            TerrainTexture HeightTexture = new TerrainTexture(SectorSize);
            HeightTexture.TerrainDataToHeightmap(terrainData);

            boundSector = new FBoundSector(SectorSize, numSection, SectionSize, transform.position, terrainData.bounds);
            boundSector.BuildBounds(SectorSize, SectionSize, TerrainScaleY, transform.position, HeightTexture.HeightMap);

            HeightTexture.Release();
        }

        private void DrawBounds()
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;

            boundSector.DrawBound();
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds();
        }
#endif

        #region Grass 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            taskHandles.Add(boundSector.InitView(drawDistance, new float4(viewOrigin, 1), planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            if(m_Count == boundSector.nativeSections.Length) { return; }

            int limit = 16;
            while (limit > 0 && m_Count < boundSector.nativeSections.Length)
            {
                FBoundSection boundSection = boundSector.nativeSections[m_Count];

                for (int i = 0; i < grassSectors.Length; ++i)
                {
                    FGrassSector grassSector = grassSectors[i];
                    taskHandles.Add(grassSector.sections[m_Count].BuildInstance(SectionSize, grassSector.grass.GetHashCode() / 1000000, TerrainScaleY, terrain.detailObjectDensity, boundSection.pivotPosition, grassSector.widthScale));
                }
                JobHandle.CompleteAll(taskHandles);

                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    grassSector.sections[m_Count].UploadGPUData();
                }

                --limit;
                ++m_Count;
                taskHandles.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            for(int i = 0; i < boundSector.nativeSections.Length; ++i)
            {
                if (boundSector.visibleMap[i] == 0) { continue; }

                for (int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    grassSector.sections[i].DispatchDraw(cmdBuffer, passIndex);
                }
            }
        }
        #endregion //Grass
    }
}
