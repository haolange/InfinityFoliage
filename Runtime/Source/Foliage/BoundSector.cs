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


        public FBoundSector(in int SectorSize, in int NumSection, in int SectionSize, in float3 SectorPivotPosition, in FAABB SectorBound)
        {
            int SectorSize_Half = SectorSize / 2;
            int SectionSize_Half = SectionSize / 2;

            bound = new FBound(new float3(SectorPivotPosition.x + SectorSize_Half, SectorPivotPosition.y + (SectorBound.size.y / 2), SectorPivotPosition.z + SectorSize_Half), SectorBound.size * 0.5f);
            sections = new FBoundSection[NumSection * NumSection];

            for (int SectorSizeX = 0; SectorSizeX <= NumSection - 1; SectorSizeX++)
            {
                for (int SectorSizeY = 0; SectorSizeY <= NumSection - 1; SectorSizeY++)
                {
                    int SectionIndex = (SectorSizeX * NumSection) + SectorSizeY;
                    float3 SectionPivotPosition = SectorPivotPosition + new float3(SectionSize * SectorSizeX, 0, SectionSize * SectorSizeY);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(SectionSize_Half, 0, SectionSize_Half);

                    sections[SectionIndex] = new FBoundSection();
                    sections[SectionIndex].PivotPosition = SectionPivotPosition;
                    sections[SectionIndex].CenterPosition = SectionCenterPosition;
                    sections[SectionIndex].BoundBox = new FAABB(SectionCenterPosition, new float3(SectionSize, 1, SectionSize));
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
        public JobHandle InitView(in float cullDistance, in float3 viewPos, FPlane* planes)
        {
            var sectionCullingJob = new FBoundSectionCullingJob();
            {
                sectionCullingJob.planes = planes;
                sectionCullingJob.viewPos = viewPos;
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
                Geometry.DrawBound(nativeSections[i].BoundBox, sectionsVisbible[i] == 1 ? Color.green : Color.red);
            }
        }

        public void BuildBounds(int SectorSize, int SectionSize, float ScaleY, float3 TerrianPosition, Texture2D Heightmap)
        {
            int SectorSize_Half = SectorSize / 2;

            for (int i = 0; i < sections.Length; i++)
            {
                ref FBoundSection Section = ref sections[i];

                float2 PositionScale = new float2(TerrianPosition.x, TerrianPosition.z) + new float2(SectorSize_Half, SectorSize_Half);
                float2 RectUV = new float2((Section.PivotPosition.x - PositionScale.x) + SectorSize_Half, (Section.PivotPosition.z - PositionScale.y) + SectorSize_Half);

                int ReverseScale = SectorSize - SectionSize;
                Color[] HeightValues = Heightmap.GetPixels(Mathf.FloorToInt(RectUV.x), ReverseScale - Mathf.FloorToInt(RectUV.y), Mathf.FloorToInt(SectionSize), Mathf.FloorToInt(SectionSize), 0);

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

                float PosY = ((Section.CenterPosition.y + MinHeight * ScaleY) + (Section.CenterPosition.y + MaxHeight * ScaleY)) * 0.5f;
                float SizeY = ((Section.CenterPosition.y + MinHeight * ScaleY) - (Section.CenterPosition.y + MaxHeight * ScaleY));
                float3 NewBoundCenter = new float3(Section.CenterPosition.x, PosY, Section.CenterPosition.z);
                Section.BoundBox = new FAABB(NewBoundCenter, new float3(SectionSize, SizeY, SectionSize));
            }
        }
#endif
    }
}
