using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Landscape.FoliagePipeline
{
    internal enum EFoliageType
    {
        Tree = 0,
        Grass = 1
    }

    public enum FoliageDrawPass
    {
        PreZ = 0,
        Forward = 1,
        Shadow = 2
    }

    public unsafe struct FoliageView
    {
        public Camera camera;
        public int cascade;
        public float3 origin;
        public float4x4 lodProjection;
        public FrustumPlane* planes;
        public int planeCount;
        internal int renderId;
        internal FrustumPlane* shadowPlanes;
        internal int4 shadowPlaneCounts;

        public bool isShadow
        {
            get { return cascade >= 0; }
        }
    }

#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public abstract unsafe class FoliageComponent : MonoBehaviour
    {
        internal EFoliageType foliageType;

        [System.NonSerialized]
        [HideInInspector]
        public Terrain terrain;
        [System.NonSerialized]
        [HideInInspector]
        public TerrainData terrainData;
        [HideInInspector]
        public BoundSector boundSector;

        internal static List<FoliageComponent> FoliageComponents = new List<FoliageComponent>(128);

        void OnEnable()
        {
            FoliageComponents.Add(this);
            OnRegiste();
        }

        void OnDisable()
        {
            UnRegiste();
            FoliageComponents.Remove(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract void OnRegiste();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract void UnRegiste();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void Cull(CommandBuffer cmdBuffer, in FoliageView view, in NativeList<JobHandle> taskHandles);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void Pack(CommandBuffer cmdBuffer, in FoliageView view);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void Draw(CommandBuffer cmdBuffer, in FoliageView view, in FoliageDrawPass pass);
    }
}
