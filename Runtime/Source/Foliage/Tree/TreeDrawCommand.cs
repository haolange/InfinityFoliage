using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct FTreeDrawCommand : IEquatable<FTreeDrawCommand>
    {
        public int lODIndex;
        public int matIndex;
        public int meshIndex;
        public int2 countOffset;
        //public int InstanceGroupID;


        public FTreeDrawCommand(in int lODIndex, in int matIndex, in int meshIndex, in int2 countOffset)
        {
            this.lODIndex = lODIndex;
            this.matIndex = matIndex;
            this.meshIndex = meshIndex;
            this.countOffset = countOffset;
            //this.InstanceGroupID = 0;
        }

        public bool Equals(FTreeDrawCommand Target)
        {
            //return InstanceGroupID.Equals(Target.InstanceGroupID);
            return lODIndex.Equals(Target.lODIndex) && matIndex.Equals(Target.matIndex) && meshIndex.Equals(Target.meshIndex);
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
            return (meshIndex >> 16) + (lODIndex << 16 | matIndex);
        }
    }
}
