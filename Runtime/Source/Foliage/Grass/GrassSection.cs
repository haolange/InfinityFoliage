using System;
using UnityEngine;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct GrassElement : IEquatable<GrassElement>
    {
        public float4x4 matrix_World;

        public GrassElement(in float4x4 matrix_World)
        {
            this.matrix_World = matrix_World;
        }

        public bool Equals(GrassElement target)
        {
            return matrix_World.Equals(target.matrix_World);
        }

        public override bool Equals(object target)
        {
            return Equals((GrassElement)target);
        }

        public override int GetHashCode()
        {
            return matrix_World.GetHashCode();
        }
    }

    internal static class GrassShaderID
    {
        internal static int TerrainSize = Shader.PropertyToID("_TerrainSize");
        internal static int InstanceOffset = Shader.PropertyToID("_InstanceOffset");
        internal static int ElementBuffer = Shader.PropertyToID("_GrassElementBuffer");
        internal static int TerrainHeightmap = Shader.PropertyToID("_TerrainHeightmap");
        internal static int TerrainNormalmap = Shader.PropertyToID("_TerrainNormalmap");
        internal static int TerrainPivotScaleY = Shader.PropertyToID("_TerrainPivotScaleY");
    }

    [Serializable]
    public class GrassSection
    {
        public int boundIndex;
        [HideInInspector]
        public int offset;
        [HideInInspector]
        public int count;
        public byte[] densityMap;
    }
}
