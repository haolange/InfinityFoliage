using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage Component")]
    public unsafe class FoliageComponent : InstanceMeshComponent
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
        public List<JobHandle> CullRefs;
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

            CullRefs = new List<JobHandle>(64);
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
                TreeSector.Initialize();
                TreeSector.BuildMeshBatchs();
                TreeSector.BuildMeshElements();
            }
        }

        public void InitViewTree(NativeArray<FPlane> Planes)
        {
            //Cull
            CullRefs.Clear();

            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                CullRefs.Add(TreeSector.InitView((FPlane*)Planes.GetUnsafePtr()));
            }
        }

        public void WaitViewTree()
        {
            //Wait
            for (int j = 0; j < CullRefs.Count; ++j)
            {
                CullRefs[j].Complete();
            }
        }

        public void DrawViewTree(CommandBuffer CmdBuffer)
        {
            //Draw Call
            for (int l = 0; l < TreeSectors.Length; ++l)
            {
                FTreeSector TreeSector = TreeSectors[l];
                TreeSector.DrawTree(CmdBuffer);
            }
        }

        public void ReleaseViewTree()
        {
            //Release
            for (int k = 0; k < TreeSectors.Length; ++k)
            {
                TreeSectors[k].ReleaseView();
            }
        }

        void ReleaseTreeSectors()
        {
            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                TreeSector.Release();
            }
        }
        #endregion //Tree
    }
}
