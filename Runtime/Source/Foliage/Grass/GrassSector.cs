using System;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FGrassSector
    {
        public FMesh grass;
        public int grassIndex;
        public float4 widthScale;
        public FBoundSector boundSector;
        public FGrassSection[] sections;

        public FGrassSector(in int length)
        {
            this.sections = new FGrassSection[length];
        }

        public void Init(FBoundSector boundSector, TerrainData terrainData)
        {
            this.boundSector = boundSector;
            DetailPrototype detailPrototype = terrainData.detailPrototypes[grassIndex];
            this.widthScale = new float4(detailPrototype.minWidth, detailPrototype.maxWidth, detailPrototype.minHeight, detailPrototype.maxHeight);

            foreach (FGrassSection section in sections)
            {
                if (section.instanceCount == 0) { continue; }
                section.Init();
            }
        }

        public void Release()
        {
            foreach (FGrassSection section in sections)
            {
                if (section.instanceCount == 0) { continue; }
                section.Release();
            }
        }
    }
}
