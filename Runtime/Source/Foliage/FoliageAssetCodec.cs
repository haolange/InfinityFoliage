using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public class GrassDensityPage
    {
        public int grassIndex;
        public int resolution;
        public int numSection;
        public int pageX;
        public int pageY;
        public int xStart;
        public int yStart;
        public int width;
        public int height;
        public int[] cellIndices;
        public int[] cellOffsets;
        public int[] cellCounts;
        public byte[] density;
    }

    public static class FoliageAssetCodec
    {
        public const int PageAxis = 4;
        public const int BaseDivisor = 8;
        private const int Magic = 0x474C4F46;
        private const int Version = 1;
        private const int GrassKind = 1;
        private const int TreeKind = 2;
        private const int ManifestKind = 3;
        private const int MaxPayload = 64 * 1024 * 1024;

        public static string TerrainPath(string assetKey)
        {
            if (string.IsNullOrEmpty(assetKey)) { throw new InvalidDataException("Foliage asset key is missing; rebake this Terrain."); }
            return assetKey;
        }

        public static string GrassBasePath(string assetKey, in int grassIndex)
        {
            return TerrainPath(assetKey) + "/grass_" + grassIndex + "_base";
        }

        public static string GrassPagePath(string assetKey, in int grassIndex, in int pageX, in int pageY)
        {
            return TerrainPath(assetKey) + "/grass_" + grassIndex + "_page_" + pageX + "_" + pageY;
        }

        public static string TreePath(string assetKey, in int treeIndex)
        {
            return TerrainPath(assetKey) + "/tree_" + treeIndex;
        }

        public static string ManifestPath(string assetKey)
        {
            return TerrainPath(assetKey) + "/index";
        }

        public static byte BaseDensity(in byte density, in int x, in int y, in int grassIndex)
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)grassIndex * 83492791u;
            h ^= h >> 13;
            h *= 1274126177u;
            return (byte)((density + (h & 7u)) / BaseDivisor);
        }

        public static GrassDensityPage CreateGrassPage(in int grassIndex, in int resolution, in int numSection, in int pageX, in int pageY, byte[] density)
        {
            if (resolution <= 0 || numSection <= 0 || resolution % numSection != 0 || density == null)
            {
                throw new InvalidDataException("Invalid grass density grid.");
            }
            if ((pageX != -1 || pageY != -1) && (pageX < 0 || pageX >= PageAxis || pageY < 0 || pageY >= PageAxis))
            {
                throw new InvalidDataException("Invalid grass detail page.");
            }

            int xStart = pageX < 0 ? 0 : (pageX * resolution) / PageAxis;
            int yStart = pageY < 0 ? 0 : (pageY * resolution) / PageAxis;
            int xEnd = pageX < 0 ? resolution : ((pageX + 1) * resolution) / PageAxis;
            int yEnd = pageY < 0 ? resolution : ((pageY + 1) * resolution) / PageAxis;
            int width = xEnd - xStart;
            int height = yEnd - yStart;
            if (density.Length != width * height) { throw new InvalidDataException("Grass density page size does not match its Terrain."); }

            int[] counts = new int[numSection * numSection];
            for (int y = 0; y < height; ++y)
            {
                int cellY = ((yStart + y) * numSection) / resolution;
                for (int x = 0; x < width; ++x)
                {
                    int cellX = ((xStart + x) * numSection) / resolution;
                    counts[(cellX * numSection) + cellY] += density[(y * width) + x];
                }
            }

            int occupied = 0;
            for (int i = 0; i < counts.Length; ++i) { if (counts[i] > 0) { ++occupied; } }
            GrassDensityPage page = new GrassDensityPage();
            page.grassIndex = grassIndex;
            page.resolution = resolution;
            page.numSection = numSection;
            page.pageX = pageX;
            page.pageY = pageY;
            page.xStart = xStart;
            page.yStart = yStart;
            page.width = width;
            page.height = height;
            page.density = density;
            page.cellIndices = new int[occupied];
            page.cellOffsets = new int[occupied];
            page.cellCounts = new int[occupied];
            int cursor = 0;
            int offset = 0;
            for (int i = 0; i < counts.Length; ++i)
            {
                if (counts[i] == 0) { continue; }
                page.cellIndices[cursor] = i;
                page.cellOffsets[cursor] = offset;
                page.cellCounts[cursor] = counts[i];
                offset += counts[i];
                ++cursor;
            }
            return page;
        }

        public static byte[] EncodeGrass(GrassDensityPage page)
        {
            if (page == null) { throw new ArgumentNullException(nameof(page)); }
            GrassDensityPage checkedPage = CreateGrassPage(page.grassIndex, page.resolution, page.numSection, page.pageX, page.pageY, page.density);
            using (MemoryStream payload = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(checkedPage.grassIndex);
                    writer.Write(checkedPage.resolution);
                    writer.Write(checkedPage.numSection);
                    writer.Write(checkedPage.pageX);
                    writer.Write(checkedPage.pageY);
                    writer.Write(checkedPage.cellIndices.Length);
                    for (int i = 0; i < checkedPage.cellIndices.Length; ++i)
                    {
                        writer.Write(checkedPage.cellIndices[i]);
                        writer.Write(checkedPage.cellOffsets[i]);
                        writer.Write(checkedPage.cellCounts[i]);
                    }
                    writer.Write(checkedPage.density.Length);
                    writer.Write(checkedPage.density);
                }
                return Pack(GrassKind, payload.ToArray());
            }
        }

        public static GrassDensityPage DecodeGrass(byte[] bytes, in int expectedGrassIndex, in int expectedResolution, in int expectedSections, in int expectedPageX, in int expectedPageY)
        {
            using (BinaryReader reader = OpenPayload(bytes, GrassKind))
            {
                int grassIndex = reader.ReadInt32();
                int resolution = reader.ReadInt32();
                int numSection = reader.ReadInt32();
                int pageX = reader.ReadInt32();
                int pageY = reader.ReadInt32();
                if (grassIndex != expectedGrassIndex || resolution != expectedResolution || numSection != expectedSections || pageX != expectedPageX || pageY != expectedPageY)
                {
                    throw new InvalidDataException("Grass page does not match the baked Terrain.");
                }
                int count = reader.ReadInt32();
                if (count < 0 || count > numSection * numSection) { throw new InvalidDataException("Invalid grass page cell count."); }
                int[] indices = new int[count];
                int[] offsets = new int[count];
                int[] counts = new int[count];
                for (int i = 0; i < count; ++i)
                {
                    indices[i] = reader.ReadInt32();
                    offsets[i] = reader.ReadInt32();
                    counts[i] = reader.ReadInt32();
                }
                int length = reader.ReadInt32();
                if (length < 0 || length > MaxPayload) { throw new InvalidDataException("Invalid grass page length."); }
                byte[] density = reader.ReadBytes(length);
                GrassDensityPage page = CreateGrassPage(grassIndex, resolution, numSection, pageX, pageY, density);
                if (reader.BaseStream.Position != reader.BaseStream.Length || count != page.cellIndices.Length)
                {
                    throw new InvalidDataException("Invalid grass page payload.");
                }
                for (int i = 0; i < count; ++i)
                {
                    if (indices[i] != page.cellIndices[i] || offsets[i] != page.cellOffsets[i] || counts[i] != page.cellCounts[i])
                    {
                        throw new InvalidDataException("Grass page cell offsets are corrupt.");
                    }
                }
                return page;
            }
        }

        public static byte[] EncodeTree(in int treeIndex, List<InstanceTransform> transforms)
        {
            if (transforms == null) { throw new ArgumentNullException(nameof(transforms)); }
            using (MemoryStream payload = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(treeIndex);
                    writer.Write(transforms.Count);
                    for (int i = 0; i < transforms.Count; ++i)
                    {
                        InstanceTransform value = transforms[i];
                        writer.Write(value.position.x); writer.Write(value.position.y); writer.Write(value.position.z);
                        writer.Write(value.rotation.x); writer.Write(value.rotation.y); writer.Write(value.rotation.z);
                        writer.Write(value.scale.x); writer.Write(value.scale.y); writer.Write(value.scale.z);
                    }
                }
                return Pack(TreeKind, payload.ToArray());
            }
        }

        public static List<InstanceTransform> DecodeTree(byte[] bytes, in int expectedTreeIndex)
        {
            using (BinaryReader reader = OpenPayload(bytes, TreeKind))
            {
                if (reader.ReadInt32() != expectedTreeIndex) { throw new InvalidDataException("Tree asset species does not match the Terrain."); }
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxPayload / 36) { throw new InvalidDataException("Invalid tree candidate count."); }
                List<InstanceTransform> transforms = new List<InstanceTransform>(count);
                for (int i = 0; i < count; ++i)
                {
                    float3 position = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float3 rotation = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float3 scale = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    transforms.Add(new InstanceTransform(position, rotation, scale));
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("Tree asset has trailing data."); }
                return transforms;
            }
        }

        public static byte[] EncodeManifest(List<string> paths)
        {
            using (MemoryStream payload = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(paths.Count);
                    for (int i = 0; i < paths.Count; ++i) { writer.Write(paths[i]); }
                }
                return Pack(ManifestKind, payload.ToArray());
            }
        }

        public static List<string> DecodeManifest(byte[] bytes)
        {
            using (BinaryReader reader = OpenPayload(bytes, ManifestKind))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 4096) { throw new InvalidDataException("Invalid foliage manifest."); }
                List<string> paths = new List<string>(count);
                for (int i = 0; i < count; ++i) { paths.Add(reader.ReadString()); }
                if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new InvalidDataException("Foliage manifest has trailing data."); }
                return paths;
            }
        }

        private static byte[] Pack(in int kind, byte[] payload)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(kind);
                    writer.Write(payload.Length);
                    writer.Write(Hash(payload));
                    using (DeflateStream compressed = new DeflateStream(output, CompressionLevel.Optimal, true))
                    {
                        compressed.Write(payload, 0, payload.Length);
                    }
                }
                return output.ToArray();
            }
        }

        private static BinaryReader OpenPayload(byte[] bytes, in int kind)
        {
            if (bytes == null) { throw new ArgumentNullException(nameof(bytes)); }
            using (MemoryStream input = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(input))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version || reader.ReadInt32() != kind)
                {
                    throw new InvalidDataException("Foliage asset format is not current; rebake this Terrain.");
                }
                int length = reader.ReadInt32();
                uint expectedHash = reader.ReadUInt32();
                if (length < 0 || length > MaxPayload) { throw new InvalidDataException("Invalid foliage asset length."); }
                byte[] payload = new byte[length];
                using (DeflateStream compressed = new DeflateStream(input, CompressionMode.Decompress, true))
                {
                    int position = 0;
                    while (position < length)
                    {
                        int read = compressed.Read(payload, position, length - position);
                        if (read == 0) { throw new EndOfStreamException("Foliage asset is truncated."); }
                        position += read;
                    }
                    if (compressed.ReadByte() != -1) { throw new InvalidDataException("Foliage asset length mismatch."); }
                }
                if (Hash(payload) != expectedHash) { throw new InvalidDataException("Foliage asset checksum mismatch."); }
                return new BinaryReader(new MemoryStream(payload, false));
            }
        }

        private static uint Hash(byte[] bytes)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < bytes.Length; ++i) { hash = (hash ^ bytes[i]) * 16777619u; }
            return hash;
        }
    }
}
