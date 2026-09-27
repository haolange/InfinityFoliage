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
        internal ulong speciesKey { get { return m_SpeciesKey; } }

        private float4 m_WidthScale;
        private ulong m_SpeciesKey;
        private GrassPage m_Base;
        private GrassPage[] m_Details;
        private bool m_Resident;
        private DrawRun[] m_Runs;

        private class GrassPage
        {
            private readonly GrassSector m_Owner;
            private readonly int m_PageX;
            private readonly int m_PageY;
            private readonly int m_Resolution;
            private readonly int m_NumSection;
            private readonly float2 m_TerrainOrigin;
            private readonly float2 m_PixelSize;
            private readonly string m_Path;
            private GrassDensityPage m_Density;
            private ComputeBuffer m_Buffer;
            private int[] m_Offsets;
            private int[] m_Counts;
            private int[] m_NextOffsets;
            private int[] m_NextCounts;
            private NativeArray<byte> m_NativeDensity;
            private NativeArray<int> m_CellX;
            private NativeArray<int> m_CellY;
            private NativeArray<int> m_CellWidth;
            private NativeArray<int> m_CellHeight;
            private NativeArray<int> m_NativeOffsets;
            private NativeArray<int> m_NativeCounts;
            private NativeArray<GrassElement> m_Elements;
            private JobHandle m_BuildHandle;
            private bool m_Building;
            private bool m_Resident;
            private bool m_KnownEmpty;
            private bool m_LoadPending;
            private FoliageResidency.PageLease m_PageLease;
            private bool m_Failed;
            private int m_Generation;
            private float m_BuildScale;
            private float m_DesiredScale;

            internal bool IsKnownEmpty { get { return m_KnownEmpty; } }

            internal GrassPage(GrassSector owner, string path, in int pageX, in int pageY, in int resolution, in int numSection, in float2 terrainOrigin, in float2 pixelSize)
            {
                m_Owner = owner;
                m_Path = path;
                m_PageX = pageX;
                m_PageY = pageY;
                m_Resolution = resolution;
                m_NumSection = numSection;
                m_TerrainOrigin = terrainOrigin;
                m_PixelSize = pixelSize;
            }

            internal void SetResident(in bool resident)
            {
                if (m_Resident == resident)
                {
                    if (resident && m_Density == null && !m_LoadPending && !m_Failed) { BeginLoad(); }
                    return;
                }
                m_Resident = resident;
                ++m_Generation;
                if (!resident)
                {
                    Release();
                    return;
                }
                m_Failed = false;
                BeginLoad();
            }

            private void BeginLoad()
            {
                FoliageResidency.PageLease lease = null;
                if (m_PageX >= 0 && !FoliageResidency.TryAcquireDetailPageSlot(m_Owner.m_SpeciesKey, out lease)) { return; }
                m_PageLease = lease;
                m_LoadPending = true;
                int generation = m_Generation;
                ResourceRequest request;
                try { request = Resources.LoadAsync<TextAsset>(m_Path); }
                catch
                {
                    m_LoadPending = false;
                    m_Failed = true;
                    FoliageResidency.ReleaseDetailPageSlot(lease);
                    m_PageLease = null;
                    throw;
                }
                request.completed += operation =>
                {
                    TextAsset asset = request.asset as TextAsset;
                    if (generation != m_Generation || !m_Resident)
                    {
                        if (asset != null) { Resources.UnloadAsset(asset); }
                        FoliageResidency.ReleaseDetailPageSlot(lease);
                        return;
                    }
                    m_LoadPending = false;
                    if (asset == null)
                    {
                        m_Failed = true;
                        FoliageResidency.ReleaseDetailPageSlot(lease);
                        m_PageLease = null;
                        Debug.LogError("Missing foliage grass asset " + m_Path + ". Rebake this Terrain.");
                        return;
                    }
                    try
                    {
                        m_Density = FoliageAssetCodec.DecodeGrass(asset.bytes, m_Owner.grassIndex, m_Resolution, m_NumSection, m_PageX, m_PageY);
                        m_KnownEmpty = m_Density.cellIndices.Length == 0;
                        StartBuild(m_DesiredScale);
                    }
                    catch (Exception exception)
                    {
                        Release();
                        m_Failed = true;
                        Debug.LogException(exception);
                    }
                    finally
                    {
                        Resources.UnloadAsset(asset);
                    }
                };
            }

            internal void SetDensityScale(in float scale)
            {
                if (m_DesiredScale == scale) { return; }
                m_DesiredScale = scale;
                if (m_Density == null || m_Building) { return; }
                try { StartBuild(scale); }
                catch (Exception exception)
                {
                    Release();
                    m_Failed = true;
                    Debug.LogException(exception);
                }
            }

            private void StartBuild(in float scale)
            {
                if (m_Density == null || m_Building) { return; }
                m_BuildScale = scale;
                int scaleQ = scale <= 0f ? 0 : scale >= 1f ? 65536 : (int)(scale * 65536f + 0.5f);
                int cellCount = m_NumSection * m_NumSection;
                m_NextOffsets = new int[cellCount];
                m_NextCounts = new int[cellCount];
                int[] xStart = new int[cellCount];
                int[] yStart = new int[cellCount];
                int[] widths = new int[cellCount];
                int[] heights = new int[cellCount];
                int cellSize = m_Resolution / m_NumSection;
                int pageEndX = m_Density.xStart + m_Density.width;
                int pageEndY = m_Density.yStart + m_Density.height;
                int total = 0;
                for (int cellX = 0; cellX < m_NumSection; ++cellX)
                {
                    for (int cellY = 0; cellY < m_NumSection; ++cellY)
                    {
                        int index = FoliageLogic.CellIndex(cellX, cellY, m_NumSection);
                        int x0 = Math.Max(m_Density.xStart, cellX * cellSize);
                        int y0 = Math.Max(m_Density.yStart, cellY * cellSize);
                        int x1 = Math.Min(pageEndX, (cellX + 1) * cellSize);
                        int y1 = Math.Min(pageEndY, (cellY + 1) * cellSize);
                        xStart[index] = x0;
                        yStart[index] = y0;
                        widths[index] = Math.Max(0, x1 - x0);
                        heights[index] = Math.Max(0, y1 - y0);
                        m_NextOffsets[index] = total;
                        for (int y = y0; y < y1; ++y)
                        {
                            for (int x = x0; x < x1; ++x)
                            {
                                byte density = m_Density.density[(y - m_Density.yStart) * m_Density.width + x - m_Density.xStart];
                                m_NextCounts[index] += FoliageLogic.ScaleGrassCount(density, scaleQ, x, y, m_Owner.grassIndex, m_PageX < 0 ? 0 : 1);
                            }
                        }
                        total += m_NextCounts[index];
                    }
                }

                if (total == 0)
                {
                    if (m_Buffer != null) { m_Buffer.Dispose(); m_Buffer = null; }
                    m_Offsets = m_NextOffsets;
                    m_Counts = m_NextCounts;
                    m_NextOffsets = null;
                    m_NextCounts = null;
                    return;
                }

                m_NativeDensity = new NativeArray<byte>(m_Density.density, Allocator.Persistent);
                m_CellX = new NativeArray<int>(xStart, Allocator.Persistent);
                m_CellY = new NativeArray<int>(yStart, Allocator.Persistent);
                m_CellWidth = new NativeArray<int>(widths, Allocator.Persistent);
                m_CellHeight = new NativeArray<int>(heights, Allocator.Persistent);
                m_NativeOffsets = new NativeArray<int>(m_NextOffsets, Allocator.Persistent);
                m_NativeCounts = new NativeArray<int>(m_NextCounts, Allocator.Persistent);
                m_Elements = new NativeArray<GrassElement>(total, Allocator.Persistent);

                var scatterJob = new GrassPageScatterJob();
                {
                    scatterJob.pageX = m_Density.xStart;
                    scatterJob.pageY = m_Density.yStart;
                    scatterJob.pageWidth = m_Density.width;
                    scatterJob.grassIndex = m_Owner.grassIndex;
                    scatterJob.layer = m_PageX < 0 ? 0 : 1;
                    scatterJob.pixelSize = m_PixelSize;
                    scatterJob.terrainOrigin = m_TerrainOrigin;
                    scatterJob.scaleQ = scaleQ;
                    scatterJob.widthScale = m_Owner.m_WidthScale;
                    scatterJob.density = m_NativeDensity;
                    scatterJob.cellX = m_CellX;
                    scatterJob.cellY = m_CellY;
                    scatterJob.cellWidth = m_CellWidth;
                    scatterJob.cellHeight = m_CellHeight;
                    scatterJob.offsets = m_NativeOffsets;
                    scatterJob.counts = m_NativeCounts;
                    scatterJob.packedElements = m_Elements;
                }
                m_BuildHandle = scatterJob.Schedule(cellCount, 8);
                m_Building = true;
            }

            internal void Flush()
            {
                if (!m_Building || !m_BuildHandle.IsCompleted) { return; }
                try
                {
                    m_BuildHandle.Complete();
                    m_Building = false;
                    if (m_DesiredScale != m_BuildScale)
                    {
                        ReleaseScratch();
                        m_NextOffsets = null;
                        m_NextCounts = null;
                        StartBuild(m_DesiredScale);
                        return;
                    }
                    ComputeBuffer next = new ComputeBuffer(m_Elements.Length, Marshal.SizeOf(typeof(GrassElement)));
                    try { next.SetData(m_Elements); }
                    catch
                    {
                        next.Dispose();
                        throw;
                    }
                    ComputeBuffer previous = m_Buffer;
                    m_Buffer = next;
                    m_Offsets = m_NextOffsets;
                    m_Counts = m_NextCounts;
                    m_NextOffsets = null;
                    m_NextCounts = null;
                    ReleaseScratch();
                    if (previous != null) { previous.Dispose(); }
                }
                catch (Exception exception)
                {
                    Release();
                    m_Failed = true;
                    Debug.LogException(exception);
                }
            }

            internal void Draw(CommandBuffer cmdBuffer, MaterialPropertyBlock block, byte[] visibleMap, DrawRun[] runs, in int numSection, in int passIndex)
            {
                if (m_Buffer == null || m_Counts == null) { return; }
                int count = FoliageLogic.MergeVisibleRuns(m_Offsets, m_Counts, visibleMap, numSection, runs);
                if (count == 0) { return; }
                FoliageAmbientSH.Bind(block);
                block.SetBuffer(GrassShaderID.ElementBuffer, m_Buffer);
                Mesh mesh = m_Owner.grass.meshes[0];
                Material material = m_Owner.grass.materials[0];
                for (int i = 0; i < count; ++i)
                {
                    block.SetInt(GrassShaderID.InstanceOffset, runs[i].start);
                    cmdBuffer.DrawMeshInstancedProcedural(mesh, 0, material, passIndex, runs[i].count, block);
                }
            }

            private void ReleaseScratch()
            {
                if (m_NativeDensity.IsCreated) { m_NativeDensity.Dispose(); }
                if (m_CellX.IsCreated) { m_CellX.Dispose(); }
                if (m_CellY.IsCreated) { m_CellY.Dispose(); }
                if (m_CellWidth.IsCreated) { m_CellWidth.Dispose(); }
                if (m_CellHeight.IsCreated) { m_CellHeight.Dispose(); }
                if (m_NativeOffsets.IsCreated) { m_NativeOffsets.Dispose(); }
                if (m_NativeCounts.IsCreated) { m_NativeCounts.Dispose(); }
                if (m_Elements.IsCreated) { m_Elements.Dispose(); }
            }

            private void RetireScratch(in JobHandle dependency)
            {
                if (m_NativeDensity.IsCreated) { m_NativeDensity.Dispose(dependency); }
                if (m_CellX.IsCreated) { m_CellX.Dispose(dependency); }
                if (m_CellY.IsCreated) { m_CellY.Dispose(dependency); }
                if (m_CellWidth.IsCreated) { m_CellWidth.Dispose(dependency); }
                if (m_CellHeight.IsCreated) { m_CellHeight.Dispose(dependency); }
                if (m_NativeOffsets.IsCreated) { m_NativeOffsets.Dispose(dependency); }
                if (m_NativeCounts.IsCreated) { m_NativeCounts.Dispose(dependency); }
                if (m_Elements.IsCreated) { m_Elements.Dispose(dependency); }
                JobHandle.ScheduleBatchedJobs();
            }

            internal void Release()
            {
                FoliageResidency.ReleaseDetailPageSlot(m_PageLease);
                m_PageLease = null;
                m_LoadPending = false;
                if (m_Building)
                {
                    RetireScratch(m_BuildHandle);
                    m_Building = false;
                }
                else { ReleaseScratch(); }
                if (m_Buffer != null) { m_Buffer.Dispose(); m_Buffer = null; }
                m_Density = null;
                m_Offsets = null;
                m_Counts = null;
                m_NextOffsets = null;
                m_NextCounts = null;
            }
        }

        public GrassSector(in int length)
        {
            sections = new GrassSection[length];
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
            for (int i = 0; i < sections.Length; ++i) { counts[i] = sections[i] != null ? sections[i].count : 0; }
            FoliageLogic.PrefixOffsets(counts, offsets);
            for (int i = 0; i < sections.Length; ++i) { sections[i].offset = offsets[i]; }
        }

        public void BuildTightBound(BoundSector spatial)
        {
            hasPackedBound = false;
            if (sections == null || spatial == null || spatial.sections == null) { return; }
            for (int i = 0; i < sections.Length; ++i)
            {
                if (sections[i] == null || sections[i].count <= 0) { continue; }
                if (!hasPackedBound) { packedBound = spatial.sections[i].boundBox; hasPackedBound = true; }
                else { packedBound.Encapsulate(spatial.sections[i].boundBox); }
            }
        }

        public void Init(TerrainData terrainData, string assetKey, in int numSection, in Vector3 terrainPosition)
        {
            DetailPrototype detailPrototype = terrainData.detailPrototypes[grassIndex];
            m_WidthScale = new float4(detailPrototype.minWidth, detailPrototype.maxWidth, detailPrototype.minHeight, detailPrototype.maxHeight);
            m_SpeciesKey = EntityId.ToULong(detailPrototype.prototype.GetEntityId());
            int resolution = terrainData.detailResolution;
            float2 origin = new float2(terrainPosition.x, terrainPosition.z);
            float2 pixelSize = new float2(terrainData.size.x / resolution, terrainData.size.z / resolution);
            m_Base = new GrassPage(this, FoliageAssetCodec.GrassBasePath(assetKey, grassIndex), -1, -1, resolution, numSection, origin, pixelSize);
            m_Details = new GrassPage[FoliageAssetCodec.PageAxis * FoliageAssetCodec.PageAxis];
            for (int x = 0; x < FoliageAssetCodec.PageAxis; ++x)
            {
                for (int y = 0; y < FoliageAssetCodec.PageAxis; ++y)
                {
                    int index = x * FoliageAssetCodec.PageAxis + y;
                    m_Details[index] = new GrassPage(this, FoliageAssetCodec.GrassPagePath(assetKey, grassIndex, x, y), x, y, resolution, numSection, origin, pixelSize);
                }
            }
            m_Runs = new DrawRun[numSection * numSection];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResident(in bool resident)
        {
            if (m_Resident == resident) { return; }
            m_Resident = resident;
            if (m_Base != null) { m_Base.SetResident(resident); }
            if (!resident && m_Details != null)
            {
                for (int i = 0; i < m_Details.Length; ++i) { m_Details[i].SetResident(false); }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDetailResident(in int pageX, in int pageY, in bool resident)
        {
            if (!m_Resident || m_Details == null) { return; }
            m_Details[pageX * FoliageAssetCodec.PageAxis + pageY].SetResident(resident);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDetailEmpty(in int pageX, in int pageY)
        {
            return m_Details != null && m_Details[pageX * FoliageAssetCodec.PageAxis + pageY].IsKnownEmpty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDensityScale(in float scale)
        {
            if (m_Base != null) { m_Base.SetDensityScale(scale); }
            if (m_Details == null) { return; }
            for (int i = 0; i < m_Details.Length; ++i) { m_Details[i].SetDensityScale(scale); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            if (!m_Resident) { return; }
            m_Base.Flush();
            for (int i = 0; i < m_Details.Length; ++i) { m_Details[i].Flush(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Draw(CommandBuffer cmdBuffer, MaterialPropertyBlock block, byte[] visibleMap, in int numSection, in int passIndex)
        {
            if (!m_Resident) { return; }
            m_Base.Draw(cmdBuffer, block, visibleMap, m_Runs, numSection, passIndex);
            for (int i = 0; i < m_Details.Length; ++i) { m_Details[i].Draw(cmdBuffer, block, visibleMap, m_Runs, numSection, passIndex); }
        }

        public void Release()
        {
            SetResident(false);
            m_Base = null;
            m_Details = null;
            m_Runs = null;
        }
    }
}
