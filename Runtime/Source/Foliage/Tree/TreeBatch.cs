using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    public struct FTreeBatch : IEquatable<FTreeBatch>
    {
        public int lODIndex;
        public FBound boundBox;
        public FSphere boundSphere;
        public float4x4 matrix_World;


        public bool Equals(FTreeBatch Target)
        {
            return lODIndex.Equals(Target.lODIndex) && boundBox.Equals(Target.boundBox) && boundSphere.Equals(Target.boundSphere) && matrix_World.Equals(Target.matrix_World);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeBatch)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = lODIndex.GetHashCode();
            hashCode += boundBox.GetHashCode();
            hashCode += boundSphere.GetHashCode();
            hashCode += matrix_World.GetHashCode();

            return hashCode;
        }
    }
}
