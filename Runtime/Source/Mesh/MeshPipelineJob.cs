using System;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
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

    public struct UpdateTreeTask : ITask
    {
        public int length;
        public float2 size;
        public float3 terrainPosition;
        public TreePrototype treePrototype;
        public TreeInstance[] treeInstances;
        public TreePrototype[] treePrototypes;
        public List<InstanceTransform> treeTransfroms;

        public void Execute()
        {
            InstanceTransform transform = new InstanceTransform();

            for (int i = 0; i < length; ++i)
            {
                ref TreeInstance treeInstance = ref treeInstances[i];
                TreePrototype searchTreePrototype = treePrototypes[treeInstance.prototypeIndex];
                if (searchTreePrototype.Equals(treePrototype))
                {
                    transform.rotation = new float3(0, treeInstance.rotation, 0);
                    transform.position = (treeInstance.position * new float3(size.x, size.y, size.x)) + terrainPosition;
                    transform.scale = new float3(treeInstance.widthScale, treeInstance.heightScale, treeInstance.widthScale);
                    treeTransfroms.Add(transform);
                }
            }
        }
    }

    public struct UpdateGrassTask : ITask
    {
        public int length;
        public byte[] dscDensity;
        public int[,] srcDensity;
        public float[,] srcHeight;
        public GrassSection grassSection;

        public void Execute()
        {
            for (int j = 0; j < length; ++j)
            {
                for (int k = 0; k < length; ++k)
                {
                    int densityIndex = j * length + k;
                    dscDensity[densityIndex] = (byte)srcDensity[j, k];
                    grassSection.count += srcDensity[j, k];
                }
            }
        }
    }

    public struct UpdateFoliageJob : IJob
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
    public unsafe struct GrassScatterJob : IJobParallelFor
    {
        [ReadOnly]
        public int split;

        [ReadOnly]
        public float densityScale;

        [ReadOnly]
        public float4 widthScale;

        [ReadOnly]
        public NativeArray<byte> flatDensity;

        [ReadOnly]
        public NativeArray<int> densityStarts;

        [ReadOnly]
        public NativeArray<int> destOffsets;

        [ReadOnly]
        public NativeArray<int> slotCounts;

        [ReadOnly]
        public NativeArray<float3> sectionPivots;

        [NativeDisableParallelForRestriction]
        public NativeArray<GrassElement> packedElements;

        public void Execute(int sectionIndex)
        {
            int slotCount = slotCounts[sectionIndex];
            if (slotCount <= 0) { return; }

            int destOffset = destOffsets[sectionIndex];
            float uniqueValue = 1.0f + (randomFloat((float)(sectionIndex + 1)) * 15.0f);
            float3 sectionPivot = sectionPivots[sectionIndex];
            int densityStart = densityStarts[sectionIndex];
            int densityLength = densityStarts[sectionIndex + 1] - densityStart;

            int written = 0;
            GrassElement grassElement;
            grassElement.matrix_World = float4x4.identity;

            for (int i = 0; i < densityLength && written < slotCount; ++i)
            {
                float scale = densityScale;
                if (scale <= 0.0001f) { break; }
                int density = (int)((float)flatDensity[densityStart + i] / scale);
                if (density == 0) { continue; }

                float3 position = sectionPivot + new float3(i % split, 0, i / split);
                for (int j = 0; j < density && written < slotCount; ++j)
                {
                    float multiplier = (j + 1) * uniqueValue;
                    float2 randomPoint = randomFloat2(new float2(position.x * multiplier, position.z * multiplier));
                    float3 newPosition = position + new float3(randomPoint.x, 0, randomPoint.y);

                    float randomRotate = randomFloat(newPosition.x - newPosition.y * multiplier);
                    float randomScale = randomFloat((newPosition.x + newPosition.z) * multiplier);
                    float yScale = widthScale.z + ((widthScale.w - widthScale.z) * randomScale);
                    float xzScale = widthScale.x + ((widthScale.y - widthScale.x) * randomScale);
                    float3 instanceScale = new float3(xzScale, yScale, xzScale);

                    grassElement.matrix_World = float4x4.TRS(newPosition, quaternion.AxisAngle(new float3(0, 1, 0), math.radians(randomRotate * 360)), instanceScale);
                    packedElements[destOffset + written] = grassElement;
                    ++written;
                }
            }

            grassElement.matrix_World = float4x4.TRS(sectionPivot, quaternion.identity, float3.zero);
            while (written < slotCount)
            {
                packedElements[destOffset + written] = grassElement;
                ++written;
            }
        }
    }

    [BurstCompile]
    public unsafe struct BoundCullingJob : IJob
    {
        public int length;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FrustumPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public Aabb* sectorBounds;

        [WriteOnly]
        public NativeArray<byte> visibleMap;

        public void Execute()
        {
            for (int index = 0; index < length; ++index)
            {
                int visible = 1;
                float2 distRadius = new float2(0, 0);
                ref Aabb sectorBound = ref sectorBounds[index];

                for (int planeIndex = 0; planeIndex < 6; ++planeIndex)
                {
                    ref FrustumPlane plane = ref planes[planeIndex];
                    distRadius.x = math.dot(plane.normalDist.xyz, sectorBound.center) + plane.normalDist.w;
                    distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectorBound.extents);
                    visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
                }
                visibleMap[index] = (byte)visible;
            }
        }
    }

    [BurstCompile]
    public unsafe struct BoundCullingParallelJob : IJobParallelFor
    {
        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FrustumPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public Aabb* sectorBounds;

        [WriteOnly]
        public NativeArray<byte> visibleMap;

        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref Aabb sectorBound = ref sectorBounds[index];

            for (int planeIndex = 0; planeIndex < 6; ++planeIndex)
            {
                ref FrustumPlane plane = ref planes[planeIndex];
                distRadius.x = math.dot(plane.normalDist.xyz, sectorBound.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectorBound.extents);
                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            visibleMap[index] = (byte)visible;
        }
    }

    [BurstCompile]
    public unsafe struct SectionCullingJob : IJobParallelFor
    {
        [ReadOnly]
        public float4 viewOrigin;

        [ReadOnly]
        public float cullDistance;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FrustumPlane* planes;

        [NativeDisableUnsafePtrRestriction]
        public BoundSection* sectionBounds;

        [WriteOnly]
        public NativeArray<byte> visibleMap;

        public void Execute(int index)
        {
            int visible = 1;
            float2 distRadius = new float2(0, 0);
            ref BoundSection sectionBound = ref sectionBounds[index];

            for (int i = 0; i < 6; ++i)
            {
                ref FrustumPlane plane = ref planes[i];
                distRadius.x = math.dot(plane.normalDist.xyz, sectionBound.boundBox.center) + plane.normalDist.w;
                distRadius.y = math.dot(math.abs(plane.normalDist.xyz), sectionBound.boundBox.extents);
                visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
            }
            float4 boundPivot = new float4(sectionBound.boundBox.center.x, sectionBound.boundBox.center.y + sectionBound.boundBox.extents.y, sectionBound.boundBox.center.z, 1);
            visibleMap[index] = (byte)math.select(visible, 0, math.distance(viewOrigin.xyz, boundPivot.xyz) > cullDistance);
        }
    }

    [BurstCompile]
    public unsafe struct TreeCullLodJob : IJobParallelFor
    {
        public int writeLod;
        public float maxDistance;
        public float3 viewOrigin;
        public float4x4 matrixProj;

        [ReadOnly]
        public NativeArray<byte> cellVisible;

        [ReadOnly]
        public NativeArray<int> cellOffset;

        [ReadOnly]
        public NativeArray<int> cellCount;

        [ReadOnly]
        public NativeArray<Aabb> bounds;

        [ReadOnly]
        public NativeArray<float> lodScreenSizes;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FrustumPlane* planes;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> instanceVisible;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> lodNow;

        public void Execute(int cell)
        {
            int offset = cellOffset[cell];
            int count = cellCount[cell];
            if (cellVisible[cell] == 0)
            {
                for (int i = 0; i < count; ++i)
                {
                    instanceVisible[offset + i] = 0;
                    if (writeLod != 0)
                    {
                        lodNow[offset + i] = -1;
                    }
                }
                return;
            }

            for (int i = 0; i < count; ++i)
            {
                int index = offset + i;
                Aabb box = bounds[index];
                int visible = 1;
                float2 distRadius = new float2(0, 0);
                for (int planeIndex = 0; planeIndex < 6; ++planeIndex)
                {
                    ref FrustumPlane plane = ref planes[planeIndex];
                    distRadius.x = math.dot(plane.normalDist.xyz, box.center) + plane.normalDist.w;
                    distRadius.y = math.dot(math.abs(plane.normalDist.xyz), box.extents);
                    visible = math.select(visible, 0, distRadius.x + distRadius.y < 0);
                }
                visible = math.select(visible, 0, math.distance(viewOrigin, box.center) > maxDistance);
                instanceVisible[index] = visible;
                if (writeLod == 0)
                {
                    continue;
                }
                if (visible == 0)
                {
                    lodNow[index] = -1;
                    continue;
                }

                float radius = math.max(math.max(math.abs(box.extents.x), math.abs(box.extents.y)), math.abs(box.extents.z));
                float distSqr = ((box.center.x - viewOrigin.x) * (box.center.x - viewOrigin.x)) + ((box.center.y - viewOrigin.y) * (box.center.y - viewOrigin.y)) + ((box.center.z - viewOrigin.z) * (box.center.z - viewOrigin.z));
                distSqr *= matrixProj.c2.z;
                float screenMultiple = math.max(0.5f * matrixProj.c0.x, 0.5f * matrixProj.c1.y) * radius;
                float screenRadiusSqr = (screenMultiple * screenMultiple) / math.max(1, distSqr);

                int lod = 0;
                int last = lodScreenSizes.Length - 1;
                for (int lodIndex = last; lodIndex >= 0; --lodIndex)
                {
                    float threshold = lodScreenSizes[lodIndex] * 0.5f;
                    if ((threshold * threshold) >= screenRadiusSqr)
                    {
                        lod = lodIndex;
                        break;
                    }
                }
                lodNow[index] = lod;
            }
        }
    }

    [BurstCompile]
    public struct TreeCompactLodJob : IJob
    {
        public int meshIndex;
        public int ditherEnabled;

        [ReadOnly]
        public NativeArray<int> instanceVisible;

        [ReadOnly]
        public NativeArray<int> lodHold;

        [ReadOnly]
        public NativeArray<int> lodNow;

        public NativeList<int> stable;
        public NativeList<int> fadeOut;
        public NativeList<int> fadeIn;

        public void Execute()
        {
            for (int i = 0; i < instanceVisible.Length; ++i)
            {
                if (instanceVisible[i] == 0) { continue; }
                int hold = lodHold[i];
                int now = lodNow[i];
                if (now < 0) { continue; }
                if (hold < 0) { hold = now; }

                int bucket;
                if (ditherEnabled == 0)
                {
                    bucket = now == meshIndex ? (int)LodBucket.Stable : (int)LodBucket.None;
                }
                else
                {
                    int delta = hold - now;
                    if (delta < 0) { delta = -delta; }
                    if (delta > 1)
                    {
                        bucket = now == meshIndex ? (int)LodBucket.Stable : (int)LodBucket.None;
                    }
                    else if (hold == now)
                    {
                        bucket = now == meshIndex ? (int)LodBucket.Stable : (int)LodBucket.None;
                    }
                    else if (hold == meshIndex)
                    {
                        bucket = (int)LodBucket.FadeOut;
                    }
                    else if (now == meshIndex)
                    {
                        bucket = (int)LodBucket.FadeIn;
                    }
                    else
                    {
                        bucket = (int)LodBucket.None;
                    }
                }

                if (bucket == (int)LodBucket.Stable) { stable.Add(i); }
                else if (bucket == (int)LodBucket.FadeOut) { fadeOut.Add(i); }
                else if (bucket == (int)LodBucket.FadeIn) { fadeIn.Add(i); }
            }
        }
    }
}
