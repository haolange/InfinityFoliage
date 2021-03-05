using System;
using Unity.Mathematics;

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
        public int BatchIndex;
        //public int InstanceGroupID;


        public FTreeElement(in int LODIndex, in int MatIndex, in int MeshIndex, in int BatchIndex, in int InstanceGroupID)
        {
            this.LODIndex = LODIndex;
            this.MatIndex = MatIndex;
            this.MeshIndex = MeshIndex;
            this.BatchIndex = BatchIndex;
            //this.InstanceGroupID = InstanceGroupID;
        }

        public bool Equals(FTreeElement Target)
        {
            //return InstanceGroupID.Equals(Target.InstanceGroupID);
            return LODIndex.Equals(Target.LODIndex) && MatIndex.Equals(Target.MatIndex) && MeshIndex.Equals(Target.MeshIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeElement)obj);
        }

        public int CompareTo(FTreeElement Target)
        {
            //return InstanceGroupID;
            //return (MeshIndex + LODIndex + MatIndex).CompareTo(Target.MeshIndex + Target.LODIndex + Target.MatIndex);
            return ((MeshIndex >> 16) + (LODIndex << 16 | MatIndex)).CompareTo((Target.MeshIndex >> 16) + (Target.LODIndex << 16 | Target.MatIndex));
        }

        public override int GetHashCode()
        {
            /*int hashCode = LODIndex;
            hashCode += MatIndex;
            hashCode += MeshIndex;

            return hashCode;*/
            //return InstanceGroupID;
            return (MeshIndex >> 16) + (LODIndex << 16 | MatIndex);
        }
    }
}
