using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct TreeElement : IEquatable<TreeElement>
    {
        public float4x4 matrix_World;

        public bool Equals(TreeElement target)
        {
            return matrix_World.Equals(target.matrix_World);
        }

        public override bool Equals(object target)
        {
            return Equals((TreeElement)target);
        }

        public override int GetHashCode()
        {
            return matrix_World.GetHashCode();
        }
    }

    internal static class TreeShaderID
    {
        internal static int IndexBuffer = UnityEngine.Shader.PropertyToID("_TreeIndexBuffer");
        internal static int ElementBuffer = UnityEngine.Shader.PropertyToID("_TreeElementBuffer");
        internal static int LodFactor = UnityEngine.Shader.PropertyToID("_LODFactor");
        internal static int LodFadeEnable = UnityEngine.Shader.PropertyToID("_LodFadeEnable");
    }
}
