using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Mathematics.mathExtent;
using Random = Unity.Mathematics.Random;

namespace Landscape.FoliagePipeline
{
#if UNITY_EDITOR
    interface ITask
    {
        void Execute();
    }

    public struct FUpdateTreeTask : ITask
    {
        public int length;
        public float2 size;
        public float3 terrainPosition;
        public TreePrototype treePrototype;
        public TreeInstance[] treeInstances;
        public TreePrototype[] treePrototypes;
        public List<FTransform> treeTransfroms;


        public void Execute()
        {
            FTransform transform = new FTransform();

            for (int i = 0; i < length; ++i)
            {
                ref TreeInstance treeInstance = ref treeInstances[i];
                TreePrototype serchTreePrototype = treePrototypes[treeInstance.prototypeIndex];
                if (serchTreePrototype.Equals(treePrototype))
                {
                    transform.rotation = new float3(0, treeInstance.rotation, 0);
                    transform.position = (treeInstance.position * new float3(size.x, size.y, size.x)) + terrainPosition;
                    transform.scale = new float3(treeInstance.widthScale, treeInstance.heightScale, treeInstance.widthScale);
                    treeTransfroms.Add(transform);
                }
            }
        }
    }

    public struct FUpdateGrassTask : ITask
    {
        public int length;
        public int[] dscDensity;
        public int[,] srcDensity;
        public float[,] srcHeight;
        public float4[] dscNormalHeight;
        public FGrassSection grassSection;


        public void Execute()
        {
            for (int j = 0; j < length; ++j)
            {
                for (int k = 0; k < length; ++k)
                {
                    int densityIndex = j * length + k;
                    dscDensity[densityIndex] = srcDensity[j, k];
                    dscNormalHeight[densityIndex].w = srcHeight[j, k];
                    grassSection.totalDensity += srcDensity[j, k];
                }
            }
        }
    }

    public struct FUpdateFoliageJob : IJob
    {
        public GCHandle taskHandle;

        public void Execute()
        {
            ITask task = (ITask)taskHandle.Target;
            task.Execute();
        }
    }
#endif

    [BurstCompile]
    public unsafe struct FTreeBatchLODJob : IJobParallelFor
    {
        [ReadOnly]
        public float3 viewOringin;

        [ReadOnly]
        public float4x4 matrix_Proj;

        [ReadOnly]
        public NativeArray<float> treeBatchLODs;

        [NativeDisableUnsafePtrRestriction]
        public FMeshBatch* treeBatchs;


