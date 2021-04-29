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
    public unsafe class FBoundSector
    {
        public FBound bound;
        public FBoundSection[] sections;
        public NativeArray<int> sectionsVisbible;
        public NativeArray<FBoundSection> nativeSections;


        public FBoundSector(in int sectorSize, in int numSection, in int sectionSize, in float3 sectorPivotPosition, in FAABB sectorBound)
        {
            int sectorSize_Half = sectorSize / 2;
            int sectionSize_Half = sectionSize / 2;

            bound = new FBound(new float3(sectorPivotPosition.x + sectorSize_Half, sectorPivotPosition.y + (sectorBound.size.y / 2), sectorPivotPosition.z + sectorSize_Half), sectorBound.size * 0.5f);
            sections = new FBoundSection[numSection * numSection];

            for (int x = 0; x <= numSection - 1; x++)
            {
                for (int y = 0; y <= numSection - 1; y++)
                {
                    int SectionIndex = (x * numSection) + y;
                    float3 SectionPivotPosition = sectorPivotPosition + new float3(sectionSize * x, 0, sectionSize * y);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(sectionSize_Half, 0, sectionSize_Half);

                    sections[SectionIndex] = new FBoundSection();
                    sections[SectionIndex].pivotPosition = SectionPivotPosition;
                    sections[SectionIndex].centerPosition = SectionCenterPosition;
                    sections[SectionIndex].boundBox = new FAABB(SectionCenterPosition, new float3(sectionSize, 1, sectionSize));
                }
            }
        }

        public void BuildNativeCollection()
        {
            nativeSections = new NativeArray<FBoundSection>(sections.Length, Allocator.Persistent);
            sectionsVisbible = new NativeArray<int>(sections.Length, Allocator.Persistent);

            for (int i = 0; i < sections.Length; i++)
            {
                nativeSections[i] = sections[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            nativeSections.Dispose();
            sectionsVisbible.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle InitView(in float cullDistance, in float3 viewOrigin, FPlane* planes)
        {
            var sectionCullingJob = new FBoundSectionCullingJob();
            {
                sectionCullingJob.planes = planes;
                sectionCullingJob.viewOrigin = viewOrigin;
                sectionCullingJob.maxDistance = cullDistance;
                sectionCullingJob.visibleMap = sectionsVisbible;
                sectionCullingJob.sectionBounds = (FBoundSection*)nativeSections.GetUnsafePtr();
            }
            return sectionCullingJob.Schedule(nativeSections.Length, 32);
        }

#if UNITY_EDITOR
        public void DrawBound()
        {
            Geometry.DrawBound(bound, Color.white);

            for (int i = 0; i < nativeSections.Length; i++)
            {
                if(sectionsVisbible[i] == 1)
                {
                    Geometry.DrawBound(nativeSections[i].boundBox, Color.green);
                }
            }
        }

        public void BuildBounds(int sectorSize, int sectionSize, float scaleY, float3 terrianPosition, Texture2D heightmap)
        {
            int SectorSize_Half = sectorSize / 2;

            for (int i = 0; i < sections.Length; i++)
            {
                ref FBoundSection Section = ref sections[i];

                float2 PositionScale = new float2(terrianPosition.x, terrianPosition.z) + new float2(SectorSize_Half, SectorSize_Half);
                float2 RectUV = new float2((Section.pivotPosition.x - PositionScale.x) + SectorSize_Half, (Section.pivotPosition.z - PositionScale.y) + SectorSize_Half);

                int ReverseScale = sectorSize - sectionSize;
                Color[] HeightValues = heightmap.GetPixels(Mathf.FloorToInt(RectUV.x), ReverseScale - Mathf.FloorToInt(RectUV.y), Mathf.FloorToInt(sectionSize), Mathf.FloorToInt(sectionSize), 0);

                float MinHeight = HeightValues[0].r;
                float MaxHeight = HeightValues[0].r;
                for (int j = 0; j < HeightValues.Length; j++)
                {
                    if (MinHeight < HeightValues[j].r)
                    {
                        MinHeight = HeightValues[j].r;
                    }

                    if (MaxHeight > HeightValues[j].r)
                    {
                        MaxHeight = HeightValues[j].r;
                    }
                }

                float PosY = ((Section.centerPosition.y + MinHeight * scaleY) + (Section.centerPosition.y + MaxHeight * scaleY)) * 0.5f;
                float SizeY = ((Section.centerPosition.y + MinHeight * scaleY) - (Section.centerPosition.y + MaxHeight * scaleY));
                float3 NewBoundCenter = new float3(Section.centerPosition.x, PosY, Section.centerPosition.z);
                Section.boundBox = new FAABB(NewBoundCenter, new float3(sectionSize, SizeY, sectionSize));
            }
        }
#endif
    }
}
