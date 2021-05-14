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

        public bool needUpdateGPU
        {
            get
            {
                return lastDensityScale != terrain.detailObjectDensity;
            }
        }
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
        public float DrawDistance
        {
            get
            {
                return terrain.detailObjectDistance + (terrain.detailObjectDistance * 0.5f);
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
        public float lastNumSection;
        [HideInInspector]
        public float lastDensityScale;
        [HideInInspector]
        public FGrassSector[] grassSectors;


        protected override void OnRegister()
        {
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            foliageType = EFoliageType.Grass;

            InitGrassSectors();
            lastDensityScale = -1;
            boundSector.BuildNativeCollection();

            if (terrain.drawTreesAndFoliage == true)
            {
                terrain.drawTreesAndFoliage = false;
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
            ReleaseGrassSectors();
            boundSector.ReleaseNativeCollection();

            if (terrain.drawTreesAndFoliage == false && FoliageComponents.Count < 2)
            {
                terrain.drawTreesAndFoliage = true;
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
        private void InitGrassSectors()
        {
            FGrassShaderProperty shaderProperty;
            shaderProperty.terrainSize = SectorSize;
            shaderProperty.terrainPivotScaleY = new float4(transform.position, TerrainScaleY);
            shaderProperty.heightmapTexture = terrainData.heightmapTexture;
            
            foreach(FGrassSector grassSector in grassSectors)
            {
                grassSector.Init(boundSector, terrainData, shaderProperty);
            }
        }
        
        private void ReleaseGrassSectors()
        {
            foreach (FGrassSector grassSector in grassSectors)
            {
                grassSector.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitViewFoliage(in float3 viewOrigin, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            taskHandles.Add(boundSector.InitView(DrawDistance, viewOrigin, planes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void BuildInstance(in NativeList<JobHandle> taskHandles)
        {
            if(!needUpdateGPU) { return; }

            foreach (FGrassSector grassSector in grassSectors)
            {
                grassSector.BuildInstance(SectionSize, TerrainScaleY, terrain.detailObjectDensity, taskHandles);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(CommandBuffer cmdBuffer, in NativeList<JobHandle> taskHandles)
        {
            if (!needUpdateGPU) { return; }
            lastDensityScale = terrain.detailObjectDensity;

            foreach (FGrassSector grassSector in grassSectors)
            {
                grassSector.SetGPUData(cmdBuffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            for(int i = 0; i < boundSector.nativeSections.Length; ++i)
            {
                if (boundSector.sectionsVisbible[i] == 0) { continue; }

                for(int j = 0; j < grassSectors.Length; ++j)
                {
                    FGrassSector grassSector = grassSectors[j];
                    grassSector.sections[i].DispatchDraw(cmdBuffer, passIndex);
                }
            }
        }
        #endregion //Grass
    }
}
