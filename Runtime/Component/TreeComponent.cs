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
        [HideInInspector]
        public float drawDistance;
        [HideInInspector]
        public FTreeSector[] treeSectors;
        private MaterialPropertyBlock m_PropertyBlock;

        protected override void OnRegister()
        {
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Tree;
            terrainData = terrain.terrainData;
            drawDistance = terrain.treeDistance;
            terrain.treeDistance = 0;
            m_PropertyBlock = new MaterialPropertyBlock();

            foreach (var treeSector in treeSectors)
            {
                treeSector.Initialize();
                treeSector.BuildRuntimeData();
            }
        }

        protected override void UnRegister()
        {
            terrain.treeDistance = drawDistance;
            foreach (var treeSector in treeSectors)
            {
                treeSector.Release();
            }
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            int sectorSize = terrainData.heightmapResolution - 1;
            boundSector = new FBoundSector(0, sectorSize, 0, transform.position, terrainData.bounds, false);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.InitView(drawDistance, viewOrigin, matrixProj, planes, taskHandles);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            foreach (var treeSector in treeSectors)
            {
                treeSector.DispatchSetup(viewOrigin, matrixProj, taskHandles);
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
