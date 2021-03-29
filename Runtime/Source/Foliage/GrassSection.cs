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

    internal static class GrassShaderID
    {
        internal static int primitiveBuffer = Shader.PropertyToID("_GrassBatchBuffer");
    }

    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int totalDensity;
        public int[] densityMap;
        internal NativeArray<int> m_nativeDensityMap;
        internal NativeList<FGrassBatch> m_nativeGrassbatchs;

        private ComputeBuffer m_grassBatchBuffer;
        private MaterialPropertyBlock m_propertyBlock;


        public void Init()
        {
            if(totalDensity == 0) { return; }

            m_propertyBlock = new MaterialPropertyBlock();
            m_nativeDensityMap = new NativeArray<int>(densityMap.Length, Allocator.Persistent);
            m_nativeGrassbatchs = new NativeList<FGrassBatch>(densityMap.Length, Allocator.Persistent);
            m_grassBatchBuffer = new ComputeBuffer(totalDensity, Marshal.SizeOf(typeof(FGrassBatch)));

            for (int i = 0; i < densityMap.Length; i++)
            {
                m_nativeDensityMap[i] = densityMap[i];
            }
        }

        public void Release()
        {
            if (totalDensity == 0) { return; }

            m_grassBatchBuffer.Dispose();
            m_nativeDensityMap.Dispose();
            m_nativeGrassbatchs.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float densityScale, in float3 sectionPivot)
        {
            if (totalDensity == 0 || densityScale == 0) { return default; }

            m_nativeGrassbatchs.Clear();

            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.densityScale = densityScale;
                grassScatterJob.sectionPivot = sectionPivot;
                grassScatterJob.densityMap = m_nativeDensityMap;
                grassScatterJob.grassbatchs = m_nativeGrassbatchs;
            }
            return grassScatterJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetGPUData(CommandBuffer cmdBuffer)
        {
            if (totalDensity == 0 || m_nativeGrassbatchs.Length == 0) { return; }

            m_grassBatchBuffer.SetData<FGrassBatch>(m_nativeGrassbatchs, 0, 0, m_nativeGrassbatchs.Length);
            //cmdBuffer.SetComputeBufferData<FGrassBatch>(m_grassBatchBuffer, m_nativeGrassbatchs, 0, 0, m_nativeGrassbatchs.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, Mesh mesh, Material material, in int passIndex)
        {
            if (totalDensity == 0 || m_nativeGrassbatchs.Length == 0) { return; }

            m_propertyBlock.Clear();
            m_propertyBlock.SetBuffer(GrassShaderID.primitiveBuffer, m_grassBatchBuffer);
            cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, m_nativeGrassbatchs.Length, m_propertyBlock);
        }

#if UNITY_EDITOR
        public void DrawBounds()
        {
            if (totalDensity == 0) { return; }

            foreach (FGrassBatch grassbatch in m_nativeGrassbatchs)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(grassbatch.position, 0.25f);
            }
        }
#endif
    }
}
