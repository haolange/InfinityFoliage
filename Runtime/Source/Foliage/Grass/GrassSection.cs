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

    internal static class GrassShaderID
    {
        internal static int TerrainSize = Shader.PropertyToID("_TerrainSize");
        internal static int ElementBuffer = Shader.PropertyToID("_GrassElementBuffer");
        internal static int TerrainHeightmap = Shader.PropertyToID("_TerrainHeightmap");
        internal static int TerrainNormalmap = Shader.PropertyToID("_TerrainNormalmap");
        internal static int TerrainPivotScaleY = Shader.PropertyToID("_TerrainPivotScaleY");
    }

    [Serializable]
    public class FGrassSection
    {
        public int boundIndex;
        public int instanceCount;
        public byte[] densityMap;
        private ComputeBuffer m_ElementBuffer;
        private NativeArray<byte> m_DensityMap;
        private NativeList<FGrassElement> m_ElementList;

        public void Init()
        {
            if(instanceCount == 0) { return; }

            m_ElementBuffer = new ComputeBuffer(instanceCount, Marshal.SizeOf(typeof(FGrassElement)));
            m_DensityMap = new NativeArray<byte>(densityMap.Length, Allocator.Persistent);
            NativeArray<byte>.Copy(densityMap, m_DensityMap);
            densityMap = null;
        }

        public void Release()
        {
            if (instanceCount == 0) { return; }
            m_ElementBuffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle BuildInstance(in int split, in float uniqueValue, in float heightScale, in float densityScale, in float3 sectionPivot, in float4 widthScale)
        {
            if (instanceCount == 0 || densityScale == 0) { return default; }

            m_ElementList = new NativeList<FGrassElement>(instanceCount, Allocator.TempJob);
            var grassScatterJob = new FGrassScatterJob();
            {
                grassScatterJob.split = split;
                grassScatterJob.widthScale = widthScale;
                grassScatterJob.uniqueValue = uniqueValue;
                grassScatterJob.densityMap = m_DensityMap;
                grassScatterJob.densityScale = densityScale;
                grassScatterJob.sectionPivot = sectionPivot;
                grassScatterJob.grassElements = m_ElementList;
            }
            return grassScatterJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UploadGPUData()
        {
            if (m_ElementList.IsCreated)
            {
                instanceCount = m_ElementList.Length;
                m_ElementBuffer.SetData<FGrassElement>(m_ElementList, 0, 0, instanceCount);

                m_DensityMap.Dispose();
                m_ElementList.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, Mesh mesh, Material material, MaterialPropertyBlock propertyBlock, in int passIndex)
        {
            if (instanceCount <= 0) { return; }

            propertyBlock.SetBuffer(GrassShaderID.ElementBuffer, m_ElementBuffer);
            cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, instanceCount, propertyBlock);
        }
    }
}
