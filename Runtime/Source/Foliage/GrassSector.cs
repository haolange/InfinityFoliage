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
                section.Init(grass.meshes[0], grass.materials[0], shaderProperty);
            }
        }

        public void Release()
        {
            foreach (FGrassSection section in sections)
            {
                section.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BuildInstance(in int split, in float heightScale, in float densityScale, in NativeList<JobHandle> taskHandles)
        {
            foreach (FGrassSection section in sections)
            {
                //if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }
                FBoundSection boundSection = boundSector.nativeSections[section.boundIndex];
                taskHandles.Add(section.BuildInstance(split, heightScale, densityScale, boundSection.pivotPosition, widthScale));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGPUData(CommandBuffer cmdBuffer)
        {
            foreach (FGrassSection section in sections)
            {
                //if (boundSector.sectionsVisbible[section.boundIndex] == 0) { continue; }
                section.SetGPUData(cmdBuffer);
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
