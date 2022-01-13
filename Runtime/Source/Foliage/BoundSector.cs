using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FBoundSector
    {
        public FAABB bound;
        public FBoundSection[] sections;
        public NativeArray<byte> visibleMap;
        public NativeArray<FBoundSection> m_Sections;

        public FBoundSector(in int numSection, in int sectorSize, in int sectionSize, in float3 sectorPivotPosition, in FAABB sectorBound, in bool needSections = true)
        {
            int sectorSize_Half = sectorSize / 2;
            int sectionSize_Half = sectionSize / 2;
            bound = new FAABB(new float3(sectorPivotPosition.x + sectorSize_Half, sectorPivotPosition.y + (sectorBound.size.y / 2), sectorPivotPosition.z + sectorSize_Half), sectorBound.size);
            
            if(!needSections) { return; }
            sections = new FBoundSection[numSection * numSection];
            for (int x = 0; x < numSection; ++x)
            {
                for (int y = 0; y < numSection; ++y)
                {
                    int sectionIndex = (x * numSection) + y;
                    float3 sectionPivotPosition = sectorPivotPosition + new float3(sectionSize * x, 0, sectionSize * y);
                    float3 sectionCenterPosition = sectionPivotPosition + new float3(sectionSize_Half, 0, sectionSize_Half);

                    sections[sectionIndex] = new FBoundSection();
                    sections[sectionIndex].pivotPosition = sectionPivotPosition;
                    sections[sectionIndex].boundBox = new FAABB(sectionCenterPosition, new float3(sectionSize, 1, sectionSize));
                }
            }
        }

        public void BuildNativeCollection()
        {
            visibleMap = new NativeArray<byte>(sections.Length, Allocator.Persistent);
            m_Sections = new NativeArray<FBoundSection>(sections.Length, Allocator.Persistent);
            NativeArray<FBoundSection>.Copy(sections, m_Sections);
            sections = null;
        }

        public void ReleaseNativeCollection()
        {
            visibleMap.Dispose();
            m_Sections.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe JobHandle InitView(in float drawDistance, in float4 viewOrigin, in FPlane* planes)
        {
            var grassCullingJob = new FGrassCullingJob();
            {
                grassCullingJob.planes = planes;
                grassCullingJob.viewOrigin = viewOrigin;
                grassCullingJob.visibleMap = visibleMap;
                grassCullingJob.cullDistance = drawDistance + (drawDistance / 2);
                grassCullingJob.sectionBounds = (FBoundSection*)m_Sections.GetUnsafePtr();
            }
            return grassCullingJob.Schedule(m_Sections.Length, 32);
        }

#if UNITY_EDITOR
        public void BuildBounds(in int sectorSize, in int sectionSize, in float scaleY, in float3 terrianPosition, Texture2D heightmap)
        {
            int sectorSizeHalf = sectorSize / 2;

            for (int i = 0; i < sections.Length; ++i)
            {
                ref FBoundSection section = ref sections[i];
                float2 positionScale = new float2(terrianPosition.x, terrianPosition.z) + new float2(sectorSizeHalf, sectorSizeHalf);
                float2 rectUV = new float2((section.pivotPosition.x - positionScale.x) + sectorSizeHalf, (section.pivotPosition.z - positionScale.y) + sectorSizeHalf);

                int reverseScale = sectorSize - sectionSize;
                Color[] heightValues = heightmap.GetPixels(Mathf.FloorToInt(rectUV.x), reverseScale - Mathf.FloorToInt(rectUV.y), Mathf.FloorToInt(sectionSize), Mathf.FloorToInt(sectionSize), 0);

                float minHeight = heightValues[0].r;
                float maxHeight = heightValues[0].r;
                for (int j = 0; j < heightValues.Length; ++j)
                {
                    if (minHeight < heightValues[j].r)
                    {
                        minHeight = heightValues[j].r;
                    }

                    if (maxHeight > heightValues[j].r)
                    {
                        maxHeight = heightValues[j].r;
                    }
                }

                int halfSectionSize = sectionSize / 2;
                float3 centerPosition = section.pivotPosition + new float3(halfSectionSize, 0, halfSectionSize);
                float posY = ((centerPosition.y + minHeight * scaleY) + (centerPosition.y + maxHeight * scaleY)) * 0.5f;
                float sizeY = ((centerPosition.y + minHeight * scaleY) - (centerPosition.y + maxHeight * scaleY));
                float3 newBoundCenter = new float3(centerPosition.x, posY, centerPosition.z);
                section.boundBox = new FAABB(newBoundCenter, new float3(sectionSize, sizeY, sectionSize));
            }
        }
#endif
    }
}
