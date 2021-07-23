using System;
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
        public byte[] dscDensity;
        public int[,] srcDensity;
        public float[,] srcHeight;
        //public float[] dscHeight;
        public FGrassSection grassSection;


        public void Execute()
        {
            for (int j = 0; j < length; ++j)
            {
                for (int k = 0; k < length; ++k)
                {
                    int densityIndex = j * length + k;
                    //dscHeight[densityIndex] = srcHeight[j, k];
                    dscDensity[densityIndex] = (byte)srcDensity[j, k];
                    grassSection.instanceCount += srcDensity[j, k];
                }
            }
        }
    }

    public struct FUpdateFoliageJob : IJob
    {
        public long taskPtr;

        public void Execute()
        {
            GCHandle handle = GCHandle.FromIntPtr((IntPtr)taskPtr);
            ITask task = (ITask)handle.Target;
            task.Execute();
        }
    }
#endif

    [BurstCompile]
    public unsafe struct FGrassScatterJob : IJob
    {
        [ReadOnly]
        public int split;

        //[ReadOnly]
        //public float heightScale;

        [ReadOnly]
        public float uniqueValue;

        [ReadOnly]
        public float densityScale;

        [ReadOnly]
        public float3 sectionPivot;

        [ReadOnly]
        public float4 widthScale;

        [ReadOnly]
        public NativeArray<byte> densityMap;

        //[ReadOnly]
        //public NativeArray<float> heightMap;

        [WriteOnly]
        public NativeList<FGrassElement> grassElements;

        public void Execute()
        {
            int density;
            //float height;
            float3 scale;
            float3 position;
            float3 newPosition;
            float4x4 matrix_World;
            FGrassElement grassElement = default;

            for (int i = 0; i < densityMap.Length; ++i)
            {
                //height = heightMap[i];
                density = (int)((float)densityMap[i] * densityScale);
                position = sectionPivot + new float3(i % split, 0 /*height * heightScale*/, i / split);

                for (int j = 0; j < density; ++j)
                {
                    float multiplier = (j + 1) * math.abs(uniqueValue);
                    float randomRotate = randomFloat(((position.x + 0.5f) * multiplier) + (position.z + 0.5f));
                    float2 randomPoint = randomFloat2(new float2(position.x + 0.5f, (position.z + 0.5f) * multiplier));
                    newPosition = position + new float3(randomPoint.x, 0, randomPoint.y);

                    float randomScale = randomFloat((newPosition.x + newPosition.z) * multiplier);
                    float yScale = widthScale.z + ((widthScale.w - widthScale.z) * randomScale);
                    float xzScale = widthScale.x + ((widthScale.y - widthScale.x) * randomScale);
                    scale = new float3(xzScale, yScale, xzScale);
                    matrix_World = float4x4.TRS(newPosition, quaternion.AxisAngle(new float3(0, 1, 0), math.radians(randomRotate * 360)), scale);

                    grassElement.matrix_World = matrix_World;
                    grassElements.Add(grassElement);
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
        public NativeArray<byte> visibleMap;

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
                visibleMap[index] = (byte)visible;
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
        public NativeArray<byte> visibleMap;

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
            visibleMap[index] = (byte)visible;
        }
    }

    [BurstCompile]
    public unsafe struct FGrassCullingJob : IJobParallelFor
    {
        [ReadOnly]
        public float4 viewOrigin;

        [ReadOnly]
        public float cullDistance;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public FBoundSection* sectionBounds;

        [WriteOnly]
        public NativeArray<byte> visibleMap;

        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref FBoundSection sectionBound = ref sectionBounds[index];

            for (int i = 0; i < 6; ++i)
            {
                ref FPlane plane = ref planes[i];
                distRadius.x = math.dot(plane.normalDist.xyz, sectionBound.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectionBound.boundBox.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            float4 boundPivot = new float4(sectionBound.boundBox.center.x, sectionBound.boundBox.center.y + sectionBound.boundBox.extents.y, sectionBound.boundBox.center.z, 1);
            visibleMap[index] = (byte)math.select(visible, 0, math.distance(viewOrigin.xyz, boundPivot.xyz) > cullDistance);
        }
    }


    [BurstCompile]
    public unsafe struct FTreeScatterJob : IJobParallelFor
    {
        public Bounds boundBox;

        [ReadOnly]
        public NativeArray<FTransform> transforms;

        [WriteOnly]
        public NativeArray<FTreeElement> treeElements;

        public void Execute(int index)
        {
            float4x4 matrixWorld = float4x4.TRS(transforms[index].position, quaternion.EulerXYZ(transforms[index].rotation), transforms[index].scale);

            FTreeElement treeElement;
            treeElement.meshIndex = 0;
            treeElement.matrix_World = matrixWorld;
            treeElement.boundBox = Geometry.CaculateWorldBound(boundBox, matrixWorld);
            treeElement.boundSphere = new FSphere(Geometry.CaculateBoundRadius(treeElement.boundBox), treeElement.boundBox.center);
            treeElements[index] = treeElement;
        }
    }

    [BurstCompile]
    public unsafe struct FTreeComputeLODJob : IJobParallelFor
    {
        public int numLOD;

        public float3 viewOringin;

        public float4x4 matrix_Proj;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public float* treeLODInfos;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeElement* treeElements;

        public void Execute(int index)
        {
            ref FTreeElement treeElement = ref treeElements[index];
            float screenRadiusSqr = Geometry.ComputeBoundsScreenRadiusSquared(treeElement.boundSphere.radius, treeElement.boundBox.center, viewOringin, matrix_Proj);

            for (int lodIndex = numLOD; lodIndex >= 0; --lodIndex)
            {
                ref float treeLODInfo = ref treeLODInfos[lodIndex];

                if (mathExtent.sqr(treeLODInfo * 0.5f) >= screenRadiusSqr)
                {
                    treeElement.meshIndex = lodIndex;
                    break;
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct FTreeCullingJob : IJobParallelFor
    {
        public float maxDistance;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FPlane* planes;

        public float3 viewOringin;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FTreeElement* treeElements;

        [WriteOnly]
        public NativeArray<int> viewTreeElements;

        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref FTreeElement treeElement = ref treeElements[index];

            for (int planeIndex = 0; planeIndex < 6; ++planeIndex)
            {
                ref FPlane plane = ref planes[planeIndex];
                distRadius.x = math.dot(plane.normalDist.xyz, treeElement.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), treeElement.boundBox.extents);

                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            viewTreeElements[index] = math.select(visible, 0, math.distance(viewOringin, treeElement.boundBox.center) > maxDistance);
        }
    }

    [BurstCompile]
    public unsafe struct FTreeSelectLODJob : IJob
    {
        public int meshIndex;

        [ReadOnly]
        public NativeArray<FTreeElement> treeElements;

        [ReadOnly]
        public NativeArray<int> viewTreeElements;

        [WriteOnly]
        public NativeList<int> passTreeSections;

        public void Execute()
        {
            for (int i = 0; i < treeElements.Length; ++i)
            {
                if (viewTreeElements[i] != 0 && treeElements[i].meshIndex == meshIndex)
                {
                    passTreeSections.Add(i);
                }
            }
        }
    }
}
