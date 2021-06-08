using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct FTreeDrawCommand : IEquatable<FTreeDrawCommand>
    {
        public int meshIndex;
        public int sectionIndex;
        public int materialIndex;
        public int2 countOffset;


        public FTreeDrawCommand(in int meshIndex, in int sectionIndex, in int materialIndex, in int2 countOffset)
        {
            this.meshIndex = meshIndex;
            this.sectionIndex = sectionIndex;
            this.materialIndex = materialIndex;
            this.countOffset = countOffset;
        }

        public bool Equals(FTreeDrawCommand target)
        {
            return meshIndex.Equals(target.meshIndex) && materialIndex.Equals(target.materialIndex) && sectionIndex.Equals(target.sectionIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((FTreeDrawCommand)target);
        }

        public override int GetHashCode()
        {
            //return (sectionIndex >> 16) + (meshIndex << 16 | materialIndex);
            return new int4(meshIndex, materialIndex, sectionIndex, countOffset.GetHashCode()).GetHashCode();
        }
    }
}
