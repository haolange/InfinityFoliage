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
        public float4[] normalHeight;
        private NativeArray<int> m_DensityMap;
        private NativeArray<float4> m_NormalHeight;
        private NativeList<FGrassBatch> m_GrassBatchs;
        private ComputeBuffer m_GrassBuffer;
        private MaterialPropertyBlock m_PropertyBlock;


        public void Init()
        {
            if(totalDensity == 0) { return; }

            m_PropertyBlock = new MaterialPropertyBlock();
            m_DensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            m_NormalHeight = new NativeArray<float4>(normalHeight.Length, Allocator.Persistent);
            m_GrassBatchs = new NativeList<FGrassBatch>(densityMap.Length, Allocator.Persistent);
            m_GrassBuffer = new ComputeBuffer(totalDensity, Marshal.SizeOf(typeof(FGrassBatch)));

            for (int i = 0; i < densityMap.Length; i++)
            {
                m_DensityMap[i] = densityMap[i];
                m_NormalHeight[i] = normalHeight[i];
            }
        }

        public void Release()
        {
            if (totalDensity == 0) { return; }

            m_DensityMap.Dispose();
            m_GrassBatchs.Dispose();
            m_GrassBuffer.Dispose();
            m_NormalHeight.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float terrainHeight, in float densityScale, in float3 sectionPivot, in float4 widthScale)
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
                //grassScatterJob.terrainHeight = terrainHeight;
                //grassScatterJob.normalHeightMap = m_normalHeight;
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
        public void DispatchDraw(CommandBuffer cmdBuffer, Mesh mesh, Material material, in int passIndex, in FGrassShaderProperty shaderProperty)
        {
            if (totalDensity == 0 || m_GrassBatchs.Length == 0) { return; }

            m_PropertyBlock.Clear();
            m_PropertyBlock.SetBuffer(GrassShaderID.primitiveBuffer, m_GrassBuffer);
            m_PropertyBlock.SetInt(GrassShaderID.terrainSize, shaderProperty.terrainSize + 1);
            m_PropertyBlock.SetVector(GrassShaderID.terrainPivotScaleY, shaderProperty.terrainPivotScaleY);
            m_PropertyBlock.SetTexture(GrassShaderID.terrainHeightmap, shaderProperty.heightmapTexture);
            cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, m_GrassBatchs.Length, m_PropertyBlock);
        }

#if UNITY_EDITOR
        public void DrawBounds()
        {
            if (totalDensity == 0) { return; }

            foreach (FGrassBatch grassBatch in m_GrassBatchs)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(grassBatch.position, 0.25f);
            }
        }
#endif
    }
}
