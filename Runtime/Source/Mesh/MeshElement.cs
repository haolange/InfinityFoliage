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
        public int meshIndex;
        public int materialIndex;
        public int sectionIndex;
        public int batchIndex;
        public int instanceId
        {
            get
            {
                return new int3(meshIndex, materialIndex, sectionIndex).GetHashCode();
            }
        }

        public FTreeSection(in int meshIndex, in int sectionIndex, in int materialIndex, in int batchIndex)
        {
            this.meshIndex = meshIndex;
            this.sectionIndex = sectionIndex;
            this.materialIndex = materialIndex;
            this.batchIndex = batchIndex;
        }

        public bool Equals(FTreeSection target)
        {
            return meshIndex.Equals(target.meshIndex) && materialIndex.Equals(target.materialIndex) && sectionIndex.Equals(target.sectionIndex);
        }

        public override bool Equals(object target)
        {
            return Equals((FTreeSection)target);
        }

        public int CompareTo(FTreeSection target)
        {
            return instanceId.CompareTo(target.instanceId);
            //return ((meshIndex >> 16) + (lODIndex << 16 | materialIndex)).CompareTo((target.meshIndex >> 16) + (target.lODIndex << 16 | target.materialIndex));
        }

        public override int GetHashCode()
        {
            return instanceId;
            //return (meshIndex >> 16) + (lODIndex << 16 | materialIndex);
        }
    }
}
