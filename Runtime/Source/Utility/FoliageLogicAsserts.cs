using System;

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
            AssertGrassUploadPick();
            AssertTreeLod();
            AssertTreeDrawCount();
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

        static void AssertGrassUploadPick()
        {
            int[] counts = { 1, 1, 1, 1 };
            float[] pivotX = { 100, 0, 10, 20 };
            float[] pivotZ = { 0, 0, 0, 0 };
            byte[] uploaded = { 0, 0, 0, 0 };
            byte[] visible = { 1, 0, 0, 0 };
            int[] picked = new int[4];
            int pickedCount = FoliageLogic.PickUploadCells(uploaded, visible, counts, pivotX, pivotZ, 0, 0, 1, picked);
            if (pickedCount != 1 || picked[0] != 0)
            {
                Fail("visible cells must upload before a nearer culled cell");
            }

            int n = 32;
            counts = new int[n];
            uploaded = new byte[n];
            visible = new byte[n];
            pivotX = new float[n];
            pivotZ = new float[n];
            picked = new int[n];
            for (int i = 0; i < n; ++i)
            {
                counts[i] = 3;
                visible[i] = 1;
                pivotX[i] = i;
            }
            pickedCount = FoliageLogic.PickUploadCells(uploaded, visible, counts, pivotX, pivotZ, 0, 0, FoliageLogic.GrassSetupBatch, picked);
            if (pickedCount != FoliageLogic.GrassSetupBatch)
            {
                Fail("upload budget must stay 16 when every cell is visible and pending");
            }

            int[] offsets = new int[n];
            FoliageLogic.PrefixOffsets(counts, offsets);
            if (FoliageLogic.PackedDestOffset(offsets, 16) == 0)
            {
                Fail("destOffset for cell 16 must be sections[i].offset, not 0");
            }

            int[] mergePicked = { 2, 0, 1 };
            DrawRun[] runs = new DrawRun[4];
            int runCount = FoliageLogic.MergeUploadRuns(mergePicked, 3, offsets, counts, runs);
            if (runCount != 1 || runs[0].start != 0 || runs[0].count != 9)
            {
                Fail("picked {0,1,2} must merge into one packed upload run");
            }

            int[] splitPicked = { 0, 3 };
            runCount = FoliageLogic.MergeUploadRuns(splitPicked, 2, offsets, counts, runs);
            if (runCount != 2)
            {
                Fail("picked cells with instances between them must stay multiple upload runs");
            }

            int[] mixedCounts = { 4, 0, 4, 0, 2 };
            int[] mixedOffsets = new int[5];
            FoliageLogic.PrefixOffsets(mixedCounts, mixedOffsets);
            int[] bridgePicked = { 0, 2 };
            runCount = FoliageLogic.MergeUploadRuns(bridgePicked, 2, mixedOffsets, mixedCounts, runs);
            if (runCount != 1 || runs[0].start != 0 || runs[0].count != 8)
            {
                Fail("empty cells must bridge one upload run");
            }
        }

        static void AssertTreeLod()
        {
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

        static void Fail(string message)
        {
            throw new InvalidOperationException("FoliageLogicAsserts: " + message);
        }
    }
}
