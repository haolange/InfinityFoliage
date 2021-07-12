using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    public struct FGrassElement : IEquatable<FGrassElement>
    {
        public float4x4 matrix_World;


        public FGrassElement(in float4x4 matrix_World)
        {
            this.matrix_World = matrix_World;
        }

        public bool Equals(FGrassElement target)
        {
            return matrix_World.Equals(target.matrix_World);
        }

        public override bool Equals(object target)
        {
            return Equals((FGrassElement)target);
        }

        public override int GetHashCode()
        {
            return matrix_World.GetHashCode();
        }
    }

    public struct FGrassShaderProperty 
    {
        public int terrainSize;
        public float4 terrainPivotScaleY;
        public RenderTexture heightmapTexture;
    }

    internal static class GrassShaderID
    {
        internal static int terrainSize = Shader.PropertyToID("_TerrainSize");
        internal static int terrainPivotScaleY = Shader.PropertyToID("_TerrainPivotScaleY");
        internal static int elementBuffer = Shader.PropertyToID("_GrassElementBuffer");
        internal static int terrainHeightmap = Shader.PropertyToID("_TerrainHeightmap");
    }

    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int instanceCount;
        //public float[] heightmap;
        public byte[] densityMap;

        private Mesh m_Mesh;
        private Material m_Material;
        private ComputeBuffer m_GrassBuffer;
        private NativeArray<byte> m_DensityMap;
        //private NativeArray<float> m_heightmap;
        private NativeList<FGrassElement> m_GrassElements;

        public void Init(Mesh mesh, Material material, in FGrassShaderProperty shaderProperty)
        {
            if(instanceCount == 0) { return; }

            m_Mesh = mesh;
            m_Material = new Material(material);
            m_GrassBuffer = new ComputeBuffer(instanceCount, Marshal.SizeOf(typeof(FGrassElement)));

            //m_heightmap = new NativeArray<float>(heightmap.Length, Allocator.Persistent);
            m_DensityMap = new NativeArray<byte>(densityMap.Length, Allocator.Persistent);

            NativeArray<byte>.Copy(densityMap, m_DensityMap);
            //NativeArray<float>.Copy(heightmap, m_heightmap);
            densityMap = null;
            
            m_Material.SetBuffer(GrassShaderID.elementBuffer, m_GrassBuffer);
            m_Material.SetInt(GrassShaderID.terrainSize, shaderProperty.terrainSize + 1);
            m_Material.SetTexture(GrassShaderID.terrainHeightmap, shaderProperty.heightmapTexture);
            m_Material.SetVector(GrassShaderID.terrainPivotScaleY, shaderProperty.terrainPivotScaleY);
        }

        public void Release()
        {
            if (instanceCount == 0) { return; }

            //m_heightmap.Dispose();
            //m_DensityMap.Dispose();
            m_GrassBuffer.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Material);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float uniqueValue, in float heightScale, in float densityScale, in float3 sectionPivot, in float4 widthScale)
        {
            if (instanceCount == 0 || densityScale == 0) { return default; }

            m_GrassElements = new NativeList<FGrassElement>(m_DensityMap.Length, Allocator.TempJob);
            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.widthScale = widthScale;
                grassScatterJob.uniqueValue = uniqueValue;
                //grassScatterJob.heightMap = m_heightmap;
                grassScatterJob.densityMap = m_DensityMap;
                grassScatterJob.densityScale = densityScale;
                grassScatterJob.sectionPivot = sectionPivot;
                //grassScatterJob.heightScale = heightScale;
                grassScatterJob.grassElements = m_GrassElements;
            }
            return grassScatterJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UploadGPUData()
        {
            if (m_GrassElements.IsCreated)
            {
                m_GrassBuffer.SetData<FGrassElement>(m_GrassElements, 0, 0, m_GrassElements.Length);

                m_DensityMap.Dispose();
                m_GrassElements.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer,in int passIndex)
        {
            if (instanceCount <= 0) { return; }
            cmdBuffer.DrawMeshInstancedProcedural(m_Mesh, 0, m_Material, passIndex, instanceCount);
        }
    }
}
