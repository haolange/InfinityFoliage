using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FTransform
    {
        public float3 position;
        public float3 rotation;
        public float3 scale;

        public FTransform(float3 position, float3 rotation, float3 scale)
        {
            this.scale = scale;
            this.rotation = rotation;
            this.position = position;
        }
    }

    public struct FTreeSection : IComparable<FTreeSection>, IEquatable<FTreeSection>
    {
        public int elementIndex;

        public FTreeSection(in int elementIndex)
        {
            this.elementIndex = elementIndex;
        }

        public bool Equals(FTreeSection target)
        {
            return elementIndex.Equals(target.elementIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((FTreeSection)target);
        }

        public int CompareTo(FTreeSection target)
        {
            return elementIndex.CompareTo(target.elementIndex);
        }

        public override int GetHashCode()
        {
            return elementIndex.GetHashCode();
        }
    }
}
