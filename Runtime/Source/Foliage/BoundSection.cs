using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FBoundSection : IEquatable<FBoundSection>
    {
        public FBound boundBox;
        public float3 pivotPosition;
        public float3 centerPosition;

        public bool Equals(FBoundSection Target)
        {
            return boundBox.Equals(Target.boundBox) && pivotPosition.Equals(Target.pivotPosition) && centerPosition.Equals(Target.centerPosition);
        }

        public override bool Equals(object obj)
        {
            return Equals((FBoundSection)obj);
        }

        public override int GetHashCode()
        {
            return boundBox.GetHashCode() + pivotPosition.GetHashCode() + centerPosition.GetHashCode();
        }
    }
}
