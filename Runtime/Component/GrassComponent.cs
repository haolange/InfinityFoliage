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
    [RequireComponent(typeof(BoundComponent))]
    [AddComponentMenu("HG/Foliage/Grass Component")]
    public unsafe class GrassComponent : FoliageComponent
    {
#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif

        [HideInInspector]
        public Terrain terrain;
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public FGrassSector[] grassSectors;
        [HideInInspector]
        public BoundComponent boundComponent;


        protected override void OnRegister()
        {
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            boundComponent = GetComponent<BoundComponent>();

            InitGrassSectors();

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
        }

#if UNITY_EDITOR
        private void DrawBounds(in bool color = false)
        {
            if (!showBounds) return;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif

        #region Grass
        private void InitGrassSectors()
        {

        }
        
        private void ReleaseGrassSectors()
        {

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitViewFoliage(in float3 viewPos, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(in NativeList<JobHandle> taskHandles)
        {

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer)
        {

        }
        #endregion //Grass
    }
}
