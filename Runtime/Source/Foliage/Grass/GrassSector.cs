using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
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

        public void Init(FBoundSector boundSector, TerrainData terrainData, in FGrassShaderProperty shaderProperty)
        {
            this.boundSector = boundSector;
            DetailPrototype detailPrototype = terrainData.detailPrototypes[grassIndex];
            this.widthScale = new float4(detailPrototype.minWidth, detailPrototype.maxWidth, detailPrototype.minHeight, detailPrototype.maxHeight);

            foreach (FGrassSection section in sections)
            {
                if (section.totalDensity == 0) { continue; }
                section.Init(grass.meshes[0], grass.materials[0], shaderProperty);
            }
        }

        public void Release()
        {
            foreach (FGrassSection section in sections)
            {
                if (section.totalDensity == 0) { continue; }
                section.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BuildInstance(in int split, in float uniqueValue, in float heightScale, in float densityScale)
        {
            foreach (FGrassSection section in sections)
            {
                if (section.totalDensity == 0) { continue; }

                FBoundSection boundSection = boundSector.nativeSections[section.boundIndex];
                section.BuildInstance(split, uniqueValue, heightScale, densityScale, boundSection.pivotPosition, widthScale);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            foreach (FGrassSection section in sections)
            {
                if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }
                section.DispatchDraw(cmdBuffer, passIndex);
            }
        }
    }
}
