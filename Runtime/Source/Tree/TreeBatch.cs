using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FTransform
    {
        public float3 Position;
        public float3 Rotation;
        public float3 Scale;
    }

    public struct FTreeBatch : IComparable<FTreeBatch>, IEquatable<FTreeBatch>
    {
        public int LODIndex;
        public int SubmeshIndex;
        public int MaterialIndex;
        public FBound BoundingBox;
        public FSphere BoundingSphere;
        public float4x4 Matrix_LocalToWorld;


        public bool Equals(FTreeBatch Target)
        {
            return LODIndex.Equals(Target.LODIndex) && SubmeshIndex.Equals(Target.SubmeshIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeBatch)obj);
        }

        public int CompareTo(FTreeBatch Target)
        {
            return LODIndex.CompareTo(Target.LODIndex) + SubmeshIndex.CompareTo(Target.SubmeshIndex) + MaterialIndex.CompareTo(Target.MaterialIndex);
        }

        public override int GetHashCode()
        {
            int hashCode = 1;

            return hashCode;
        }
    }
}
