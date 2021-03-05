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

    [BurstCompile]
    public struct FTreeDrawCommandBuildJob : IJob
    {
        public int MaxLOD;

        [ReadOnly]
        public NativeArray<int> ViewTreeBatchs;

        [WriteOnly]
        public NativeArray<int> TreeBatchIndexs;

        [ReadOnly]
        public NativeList<FTreeElement> TreeElements;

        public NativeList<FTreeElement> PassTreeElements;

        public NativeList<FTreeDrawCommand> TreeDrawCommands;


        public void Execute()
        {
            //Gather PassTreeElement
            FTreeElement TreeElement;
            for (int i = 0; i < TreeElements.Length; ++i)
            {
                TreeElement = TreeElements[i];

                if (ViewTreeBatchs[TreeElement.BatchIndex] != 0 && TreeElement.LODIndex == MaxLOD)
                {
                    PassTreeElements.Add(TreeElement);
                }
            }

            //Sort PassTreeElement
            //PassTreeElements.Sort();

            //Build TreeDrawCommand
            FTreeElement PassTreeElement;
            FTreeElement CachePassTreeElement = new FTreeElement(-1, -1, -1, -1, -1);

            FTreeDrawCommand TreeDrawCommand;
            FTreeDrawCommand CacheTreeDrawCommand;

            for (int i = 0; i < PassTreeElements.Length; ++i)
            {
                PassTreeElement = PassTreeElements[i];
                TreeBatchIndexs[i] = PassTreeElement.BatchIndex;

                if (!PassTreeElement.Equals(CachePassTreeElement))
                {
                    CachePassTreeElement = PassTreeElement;

                    TreeDrawCommand.CountOffset.x = 0;
                    TreeDrawCommand.CountOffset.y = i;
                    TreeDrawCommand.LODIndex = PassTreeElement.LODIndex;
                    TreeDrawCommand.MatIndex = PassTreeElement.MatIndex;
                    TreeDrawCommand.MeshIndex = PassTreeElement.MeshIndex;
                    //TreeDrawCommand.InstanceGroupID = PassTreeElement.InstanceGroupID;
                    TreeDrawCommands.Add(TreeDrawCommand);
                }

                CacheTreeDrawCommand = TreeDrawCommands[TreeDrawCommands.Length - 1];
                CacheTreeDrawCommand.CountOffset.x += 1;
                TreeDrawCommands[TreeDrawCommands.Length - 1] = CacheTreeDrawCommand;
            }
        }
    }
}
