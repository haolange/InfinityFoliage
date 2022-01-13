using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FBoundSection : IEquatable<FBoundSection>
    {
        public FAABB boundBox;
        public float3 pivotPosition;

        public bool Equals(FBoundSection Target)
        {
            return boundBox.Equals(Target.boundBox) && pivotPosition.Equals(Target.pivotPosition);
        }

        public override bool Equals(object obj)
        {
            return Equals((FBoundSection)obj);
        }

        public override int GetHashCode()
        {
            return new int2(boundBox.GetHashCode(), pivotPosition.GetHashCode()).GetHashCode();
        }
    }
}
