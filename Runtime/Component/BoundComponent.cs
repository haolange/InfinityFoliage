using UnityEngine;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [RequireComponent(typeof(Terrain))]
    [AddComponentMenu("HG/Foliage/Bound Component")]

    public class BoundComponent : MonoBehaviour
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

        [HideInInspector]
        public Terrain terrain;
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public FBoundSector BoundSector;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif

        void OnEnable()
        {
            terrain = GetComponent<Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            BoundSector.BuildNativeCollection();
        }

        void Update()
        {
        
        }

        void OnDisable()
        {
            BoundSector.ReleaseNativeCollection();
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            terrain = GetComponent<UnityEngine.Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;

            TerrainTexture HeightTexture = new TerrainTexture(SectorSize);
            HeightTexture.TerrainDataToHeightmap(terrainData);

            if(BoundSector != null)
            {
                if (BoundSector.NativeSections.IsCreated == true)
                {
                    BoundSector.ReleaseNativeCollection();
                }
            }

            BoundSector = new FBoundSector(SectorSize, NumSection, SectionSize, transform.position, terrainData.bounds);
            BoundSector.BuildBounds(SectorSize, SectionSize, TerrainScaleY, transform.position, HeightTexture.HeightMap);
            BoundSector.BuildNativeCollection();

            HeightTexture.Release();
        }

        void OnDrawGizmosSelected()
        {
            if (showBounds)
            {
                BoundSector.DrawBound();
            }
        }
#endif
    }
}

