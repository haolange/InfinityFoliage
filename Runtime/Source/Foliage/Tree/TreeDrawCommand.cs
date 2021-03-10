using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct FTreeDrawCommand : IEquatable<FTreeDrawCommand>
    {
        public int LODIndex;
        public int MatIndex;
        public int MeshIndex;
        public int2 CountOffset;
        //public int InstanceGroupID;


        public FTreeDrawCommand(in int LODIndex, in int MatIndex, in int MeshIndex, in int2 CountOffset)
        {
            this.LODIndex = LODIndex;
            this.MatIndex = MatIndex;
            this.MeshIndex = MeshIndex;
            this.CountOffset = CountOffset;
            //this.InstanceGroupID = 0;
        }

        public bool Equals(FTreeDrawCommand Target)
        {
            //return InstanceGroupID.Equals(Target.InstanceGroupID);
            return LODIndex.Equals(Target.LODIndex) && MatIndex.Equals(Target.MatIndex) && MeshIndex.Equals(Target.MeshIndex) && CountOffset.Equals(Target.CountOffset);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeDrawCommand)obj);
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
