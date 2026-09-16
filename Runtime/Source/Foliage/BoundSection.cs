using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct BoundSection : IEquatable<BoundSection>
    {
        public Aabb boundBox;
        public float2 pivotPosition;

        public bool Equals(BoundSection target)
        {
            return boundBox.Equals(target.boundBox) && pivotPosition.Equals(target.pivotPosition);
        }

        public override bool Equals(object obj)
        {
            return Equals((BoundSection)obj);
        }

        public override int GetHashCode()
        {
            return new int2(boundBox.GetHashCode(), pivotPosition.GetHashCode()).GetHashCode();
        }
    }
}
