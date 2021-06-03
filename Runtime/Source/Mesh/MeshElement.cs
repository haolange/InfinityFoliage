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
        public int lODIndex;
        public int matIndex;
        public int meshIndex;
        public int batchIndex;
        public int instanceId
        {
            get
            {
                return new int3(lODIndex, matIndex, meshIndex).GetHashCode();
            }
        }

        public FTreeSection(in int lODIndex, in int matIndex, in int meshIndex, in int batchIndex)
        {
            this.lODIndex = lODIndex;
            this.matIndex = matIndex;
            this.meshIndex = meshIndex;
            this.batchIndex = batchIndex;
        }

        public bool Equals(FTreeSection target)
        {
            return lODIndex.Equals(target.lODIndex) && matIndex.Equals(target.matIndex) && meshIndex.Equals(target.meshIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((FTreeSection)target);
        }

        public int CompareTo(FTreeSection target)
        {
            return instanceId.CompareTo(target.instanceId);
            //return ((meshIndex >> 16) + (lODIndex << 16 | matIndex)).CompareTo((target.meshIndex >> 16) + (target.lODIndex << 16 | target.matIndex));
        }

        public override int GetHashCode()
        {
            return instanceId;
            //return (meshIndex >> 16) + (lODIndex << 16 | matIndex);
        }
    }
}
