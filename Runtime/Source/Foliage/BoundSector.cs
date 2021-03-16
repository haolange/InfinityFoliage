using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FBoundSector
    {
        [SerializeField]
        internal FBound Bound;
        [SerializeField]
        internal FBoundSection[] Sections;
        internal NativeArray<FBoundSection> NativeSections;


        public FBoundSector(in int SectorSize, in int NumSection, in int SectionSize, in float3 SectorPivotPosition, in FAABB SectorBound)
        {
            int SectorSize_Half = SectorSize / 2;
            int SectionSize_Half = SectionSize / 2;

            Bound = new FBound(new float3(SectorPivotPosition.x + SectorSize_Half, SectorPivotPosition.y + (SectorBound.size.y / 2), SectorPivotPosition.z + SectorSize_Half), SectorBound.size * 0.5f);
            Sections = new FBoundSection[NumSection * NumSection];

            for (int SectorSizeX = 0; SectorSizeX <= NumSection - 1; SectorSizeX++)
            {
                for (int SectorSizeY = 0; SectorSizeY <= NumSection - 1; SectorSizeY++)
                {
                    int SectionIndex = (SectorSizeX * NumSection) + SectorSizeY;
                    float3 SectionPivotPosition = SectorPivotPosition + new float3(SectionSize * SectorSizeX, 0, SectionSize * SectorSizeY);
                    float3 SectionCenterPosition = SectionPivotPosition + new float3(SectionSize_Half, 0, SectionSize_Half);

                    Sections[SectionIndex] = new FBoundSection();
                    Sections[SectionIndex].PivotPosition = SectionPivotPosition;
                    Sections[SectionIndex].CenterPosition = SectionCenterPosition;
                    Sections[SectionIndex].BoundBox = new FAABB(SectionCenterPosition, new float3(SectionSize, 1, SectionSize));
                }
            }
        }

        public void BuildNativeCollection()
        {
            NativeSections = new NativeArray<FBoundSection>(Sections.Length, Allocator.Persistent);

            for (int i = 0; i < Sections.Length; i++)
            {
                NativeSections[i] = Sections[i];
            }
        }

        public void ReleaseNativeCollection()
        {
            NativeSections.Dispose();
        }

#if UNITY_EDITOR
        public void DrawBound()
        {
            Geometry.DrawBound(Bound, Color.white);

            for (int i = 0; i < Sections.Length; i++)
            {
                Geometry.DrawBound(Sections[i].BoundBox, Color.yellow);
            }
        }

        public void BuildBounds(int SectorSize, int SectionSize, float ScaleY, float3 TerrianPosition, Texture2D Heightmap)
        {
            int SectorSize_Half = SectorSize / 2;

            for (int i = 0; i < Sections.Length; i++)
            {
                ref FBoundSection Section = ref Sections[i];

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
