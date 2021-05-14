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
    public struct FGrassBatch : IEquatable<FGrassBatch>
    {
        public float3 position;
        public float4x4 matrix_World;


        public FGrassBatch(in float3 position, in float4x4 matrix_World)
        {
            this.position = position;
            this.matrix_World = matrix_World;
        }

        public bool Equals(FGrassBatch Target)
        {
            return position.Equals(Target.position) && matrix_World.Equals(Target.matrix_World);
        }

        public override bool Equals(object obj)
        {
            return Equals((FGrassBatch)obj);
        }

        public override int GetHashCode()
        {
            return (position.GetHashCode() << 16) + (matrix_World.GetHashCode() << 16);
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
        internal static int primitiveBuffer = Shader.PropertyToID("_GrassBatchBuffer");
        internal static int terrainHeightmap = Shader.PropertyToID("_TerrainHeightmap");
    }

    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int totalDensity;
        public int[] densityMap;
        public float[] heightmap;
        private NativeArray<int> m_DensityMap;
        private NativeArray<float> m_heightmap;
        private NativeList<FGrassBatch> m_GrassBatchs;

        private Mesh m_Mesh;
        private Material m_Material;
        private ComputeBuffer m_GrassBuffer;


        public void Init(Mesh mesh, Material material, in FGrassShaderProperty shaderProperty)
        {
            if(totalDensity == 0) { return; }

            m_Mesh = mesh;
            m_Material = new Material(material);
            m_GrassBuffer = new ComputeBuffer(totalDensity, Marshal.SizeOf(typeof(FGrassBatch)));

            m_DensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            m_heightmap = new NativeArray<float>(heightmap.Length, Allocator.Persistent);
            m_GrassBatchs = new NativeList<FGrassBatch>(densityMap.Length, Allocator.Persistent);

            for (int i = 0; i < densityMap.Length; ++i)
            {
                m_DensityMap[i] = densityMap[i];
                m_heightmap[i] = heightmap[i];
            }

            m_Material.SetBuffer(GrassShaderID.primitiveBuffer, m_GrassBuffer);
            m_Material.SetInt(GrassShaderID.terrainSize, shaderProperty.terrainSize + 1);
            m_Material.SetVector(GrassShaderID.terrainPivotScaleY, shaderProperty.terrainPivotScaleY);
            m_Material.SetTexture(GrassShaderID.terrainHeightmap, shaderProperty.heightmapTexture);
        }

        public void Release()
        {
            if (totalDensity == 0) { return; }

            m_DensityMap.Dispose();
            m_GrassBatchs.Dispose();
            m_GrassBuffer.Dispose();
            m_heightmap.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Material);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float heightScale, in float densityScale, in float3 sectionPivot, in float4 widthScale)
        {
            if (totalDensity == 0 || densityScale == 0) { return default; }

            m_GrassBatchs.Clear();

            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.widthScale = widthScale;
                grassScatterJob.densityMap = m_DensityMap;
                grassScatterJob.grassBatchs = m_GrassBatchs;
                grassScatterJob.densityScale = densityScale;
                grassScatterJob.sectionPivot = sectionPivot;
                //grassScatterJob.heightMap = m_heightmap;
                //grassScatterJob.heightScale = heightScale;
            }
            return grassScatterJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGPUData(CommandBuffer cmdBuffer)
        {
            if (totalDensity == 0 || m_GrassBatchs.Length == 0) { return; }

            m_GrassBuffer.SetData<FGrassBatch>(m_GrassBatchs, 0, 0, m_GrassBatchs.Length);
            //cmdBuffer.SetComputeBufferData<FGrassBatch>(m_GrassBuffer, m_GrassBatchs, 0, 0, m_GrassBatchs.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer,in int passIndex)
        {
            if (totalDensity == 0 || m_GrassBuffer.count == 0) { return; }
            cmdBuffer.DrawMeshInstancedProcedural(m_Mesh, 0, m_Material, passIndex, m_GrassBuffer.count);
        }
    }
}
