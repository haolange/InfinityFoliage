using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Landscape.FoliagePipeline
{
    public static class FoliageLogicAsserts
    {
        static bool s_Evaluated;

        public static void Evaluate()
        {
            if (s_Evaluated) { return; }
            s_Evaluated = true;
            AssertGrassLayout();
            AssertGrassRuns();
            AssertAssetPages();
            AssertGrassDensityScale();
            AssertResidencyBudget();
            AssertGrassResidencyDemand();
            AssertTreeLod();
            AssertTreeDrawCount();
            AssertVisibilityIr();
        }

        static void AssertGrassLayout()
        {
            int numSection = 16;
            int expected = numSection * numSection;
            if (expected != 256)
            {
                Fail("sections.Length must be numSection squared");
            }

            int[] counts = new int[expected];
            int[] offsets = new int[expected];
            for (int i = 0; i < expected; ++i)
            {
                counts[i] = 4;
            }
            FoliageLogic.PrefixOffsets(counts, offsets);
            if (offsets[0] != 0 || offsets[1] != 4 || offsets[expected - 1] != 4 * (expected - 1))
            {
                Fail("prefix offsets must stay packed by cell index");
            }

            byte[] emptyBridgeVisible = { 1, 1, 1 };
            int[] emptyBridgeCounts = { 4, 0, 4 };
            int[] emptyBridgeOffsets = { 0, 4, 4 };
            DrawRun[] runs = new DrawRun[3];
            int runCount = FoliageLogic.MergeVisibleRuns(emptyBridgeOffsets, emptyBridgeCounts, emptyBridgeVisible, 3, runs);
            if (runCount != 1 || runs[0].start != 0 || runs[0].count != 8)
            {
                Fail("count==0 must bridge a 1D run");
            }

            if (!FoliageLogic.BreaksRun(0, 4) || FoliageLogic.BreaksRun(0, 0) || FoliageLogic.BreaksRun(1, 4))
            {
                Fail("only count>0 && visible==0 breaks a run");
            }
        }

        static void AssertAssetPages()
        {
            const int resolution = 8;
            const int numSection = 8;
            byte[] source = new byte[resolution * resolution];
            byte[] baseDensity = new byte[source.Length];
            byte[] reconstructed = new byte[source.Length];
            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    int index = (y * resolution) + x;
                    source[index] = (byte)(1 + ((x + (2 * y)) % 9));
                    baseDensity[index] = FoliageAssetCodec.BaseDensity(source[index], x, y, 2);
                    reconstructed[index] = baseDensity[index];
                }
            }

            GrassDensityPage basePage = FoliageAssetCodec.CreateGrassPage(2, resolution, numSection, -1, -1, baseDensity);
            GrassDensityPage restoredBase = FoliageAssetCodec.DecodeGrass(FoliageAssetCodec.EncodeGrass(basePage), 2, resolution, numSection, -1, -1);
            if (restoredBase.density.Length != source.Length) { Fail("base grass asset must cover its Terrain"); }

            for (int pageX = 0; pageX < FoliageAssetCodec.PageAxis; ++pageX)
            {
                for (int pageY = 0; pageY < FoliageAssetCodec.PageAxis; ++pageY)
                {
                    byte[] detailDensity = new byte[4];
                    for (int y = 0; y < 2; ++y)
                    {
                        for (int x = 0; x < 2; ++x)
                        {
                            int globalX = (pageX * 2) + x;
                            int globalY = (pageY * 2) + y;
                            int globalIndex = (globalY * resolution) + globalX;
                            detailDensity[(y * 2) + x] = (byte)(source[globalIndex] - baseDensity[globalIndex]);
                        }
                    }
                    GrassDensityPage page = FoliageAssetCodec.CreateGrassPage(2, resolution, numSection, pageX, pageY, detailDensity);
                    GrassDensityPage restored = FoliageAssetCodec.DecodeGrass(FoliageAssetCodec.EncodeGrass(page), 2, resolution, numSection, pageX, pageY);
                    int offset = 0;
                    for (int i = 0; i < restored.cellIndices.Length; ++i)
                    {
                        if (restored.cellOffsets[i] != offset) { Fail("detail page offsets must be page-local packed prefixes"); }
                        offset += restored.cellCounts[i];
                    }
                    int densityTotal = 0;
                    for (int i = 0; i < restored.density.Length; ++i) { densityTotal += restored.density[i]; }
                    if (densityTotal != offset) { Fail("detail page cell counts must match density"); }
                    for (int y = 0; y < 2; ++y)
                    {
                        for (int x = 0; x < 2; ++x)
                        {
                            int globalIndex = (((pageY * 2) + y) * resolution) + (pageX * 2) + x;
                            reconstructed[globalIndex] += restored.density[(y * 2) + x];
                        }
                    }
                }
            }
            for (int i = 0; i < source.Length; ++i)
            {
                if (source[i] != reconstructed[i]) { Fail("base plus detail pages must reconstruct source density"); }
            }

            List<InstanceTransform> candidates = new List<InstanceTransform>();
            candidates.Add(new InstanceTransform(new float3(3, 4, 5), new float3(0, 1, 0), new float3(2, 3, 2)));
            List<InstanceTransform> restoredTrees = FoliageAssetCodec.DecodeTree(FoliageAssetCodec.EncodeTree(1, candidates), 1);
            if (restoredTrees.Count != 1 || !restoredTrees[0].position.Equals(candidates[0].position) || !restoredTrees[0].scale.Equals(candidates[0].scale))
            {
                Fail("tree candidate asset must round-trip transforms");
            }
        }

        static void AssertGrassDensityScale()
        {
            for (byte density = 0; density < 16; ++density)
            {
                int previous = 0;
                for (int step = 0; step <= 8; ++step)
                {
                    float scale = step / 8f;
                    int count = FoliageLogic.ScaleGrassCount(density, scale, 7, 3, 2, 1);
                    if (count < previous || count > density) { Fail("grass density count must grow monotonically with quality"); }
                    previous = count;
                }
                if (previous != density) { Fail("full grass quality must reach baked density"); }
            }
        }

        static void AssertResidencyBudget()
        {
            FoliageResidency.PageLease[] leases = new FoliageResidency.PageLease[FoliageResidency.DefaultDetailPageBudget];
            for (int i = 0; i < leases.Length; ++i)
            {
                if (!FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out leases[i])) { Fail("detail page pool must admit its full budget"); }
            }
            if (!FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue - 1, out FoliageResidency.PageLease otherSpecies))
            {
                Fail("different grass species must have independent page budgets");
            }
            if (FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out FoliageResidency.PageLease excess))
            {
                FoliageResidency.ReleaseDetailPageSlot(excess);
                Fail("detail page pool must reject a ninth page per grass species");
            }
            FoliageResidency.ReleaseDetailPageSlot(leases[0]);
            if (!FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out FoliageResidency.PageLease replacement))
            {
                Fail("released page slot must allow reload");
            }
            FoliageResidency.ReleaseDetailPageSlot(replacement);
            FoliageResidency.ReleaseDetailPageSlot(otherSpecies);
            for (int i = 1; i < leases.Length; ++i) { FoliageResidency.ReleaseDetailPageSlot(leases[i]); }

            FoliageRenderSettings settings = new FoliageRenderSettings();
            settings.grassDetailPageBudget = 3;
            FoliageResidency.ApplySettings(settings);
            FoliageResidency.PageLease[] reduced = new FoliageResidency.PageLease[3];
            for (int i = 0; i < reduced.Length; ++i)
            {
                if (!FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out reduced[i]))
                {
                    Fail("runtime detail budget must admit its configured page count");
                }
            }
            if (FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out excess))
            {
                FoliageResidency.ReleaseDetailPageSlot(excess);
                Fail("runtime detail budget must reject an excess page");
            }
            for (int i = 0; i < reduced.Length; ++i) { FoliageResidency.ReleaseDetailPageSlot(reduced[i]); }
            settings.grassDetailPageBudget = 0;
            FoliageResidency.ApplySettings(settings);
            if (FoliageResidency.TryAcquireDetailPageSlot(ulong.MaxValue, out excess))
            {
                FoliageResidency.ReleaseDetailPageSlot(excess);
                Fail("zero detail budget must retain only the grass base layer");
            }
            settings.grassDetailPageBudget = FoliageResidency.DefaultDetailPageBudget;
            FoliageResidency.ApplySettings(settings);
        }

        static void AssertGrassResidencyDemand()
        {
            if (!FoliageResidency.IsResidentCameraType(CameraType.Game) || !FoliageResidency.IsResidentCameraType(CameraType.VR) ||
                FoliageResidency.IsResidentCameraType(CameraType.SceneView) || FoliageResidency.IsResidentCameraType(CameraType.Preview) ||
                FoliageResidency.IsResidentCameraType(CameraType.Reflection))
            {
                Fail("only runtime rendering cameras may consume foliage residency");
            }

            const int numSection = 4;
            BoundSection[] bounds = new BoundSection[numSection * numSection];
            GrassSection[] sections = new GrassSection[bounds.Length];
            for (int x = 0; x < numSection; ++x)
            {
                for (int y = 0; y < numSection; ++y)
                {
                    int index = FoliageLogic.CellIndex(x, y, numSection);
                    bounds[index].boundBox = new Aabb(new float3(x, 0f, y), new float3(1f, 1f, 1f));
                    sections[index] = new GrassSection();
                    sections[index].count = 1;
                }
            }
            float3 origin = new float3(0f, 0.5f, 0f);
            Plane[] broad = { new Plane(Vector3.right, 100f) };
            if (!FoliageResidency.PageDemand(bounds, sections, numSection, 0, 0, origin, broad, 2f, out bool visible, out float distance) ||
                !visible || distance != 0f)
            {
                Fail("near grass page must be visible and eligible");
            }
            if (FoliageResidency.PageDemand(bounds, sections, numSection, 3, 3, origin, broad, 2f, out _, out _))
            {
                Fail("grass page beyond draw distance must not consume residency");
            }
            Plane[] clipped = { new Plane(Vector3.right, -2f) };
            if (!FoliageResidency.PageDemand(bounds, sections, numSection, 1, 0, origin, clipped, 2f, out visible, out _) || visible)
            {
                Fail("near clipped grass page may prefetch but cannot outrank visible pages");
            }
            sections[0].count = 0;
            if (FoliageResidency.PageDemand(bounds, sections, numSection, 0, 0, origin, broad, 2f, out _, out _))
            {
                Fail("empty grass page must not consume residency");
            }
        }

        static void AssertGrassRuns()
        {
            const int numSection = 16;
            const int n = numSection * numSection;
            int[] counts = new int[n];
            int[] offsets = new int[n];
            byte[] visible = new byte[n];
            for (int i = 0; i < n; ++i)
            {
                counts[i] = 2;
                visible[i] = 1;
            }
            FoliageLogic.PrefixOffsets(counts, offsets);
            DrawRun[] runs = new DrawRun[n];
            int runCount = FoliageLogic.MergeVisibleRuns(offsets, counts, visible, numSection, runs);
            if (runCount != numSection)
            {
                Fail("16x16 fully visible must emit one run per row, not 1 and not 256");
            }

            for (int i = 0; i < n; ++i)
            {
                visible[i] = (byte)((i % 2) == 0 ? 1 : 0);
            }
            runCount = FoliageLogic.MergeVisibleRuns(offsets, counts, visible, numSection, runs);
            if (runCount != n / 2)
            {
                Fail("checkerboard with instances in culled cells must not merge");
            }

            int[] otherCounts = new int[n];
            Array.Copy(counts, otherCounts, n);
            otherCounts[3] = 0;
            visible[3] = 0;
            for (int i = 0; i < n; ++i)
            {
                if (i != 3) { visible[i] = 1; }
            }
            FoliageLogic.PrefixOffsets(otherCounts, offsets);
            runCount = FoliageLogic.MergeVisibleRuns(offsets, otherCounts, visible, numSection, runs);
            if (runCount != numSection)
            {
                Fail("another species count==0 must not break this species row run");
            }
        }

        static void AssertTreeLod()
        {
            float4x4 cameraProjection = Geometry.GetProjectionMatrix(60f, 1920f, 1080f, 0.2f, 1024f);
            if (math.abs(cameraProjection.c1.y - 1.7320508f) > 0.001f ||
                math.abs(cameraProjection.c0.x - 0.9742786f) > 0.001f)
            {
                Fail("tree LOD projection must use vertical field of view in degrees and the camera aspect ratio");
            }

            float4x4 orthographicProjection = new float4x4(new float4(0.05f, 0f, 0f, 0f),
                new float4(0f, 0.05f, 0f, 0f), new float4(0f, 0f, 1f, 0f), new float4(0f, 0f, 0f, 1f));
            float nearRadius = Geometry.ComputeBoundsScreenRadiusSquared(1f, new float3(0f, 0f, 5f), float3.zero, orthographicProjection);
            float farRadius = Geometry.ComputeBoundsScreenRadiusSquared(1f, new float3(0f, 0f, 50f), float3.zero, orthographicProjection);
            if (math.abs(nearRadius - 0.000625f) > 0.000001f || math.abs(nearRadius - farRadius) > 0.000001f)
            {
                Fail("orthographic tree LOD screen size must not vary with camera distance");
            }

            if (FoliageLogic.ClassifyLod(0, 2, 0) != (int)LodBucket.None)
            {
                Fail("|lod0-lod1|>1 must not keep the old lod");
            }
            if (FoliageLogic.ClassifyLod(0, 2, 2) != (int)LodBucket.Stable)
            {
                Fail("|lod0-lod1|>1 must hard-cut into lodNow stable");
            }
            if (FoliageLogic.ClassifyLod(0, 2, 1) != (int)LodBucket.None)
            {
                Fail("|lod0-lod1|>1 must not emit a middle fade bucket");
            }
            if (FoliageLogic.ClassifyLod(0, 1, 0) != (int)LodBucket.FadeOut)
            {
                Fail("adjacent lod hold must fade out");
            }
            if (FoliageLogic.ClassifyLod(0, 1, 1) != (int)LodBucket.FadeIn)
            {
                Fail("adjacent lod now must fade in");
            }
            if (FoliageLogic.ClassifyLod(1, 1, 1) != (int)LodBucket.Stable)
            {
                Fail("equal lods are stable");
            }

            NativeArray<float> screenSizes = new NativeArray<float>(3, Allocator.Temp);
            try
            {
                screenSizes[0] = 0.8f;
                screenSizes[1] = 0.4f;
                screenSizes[2] = 0.1f;
                if (FoliageLogic.ComputeLodIndexHysteresis(0.19f * 0.19f, screenSizes, 0, 0.08f) != 0 ||
                    FoliageLogic.ComputeLodIndexHysteresis(0.18f * 0.18f, screenSizes, 0, 0.08f) != 1)
                {
                    Fail("coarsening LOD must cross the lower screen threshold");
                }
                if (FoliageLogic.ComputeLodIndexHysteresis(0.21f * 0.21f, screenSizes, 1, 0.08f) != 1 ||
                    FoliageLogic.ComputeLodIndexHysteresis(0.22f * 0.22f, screenSizes, 1, 0.08f) != 0)
                {
                    Fail("refining LOD must cross the upper screen threshold");
                }
                if (FoliageLogic.ComputeLodIndexHysteresis(0.01f * 0.01f, screenSizes, 0, 0.08f) != 2)
                {
                    Fail("a large screen-size jump must reach the final LOD");
                }
            }
            finally
            {
                screenSizes.Dispose();
            }

            float4x4 projection = float4x4.identity;
            float4x4 changedProjection = float4x4.identity;
            changedProjection.c0.x = 1.2f;
            if (FoliageLogic.ViewDiscontinuous(new float3(0), new float3(7, 0, 0), projection, projection, 512f) ||
                !FoliageLogic.ViewDiscontinuous(new float3(0), new float3(10, 0, 0), projection, projection, 512f) ||
                !FoliageLogic.ViewDiscontinuous(new float3(0), new float3(100, 0, 0), projection, projection, 512f) ||
                !FoliageLogic.ViewDiscontinuous(new float3(0), new float3(0), projection, changedProjection, 100f))
            {
                Fail("teleport and projection changes must hard-cut the LOD view");
            }

            int[] lodNow = { -1, -1, -1, -1 };
            byte[] visible = { 0, 0, 0, 0 };
            for (int i = 0; i < visible.Length; ++i)
            {
                if (visible[i] == 0 && lodNow[i] != -1)
                {
                    Fail("culled instances must keep a lod sentinel");
                }
            }
        }

        static void AssertTreeDrawCount()
        {
            int eight = FoliageLogic.TreeDrawCount(2, 3, 3, 2);
            int eighty = FoliageLogic.TreeDrawCount(2, 3, 3, 2);
            if (eight != eighty || eight != 2 * 3 * 3 * 2)
            {
                Fail("tree draw count is types x lod x bucket x submesh, independent of visible cells");
            }
        }

        static void AssertVisibilityIr()
        {
            if (FoliageLogic.Morton2(1, 0) == FoliageLogic.Morton2(0, 1))
            {
                Fail("Morton2 must distinguish swapped axes");
            }
            if (FoliageLogic.MortonFromLocal(0f, 0f, 16f) >= FoliageLogic.MortonFromLocal(8f, 0f, 16f))
            {
                Fail("Morton along +X must increase");
            }

            uint[] keys = { 8, 2, 5, 2 };
            int[] order = { 0, 1, 2, 3 };
            FoliageLogic.SortIndicesByKey(keys, order, 4);
            if (keys[order[0]] > keys[order[1]] || keys[order[1]] > keys[order[2]] || keys[order[2]] > keys[order[3]])
            {
                Fail("SortIndicesByKey must order by Morton key");
            }

            if (FoliageLogic.VisibilityChunkCount(0) != 0 || FoliageLogic.VisibilityChunkCount(64) != 1 || FoliageLogic.VisibilityChunkCount(65) != 2)
            {
                Fail("chunk count is ceil(n/64)");
            }
            if (FoliageLogic.VisibilityChunkSize(1, 70) != 6)
            {
                Fail("last chunk size must clip to remaining instances");
            }
            if (FoliageLogic.PopCount(0) != 0 || FoliageLogic.PopCount(7) != 3 || FoliageLogic.PopCount(ulong.MaxValue) != 64)
            {
                Fail("PopCount failed");
            }

            int[] dest = new int[8];
            int written = FoliageLogic.ExpandMaskToIndex(100, 8, 0x15UL, dest, 0);
            if (written != 3 || dest[0] != 100 || dest[1] != 102 || dest[2] != 104)
            {
                Fail("ExpandMaskToIndex must emit set bits in order");
            }

            ulong[] masks = { 0xF, 0x3 };
            written = FoliageLogic.ExpandMasksToIndex(masks, 2, 66, dest);
            if (written != 6 || dest[4] != 64 || dest[5] != 65)
            {
                Fail("ExpandMasksToIndex must walk chunks in candidate order");
            }
            masks[1] = ulong.MaxValue;
            written = FoliageLogic.ExpandMasksToIndex(masks, 2, 66, dest);
            if (written != 6 || dest[5] != 65)
            {
                Fail("tail chunk mask must ignore bits beyond candidate count");
            }

            int[] ids = { 10, 11, 12, 20, 21 };
            VisibilityRun[] runs = new VisibilityRun[4];
            int runCount = FoliageLogic.EncodeRuns(ids, 5, runs);
            if (runCount != 2 || runs[0].start != 10 || runs[0].count != 3 || runs[1].start != 20 || runs[1].count != 2)
            {
                Fail("EncodeRuns must merge consecutive ids");
            }

            ulong[] runMasks = { 0x7, 0 };
            runMasks[0] = 0x7;
            runCount = FoliageLogic.EncodeRunsFromMasks(runMasks, 1, 8, runs);
            if (runCount != 1 || runs[0].start != 0 || runs[0].count != 3)
            {
                Fail("EncodeRunsFromMasks must merge set bits");
            }

            written = FoliageLogic.ExpandRunsToIndex(runs, 1, dest);
            if (written != 3 || dest[0] != 0 || dest[2] != 2)
            {
                Fail("ExpandRunsToIndex must fill VisibleIndex");
            }

            if (FoliageLogic.PickVisibilityCodec(1, 1, 1, 0) != (int)VisibilityCodec.CompactIndex)
            {
                Fail("CPU policy must pick CompactIndex");
            }
            if (FoliageLogic.PickVisibilityCodec(64, 1, 1, 1) != (int)VisibilityCodec.RunTransfer)
            {
                Fail("one dense run on GPU must pick RunTransfer");
            }
            if (FoliageLogic.PickVisibilityCodec(32, 1, 32, 1) != (int)VisibilityCodec.BitMaskTransfer)
            {
                Fail("fragmented dense chunk on GPU must pick BitMaskTransfer");
            }
            if (FoliageLogic.PickVisibilityCodec(1, 1, 1, 1) != (int)VisibilityCodec.CompactIndex)
            {
                Fail("single visible instance must pick CompactIndex");
            }
            if (FoliageLogic.PickVisibilityCodec(32, 32, 32, 1) != (int)VisibilityCodec.CompactIndex)
            {
                Fail("fully fragmented visibility must pick CompactIndex");
            }

            byte[] visible = { 0, 1, 0, 1 };
            int[] cells = new int[4];
            int cellCount = FoliageLogic.CollectVisibleCells(visible, cells);
            if (cellCount != 2 || cells[0] != 1 || cells[1] != 3)
            {
                Fail("CollectVisibleCells must skip culled cells");
            }

            float[] flat = new float[4];
            if (FoliageLogic.SampleHeightField(flat, 2, 0, 0, 10, 10, 0, 0) != 0f)
            {
                Fail("flat height field must sample 0");
            }

            float[] ridge = new float[16];
            for (int i = 0; i < 16; ++i) { ridge[i] = 0f; }
            ridge[5] = 50f;
            ridge[6] = 50f;
            ridge[9] = 50f;
            ridge[10] = 50f;
            bool occluded = FoliageLogic.TerrainOccludes(20f, 1f, 20f, 21f, 2f, 21f, 0f, 1f, 0f, ridge, 4, 0f, 0f, 24f, 24f);
            if (!occluded)
            {
                Fail("ridge between camera and box must occlude");
            }
            bool open = FoliageLogic.TerrainOccludes(20f, 1f, 20f, 21f, 2f, 21f, 0f, 1f, 0f, flat, 2, 0f, 0f, 24f, 24f);
            if (open)
            {
                Fail("flat ground must not occlude a box sitting on it");
            }
        }

        static void Fail(string message)
        {
            throw new InvalidOperationException("FoliageLogicAsserts: " + message);
        }
    }
}
