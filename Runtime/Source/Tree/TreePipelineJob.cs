using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [BurstCompile]
    public unsafe struct FTreeElementCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* FrustumPlanes;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeElement* TreeElements;

        [WriteOnly]
        public NativeArray<int> ViewTreeElements;


        public void Execute(int index)
        {
            int VisibleState = 1;
            float2 distRadius = new float2(0, 0);
            ref FTreeElement TreeElement = ref TreeElements[index];

            for (int i = 0; i < 6; ++i)
            {
                Unity.Burst.CompilerServices.Loop.ExpectVectorized();

                ref FPlane FrustumPlane = ref FrustumPlanes[i];
                distRadius.x = math.dot(FrustumPlane.normalDist.xyz, TreeElement.BoundBox.center) + FrustumPlane.normalDist.w;
                distRadius.y = math.dot(math.abs(FrustumPlane.normalDist.xyz), TreeElement.BoundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }

            ViewTreeElements[index] = VisibleState;
        }
    }
}
