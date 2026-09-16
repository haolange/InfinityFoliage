using System;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FrustumPlane : IEquatable<FrustumPlane>
    {
        private float4 m_NormalDist;

        public float4 normalDist { get { return m_NormalDist; } set { m_NormalDist = value; } }


        public FrustumPlane(float3 inNormal, float3 inPoint)
        {
            m_NormalDist = new float4(1, 1, 1, 1);
            m_NormalDist.xyz = math.normalize(inNormal);
            m_NormalDist.w = -math.dot(m_NormalDist.xyz, inPoint);
        }

        public FrustumPlane(float3 inNormal, float d)
        {
            m_NormalDist = new float4(1, 1, 1, 1);
            m_NormalDist.xyz = math.normalize(inNormal);
            m_NormalDist.w = d;
        }

        public FrustumPlane(float3 a, float3 b, float3 c)
        {
            m_NormalDist = new float4(1, 1, 1, 1);
            m_NormalDist.xyz = math.normalize(math.cross(b - a, c - a));
            m_NormalDist.w = -math.dot(m_NormalDist.xyz, a);
        }

        public override bool Equals(object other)
        {
            if (!(other is FrustumPlane)) return false;

            return Equals((FrustumPlane)other);
        }

        public bool Equals(FrustumPlane other)
        {
            return normalDist.Equals(other.normalDist);
        }

        public override int GetHashCode()
        {
            return m_NormalDist.GetHashCode();
        }

        public static implicit operator Plane(FrustumPlane plane) { return new Plane(plane.normalDist.xyz, plane.normalDist.w); }

        public static implicit operator FrustumPlane(Plane plane) { return new FrustumPlane(plane.normal, plane.distance); }
    }

    [Serializable]
    public struct Aabb : IEquatable<Aabb>
    {
        [SerializeField]
        private float3 m_Center;
        [SerializeField]
        private float3 m_Extents;

        public float3 center { get { return m_Center; } set { m_Center = value; } }
        public float3 size { get { return m_Extents * 2.0F; } set { m_Extents = value * 0.5F; } }
        public float3 extents { get { return m_Extents; } set { m_Extents = value; } }
        public float3 min { get { return center - extents; } set { SetMinMax(value, max); } }
        public float3 max { get { return center + extents; } set { SetMinMax(min, value); } }

        public Aabb(float3 center, float3 size)
        {
            m_Center = center;
            m_Extents = size * 0.5F;
        }

        public override bool Equals(object other)
        {
            if (!(other is Aabb)) return false;

            return Equals((Aabb)other);
        }

        public bool Equals(Aabb other)
        {
            return center.Equals(other.center) && extents.Equals(other.extents);
        }

        public override int GetHashCode()
        {
            return center.GetHashCode() ^ (extents.GetHashCode() << 2);
        }

        public void SetMinMax(in float3 min, in float3 max)
        {
            extents = (max - min) * 0.5F;
            center = min + extents;
        }

        public void Encapsulate(in Aabb other)
        {
            SetMinMax(math.min(min, other.min), math.max(max, other.max));
        }

        public static implicit operator Bounds(Aabb AABB) { return new Bounds(AABB.center, AABB.size); }

        public static implicit operator Aabb(Bounds Bound) { return new Aabb(Bound.center, Bound.size); }
    }

    [Serializable]
    public struct BoundSphere : IEquatable<BoundSphere>
    {
        private float m_Radius;
        private float3 m_Center;

        public float radius { get { return m_Radius; } set { m_Radius = value; } }
        public float3 center { get { return m_Center; } set { m_Center = value; } }


        public BoundSphere(float radius, float3 center)
        {
            m_Radius = radius;
            m_Center = center;
        }

        public override bool Equals(object other)
        {
            if (!(other is BoundSphere)) return false;

            return Equals((BoundSphere)other);
        }

        public bool Equals(BoundSphere other)
        {
            return radius.Equals(other.radius) && center.Equals(other.center);
        }

        public override int GetHashCode()
        {
            return radius.GetHashCode() ^ (center.GetHashCode() << 2);
        }
    }

#if UNITY_EDITOR
    public class TerrainTexture
    {
        public Texture2D HeightMap;

        public TerrainTexture(int TextureSize)
        {
            HeightMap = new Texture2D(TextureSize, TextureSize, TextureFormat.R16, false, true);
        }

        public void TerrainDataToHeightmap(TerrainData InTerrainData)
        {
            if (HeightMap.width != 0)
                HeightmapLoader.TerrainDataToTexture(HeightMap, InTerrainData);
        }

        public void Release()
        {
            UnityEngine.Object.DestroyImmediate(HeightMap);
        }
    }
