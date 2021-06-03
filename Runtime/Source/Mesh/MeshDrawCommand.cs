using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    public struct FMeshDrawCommand : IEquatable<FMeshDrawCommand>
    {
        public int meshIndex;
        public int sectionIndex;
        public int materialIndex;
        public int2 countOffset;


        public FMeshDrawCommand(in int meshIndex, in int sectionIndex, in int materialIndex, in int2 countOffset)
        {
            this.meshIndex = meshIndex;
            this.sectionIndex = sectionIndex;
            this.materialIndex = materialIndex;
            this.countOffset = countOffset;
        }

        public bool Equals(FMeshDrawCommand Target)
        {
            return meshIndex.Equals(Target.meshIndex) && materialIndex.Equals(Target.materialIndex) && sectionIndex.Equals(Target.sectionIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshDrawCommand)obj);
        }

        public override int GetHashCode()
        {
            //return (sectionIndex >> 16) + (meshIndex << 16 | materialIndex);
            return new int4(meshIndex, materialIndex, sectionIndex, countOffset.GetHashCode()).GetHashCode();
        }
    }
}
