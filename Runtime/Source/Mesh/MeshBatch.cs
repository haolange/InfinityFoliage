using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    public struct FMeshBatch : IEquatable<FMeshBatch>
    {
        public int lODIndex;
        public FBound boundBox;
        public FSphere boundSphere;
        public float4x4 matrix_World;


        public bool Equals(FMeshBatch Target)
        {
            return lODIndex.Equals(Target.lODIndex) && boundBox.Equals(Target.boundBox) && boundSphere.Equals(Target.boundSphere) && matrix_World.Equals(Target.matrix_World);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshBatch)obj);
        }

        public override int GetHashCode()
        {
            return new int4(lODIndex, boundBox.GetHashCode(), boundSphere.GetHashCode(), matrix_World.GetHashCode()).GetHashCode();
        }
    }
}
