using System;
using Unity.Mathematics;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FTransform
    {
        public float3 Position;
        public float3 Rotation;
        public float3 Scale;


        public FTransform(float3 Position, float3 Rotation, float3 Scale)
        {
            this.Scale = Scale;
            this.Rotation = Rotation;
            this.Position = Position;
        }
    }

    public struct FTreeElement : IComparable<FTreeElement>, IEquatable<FTreeElement>
    {
        public int LODIndex;
        public int MatIndex;
        public int MeshIndex;
        public FBound BoundBox;
        public FSphere BoundSphere;
        public float4x4 Matrix_LocalToWorld;


        public bool Equals(FTreeElement Target)
        {
            return LODIndex.Equals(Target.LODIndex) && MatIndex.Equals(Target.MatIndex) && MeshIndex.Equals(Target.MeshIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeElement)obj);
        }

        public int CompareTo(FTreeElement Target)
        {
            return LODIndex.CompareTo(Target.LODIndex) + MatIndex.CompareTo(Target.MatIndex) + MeshIndex.CompareTo(Target.MeshIndex);
        }

        public override int GetHashCode()
        {
            int hashCode = 1;
            hashCode += LODIndex;
            hashCode += MatIndex;
            hashCode += MeshIndex;
            hashCode += BoundBox.GetHashCode();
            hashCode += BoundSphere.GetHashCode();
            hashCode += Matrix_LocalToWorld.GetHashCode();

            return hashCode;
        }
    }
}
