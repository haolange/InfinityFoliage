using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class GrassSector
    {
        public FoliageMesh grass;
        public int grassIndex;
        public GrassSection[] sections;
        [HideInInspector]
        public Aabb packedBound;
        [HideInInspector]
        public bool hasPackedBound;

        public float4 widthScale { get { return m_WidthScale; } }

        private float4 m_WidthScale;
        private ComputeBuffer m_PackedBuffer;
        private NativeArray<GrassElement> m_PackedCpu;
        private NativeArray<byte> m_FlatDensity;
        private NativeArray<int> m_DensityStarts;
        private NativeArray<int> m_DestOffsets;
        private NativeArray<int> m_SlotCounts;
        private NativeArray<float3> m_SectionPivots;

        public GrassSector(in int length)
        {
            this.sections = new GrassSection[length];
        }

        public int PackedCount
        {
            get
            {
                if (sections == null || sections.Length == 0) { return 0; }
                GrassSection last = sections[sections.Length - 1];
                return last.offset + last.count;
            }
        }

        public void BuildPackedOffsets()
        {
            if (sections == null) { return; }
            int[] counts = new int[sections.Length];
            int[] offsets = new int[sections.Length];
            for (int i = 0; i < sections.Length; ++i)
            {
                counts[i] = sections[i] != null ? sections[i].count : 0;
            }
            FoliageLogic.PrefixOffsets(counts, offsets);
            for (int i = 0; i < sections.Length; ++i)
            {
                sections[i].offset = offsets[i];
            }
        }

        public void BuildTightBound(BoundSector spatial)
        {
            hasPackedBound = false;
            if (sections == null || spatial == null || spatial.sections == null) { return; }
            for (int i = 0; i < sections.Length; ++i)
            {
                if (sections[i] == null || sections[i].count <= 0) { continue; }
                if (!hasPackedBound)
                {
                    packedBound = spatial.sections[i].boundBox;
                    hasPackedBound = true;
                }
                else
                {
                    packedBound.Encapsulate(spatial.sections[i].boundBox);
                }
            }
        }

        public void Init(TerrainData terrainData)
        {
            DetailPrototype detailPrototype = terrainData.detailPrototypes[grassIndex];
            m_WidthScale = new float4(detailPrototype.minWidth, detailPrototype.maxWidth, detailPrototype.minHeight, detailPrototype.maxHeight);

            int packedCount = PackedCount;
            if (packedCount <= 0) { return; }

            int sectionCount = sections.Length;
            int densityTotal = 0;
            for (int i = 0; i < sectionCount; ++i)
            {
                GrassSection section = sections[i];
                if (section == null || section.densityMap == null) { continue; }
                densityTotal += section.densityMap.Length;
            }

            m_PackedCpu = new NativeArray<GrassElement>(packedCount, Allocator.Persistent);
            m_FlatDensity = new NativeArray<byte>(densityTotal, Allocator.Persistent);
            m_DensityStarts = new NativeArray<int>(sectionCount + 1, Allocator.Persistent);
            m_DestOffsets = new NativeArray<int>(sectionCount, Allocator.Persistent);
            m_SlotCounts = new NativeArray<int>(sectionCount, Allocator.Persistent);
            m_SectionPivots = new NativeArray<float3>(sectionCount, Allocator.Persistent);

            int cursor = 0;
            for (int i = 0; i < sectionCount; ++i)
            {
                GrassSection section = sections[i];
                m_DensityStarts[i] = cursor;
                m_DestOffsets[i] = section != null ? section.offset : 0;
                m_SlotCounts[i] = section != null ? section.count : 0;
                if (section == null || section.densityMap == null || section.densityMap.Length == 0) { continue; }
                NativeArray<byte>.Copy(section.densityMap, 0, m_FlatDensity, cursor, section.densityMap.Length);
                cursor += section.densityMap.Length;
            }
            m_DensityStarts[sectionCount] = cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BindPivots(BoundSection[] boundSections)
        {
            if (!m_SectionPivots.IsCreated || boundSections == null) { return; }
            int sectionCount = sections.Length;
            for (int i = 0; i < sectionCount; ++i)
            {
                float2 pivot = boundSections[i].pivotPosition;
                m_SectionPivots[i] = new float3(pivot.x, 0, pivot.y);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle ScheduleBuild(in int split, in float densityScale)
        {
            if (!m_PackedCpu.IsCreated || !m_FlatDensity.IsCreated) { return default; }

            var scatterJob = new GrassScatterJob();
            {
                scatterJob.split = split;
                scatterJob.densityScale = densityScale;
                scatterJob.widthScale = m_WidthScale;
                scatterJob.flatDensity = m_FlatDensity;
                scatterJob.densityStarts = m_DensityStarts;
                scatterJob.destOffsets = m_DestOffsets;
                scatterJob.slotCounts = m_SlotCounts;
                scatterJob.sectionPivots = m_SectionPivots;
                scatterJob.packedElements = m_PackedCpu;
            }
            return scatterJob.Schedule(sections.Length, 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsurePackedBuffer()
        {
            if (m_PackedBuffer != null || !m_PackedCpu.IsCreated) { return; }
            m_PackedBuffer = new ComputeBuffer(m_PackedCpu.Length, Marshal.SizeOf(typeof(GrassElement)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushUploadRuns(DrawRun[] runs, in int runCount)
        {
            if (!m_PackedCpu.IsCreated || runCount <= 0) { return; }
            EnsurePackedBuffer();
            if (m_PackedBuffer == null) { return; }
            for (int i = 0; i < runCount; ++i)
            {
                if (runs[i].count <= 0) { continue; }
                m_PackedBuffer.SetData(m_PackedCpu, runs[i].start, runs[i].start, runs[i].count);
            }
        }

        public void ReleaseScatterScratch()
        {
            if (m_FlatDensity.IsCreated) { m_FlatDensity.Dispose(); }
            if (m_DensityStarts.IsCreated) { m_DensityStarts.Dispose(); }
            if (m_DestOffsets.IsCreated) { m_DestOffsets.Dispose(); }
            if (m_SlotCounts.IsCreated) { m_SlotCounts.Dispose(); }
            if (m_SectionPivots.IsCreated) { m_SectionPivots.Dispose(); }
            if (m_PackedCpu.IsCreated) { m_PackedCpu.Dispose(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CollectDraw(int[] offsets, int[] counts)
        {
            for (int i = 0; i < sections.Length; ++i)
            {
                offsets[i] = sections[i].offset;
                counts[i] = sections[i].count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DrawRuns(CommandBuffer cmdBuffer, MaterialPropertyBlock propertyBlock, DrawRun[] runs, in int runCount, in int passIndex)
        {
            if (m_PackedBuffer == null) { return; }
            Mesh mesh = grass.meshes[0];
            Material material = grass.materials[0];

            FoliageAmbientSH.Bind(propertyBlock);
            propertyBlock.SetBuffer(GrassShaderID.ElementBuffer, m_PackedBuffer);
            for (int i = 0; i < runCount; ++i)
            {
                if (runs[i].count <= 0) { continue; }
                propertyBlock.SetInt(GrassShaderID.InstanceOffset, runs[i].start);
                cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, runs[i].count, propertyBlock);
            }
        }

        public void Release()
        {
            ReleaseScatterScratch();
            if (m_PackedBuffer != null)
            {
                m_PackedBuffer.Dispose();
                m_PackedBuffer = null;
            }
        }
    }
}
