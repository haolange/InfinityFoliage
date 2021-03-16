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

        internal FBoundSector boundSector;
        internal FGrassSection[] details;


        public FGrassSector(FBoundSector boundSector)
        {
            this.boundSector = boundSector;
            this.details = new FGrassSection[boundSector.Sections.Length];
        }
    }
}
