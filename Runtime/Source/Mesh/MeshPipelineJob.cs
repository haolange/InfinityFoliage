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
        public int heightRes;
        public float maxDistance;
        public float3 viewOrigin;
        public float4x4 matrixProj;
        public float3 terrainPos;
        public float3 terrainSize;

        [ReadOnly]
        public NativeArray<byte> cellVisible;

        [ReadOnly]
        public NativeArray<int> instanceCell;

        [ReadOnly]
        public NativeArray<Aabb> bounds;

        [ReadOnly]
        public NativeArray<float> lodScreenSizes;

        [ReadOnly]
        public NativeArray<float> heights;

        [ReadOnly]
        [NativeDisableUnsafePtrRestriction]
        public FrustumPlane* planes;

        [WriteOnly]
        public NativeArray<ulong> chunkMasks;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> lodNow;

        public void Execute(int chunk)
        {
            int candidateBase = chunk * 64;
            int remain = bounds.Length - candidateBase;
            int count = remain > 64 ? 64 : (remain < 0 ? 0 : remain);
            ulong mask = 0;

            for (int bit = 0; bit < count; ++bit)
            {
                int index = candidateBase + bit;
                int cell = instanceCell[index];
                if (cellVisible[cell] == 0)
                {
                    if (writeLod != 0) { lodNow[index] = -1; }
                    continue;
                }

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
                if (visible != 0 && heights.IsCreated && heightRes > 1)
                {
                    visible = math.select(visible, 0, TerrainOccludes(box) != 0);
                }
                if (visible == 0)
                {
                    if (writeLod != 0) { lodNow[index] = -1; }
                    continue;
                }

                mask |= 1UL << bit;
                if (writeLod == 0) { continue; }

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

            chunkMasks[chunk] = mask;
        }

        int TerrainOccludes(Aabb box)
        {
            float3 min = box.min;
            float3 max = box.max;
            float centerX = (min.x + max.x) * 0.5f;
            float centerZ = (min.z + max.z) * 0.5f;
            float dx = centerX - viewOrigin.x;
            float dz = centerZ - viewOrigin.z;
            float boxDist = math.sqrt((dx * dx) + (dz * dz));
            if (boxDist < 0.5f) { return 0; }

            float boxTopSlope = (max.y - viewOrigin.y) / boxDist;
            for (int s = 1; s < 8; ++s)
            {
                float t = s / 8.0f;
                if (t > 0.85f) { break; }
                float dist = boxDist * t;
                if (dist < 1f) { continue; }
                float height = SampleHeight(viewOrigin.x + (dx * t), viewOrigin.z + (dz * t));
                float terrainSlope = (height - viewOrigin.y) / dist;
                if (terrainSlope > boxTopSlope + 0.05f) { return 1; }
            }
            return 0;
        }

        float SampleHeight(float worldX, float worldZ)
        {
            float u = terrainSize.x > 0.0001f ? (worldX - terrainPos.x) / terrainSize.x : 0f;
            float v = terrainSize.z > 0.0001f ? (worldZ - terrainPos.z) / terrainSize.z : 0f;
            u = math.clamp(u, 0f, 1f);
            v = math.clamp(v, 0f, 1f);
            float fx = u * (heightRes - 1);
            float fz = v * (heightRes - 1);
            int x0 = (int)fx;
            int z0 = (int)fz;
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            if (x1 >= heightRes) { x1 = heightRes - 1; }
            if (z1 >= heightRes) { z1 = heightRes - 1; }
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = heights[(z0 * heightRes) + x0];
            float h10 = heights[(z0 * heightRes) + x1];
            float h01 = heights[(z1 * heightRes) + x0];
            float h11 = heights[(z1 * heightRes) + x1];
            return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
        }
    }

    [BurstCompile]
    public struct TreeEmitLodMasksJob : IJob
    {
        public int meshIndex;
        public int ditherEnabled;
        public int instanceCount;

        [ReadOnly]
        public NativeArray<ulong> chunkMasks;

        [ReadOnly]
        public NativeArray<int> lodHold;

        [ReadOnly]
        public NativeArray<int> lodNow;

        public NativeArray<ulong> stableMask;
        public NativeArray<ulong> fadeOutMask;
        public NativeArray<ulong> fadeInMask;
        public NativeArray<int> bucketCounts;

        public void Execute()
        {
            int stableCount = 0;
            int fadeOutCount = 0;
            int fadeInCount = 0;
            int chunkCount = chunkMasks.Length;
            for (int chunk = 0; chunk < chunkCount; ++chunk)
            {
                int candidateBase = chunk * 64;
                int remain = instanceCount - candidateBase;
                int count = remain > 64 ? 64 : (remain < 0 ? 0 : remain);
                ulong vis = chunkMasks[chunk];
                ulong stable = 0;
                ulong fadeOut = 0;
                ulong fadeIn = 0;
                for (int bit = 0; bit < count; ++bit)
                {
                    if ((vis & (1UL << bit)) == 0) { continue; }
                    int index = candidateBase + bit;
                    int hold = lodHold[index];
                    int now = lodNow[index];
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

                    ulong bitMask = 1UL << bit;
                    if (bucket == (int)LodBucket.Stable)
                    {
                        stable |= bitMask;
                        ++stableCount;
                    }
                    else if (bucket == (int)LodBucket.FadeOut)
                    {
                        fadeOut |= bitMask;
                        ++fadeOutCount;
                    }
                    else if (bucket == (int)LodBucket.FadeIn)
                    {
                        fadeIn |= bitMask;
                        ++fadeInCount;
                    }
                }
                stableMask[chunk] = stable;
                fadeOutMask[chunk] = fadeOut;
                fadeInMask[chunk] = fadeIn;
            }
            bucketCounts[0] = stableCount;
            bucketCounts[1] = fadeOutCount;
            bucketCounts[2] = fadeInCount;
        }
    }
}
