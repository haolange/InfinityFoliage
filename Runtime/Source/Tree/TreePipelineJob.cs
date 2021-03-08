using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [BurstCompile]
    public unsafe struct FTreeBatchLODJob : IJobParallelFor
    {
        [ReadOnly]
        public float3 ViewOringin;

        [ReadOnly]
        public float4x4 Matrix_Proj;

        [ReadOnly]
        public NativeArray<float> TreeBatchLODs;

        [NativeDisableUnsafePtrRestriction]
        public FTreeBatch* TreeBatchs;


        public void Execute(int index)
        {
            float ScreenRadiusSquared = 0;
            ref FTreeBatch TreeBatch = ref TreeBatchs[index];

            for (int i = TreeBatchLODs.Length - 1; i >= 0; --i)
            {
                float LODSize = (TreeBatchLODs[i] * TreeBatchLODs[i]) * 0.5f;
                //TreeBatch.LODIndex = math.select(TreeBatch.LODIndex, i, LODSize > ScreenRadiusSquared);
                if (ScreenRadiusSquared < LODSize)
                {
                    TreeBatch.LODIndex = i;
                    break;
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct FTreeBatchCullingJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* Planes;

        [ReadOnly]
        public float3 ViewOringin;

        [ReadOnly]
        public float4x4 Matrix_Proj;

        [NativeDisableUnsafePtrRestriction]
        public FTreeBatch* TreeBatchs;

        [WriteOnly]
        public NativeArray<int> ViewTreeBatchs;

        [ReadOnly]
        public NativeArray<float> TreeBatchLODs;


        public void Execute(int index)
        {
            ref FTreeBatch TreeBatch = ref TreeBatchs[index];

            float BoundRadius = Geometry.GetBoundRadius(TreeBatch.BoundBox);
            float ScreenRadiusSquared = Geometry.ComputeBoundsScreenRadiusSquared(BoundRadius, TreeBatch.BoundBox.center, ViewOringin, Matrix_Proj);
            for (int i = TreeBatchLODs.Length - 1; i >= 0; --i)
            {
                //float LODSize = (TreeBatchLODs[i] * TreeBatchLODs[i]) * 0.5f;
                //LODIndex = math.select(LODIndex, i, ScreenRadiusSquared < LODSize);
                if (ScreenRadiusSquared < (TreeBatchLODs[i] * TreeBatchLODs[i]) * 0.5f)
                {
                    TreeBatch.LODIndex = i;
                    break;
                }
            }

            int VisibleState = 1;
            float2 distRadius = new float2(0, 0);
            for (int j = 0; j < 6; ++j)
            {
                Unity.Burst.CompilerServices.Loop.ExpectVectorized();

                ref FPlane Plane = ref Planes[j];
                distRadius.x = math.dot(Plane.normalDist.xyz, TreeBatch.BoundBox.center) + Plane.normalDist.w;
                distRadius.y = math.dot(math.abs(Plane.normalDist.xyz), TreeBatch.BoundBox.extents);

                VisibleState = math.select(VisibleState, 0, distRadius.x + distRadius.y < 0);
            }
            ViewTreeBatchs[index] = VisibleState;
        }
    }

    [BurstCompile]
    public unsafe struct FTreeDrawCommandBuildJob : IJob
    {
        public int MaxLOD;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeBatch* TreeBatchs;

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
                ref FTreeBatch TreeBatch = ref TreeBatchs[TreeElement.BatchIndex];

                if (ViewTreeBatchs[TreeElement.BatchIndex] != 0 && TreeElement.LODIndex == TreeBatch.LODIndex)
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
