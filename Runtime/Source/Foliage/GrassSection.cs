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
        public int sectorSize;
        public float sectorScaleY;
        public RenderTexture heightmapTexture;
    }

    internal static class GrassShaderID
    {
        internal static int terrainSize = Shader.PropertyToID("_TerrainSize");
        internal static int terrainScaleY = Shader.PropertyToID("_TerrainScaleY");
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

        private NativeArray<int> m_densityMap;
        private NativeArray<float4> m_normalHeight;
        private NativeList<FGrassBatch> m_grassBatchs;

        private ComputeBuffer m_grassBuffer;
        private MaterialPropertyBlock m_propertyBlock;


        public void Init()
        {
            if(totalDensity == 0) { return; }

            m_propertyBlock = new MaterialPropertyBlock();
            m_densityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            m_normalHeight = new NativeArray<float4>(normalHeight.Length, Allocator.Persistent);
            m_grassBatchs = new NativeList<FGrassBatch>(densityMap.Length, Allocator.Persistent);
            m_grassBuffer = new ComputeBuffer(totalDensity, Marshal.SizeOf(typeof(FGrassBatch)));

            for (int i = 0; i < densityMap.Length; i++)
            {
                m_densityMap[i] = densityMap[i];
                m_normalHeight[i] = normalHeight[i];
            }
        }

        public void Release()
        {
            if (totalDensity == 0) { return; }

            m_densityMap.Dispose();
            m_normalHeight.Dispose();
            m_grassBatchs.Dispose();
            m_grassBuffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float terrainHeight, in float densityScale, in float3 sectionPivot)
        {
            if (totalDensity == 0 || densityScale == 0) { return default; }

            m_grassBatchs.Clear();

            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.densityMap = m_densityMap;
                grassScatterJob.grassBatchs = m_grassBatchs;
                grassScatterJob.densityScale = densityScale;
                grassScatterJob.sectionPivot = sectionPivot;
                grassScatterJob.terrainHeight = terrainHeight;
                grassScatterJob.normalHeightMap = m_normalHeight;
            }
            return grassScatterJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGPUData(CommandBuffer cmdBuffer)
        {
            if (totalDensity == 0 || m_grassBatchs.Length == 0) { return; }

            m_grassBuffer.SetData<FGrassBatch>(m_grassBatchs, 0, 0, m_grassBatchs.Length);
            //cmdBuffer.SetComputeBufferData<FGrassBatch>(m_grassBuffer, m_grassBatchs, 0, 0, m_grassBatchs.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, Mesh mesh, Material material, in int passIndex, in FGrassShaderProperty grassShaderProperty)
        {
            if (totalDensity == 0 || m_grassBatchs.Length == 0) { return; }

            m_propertyBlock.Clear();
            m_propertyBlock.SetBuffer(GrassShaderID.primitiveBuffer, m_grassBuffer);
            m_propertyBlock.SetInt(GrassShaderID.terrainSize, grassShaderProperty.sectorSize + 1);
            m_propertyBlock.SetFloat(GrassShaderID.terrainScaleY, grassShaderProperty.sectorScaleY);
            m_propertyBlock.SetTexture(GrassShaderID.terrainHeightmap, grassShaderProperty.heightmapTexture);
            cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, m_grassBatchs.Length, m_propertyBlock);
        }

#if UNITY_EDITOR
        public void DrawBounds()
        {
            if (totalDensity == 0) { return; }

            foreach (FGrassBatch grassBatch in m_grassBatchs)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(grassBatch.position, 0.25f);
            }
        }
#endif
    }
}