#endif

    public static class HeightmapLoader
    {
        public static void ImportRaw(string path, int m_Depth, int m_Resolution, bool m_FlipVertically, TerrainData terrainData)
        {
            // Read data
            byte[] data;
            using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read)))
            {
                data = br.ReadBytes(m_Resolution * m_Resolution * (int)m_Depth);
                br.Close();
            }

            int heightmapRes = terrainData.heightmapResolution;
            float[,] heights = new float[heightmapRes, heightmapRes];

            float normalize = 1.0F / (1 << 16);
            for (int y = 0; y < heightmapRes; ++y)
            {
                for (int x = 0; x < heightmapRes; ++x)
                {
                    int index = Mathf.Clamp(x, 0, m_Resolution - 1) + Mathf.Clamp(y, 0, m_Resolution - 1) * m_Resolution;
                    ushort compressedHeight = System.BitConverter.ToUInt16(data, index * 2);
                    float height = compressedHeight * normalize;
                    int destY = m_FlipVertically ? heightmapRes - 1 - y : y;
                    heights[destY, x] = height;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        public static void ExportRaw(string path, int m_Depth, bool m_FlipVertically, TerrainData terrainData)
        {
            // Write data
            int heightmapRes = terrainData.heightmapResolution;
            float[,] heights = terrainData.GetHeights(0, 0, heightmapRes, heightmapRes);
            byte[] data = new byte[heightmapRes * heightmapRes * (int)m_Depth];

            float normalize = (1 << 16);
            for (int y = 0; y < heightmapRes; ++y)
            {
                for (int x = 0; x < heightmapRes; ++x)
                {
                    int index = x + y * heightmapRes;
                    int srcY = m_FlipVertically ? heightmapRes - 1 - y : y;
                    int height = Mathf.RoundToInt(heights[srcY, x] * normalize);
                    ushort compressedHeight = (ushort)Mathf.Clamp(height, 0, ushort.MaxValue);

                    byte[] byteData = System.BitConverter.GetBytes(compressedHeight);
                    data[index * 2 + 0] = byteData[0];
                    data[index * 2 + 1] = byteData[1];
                }
            }

            FileStream fs = new FileStream(path, FileMode.Create);
            fs.Write(data, 0, data.Length);
            fs.Close();
        }

        public static void InitTerrainData(int TextureSize, TerrainData DescTerrainData)
        {
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(DescTerrainData, "InitTerrainData");
#endif
            float[,] heights = new float[TextureSize, TextureSize];

            for (int x = 0; x <= TextureSize - 1; ++x)
            {
                for (int y = 0; y <= TextureSize - 1; ++y)
                {
                    heights[(TextureSize - 1) - x, y] = 0;
                }
            }

            DescTerrainData.SetHeights(0, 0, heights);
        }

        public static void TextureToTerrainData(Texture2D SourceHeightMap, TerrainData DescTerrainData)
        {
            Color[] HeightData = SourceHeightMap.GetPixels(0);
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(DescTerrainData, "CopyTextureToTerrainData");
#endif

            int TextureSize = SourceHeightMap.width;
            float[,] heights = new float[TextureSize, TextureSize];

            for (int x = 0; x <= TextureSize - 1; ++x)
            {
                for (int y = 0; y <= TextureSize - 1; ++y)
                {
                    heights[(TextureSize - 1) - x, y] = HeightData[x * TextureSize + y].r;
                }
            }

            DescTerrainData.SetHeights(0, 0, heights);
        }

        public static void TerrainDataToTexture(Texture2D SourceHeightMap, TerrainData DescTerrainData)
        {
#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(SourceHeightMap, "CopyTerrainDataToTexture");
#endif

            int TextureSize = SourceHeightMap.width;
            Color[] HeightData = new Color[TextureSize * TextureSize];
            for (int x = 0; x <= TextureSize - 1; ++x)
            {
                for (int y = 0; y <= TextureSize - 1; ++y)
                {
                    HeightData[x * TextureSize + y].r = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].g = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].b = DescTerrainData.GetHeight(y, (TextureSize - 1) - x) / DescTerrainData.heightmapScale.y;
                    HeightData[x * TextureSize + y].a = 1;
                }
            }

            SourceHeightMap.SetPixels(HeightData, 0);
            SourceHeightMap.Apply();
        }
    }

    public static class Geometry
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CaculateBoundRadius(in Aabb bound)
        {
            return math.max(math.max(math.abs(bound.extents.x), math.abs(bound.extents.y)), math.abs(bound.extents.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Aabb CaculateWorldBound(in Aabb bound, in Matrix4x4 matrix)
        {
            float4 center = matrix * new float4(bound.center.x, bound.center.y, bound.center.z, 1);
            float4 extents = math.abs(matrix.GetColumn(0) * bound.extents.x) + math.abs(matrix.GetColumn(1) * bound.extents.y) + math.abs(matrix.GetColumn(2) * bound.extents.z);
            return new Aabb(center.xyz, extents.xyz * 2);
        }

        public static Aabb CaculateWorldBound(in Aabb bound, in float4x4 matrix)
        {
            return CaculateWorldBound(bound, (Matrix4x4)matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectAABBFrustum(in Aabb bound, FrustumPlane[] plane)
        {
            for (int i = 0; i < 6; ++i)
            {
                float3 normal = plane[i].normalDist.xyz;
                float distance = plane[i].normalDist.w;

                float dist = math.dot(normal, bound.center) + distance;
                float radius = math.dot(bound.extents, math.abs(normal));

                if (dist + radius < 0)
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Squared(in float a)
        {
            return a * a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistSquared(in float3 v1, in float3 v2)
        {
            return Squared(v2.x - v1.x) + Squared(v2.y - v1.y) + Squared(v2.z - v1.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LogX(in float refValue, in float value)
        {
            return math.log(value) / math.log(refValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4x4 GetProjectionMatrix(in float halfFOV, in float width, in float height, in float minZ, in float maxZ)
        {
            float4 column0 = new float4(1.0f / math.tan(halfFOV), 0.0f, 0.0f, 0.0f);
            float4 column1 = new float4(0.0f, width / math.tan(halfFOV) / height, 0.0f, 0.0f);
            float4 column2 = new float4(0.0f, 0.0f, minZ == maxZ ? 1.0f : maxZ / (maxZ - minZ), 1.0f);
            float4 column3 = new float4(0.0f, 0.0f, -minZ * (minZ == maxZ ? 1.0f : maxZ / (maxZ - minZ)), 0.0f);

            return new float4x4(column0, column1, column2, column3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeBoundsScreenRadiusSquared(in float sphereRadius, in float3 boundOrigin, in float3 viewOrigin, in Matrix4x4 projMatrix)
        {
            float DistSqr = DistSquared(boundOrigin, viewOrigin) * projMatrix.m23;

            float ScreenMultiple = math.max(0.5f * projMatrix.m00, 0.5f * projMatrix.m11);
            ScreenMultiple *= sphereRadius;

            return (ScreenMultiple * ScreenMultiple) / math.max(1, DistSqr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeBoundsScreenRadiusSquared(in float sphereRadius, in float3 boundOrigin, in float3 viewOrigin, in float4x4 projMatrix)
        {
            float DistSqr = DistSquared(boundOrigin, viewOrigin) * projMatrix.c2.z;

            float ScreenMultiple = math.max(0.5f * projMatrix.c0.x, 0.5f * projMatrix.c1.y);
            ScreenMultiple *= sphereRadius;

            return (ScreenMultiple * ScreenMultiple) / math.max(1, DistSqr);
        }


        //Debug
        public static Color[] LODColors = new Color[7] { new Color(1, 1, 1, 1), new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) };
#if UNITY_EDITOR
        public static void DrawBound(Bounds b)
        {
            // bottom
            var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
            var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
            var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
            var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

            Debug.DrawLine(p1, p2, Color.blue);
            Debug.DrawLine(p2, p3, Color.red);
            Debug.DrawLine(p3, p4, Color.yellow);
            Debug.DrawLine(p4, p1, Color.magenta);

            // top
            var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
            var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
            var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
            var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

            Debug.DrawLine(p5, p6, Color.blue);
            Debug.DrawLine(p6, p7, Color.red);
            Debug.DrawLine(p7, p8, Color.yellow);
            Debug.DrawLine(p8, p5, Color.magenta);

            // sides
            Debug.DrawLine(p1, p5, Color.white);
            Debug.DrawLine(p2, p6, Color.gray);
            Debug.DrawLine(p3, p7, Color.green);
            Debug.DrawLine(p4, p8, Color.cyan);
        }

        public static void DrawBound(Bounds b, Color DebugColor)
        {
            // bottom
            var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
            var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
            var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
            var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

            Debug.DrawLine(p1, p2, DebugColor);
            Debug.DrawLine(p2, p3, DebugColor);
            Debug.DrawLine(p3, p4, DebugColor);
            Debug.DrawLine(p4, p1, DebugColor);

            // top
            var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
            var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
            var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
            var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

            Debug.DrawLine(p5, p6, DebugColor);
            Debug.DrawLine(p6, p7, DebugColor);
            Debug.DrawLine(p7, p8, DebugColor);
            Debug.DrawLine(p8, p5, DebugColor);

            // sides
            Debug.DrawLine(p1, p5, DebugColor);
            Debug.DrawLine(p2, p6, DebugColor);
            Debug.DrawLine(p3, p7, DebugColor);
            Debug.DrawLine(p4, p8, DebugColor);
        }

        public static void DrawRect(Rect rect, Color color)
        {

            Vector3[] line = new Vector3[5];

            line[0] = new Vector3(rect.x, rect.y, 0);

            line[1] = new Vector3(rect.x + rect.width, rect.y, 0);

            line[2] = new Vector3(rect.x + rect.width, rect.y + rect.height, 0);

            line[3] = new Vector3(rect.x, rect.y + rect.height, 0);

            line[4] = new Vector3(rect.x, rect.y, 0);

            for (int i = 0; i < line.Length - 1; ++i)
            {
                Debug.DrawLine(line[i], line[i + 1], color);
            }
        }
#endif
    }
}
