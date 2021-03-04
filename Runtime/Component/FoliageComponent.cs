using UnityEngine;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage Component")]
    public class FoliageComponent : InstanceMeshComponent
    {
#if UNITY_EDITOR
        [Header("Debug")]
        public bool DisplayBounds = false;
#endif

        [HideInInspector]
        public Terrain UnityTerrain;
        [HideInInspector]
        public TerrainData UnityTerrainData;

        [HideInInspector]
        public FTreeSector[] TreeSectors;


        protected override void OnRegister()
        {
            UnityTerrain = GetComponent<Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;
            if (UnityTerrain.drawTreesAndFoliage == true)
            {
                UnityTerrain.drawTreesAndFoliage = false;
            }

            InitTreeSectors();
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
            ReleaseTreeSectors();
        }

#if UNITY_EDITOR
        public void Serialize()
        {

        }

        public void DrawBounds(in bool LODColor = false)
        {
            if (DisplayBounds)
            {
                for (int i = 0; i < TreeSectors.Length; ++i)
                {
                    FTreeSector TreeSector = TreeSectors[i];
                    if (TreeSector != null)
                    {
                        TreeSector.DrawBounds(LODColor);
                    }
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif

        #region Tree
        void InitTreeSectors()
        {
            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                if (TreeSector != null)
                {
                    TreeSector.Initialize();
                    TreeSector.BuildMeshBatchs();
                }
            }
        }

        void ReleaseTreeSectors()
        {
            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                if (TreeSector != null)
                {
                    TreeSector.Release();
                }
            }
        }
        #endregion //Tree
    }
}
