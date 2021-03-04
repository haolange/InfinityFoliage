using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [BurstCompile]
    public unsafe struct FTreeBatchCullingJob : IJob
    {
        public int Length;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* Planes;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeBatch* TreeBatchs;

        [WriteOnly]
        public NativeArray<int> ViewTreeBatchs;


        public void Execute()
        {
            for (int index = 0; index < Length; ++index)
            {
                int VisibleState = 1;
                float2 distRadius = new float2(0, 0);
                ref FTreeBatch TreeBatch = ref TreeBatchs[index];

                for (int i = 0; i < 6; ++i)
                {
                    Unity.Burst.CompilerServices.Loop.ExpectVectorized();

                    ref FPlane Plane = ref Planes[i];
                    distRadius.x = math.dot(Plane.normalDist.xyz, TreeBatch.BoundBox.center) + Plane.normalDist.w;
                    distRadius.y = math.dot(math.abs(Plane.normalDist.xyz), TreeBatch.BoundBox.extents);

                    VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
                }

                ViewTreeBatchs[index] = VisibleState;
            }
        }
    }

    [BurstCompile]
    public unsafe struct FTreeBatchCullingParallelJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* Planes;

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

                ref FPlane Plane = ref Planes[i];
                distRadius.x = math.dot(Plane.normalDist.xyz, TreeBatch.BoundBox.center) + Plane.normalDist.w;
                distRadius.y = math.dot(math.abs(Plane.normalDist.xyz), TreeBatch.BoundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            ViewTreeBatchs[index] = VisibleState;
        }
    }
}
