using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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
            AssertShadowJob();
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

            AssertDistanceFade();
            AssertTemporalCatchUp();

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

        static unsafe void AssertTemporalCatchUp()
        {
            var sections = new NativeArray<BoundSection>(1, Allocator.TempJob);
            var visibility = new NativeArray<uint>(1, Allocator.TempJob);
            var flags = new NativeArray<byte>(1, Allocator.TempJob);
            var cells = new NativeArray<int>(1, Allocator.TempJob);
            var bounds = new NativeArray<Aabb>(1, Allocator.TempJob);
            var sizes = new NativeArray<float>(3, Allocator.TempJob);
            var masks = new NativeArray<ulong>(1, Allocator.TempJob);
            var hold = new NativeArray<int>(1, Allocator.TempJob);
            var now = new NativeArray<int>(1, Allocator.TempJob);
            var stable = new NativeArray<int>(1, Allocator.TempJob);
            var screen = new NativeArray<float>(1, Allocator.TempJob);
            var dither = new NativeArray<byte>(3, Allocator.TempJob);
            var fade = new NativeArray<float>(3, Allocator.TempJob);
            try
            {
                FrustumPlane* planes = stackalloc FrustumPlane[6];
                for (int i = 0; i < 6; ++i) { planes[i].normalDist = new float4(0, 0, 0, 1); }
                flags[0] = FoliageLogic.CellVisible;
                bounds[0] = new Aabb(new float3(0, 0, 100), new float3(1));
                sizes[0] = 1f;
                sizes[1] = 0.4f;
                sizes[2] = 0.1f;
                for (int i = 0; i < 3; ++i) { dither[i] = 1; }
                var cull = new TreeCullLodJob();
                {
                    cull.sections = sections;
                    cull.visibility = visibility;
                    cull.maxDistance = 512f;
                    cull.lodHoldProj = float4x4.PerspectiveFov(math.radians(90f), 1f, 0.1f, 1000f);
                    cull.lodNowProj = cull.lodHoldProj;
                    cull.temporalStep = 1;
                    cull.cellFlags = flags;
                    cull.instanceCell = cells;
                    cull.bounds = bounds;
                    cull.lodScreenSizes = sizes;
                    cull.planes = planes;
                    cull.chunkMasks = masks;
                    cull.lodHold = hold;
                    cull.lodStable = stable;
                    cull.lodNow = now;
                    cull.lodScreenSqr = screen;
                }
                var advance = new TreeAdvanceFadeJob();
                {
                    advance.advance = 1;
                    advance.ditherEnabled = 1;
                    advance.step = 0.5f;
                    advance.lodHold = hold;
                    advance.lodNow = now;
                    advance.lodDither = dither;
                    advance.lodStable = stable;
                    advance.fadeState = fade;
                }
                for (int stage = 0; stage < 4; ++stage)
                {
                    int previous = stable[0];
                    int target = stage < 2 ? previous + 1 : previous - 1;
                    cull.lodNowOrigin = stage < 2 ? float3.zero : new float3(0, 0, 99);
                    for (int frame = 0; frame < 3; ++frame)
                    {
                        advance.Schedule(cull.Schedule(1, 1)).Complete();
                        if (masks[0] != 1 || hold[0] != previous || now[0] != target ||
                            stable[0] != (frame == 2 ? target : previous) ||
                            math.abs(fade[0] - frame * 0.5f) > 1e-6f)
                        {
                            Fail("continuous temporal motion must finish each adjacent fade before advancing or reversing");
                        }
                    }
                }
                cull.temporalStep = 0;
                cull.lodNowOrigin = float3.zero;
                advance.hardCut = 1;
                advance.Schedule(cull.Schedule(1, 1)).Complete();
                if (now[0] != 2 || stable[0] != 2 || fade[0] != 1f || fade[1] != 0f)
                {
                    Fail("discontinuous temporal views must still hard-cut directly to the requested lod");
                }
            }
            finally
            {
                sections.Dispose();
                visibility.Dispose();
                flags.Dispose();
                cells.Dispose();
                bounds.Dispose();
                sizes.Dispose();
                masks.Dispose();
                hold.Dispose();
                now.Dispose();
                stable.Dispose();
                screen.Dispose();
                dither.Dispose();
                fade.Dispose();
            }
        }

        static void AssertDistanceFade()
        {
            float[] thresholds = { 0.60f, 0.30f, 0.10f };
            ExpectDistance(0.90f, thresholds, 0, -1, -1, 0f, "S=0.90 stays on the fine stable lod");
            ExpectDistance(0.70f, thresholds, 0, -1, -1, 0f, "S=0.70 snaps to the fine stable lod");
            float invX = (1f / 0.60f - 1f / 0.65f) / (1f / 0.60f - 1f / 0.70f);
            float linearX = (0.65f - 0.60f) / (0.70f - 0.60f);
            ExpectDistance(0.65f, thresholds, -1, 0, 1, invX, "S=0.65 fades with inverse screen coverage");
            DistanceFade linearProbe = FoliageLogic.ClassifyDistanceThresholds(0.65f, thresholds, 1f, 0.25f, 1, 1);
            if (math.abs(linearProbe.x - linearX) < 1e-3f)
            {
                Fail("S=0.65 must not interpolate x linearly in screen coverage");
            }
            ExpectDistance(0.60f, thresholds, 1, -1, -1, 0f, "S=0.60 snaps to the next stable lod");
            ExpectDistance(0.50f, thresholds, 1, -1, -1, 0f, "S=0.50 stays on the middle stable lod");
            float midFar = 0.30f;
            float midNear = 0.60f;
            float midBand = midFar + (0.25f * (midNear - midFar));
            float midX = (1f / midFar - 1f / 0.34f) / (1f / midFar - 1f / midBand);
            ExpectDistance(0.34f, thresholds, -1, 1, 2, midX, "S=0.34 fades the middle lod into the coarse lod");
            ExpectDistance(0.20f, thresholds, 2, -1, -1, 0f, "S=0.20 stays on the last stable lod");
            DistanceFade culled = FoliageLogic.ClassifyDistanceThresholds(0.05f, thresholds, 1f, 0.25f, 1, 1);
            if (culled.lod >= 0 || culled.fadeOutLod >= 0)
            {
                Fail("S=0.05 is outside every threshold");
            }
            DistanceFade hard = FoliageLogic.ClassifyDistanceThresholds(0.65f, thresholds, 1f, 0.25f, 1, 0);
            if (hard.lod != 0 || hard.fadeOutLod >= 0)
            {
                Fail("disabled dither stays on the hard lod");
            }

            float far = FoliageLogic.DistanceLodFarEdge(0.3f);
            if (math.abs(far - (0.15f * 0.15f)) > 1e-6f)
            {
                Fail("lod 0 far edge is the next screen-size threshold squared");
            }
            float band = far + (0.25f * (1f - far));
            float sample = 0.1f;
            DistanceFade produced = FoliageLogic.ClassifyDistanceEdges(sample, 0, 3, far, 1f, 0.25f, 2, 1);
            float expected = (math.rsqrt(far) - math.rsqrt(sample)) / (math.rsqrt(far) - math.rsqrt(band));
            float linear = (sample - far) / (band - far);
            if (produced.fadeOutLod != 0 || produced.fadeInLod != 1 || math.abs(produced.x - expected) > 1e-3f)
            {
                Fail("production distance fade uses rsqrt of squared screen coverage");
            }
            if (math.abs(produced.x - linear) < 1e-3f)
            {
                Fail("production distance fade must not interpolate x linearly in squared screen coverage");
            }
            DistanceFade last = FoliageLogic.ClassifyDistanceEdges(0.001f, 2, 3, far, 1f, 0.25f, 2, 1);
            if (last.lod != 2 || last.fadeOutLod >= 0)
            {
                Fail("the last lod stays stable until distance cull");
            }

            float[] byInstance = { 0.2f, 0.5f, 0.8f };
            ulong[] masks = { 0x7UL };
            int[] indices = new int[3];
            float[] weights = new float[3];
            int written = FoliageLogic.ExpandMasksWithWeights(masks, 1, 3, byInstance, indices, weights);
            int[] keptIndex = new int[3];
            float[] keptWeight = new float[3];
            int kept = FoliageLogic.FilterCompactPairs(indices, weights, written, 1, keptIndex, keptWeight);
            if (written != 3 || kept != 2 || keptIndex[0] != 0 || keptWeight[0] != 0.2f || keptIndex[1] != 2 || keptWeight[1] != 0.8f)
            {
                Fail("dropping one instance must keep the remaining index and weight pairs aligned");
            }
            AssertDistanceJobChain();
        }

        static void AssertDistanceJobChain()
        {
            var chunkMasks = new NativeArray<ulong>(1, Allocator.TempJob);
            var lodNow = new NativeArray<int>(2, Allocator.TempJob);
            var screenSqr = new NativeArray<float>(2, Allocator.TempJob);
            var screenSizes = new NativeArray<float>(3, Allocator.TempJob);
            var dither = new NativeArray<byte>(3, Allocator.TempJob);
            var weights = new NativeArray<float>(2, Allocator.TempJob);
            int slotCount = 3 * FoliageLogic.LodBucketCount;
            var bucketMasks = new NativeArray<ulong>(slotCount, Allocator.TempJob);
            var bucketCounts = new NativeArray<int>(slotCount, Allocator.TempJob);
            var indices = new NativeArray<int>(4, Allocator.TempJob);
            var packedWeights = new NativeArray<float>(4, Allocator.TempJob);
            var ranges = new NativeArray<int2>(slotCount, Allocator.TempJob);
            JobHandle handle = default;
            try
            {
                chunkMasks[0] = 3;
                lodNow[0] = 0;
                lodNow[1] = 1;
                screenSqr[0] = 0.1f;
                screenSqr[1] = 0.004f;
                screenSizes[0] = 0.6f;
                screenSizes[1] = 0.3f;
                screenSizes[2] = 0.1f;
                for (int i = 0; i < 3; ++i) { dither[i] = 1; }
                var emitJob = new TreeEmitDistanceMasksJob();
                {
                    emitJob.ditherEnabled = 1;
                    emitJob.instanceCount = 2;
                    emitJob.fadeWidth = 0.2f;
                    emitJob.chunkMasks = chunkMasks;
                    emitJob.lodNow = lodNow;
                    emitJob.screenSqr = screenSqr;
                    emitJob.lodScreenSizes = screenSizes;
                    emitJob.lodDither = dither;
                    emitJob.bucketMasks = bucketMasks;
                    emitJob.bucketCounts = bucketCounts;
                    emitJob.lodWeight = weights;
                }
                handle = emitJob.Schedule();
                var compactJob = new TreeCompactJob();
                {
                    compactJob.instanceCount = 2;
                    compactJob.weighted = 1;
                    compactJob.bucketMasks = bucketMasks;
                    compactJob.candidateWeights = weights;
                    compactJob.indices = indices;
                    compactJob.weights = packedWeights;
                    compactJob.ranges = ranges;
                }
                handle = compactJob.Schedule(handle);
                handle.Complete();
                if (bucketMasks[FoliageLogic.BucketSlot(0, LodBucket.FadeOut)] != 1 || bucketMasks[FoliageLogic.BucketSlot(1, LodBucket.FadeIn)] != 1 ||
                    bucketMasks[FoliageLogic.BucketSlot(1, LodBucket.FadeOut)] != 2 || bucketMasks[FoliageLogic.BucketSlot(2, LodBucket.FadeIn)] != 2 ||
                    weights[0] <= 0f || weights[0] >= 1f || weights[1] <= 0f || weights[1] >= 1f)
                {
                    Fail("distance LOD job must preserve adjacent masks and shared instance weights");
                }
                int2 fadeInRange = ranges[FoliageLogic.BucketSlot(2, LodBucket.FadeIn)];
                if (fadeInRange.y != 1 || indices[fadeInRange.x] != 1 || packedWeights[fadeInRange.x] != weights[1])
                {
                    Fail("compact ranges must keep each bucket's index and weight in the same slot");
                }
            }
            finally
            {
                handle.Complete();
                bucketMasks.Dispose();
                bucketCounts.Dispose();
                indices.Dispose();
                packedWeights.Dispose();
                ranges.Dispose();
                chunkMasks.Dispose();
                lodNow.Dispose();
                screenSqr.Dispose();
                screenSizes.Dispose();
                dither.Dispose();
                weights.Dispose();
            }
        }

        static void ExpectDistance(float screen, float[] thresholds, int lod, int fadeOut, int fadeIn, float x, string message)
        {
            DistanceFade fade = FoliageLogic.ClassifyDistanceThresholds(screen, thresholds, 1f, 0.25f, 1, 1);
            if (fade.lod != lod || fade.fadeOutLod != fadeOut || fade.fadeInLod != fadeIn || math.abs(fade.x - x) > 1e-3f)
            {
                Fail(message);
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

        static void AssertShadowJob()
        {
            var visibility = new NativeArray<uint>(65, Allocator.TempJob);
            var source = new NativeArray<ulong>(12, Allocator.TempJob);
            var masks = new NativeArray<ulong>(12, Allocator.TempJob);
            var chunks = new NativeArray<ulong>(2, Allocator.TempJob);
            var weights = new NativeArray<float>(65, Allocator.TempJob);
            var indices = new NativeArray<int>(195, Allocator.TempJob);
            var packedWeights = new NativeArray<float>(195, Allocator.TempJob);
            var ranges = new NativeArray<int2>(6, Allocator.TempJob);
            try
            {
                visibility[0] = 3;
                visibility[32] = 1;
                visibility[64] = 2;
                weights[0] = 0.25f;
                weights[32] = 0.5f;
                weights[64] = 0.75f;
                source[2] = 1UL | (1UL << 32);
                source[3] = 1;
                source[10] = source[2];
                source[11] = 1;
                for (int view = 0; view < 2; ++view)
                {
                    var filter = new TreeFilterMasksJob();
                    {
                        filter.visibilityBit = 1u << view;
                        filter.visibility = visibility;
                        filter.source = source;
                        filter.masks = masks;
                        filter.chunks = chunks;
                    }
                    filter.Run();
                    var compact = new TreeCompactJob();
                    {
                        compact.instanceCount = visibility.Length;
                        compact.weighted = 1;
                        compact.bucketMasks = masks;
                        compact.candidateWeights = weights;
                        compact.indices = indices;
                        compact.weights = packedWeights;
                        compact.ranges = ranges;
                    }
                    compact.Run();
                    int second = view == 0 ? 32 : 64;
                    if (ranges[1].y != 2 || ranges[5].y != 2 || indices[ranges[1].x] != 0 ||
                        indices[ranges[1].x + 1] != second || indices[ranges[5].x + 1] != second ||
                        packedWeights[ranges[1].x + 1] != weights[second] || packedWeights[ranges[5].x + 1] != weights[second])
                    {
                        Fail("camera and caster filters must preserve both fade buckets, candidate order and same-slot weights, including offscreen casters");
                    }
                }
                if (source[2] != (1UL | (1UL << 32)) || source[3] != 1 || new FoliageRenderSettings().castGrassMainShadows)
                {
                    Fail("view filtering must preserve the shared source and grass shadows must default off");
                }
            }
            finally
            {
                visibility.Dispose(); source.Dispose(); masks.Dispose(); chunks.Dispose();
                weights.Dispose(); indices.Dispose(); packedWeights.Dispose(); ranges.Dispose();
            }
        }

        static void AssertPrefixCompact()
        {
            int[] chunkCounts = { 0, 1, 63, 64, 65, 257, 4097 };
            for (int sample = 0; sample < chunkCounts.Length; ++sample)
            {
                int chunks = chunkCounts[sample];
                int candidates = chunks == 0 ? 0 : (chunks - 1) * 64 + 7;
                ulong[] masks = new ulong[chunks];
                int[] counts = new int[chunks];
                int[] offsets = new int[chunks];
                float[] sourceWeights = new float[candidates];
                for (int i = 0; i < candidates; ++i) { sourceWeights[i] = (i % 97) / 96f; }
                for (int chunk = 0; chunk < chunks; ++chunk)
                {
                    masks[chunk] = chunk % 3 == 0 ? ulong.MaxValue : chunk % 3 == 1 ? 0xAAAAAAAAAAAAAAAAUL : 0UL;
                    int valid = FoliageLogic.VisibilityChunkSize(chunk, candidates);
                    ulong keep = valid == 64 ? ulong.MaxValue : (1UL << valid) - 1UL;
                    counts[chunk] = FoliageLogic.PopCount(masks[chunk] & keep);
                }
                FoliageLogic.PrefixOffsets(counts, offsets);
                int total = chunks == 0 ? 0 : offsets[chunks - 1] + counts[chunks - 1];
                int[] compact = new int[total + 1];
                int[] prefixed = new int[total + 1];
                float[] weights = new float[total + 1];
                compact[total] = -1;
                prefixed[total] = -1;
                weights[total] = -1f;
                int written = FoliageLogic.ExpandMasksWithWeights(masks, chunks, candidates, sourceWeights, compact, weights);
                if (written != total) { Fail("prefix total must match clipped mask population"); }
                int previousEnd = 0;
                for (int chunk = 0; chunk < chunks; ++chunk)
                {
                    if (offsets[chunk] != previousEnd) { Fail("exclusive prefix must support arbitrary chunk lengths"); }
                    int count = FoliageLogic.ExpandMaskToIndex(chunk * 64,
                        FoliageLogic.VisibilityChunkSize(chunk, candidates), masks[chunk], prefixed, offsets[chunk]);
                    if (count != counts[chunk]) { Fail("per-chunk compact must match prefix capacity"); }
                    previousEnd += count;
                }
                for (int i = 0; i < total; ++i)
                {
                    if (compact[i] != prefixed[i] || (i > 0 && compact[i - 1] >= compact[i]))
                    {
                        Fail("compact indices must retain candidate order across scan block boundaries");
                    }
                    if (weights[i] != sourceWeights[compact[i]]) { Fail("compact weights must share the index output slot"); }
                }
                if (compact[total] != -1 || prefixed[total] != -1 || weights[total] != -1f)
                {
                    Fail("compact must not write beyond its prefix capacity");
                }
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

            AssertPrefixCompact();

            byte[] visible = { 0, 1, 0, 1 };
            int[] cells = new int[4];
            int cellCount = FoliageLogic.CollectVisibleCells(visible, cells);
            if (cellCount != 2 || cells[0] != 1 || cells[1] != 3)
            {
                Fail("CollectVisibleCells must skip culled cells");
            }

            float[] ridge = new float[16];
            ridge[5] = 50f;
            ridge[6] = 50f;
            ridge[9] = 50f;
            ridge[10] = 50f;
            if (CellTerrainFlag(ridge, 4) != FoliageLogic.CellFrustum)
            {
                Fail("ridge between camera and cell must clear the terrain flag");
            }
            if (CellTerrainFlag(new float[16], 4) != FoliageLogic.CellVisible)
            {
                Fail("flat ground must keep a cell sitting on it visible");
            }
        }

        static byte CellTerrainFlag(float[] heights, in int heightRes)
        {
            var cellVisible = new NativeArray<byte>(1, Allocator.TempJob);
            var cellBounds = new NativeArray<BoundSection>(1, Allocator.TempJob);
            var nativeHeights = new NativeArray<float>(heights, Allocator.TempJob);
            var cellFlags = new NativeArray<byte>(1, Allocator.TempJob);
            try
            {
                cellVisible[0] = 1;
                BoundSection section = default;
                section.boundBox = new Aabb(new float3(20.5f, 1.5f, 20.5f), new float3(1f, 1f, 1f));
                cellBounds[0] = section;
                var terrainJob = new TreeCellTerrainJob();
                {
                    terrainJob.heightRes = heightRes;
                    terrainJob.viewOrigin = new float3(0f, 1f, 0f);
                    terrainJob.terrainPos = float3.zero;
                    terrainJob.terrainSize = new float3(24f, 50f, 24f);
                    terrainJob.cellVisible = cellVisible;
                    terrainJob.cellBounds = cellBounds;
                    terrainJob.heights = nativeHeights;
                    terrainJob.cellFlags = cellFlags;
                }
                terrainJob.Schedule(1, 1).Complete();
                return cellFlags[0];
            }
            finally
            {
                cellVisible.Dispose();
                cellBounds.Dispose();
                nativeHeights.Dispose();
                cellFlags.Dispose();
            }
        }

        static void Fail(string message)
        {
            throw new InvalidOperationException("FoliageLogicAsserts: " + message);
        }
    }
}
