using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    public struct FTreeElement : IEquatable<FTreeElement>
    {
        public int meshIndex;
        public FBound boundBox;
        public FSphere boundSphere;
        public float4x4 matrix_World;


        public bool Equals(FTreeElement target)
        {
            return meshIndex.Equals(target.meshIndex) && boundBox.Equals(target.boundBox) && boundSphere.Equals(target.boundSphere) && matrix_World.Equals(target.matrix_World);
        }

        public override bool Equals(object target)
        {
            return Equals((FTreeElement)target);
        }

        public override int GetHashCode()
        {
            return new int4(meshIndex, boundBox.GetHashCode(), boundSphere.GetHashCode(), matrix_World.GetHashCode()).GetHashCode();
        }
    }
}
