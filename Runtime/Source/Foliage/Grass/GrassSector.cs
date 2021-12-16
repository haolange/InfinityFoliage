using System;
using UnityEngine;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FGrassSector
    {
        public FMesh grass;
        public int grassIndex;
        public float4 widthScale
        {
            get
            {
                return m_WidthScale;
            }
        }
        public FGrassSection[] sections;

        private float4 m_WidthScale;

        public FGrassSector(in int length)
        {
            this.sections = new FGrassSection[length];
        }

        public void Init(TerrainData terrainData)
        {
            DetailPrototype detailPrototype = terrainData.detailPrototypes[grassIndex];
            this.m_WidthScale = new float4(detailPrototype.minWidth, detailPrototype.maxWidth, detailPrototype.minHeight, detailPrototype.maxHeight);

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
