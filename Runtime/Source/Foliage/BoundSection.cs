using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FBoundSection : IEquatable<FBoundSection>
    {
        public FBound BoundBox;
        public float3 PivotPosition;
        public float3 CenterPosition;


        public bool Equals(FBoundSection Target)
        {
            return BoundBox.Equals(Target.BoundBox) && PivotPosition.Equals(Target.PivotPosition) && CenterPosition.Equals(Target.CenterPosition);
        }

        public override bool Equals(object obj)
        {
            return Equals((FBoundSection)obj);
        }

        public override int GetHashCode()
        {
            return BoundBox.GetHashCode() + PivotPosition.GetHashCode() + CenterPosition.GetHashCode();
        }
    }
}
