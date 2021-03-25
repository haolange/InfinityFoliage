using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FGrassSector
    {
        public FMesh grass;
        public int grassIndex;
        public int cullDistance = 128;
        public FBoundSector boundSector;
        public FGrassSection[] sections;


        public FGrassSector(in int length)
        {
            this.sections = new FGrassSection[length];
        }

        public void Init(FBoundSector boundSector)
        {
            this.boundSector = boundSector;

            foreach (FGrassSection section in sections)
            {
                section.BuildNativeCollection();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BuildInstance(in int split, in float densityScale, in NativeList<JobHandle> taskHandles)
        {
            foreach (FGrassSection section in sections)
            {
                if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }

                FBoundSection boundSection = boundSector.nativeSections[section.boundIndex];
                taskHandles.Add(section.BuildInstance(split, densityScale, boundSection.PivotPosition));
            }
        }

        public void Release()
        {
            foreach (FGrassSection section in sections)
            {
                section.ReleaseNativeCollection();
            }
        }

#if UNITY_EDITOR
        public void DrawBounds()
        {
            foreach (FGrassSection section in sections)
            {
                if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }

                section.DrawBounds();
            }
        }
#endif
    }
}
