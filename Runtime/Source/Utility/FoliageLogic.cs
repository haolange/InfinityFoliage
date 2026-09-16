using System;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    public enum LodBucket : byte
    {
        None = 0,
        Stable = 1,
        FadeOut = 2,
        FadeIn = 3
    }

    public struct DrawRun
    {
        public int start;
        public int count;
    }

    public static class FoliageLogic
    {
        public const int GrassSetupBatch = 16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CellIndex(int x, int y, int numSection)
        {
            return (x * numSection) + y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CellFromLocal(float localX, float localZ, float sectionSize, int numSection)
        {
            int x = (int)Math.Floor(localX / sectionSize);
            int y = (int)Math.Floor(localZ / sectionSize);
            if (x < 0) { x = 0; }
            if (y < 0) { y = 0; }
            if (x >= numSection) { x = numSection - 1; }
            if (y >= numSection) { y = numSection - 1; }
            return CellIndex(x, y, numSection);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PrefixOffsets(int[] counts, int[] offsets)
        {
            int running = 0;
            for (int i = 0; i < counts.Length; ++i)
            {
                int count = counts[i];
                if (count < 0) { count = 0; }
                offsets[i] = running;
                running += count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PickUploadCells(byte[] uploaded, byte[] visible, int[] counts, float[] pivotX, float[] pivotZ, float viewX, float viewZ, int budget, int[] picked)
        {
            int pickedCount = PickNearestUploadCells(uploaded, visible, counts, pivotX, pivotZ, viewX, viewZ, budget, picked, 0, 1);
            if (pickedCount < budget)
            {
                pickedCount = PickNearestUploadCells(uploaded, visible, counts, pivotX, pivotZ, viewX, viewZ, budget, picked, pickedCount, 0);
            }
            return pickedCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int PickNearestUploadCells(byte[] uploaded, byte[] visible, int[] counts, float[] pivotX, float[] pivotZ, float viewX, float viewZ, int budget, int[] picked, int pickedCount, byte requireVisible)
        {
            int n = counts.Length;
            while (pickedCount < budget)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < n; ++i)
                {
                    if (uploaded[i] != 0 || counts[i] <= 0) { continue; }
                    if (requireVisible != 0 && visible[i] == 0) { continue; }
                    if (requireVisible == 0 && visible[i] != 0) { continue; }
                    if (ContainsIndex(picked, pickedCount, i)) { continue; }
                    float dx = pivotX[i] - viewX;
                    float dz = pivotZ[i] - viewZ;
                    float d = (dx * dx) + (dz * dz);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (best < 0) { break; }
                picked[pickedCount] = best;
                ++pickedCount;
            }
            return pickedCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ContainsIndex(int[] picked, int pickedCount, int index)
        {
            for (int i = 0; i < pickedCount; ++i)
            {
                if (picked[i] == index) { return true; }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MergeUploadRuns(int[] picked, int pickedCount, int[] offsets, int[] counts, DrawRun[] runs)
        {
            if (pickedCount <= 0) { return 0; }
            SortInts(picked, pickedCount);

            int runCount = 0;
            int first = picked[0];
            int last = picked[0];
            for (int i = 1; i < pickedCount; ++i)
            {
                int cell = picked[i];
                if (CanBridgeUpload(last, cell, counts))
                {
                    last = cell;
                    continue;
                }

                if (EmitUploadRun(offsets, counts, first, last, runs, runCount))
                {
                    ++runCount;
                }
                first = cell;
                last = cell;
            }

            if (EmitUploadRun(offsets, counts, first, last, runs, runCount))
            {
                ++runCount;
            }
            return runCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool CanBridgeUpload(int from, int to, int[] counts)
        {
            if (to <= from) { return false; }
            for (int i = from + 1; i < to; ++i)
            {
                if (counts[i] > 0) { return false; }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool EmitUploadRun(int[] offsets, int[] counts, int first, int last, DrawRun[] runs, int runCount)
        {
            int destStart;
            int destCount;
            PackedUploadRange(offsets[first], offsets[last], counts[last], out destStart, out destCount);
            if (destCount <= 0) { return false; }
            runs[runCount].start = destStart;
            runs[runCount].count = destCount;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SortInts(int[] values, int length)
        {
            for (int i = 1; i < length; ++i)
            {
                int key = values[i];
                int j = i - 1;
                while (j >= 0 && values[j] > key)
                {
                    values[j + 1] = values[j];
                    --j;
                }
                values[j + 1] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MergeVisibleRuns(int[] offsets, int[] counts, byte[] visible, int numSection, DrawRun[] runs)
        {
            int runCount = 0;
            int i = 0;
            int n = counts.Length;
            while (i < n)
            {
                while (i < n && !IsRunStart(visible[i], counts[i])) { ++i; }
                if (i >= n) { break; }

                int first = i;
                int last = i;
                int j = i + 1;
                while (j < n)
                {
                    if (numSection > 0 && (j % numSection) == 0) { break; }
                    if (counts[j] == 0) { ++j; continue; }
                    if (visible[j] != 0) { last = j; ++j; continue; }
                    break;
                }

                runs[runCount].start = offsets[first];
                runs[runCount].count = (offsets[last] + counts[last]) - offsets[first];
                ++runCount;
                i = j;
            }
            return runCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PackedDestOffset(int[] offsets, int sectionIndex)
        {
            return offsets[sectionIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PackedUploadRange(int firstOffset, int lastOffset, int lastCount, out int destStart, out int destCount)
        {
            destStart = firstOffset;
            destCount = (lastOffset + lastCount) - destStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PackedUploadRange(int[] offsets, int[] counts, int begin, int end, out int destStart, out int destCount)
        {
            PackedUploadRange(offsets[begin], offsets[end - 1], counts[end - 1], out destStart, out destCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRunStart(byte visible, int count)
        {
            return visible != 0 && count > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BreaksRun(byte visible, int count)
        {
            return count > 0 && visible == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClassifyLod(int lodHold, int lodNow, int meshIndex)
        {
            int delta = lodHold - lodNow;
            if (delta < 0) { delta = -delta; }
            if (delta > 1) { return lodNow == meshIndex ? (int)LodBucket.Stable : (int)LodBucket.None; }
            if (lodHold == lodNow) { return lodNow == meshIndex ? (int)LodBucket.Stable : (int)LodBucket.None; }
            if (lodHold == meshIndex) { return (int)LodBucket.FadeOut; }
            if (lodNow == meshIndex) { return (int)LodBucket.FadeIn; }
            return (int)LodBucket.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeLodIndex(float screenRadiusSqr, float[] lodScreenSizes)
        {
            int last = lodScreenSizes.Length - 1;
            for (int lodIndex = last; lodIndex >= 0; --lodIndex)
            {
                float threshold = lodScreenSizes[lodIndex] * 0.5f;
                if ((threshold * threshold) >= screenRadiusSqr) { return lodIndex; }
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TreeDrawCount(int typeCount, int lodCount, int bucketCount, int submeshCount)
        {
            return typeCount * lodCount * bucketCount * submeshCount;
        }
    }
}
