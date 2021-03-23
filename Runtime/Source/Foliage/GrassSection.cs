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


        public void BuildNativeCollection()
        {
            nativeDensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);

            for (int i = 0; i < densityMap.Length; i++)
            {
                nativeDensityMap[i] = densityMap[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            nativeDensityMap.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float2 pivotPosition)
        {
            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.pivotPosition = pivotPosition;
                grassScatterJob.nativeDensityMap = nativeDensityMap;
            }
            return grassScatterJob.Schedule();
        }
    }
}
