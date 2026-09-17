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

    public enum VisibilityCodec : byte
    {
        CompactIndex = 0,
        BitMaskTransfer = 1,
        RunTransfer = 2
    }

    public struct VisibilityChunk
    {
        public int candidateBase;
        public int count;
        public ulong mask;
    }

    public struct VisibilityRun
    {
        public int start;
        public int count;
    }

    public static class FoliageLogic
    {
        public const int GrassSetupBatch = 16;
        public const int VisibilityChunkWidth = 64;
        public const int VisibilityIndexBytes = 4;
        public const int VisibilityRunBytes = 8;
        public const int VisibilityChunkBytes = 16;
        public const int VisibilityExpandMaskWeight = 2;
        public const int VisibilityExpandRunWeight = 1;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Morton2(uint x, uint z)
        {
            return Part1By1(x) | (Part1By1(z) << 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint MortonFromLocal(float localX, float localZ, float worldSize)
        {
            float w = worldSize > 0.0001f ? worldSize : 1f;
            int qx = (int)((localX / w) * 65535f);
            int qz = (int)((localZ / w) * 65535f);
            if (qx < 0) { qx = 0; }
            if (qz < 0) { qz = 0; }
            if (qx > 65535) { qx = 65535; }
            if (qz > 65535) { qz = 65535; }
            return Morton2((uint)qx, (uint)qz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Part1By1(uint n)
        {
            n &= 0x0000ffff;
            n = (n | (n << 8)) & 0x00FF00FF;
            n = (n | (n << 4)) & 0x0F0F0F0F;
            n = (n | (n << 2)) & 0x33333333;
            n = (n | (n << 1)) & 0x55555555;
            return n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SortIndicesByKey(uint[] keys, int[] indices, int length)
        {
            for (int start = (length >> 1) - 1; start >= 0; --start)
            {
                SiftKey(keys, indices, start, length);
            }
            for (int end = length - 1; end > 0; --end)
            {
                int tmp = indices[0];
                indices[0] = indices[end];
                indices[end] = tmp;
                SiftKey(keys, indices, 0, end);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SiftKey(uint[] keys, int[] indices, int root, int length)
        {
            while (true)
            {
                int left = (root << 1) + 1;
                if (left >= length) { return; }
                int max = left;
                int right = left + 1;
                if (right < length && keys[indices[right]] > keys[indices[left]])
                {
                    max = right;
                }
                if (keys[indices[max]] <= keys[indices[root]]) { return; }
                int tmp = indices[root];
                indices[root] = indices[max];
                indices[max] = tmp;
                root = max;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VisibilityChunkCount(int instanceCount)
        {
            if (instanceCount <= 0) { return 0; }
            return (instanceCount + VisibilityChunkWidth - 1) / VisibilityChunkWidth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VisibilityChunkBase(int chunk)
        {
            return chunk * VisibilityChunkWidth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int VisibilityChunkSize(int chunk, int instanceCount)
        {
            int remain = instanceCount - VisibilityChunkBase(chunk);
            if (remain > VisibilityChunkWidth) { return VisibilityChunkWidth; }
            if (remain < 0) { return 0; }
            return remain;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
            value = value - ((value >> 1) & 0x5555555555555555UL);
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaskVisibleCount(ulong[] masks, int chunkCount, int instanceCount)
        {
            int visible = 0;
            for (int chunk = 0; chunk < chunkCount; ++chunk)
            {
                ulong mask = masks[chunk];
                int count = VisibilityChunkSize(chunk, instanceCount);
                if (count < VisibilityChunkWidth)
                {
                    ulong keep = count <= 0 ? 0UL : (count >= 64 ? ulong.MaxValue : ((1UL << count) - 1UL));
                    mask &= keep;
                }
                visible += PopCount(mask);
            }
            return visible;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ExpandMaskToIndex(int candidateBase, int count, ulong mask, int[] dest, int destStart)
        {
            int written = 0;
            for (int bit = 0; bit < count; ++bit)
            {
                if ((mask & (1UL << bit)) == 0) { continue; }
                dest[destStart + written] = candidateBase + bit;
                ++written;
            }
            return written;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ExpandMasksToIndex(ulong[] masks, int chunkCount, int instanceCount, int[] dest)
        {
            int written = 0;
            for (int chunk = 0; chunk < chunkCount; ++chunk)
            {
                int count = VisibilityChunkSize(chunk, instanceCount);
                written += ExpandMaskToIndex(VisibilityChunkBase(chunk), count, masks[chunk], dest, written);
            }
            return written;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeRuns(int[] ids, int idCount, VisibilityRun[] runs)
        {
            if (idCount <= 0) { return 0; }
            int runCount = 0;
            int start = ids[0];
            int last = ids[0];
            for (int i = 1; i < idCount; ++i)
            {
                int id = ids[i];
                if (id == last + 1)
                {
                    last = id;
                    continue;
                }
                runs[runCount].start = start;
                runs[runCount].count = (last - start) + 1;
                ++runCount;
                start = id;
                last = id;
            }
            runs[runCount].start = start;
            runs[runCount].count = (last - start) + 1;
            ++runCount;
            return runCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeRunsFromMasks(ulong[] masks, int chunkCount, int instanceCount, VisibilityRun[] runs)
        {
            int runCount = 0;
            int runStart = -1;
            int runLast = -2;
            for (int chunk = 0; chunk < chunkCount; ++chunk)
            {
                int baseIndex = VisibilityChunkBase(chunk);
                int count = VisibilityChunkSize(chunk, instanceCount);
                ulong mask = masks[chunk];
                for (int bit = 0; bit < count; ++bit)
                {
                    if ((mask & (1UL << bit)) == 0) { continue; }
                    int id = baseIndex + bit;
                    if (runStart < 0)
                    {
                        runStart = id;
                        runLast = id;
                        continue;
                    }
                    if (id == runLast + 1)
                    {
                        runLast = id;
                        continue;
                    }
                    runs[runCount].start = runStart;
                    runs[runCount].count = (runLast - runStart) + 1;
                    ++runCount;
                    runStart = id;
                    runLast = id;
                }
            }
            if (runStart >= 0)
            {
                runs[runCount].start = runStart;
                runs[runCount].count = (runLast - runStart) + 1;
                ++runCount;
            }
            return runCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ExpandRunsToIndex(VisibilityRun[] runs, int runCount, int[] dest)
        {
            int written = 0;
            for (int i = 0; i < runCount; ++i)
            {
                int start = runs[i].start;
                int count = runs[i].count;
                for (int k = 0; k < count; ++k)
                {
                    dest[written] = start + k;
                    ++written;
                }
            }
            return written;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PickVisibilityCodec(int visibleCount, int chunkCount, int runCount, int gpuReady)
        {
            if (gpuReady == 0 || visibleCount <= 0)
            {
                return (int)VisibilityCodec.CompactIndex;
            }

            int costIndex = visibleCount * VisibilityIndexBytes;
            int costMask = (chunkCount * VisibilityChunkBytes) + (visibleCount * VisibilityExpandMaskWeight);
            int costRun = (runCount * VisibilityRunBytes) + (visibleCount * VisibilityExpandRunWeight);
            int codec = (int)VisibilityCodec.CompactIndex;
            int best = costIndex;
            if (costMask < best)
            {
                best = costMask;
                codec = (int)VisibilityCodec.BitMaskTransfer;
            }
            if (costRun < best)
            {
                codec = (int)VisibilityCodec.RunTransfer;
            }
            return codec;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CollectVisibleCells(byte[] visible, int[] cells)
        {
            int count = 0;
            for (int i = 0; i < visible.Length; ++i)
            {
                if (visible[i] == 0) { continue; }
                cells[count] = i;
                ++count;
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SampleHeightField(float[] heights, int heightRes, float terrainPosX, float terrainPosZ, float terrainSizeX, float terrainSizeZ, float worldX, float worldZ)
        {
            if (heights == null || heightRes <= 1) { return 0f; }
            float u = terrainSizeX > 0.0001f ? (worldX - terrainPosX) / terrainSizeX : 0f;
            float v = terrainSizeZ > 0.0001f ? (worldZ - terrainPosZ) / terrainSizeZ : 0f;
            if (u < 0f) { u = 0f; }
            if (v < 0f) { v = 0f; }
            if (u > 1f) { u = 1f; }
            if (v > 1f) { v = 1f; }
            float fx = u * (heightRes - 1);
            float fz = v * (heightRes - 1);
            int x0 = (int)fx;
            int z0 = (int)fz;
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            if (x1 >= heightRes) { x1 = heightRes - 1; }
            if (z1 >= heightRes) { z1 = heightRes - 1; }
            float tx = fx - x0;
            float tz = fz - z0;
            float h00 = heights[(z0 * heightRes) + x0];
            float h10 = heights[(z0 * heightRes) + x1];
            float h01 = heights[(z1 * heightRes) + x0];
            float h11 = heights[(z1 * heightRes) + x1];
            float h0 = h00 + ((h10 - h00) * tx);
            float h1 = h01 + ((h11 - h01) * tx);
            return h0 + ((h1 - h0) * tz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TerrainOccludes(float boxMinX, float boxMinY, float boxMinZ, float boxMaxX, float boxMaxY, float boxMaxZ, float viewX, float viewY, float viewZ, float[] heights, int heightRes, float terrainPosX, float terrainPosZ, float terrainSizeX, float terrainSizeZ)
        {
            if (heights == null || heightRes <= 1) { return false; }

            float centerX = (boxMinX + boxMaxX) * 0.5f;
            float centerZ = (boxMinZ + boxMaxZ) * 0.5f;
            float dx = centerX - viewX;
            float dz = centerZ - viewZ;
            float boxDist = (float)Math.Sqrt((dx * dx) + (dz * dz));
            if (boxDist < 0.5f) { return false; }

            float boxTopSlope = (boxMaxY - viewY) / boxDist;
            const int samples = 8;
            for (int s = 1; s < samples; ++s)
            {
                float t = s / (float)samples;
                if (t > 0.85f) { break; }
                float px = viewX + (dx * t);
                float pz = viewZ + (dz * t);
                float dist = boxDist * t;
                if (dist < 1f) { continue; }
                float height = SampleHeightField(heights, heightRes, terrainPosX, terrainPosZ, terrainSizeX, terrainSizeZ, px, pz);
                float terrainSlope = (height - viewY) / dist;
                if (terrainSlope > boxTopSlope + 0.05f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
