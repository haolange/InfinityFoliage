using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    public struct FGrassElement : IEquatable<FGrassElement>
    {
        public float3 position;
        public float4x4 worldMatrix;


        public FGrassElement(in float3 position, in float4x4 worldMatrix)
        {
            this.position = position;
            this.worldMatrix = worldMatrix;
        }

        public bool Equals(FGrassElement Target)
        {
            return position.Equals(Target.position) && worldMatrix.Equals(Target.worldMatrix);
        }

        public override bool Equals(object obj)
        {
            return Equals((FGrassElement)obj);
        }

        public override int GetHashCode()
        {
            return (position.GetHashCode() << 16) + (worldMatrix.GetHashCode() << 16);
        }
    }

    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int[] densityMap;
        internal NativeArray<int> nativeDensityMap;
        internal NativeList<FGrassElement> nativegrassElements;


        public void BuildNativeCollection()
        {
            nativeDensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            nativegrassElements = new NativeList<FGrassElement>(densityMap.Length, Allocator.Persistent);

            for (int i = 0; i < densityMap.Length; i++)
            {
                nativeDensityMap[i] = densityMap[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            nativeDensityMap.Dispose();
            nativegrassElements.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float3 sectionPivot)
        {
            nativegrassElements.Clear();

            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.sectionPivot = sectionPivot;
                grassScatterJob.nativeDensityMap = nativeDensityMap;
                grassScatterJob.nativegrassElements = nativegrassElements;
            }
            return grassScatterJob.Schedule();
        }

#if UNITY_EDITOR
        public void DrawBounds()
        {
            foreach (FGrassElement grassElement in nativegrassElements)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(grassElement.position, 0.05f);
            }
        }
#endif
    }
}
