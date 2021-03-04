using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage Component")]
    public class FoliageComponent : InstanceMeshComponent
    {
        public static List<FoliageComponent> FoliageComponents = new List<FoliageComponent>(64);

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
            FoliageComponents.Add(this);

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
            FoliageComponents.Remove(this);
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
                    TreeSector.BuildMeshElements();
                }
            }
        }

        public void DrawTree(CommandBuffer CmdBuffer)
        {
            if (Application.isPlaying == false) { return; }

            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                if (TreeSector != null)
                {
                    TreeSector.DrawTree(CmdBuffer);
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
