using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [RequireComponent(typeof(BoundComponent))]
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
        public Terrain terrain;
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public FTreeSector[] treeSectors;
        [HideInInspector]
        public BoundComponent boundComponent;


        protected override void OnRegister()
        {
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            boundComponent = GetComponent<BoundComponent>();
            boundComponent.treeComponent = this;

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
            boundComponent.treeComponent = null;
        }

#if UNITY_EDITOR
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
                treeSector.BuildMeshBatch();
                treeSector.BuildMeshElement();
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
        public override void InitViewFoliage(in float3 viewPos, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                var taskHandle = treeSector.InitView(drawDistance, viewPos, matrixProj, planes);
                taskHandles.Add(taskHandle); 
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                var taskHandle = treeSector.DispatchSetup();
                taskHandles.Add(taskHandle); 
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer)
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.DispatchDraw(cmdBuffer);
            }
        }
        #endregion //Tree
    }
}
