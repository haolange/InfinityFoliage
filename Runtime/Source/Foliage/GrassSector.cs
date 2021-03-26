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
                section.Init();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BuildInstance(in int split, in float densityScale, in NativeList<JobHandle> taskHandles)
        {
            foreach (FGrassSection section in sections)
            {
                //if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }

                FBoundSection boundSection = boundSector.nativeSections[section.boundIndex];
                taskHandles.Add(section.BuildInstance(split, densityScale, boundSection.pivotPosition));
            }
        }

        public void Release()
        {
            foreach (FGrassSection section in sections)
            {
                section.Release();
            }
        }

        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, in bool needUpdateGPU)
        {
            foreach (FGrassSection section in sections)
            {
                if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }

                section.DispatchDraw(cmdBuffer, grass.meshes[0], grass.materials[0], passIndex, needUpdateGPU);
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
