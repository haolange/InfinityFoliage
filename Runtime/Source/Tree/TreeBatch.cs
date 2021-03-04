using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    public struct FTreeBatch : IEquatable<FTreeBatch>
    {
        public FBound BoundBox;
        public FSphere BoundSphere;
        public float4x4 Matrix_World;


        public bool Equals(FTreeBatch Target)
        {
            return BoundBox.Equals(Target.BoundBox) && BoundSphere.Equals(Target.BoundSphere) && Matrix_World.Equals(Target.Matrix_World);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeBatch)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 1;
            hashCode += BoundBox.GetHashCode();
            hashCode += BoundSphere.GetHashCode();
            hashCode += Matrix_World.GetHashCode();

            return hashCode;
        }
    }
}
