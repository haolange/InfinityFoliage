using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;
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

        //[HideInInspector]
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
            ReleaseTreeSectors();
            FoliageComponents.Remove(this);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InitViewTree(in float3 ViewOringin, in float4x4 Matrix_Proj, in NativeArray<FPlane> Planes, in NativeList<JobHandle> CullHandles)
        {
            for (int i = 0; i < TreeSectors.Length; ++i)
            {
                FTreeSector TreeSector = TreeSectors[i];
                CullHandles.Add(TreeSector.InitView(ViewOringin, Matrix_Proj, (FPlane*)Planes.GetUnsafePtr()));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchSetup(in NativeList<JobHandle> GatherHandles)
        {
            //Build DrawCall
            for (int l = 0; l < TreeSectors.Length; ++l)
            {
                FTreeSector TreeSector = TreeSectors[l];
                GatherHandles.Add(TreeSector.DispatchSetup());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer CmdBuffer)
        {
            //Record DrawCall
            for (int l = 0; l < TreeSectors.Length; ++l)
            {
                FTreeSector TreeSector = TreeSectors[l];
                TreeSector.DispatchDraw(CmdBuffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
