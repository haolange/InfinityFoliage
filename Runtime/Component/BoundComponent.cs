using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("HG/Foliage/Bound Component")]
    public unsafe class BoundComponent : MonoBehaviour
    {
        [Header("Setting")]
        public int NumSection = 16;
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
                return SectorSize / NumSection;
            }
        }
        public float TerrainScaleY
        {
            get
            {
                return terrainData.size.y;
            }
        }
        public float drawDistance
        {
            get
            {
                return terrain.detailObjectDistance + (terrain.detailObjectDistance * 0.5f);
            }
        }

        [HideInInspector]
        public Terrain terrain;
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public FBoundSector BoundSector;
        [HideInInspector]
        public TreeComponent treeComponent;
        [HideInInspector]
        public GrassComponent grassComponent;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif

        public static List<BoundComponent> s_boundComponents = new List<BoundComponent>(128);


        void OnEnable()
        {
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            BoundSector.BuildNativeCollection();
            s_boundComponents.Add(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InitSectionView(in float3 viewPos, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            taskHandles.Add(BoundSector.InitView(drawDistance, viewPos, planes));
        }

        void OnDisable()
        {
            BoundSector.ReleaseNativeCollection();
            s_boundComponents.Remove(this);
        }

        public static void InitSectorView(in float3 viewPos, FPlane* planes, ref NativeArray<int> boundsVisible, ref NativeArray<FBound> sectorsBound)
        {
            boundsVisible = new NativeArray<int>(s_boundComponents.Count, Allocator.TempJob);
            sectorsBound = new NativeArray<FBound>(s_boundComponents.Count, Allocator.TempJob);

            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                sectorsBound[i] = s_boundComponents[i].BoundSector.bound;
            }

            var sectorCullingJob = new FBoundSectorCullingJob();
            {
                sectorCullingJob.planes = planes;
                sectorCullingJob.visibleMap = boundsVisible;
                sectorCullingJob.sectorBounds = (FBound*)sectorsBound.GetUnsafePtr();
            }
            sectorCullingJob.Schedule(sectorsBound.Length, 8).Complete();
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            terrain = GetComponent<UnityEngine.Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            TerrainTexture HeightTexture = new TerrainTexture(SectorSize);
            HeightTexture.TerrainDataToHeightmap(terrainData);

            BoundSector = new FBoundSector(SectorSize, NumSection, SectionSize, transform.position, terrainData.bounds);
            BoundSector.BuildBounds(SectorSize, SectionSize, TerrainScaleY, transform.position, HeightTexture.HeightMap);

            HeightTexture.Release();
        }

        void OnDrawGizmosSelected()
        {
            if (showBounds && enabled && gameObject.activeSelf)
            {
                BoundSector.DrawBound();
            }
        }
#endif
    }
}

