using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class BoundSector
    {
        public Aabb bound;
        public BoundSection[] sections;
        public NativeArray<byte> visibleMap;
        public NativeArray<BoundSection> nativeSections;

        public BoundSector(in int numSection, in int sectorSize, in int sectionSize, in float3 sectorPivotPosition, in Aabb sectorBound, in bool needSections = true)
        {
            int sectorSizeHalf = sectorSize / 2;
            int sectionSizeHalf = sectionSize / 2;
            bound = new Aabb(new float3(sectorPivotPosition.x + sectorSizeHalf, sectorPivotPosition.y + (sectorBound.size.y * 0.5f), sectorPivotPosition.z + sectorSizeHalf), sectorBound.size);

            if (!needSections) { return; }
            sections = new BoundSection[numSection * numSection];
            for (int x = 0; x < numSection; ++x)
            {
                for (int y = 0; y < numSection; ++y)
                {
                    int sectionIndex = FoliageLogic.CellIndex(x, y, numSection);
                    float3 sectionPivotPosition = sectorPivotPosition + new float3(sectionSize * x, 0, sectionSize * y);
                    float3 sectionCenterPosition = sectionPivotPosition + new float3(sectionSizeHalf, 0, sectionSizeHalf);

                    sections[sectionIndex] = new BoundSection();
                    sections[sectionIndex].pivotPosition = sectionPivotPosition.xz;
                    sections[sectionIndex].boundBox = new Aabb(sectionCenterPosition, new float3(sectionSize, 1, sectionSize));
                }
            }
        }

        public void BuildNativeCollection()
        {
            visibleMap = new NativeArray<byte>(sections.Length, Allocator.Persistent);
            nativeSections = new NativeArray<BoundSection>(sections.Length, Allocator.Persistent);
            NativeArray<BoundSection>.Copy(sections, nativeSections);
        }

        public void ReleaseNativeCollection()
        {
            if (visibleMap.IsCreated) { visibleMap.Dispose(); }
            if (nativeSections.IsCreated) { nativeSections.Dispose(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe JobHandle InitView(in float drawDistance, in float4 viewOrigin, in FrustumPlane* planes)
        {
            var cullingJob = new SectionCullingJob();
            {
                cullingJob.planes = planes;
                cullingJob.viewOrigin = viewOrigin;
                cullingJob.visibleMap = visibleMap;
                cullingJob.cullDistance = drawDistance + (drawDistance * 0.5f);
                cullingJob.sectionBounds = (BoundSection*)nativeSections.GetUnsafePtr();
            }
            return cullingJob.Schedule(nativeSections.Length, 32);
        }

#if UNITY_EDITOR
        public void BuildBounds(in int sectorSize, in int sectionSize, in float scaleY, in float3 terrianPosition, Texture2D heightmap)
        {
            int sectorSizeHalf = sectorSize / 2;

            for (int i = 0; i < sections.Length; ++i)
            {
                ref BoundSection section = ref sections[i];
                float2 positionScale = new float2(terrianPosition.x, terrianPosition.z) + new float2(sectorSizeHalf, sectorSizeHalf);
                float2 rectUV = new float2((section.pivotPosition.x - positionScale.x) + sectorSizeHalf, (section.pivotPosition.y - positionScale.y) + sectorSizeHalf);

                int reverseScale = sectorSize - sectionSize;
                Color[] heightValues = heightmap.GetPixels(Mathf.FloorToInt(rectUV.x), reverseScale - Mathf.FloorToInt(rectUV.y), Mathf.FloorToInt(sectionSize), Mathf.FloorToInt(sectionSize), 0);

                float minHeight = heightValues[0].r;
                float maxHeight = heightValues[0].r;
                for (int j = 0; j < heightValues.Length; ++j)
                {
                    if (minHeight > heightValues[j].r)
                    {
                        minHeight = heightValues[j].r;
                    }

                    if (maxHeight < heightValues[j].r)
                    {
                        maxHeight = heightValues[j].r;
                    }
                }

                int halfSectionSize = sectionSize / 2;
                float3 centerPosition = new float3(section.pivotPosition.x, 0, section.pivotPosition.y) + new float3(halfSectionSize, 0, halfSectionSize);
                float posY = ((centerPosition.y + minHeight * scaleY) + (centerPosition.y + maxHeight * scaleY)) * 0.5f;
                float sizeY = math.abs((centerPosition.y + minHeight * scaleY) - (centerPosition.y + maxHeight * scaleY));
                if (sizeY < 1f) { sizeY = 1f; }
                float3 newBoundCenter = new float3(centerPosition.x, posY, centerPosition.z);
                section.boundBox = new Aabb(newBoundCenter, new float3(sectionSize, sizeY, sectionSize));
            }
        }
#endif
    }
}