        public void Execute(int index)
        {
            float screenRadiusSquared = 0;
            ref FMeshBatch treeBatch = ref treeBatchs[index];

            for (int i = treeBatchLODs.Length - 1; i >= 0; --i)
            {
                float LODSize = (treeBatchLODs[i] * treeBatchLODs[i]) * 0.5f;
                //TreeBatch.LODIndex = math.select(TreeBatch.LODIndex, i, LODSize > ScreenRadiusSquared);
                if (screenRadiusSquared < LODSize)
                {
                    treeBatch.lODIndex = i;
                    break;
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct FGrassScatterJob : IJob
    {
        [ReadOnly]
        public int split;

        [ReadOnly]
        public float densityScale;

        //[ReadOnly]
        //public float terrainHeight;

        [ReadOnly]
        public float3 sectionPivot;

        [ReadOnly]
        public float4 widthScale;

        [ReadOnly]
        public NativeArray<int> densityMap;

        //[ReadOnly]
        //public NativeArray<float4> normalHeightMap;

        [WriteOnly]
        public NativeList<FGrassBatch> grassBatchs;


        public void Execute()
        {
            int density;
            float3 scale;
            float3 position;
            float3 newPosition;
            //float4 normalHeight;
            float4x4 matrix_World;
            FGrassBatch grassBatch = default;

            for (int i = 0; i < densityMap.Length; ++i)
            {
                //normalHeight = normalHeightMap[i];
                density = (int)((float)densityMap[i] * densityScale);
                position = sectionPivot + new float3(i % split, 0 /*normalHeight.w * terrainHeight*/, i / split);

                for (int j = 0; j < density; ++j)
                {
                    float randomRotate = randomFloat(((position.x + 0.5f) * (j + 1)) + (position.z + 0.5f));
                    float2 randomPoint = randomFloat2(new float2(position.x + 0.5f, (position.z + 0.5f) * (j + 1)));
                    newPosition = position + new float3(randomPoint.x, 0, randomPoint.y);

                    float randomScale = randomFloat(newPosition.x + newPosition.z) * 0.75f;
                    float yScale = widthScale.z + ((widthScale.w - widthScale.z) * randomScale);
                    float xzScale = widthScale.x + ((widthScale.y - widthScale.x) * randomScale);
                    scale = new float3(xzScale, yScale, xzScale);
                    matrix_World = float4x4.TRS(newPosition, quaternion.AxisAngle(new float3(0, 1, 0), math.radians(randomRotate * 360)), scale);

                    grassBatch.position = newPosition;
                    grassBatch.matrix_World = matrix_World;
                    grassBatchs.Add(grassBatch);
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct FBoundCullingJob : IJob
    {
        public int length;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public FBound* sectorBounds;

        [WriteOnly]
        public NativeArray<int> visibleMap;


        public void Execute()
        {
            for (int index = 0; index < length; ++index)
            {
                int visible = 1;
                float2 distRadius = new float2(0, 0);
                ref FBound sectorBound = ref sectorBounds[index];

                for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
                {
                    ref FPlane plane = ref planes[PlaneIndex];
                    distRadius.x = math.dot(plane.normalDist.xyz, sectorBound.center) + plane.normalDist.w;
                    distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectorBound.extents);

                    visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
                }
                visibleMap[index] = visible;
            }
        }
    }

    [BurstCompile]
    public unsafe struct FBoundCullingParallelJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public FBound* sectorBounds;

        [WriteOnly]
        public NativeArray<int> visibleMap;


        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref FBound sectorBound = ref sectorBounds[index];

            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
            {
                ref FPlane plane = ref planes[PlaneIndex];
                distRadius.x = math.dot(plane.normalDist.xyz, sectorBound.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectorBound.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            visibleMap[index] = visible;
        }
    }

    [BurstCompile]
    public unsafe struct FGrassSectionCullingJob : IJobParallelFor
    {
        [ReadOnly]
        public float maxDistance;

        [ReadOnly]
        public float3 viewOrigin;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public FBoundSection* sectionBounds;

        [WriteOnly]
        public NativeArray<int> visibleMap;


        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref FBoundSection sectionBound = ref sectionBounds[index];

            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
            {
                ref FPlane plane = ref planes[PlaneIndex];
                distRadius.x = math.dot(plane.normalDist.xyz, sectionBound.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectionBound.boundBox.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            visibleMap[index] = math.select(visible, 0, math.distance(viewOrigin, sectionBound.boundBox.center) > maxDistance);
        }
    }

    [BurstCompile]
    public unsafe struct FTreeBatchCullingJob : IJobParallelFor
    {
        [ReadOnly]
        public int numLOD;

        [ReadOnly]
        public float maxDistance;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        [ReadOnly]
        public float3 viewOringin;

        [ReadOnly]
        public float4x4 matrix_Proj;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public float* treeLODInfos;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FMeshBatch* treeBatchs;

        [WriteOnly]
        public NativeArray<int> viewTreeBatchs;


        public void Execute(int index)
        {
            ref FMeshBatch treeBatch = ref treeBatchs[index];

            //Calculate LOD
            float ScreenRadiusSquared = Geometry.ComputeBoundsScreenRadiusSquared(treeBatch.boundSphere.radius, treeBatch.boundBox.center, viewOringin, matrix_Proj);

            for (int LODIndex = numLOD; LODIndex >= 0; --LODIndex)
            {
                ref float TreeLODInfo = ref treeLODInfos[LODIndex];

                if (mathExtent.sqr(TreeLODInfo * 0.5f) >= ScreenRadiusSquared)
                {
                    treeBatch.lODIndex = LODIndex;
                    break;
                }
            }

            //Culling Batch
            int visible = 1;
            float2 distRadius = new float2(0, 0);

            for (int PlaneIndex = 0; PlaneIndex < 6; ++PlaneIndex)
            {
                ref FPlane plane = ref planes[PlaneIndex];
                distRadius.x = math.dot(plane.normalDist.xyz, treeBatch.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), treeBatch.boundBox.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            viewTreeBatchs[index] = math.select(visible, 0, math.distance(viewOringin, treeBatch.boundBox.center) > maxDistance);
        }
    }

    [BurstCompile]
    public unsafe struct FTreeDrawCommandBuildJob : IJob
    {
        public int maxLOD;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FMeshBatch* treeBatchs;

        [ReadOnly]
        public NativeArray<int> viewTreeBatchs;

        [WriteOnly]
        public NativeArray<int> treeBatchIndexs;

        [ReadOnly]
        public NativeList<FMeshElement> treeElements;

        public NativeList<FMeshElement> passTreeElements;

        public NativeList<FMeshDrawCommand> treeDrawCommands;


        public void Execute()
        {
            //Gather PassTreeElement
            FMeshElement treeElement;
            for (int i = 0; i < treeElements.Length; ++i)
            {
                treeElement = treeElements[i];
                ref FMeshBatch treeBatch = ref treeBatchs[treeElement.batchIndex];

                if (viewTreeBatchs[treeElement.batchIndex] != 0 && treeElement.lODIndex == treeBatch.lODIndex)
                {
                    passTreeElements.Add(treeElement);
                }
            }

            //Sort PassTreeElement
            //PassTreeElements.Sort();

            //Build TreeDrawCommand
            FMeshElement passTreeElement;
            FMeshElement cachePassTreeElement = new FMeshElement(-1, -1, -1, -1);

            FMeshDrawCommand treeDrawCommand;
            FMeshDrawCommand cacheTreeDrawCommand;

            for (int i = 0; i < passTreeElements.Length; ++i)
            {
                passTreeElement = passTreeElements[i];
                treeBatchIndexs[i] = passTreeElement.batchIndex;

                if (!passTreeElement.Equals(cachePassTreeElement))
                {
                    cachePassTreeElement = passTreeElement;

                    treeDrawCommand.countOffset.x = 0;
                    treeDrawCommand.countOffset.y = i;
                    treeDrawCommand.lODIndex = passTreeElement.lODIndex;
                    treeDrawCommand.matIndex = passTreeElement.matIndex;
                    treeDrawCommand.meshIndex = passTreeElement.meshIndex;
                    //TreeDrawCommand.InstanceGroupID = PassTreeElement.InstanceGroupID;
                    treeDrawCommands.Add(treeDrawCommand);
                }

                cacheTreeDrawCommand = treeDrawCommands[treeDrawCommands.Length - 1];
                cacheTreeDrawCommand.countOffset.x += 1;
                treeDrawCommands[treeDrawCommands.Length - 1] = cacheTreeDrawCommand;
            }
        }
    }
}
