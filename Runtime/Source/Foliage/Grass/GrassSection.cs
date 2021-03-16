using System;
using Unity.Collections;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FGrassSection
    {
        public int BoundIndex;
        internal int[] DensityMap;
        internal NativeArray<int> NativeDensityMap;


        public void BuildNativeCollection()
        {
            NativeDensityMap = new NativeArray<int>(DensityMap.Length, Allocator.Persistent);

            for (int i = 0; i < DensityMap.Length; i++)
            {
                NativeDensityMap[i] = DensityMap[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            NativeDensityMap.Dispose();
        }
    }
}
