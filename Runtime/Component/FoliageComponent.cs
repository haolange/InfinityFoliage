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
        public abstract void InitView(in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void FlushPendingUploads();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex);
    }
}
