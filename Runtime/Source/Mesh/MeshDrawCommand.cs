using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct FMeshDrawCommand : IEquatable<FMeshDrawCommand>
    {
        public int lODIndex;
        public int matIndex;
        public int meshIndex;
        public int2 countOffset;


        public FMeshDrawCommand(in int lODIndex, in int matIndex, in int meshIndex, in int2 countOffset)
        {
            this.lODIndex = lODIndex;
            this.matIndex = matIndex;
            this.meshIndex = meshIndex;
            this.countOffset = countOffset;
        }

        public bool Equals(FMeshDrawCommand Target)
        {
            return lODIndex.Equals(Target.lODIndex) && matIndex.Equals(Target.matIndex) && meshIndex.Equals(Target.meshIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshDrawCommand)obj);
        }

        public override int GetHashCode()
        {
            //return (meshIndex >> 16) + (lODIndex << 16 | matIndex);
            return new int4(lODIndex, matIndex, meshIndex, countOffset.GetHashCode()).GetHashCode();
        }
    }
}
