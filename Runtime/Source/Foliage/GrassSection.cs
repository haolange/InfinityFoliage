using System;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int[] densityMap;
        internal NativeArray<int> nativeDensityMap;
        internal NativeList<float4x4> nativeWorldMatrixs;


        public void BuildNativeCollection()
        {
            nativeDensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            nativeWorldMatrixs = new NativeList<float4x4>(densityMap.Length, Allocator.Persistent);

            for (int i = 0; i < densityMap.Length; i++)
            {
                nativeDensityMap[i] = densityMap[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            nativeDensityMap.Dispose();
            nativeWorldMatrixs.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float3 sectionPivot)
        {
            nativeWorldMatrixs.Clear();

            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.sectionPivot = sectionPivot;
                grassScatterJob.nativeDensityMap = nativeDensityMap;
                grassScatterJob.nativeWorldMatrixs = nativeWorldMatrixs;
            }
            return grassScatterJob.Schedule();
        }
    }
}
