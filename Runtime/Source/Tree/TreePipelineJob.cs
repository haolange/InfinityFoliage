using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [BurstCompile]
    public unsafe struct FTreeBatchCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* FrustumPlanes;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeBatch* TreeBatchs;

        [WriteOnly]
        public NativeArray<int> ViewTreeBatchs;


        public void Execute(int index)
        {
            int VisibleState = 1;
            float2 distRadius = new float2(0, 0);
            ref FTreeBatch TreeBatch = ref TreeBatchs[index];

            for (int i = 0; i < 6; ++i)
            {
                Unity.Burst.CompilerServices.Loop.ExpectVectorized();

                ref FPlane FrustumPlane = ref FrustumPlanes[i];
                distRadius.x = math.dot(FrustumPlane.normalDist.xyz, TreeBatch.BoundingBox.center) + FrustumPlane.normalDist.w;
                distRadius.y = math.dot(math.abs(FrustumPlane.normalDist.xyz), TreeBatch.BoundingBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            ViewTreeBatchs[index] = VisibleState;
        }
    }
}
