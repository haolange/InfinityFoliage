using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage/Tree Component")]
    public unsafe class TreeComponent : FoliageComponent
    {
#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif
        public float drawDistance
        {
            get
            {
                return terrain.treeDistance;
            }
        }

        [HideInInspector]
        public FTreeSector[] treeSectors;
        private MaterialPropertyBlock m_PropertyBlock;


        protected override void OnRegister()
        {
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Tree;
            terrainData = terrain.terrainData;
            m_PropertyBlock = new MaterialPropertyBlock();

            InitTreeSectors();

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
            ReleaseTreeSectors();

            if (terrain.drawTreesAndFoliage == false && FoliageComponents.Count < 2)
            {
                terrain.drawTreesAndFoliage = true;
            }
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            terrain = GetComponent<Terrain>();
            terrainData = GetComponent<TerrainCollider>().terrainData;
            int sectorSize = terrainData.heightmapResolution - 1;
            boundSector = new FBoundSector(sectorSize, 0, 0, transform.position, terrainData.bounds);
        }

        private void DrawBounds(in bool color = false)
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;

            foreach (var treeSector in treeSectors)
            {
                treeSector.DrawBounds(color);
            }
        }

        protected  virtual  void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif

        #region Tree
        private void InitTreeSectors()
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.Initialize();
                treeSector.BuildTreeElement();
                treeSector.BuildTreeSection();
                treeSector.BuildTreeBuffer();
            }
        }
        
        private void ReleaseTreeSectors()
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitViewFoliage(in float3 viewOrigin, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                var taskHandle = treeSector.InitView(drawDistance, viewOrigin, matrixProj, planes);
                taskHandles.Add(taskHandle); 
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(CommandBuffer cmdBuffer, in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                var taskHandle = treeSector.DispatchSetup();
                taskHandles.Add(taskHandle); 
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.DispatchDraw(cmdBuffer, passIndex, m_PropertyBlock);
            }
        }
        #endregion //Tree
    }
}
