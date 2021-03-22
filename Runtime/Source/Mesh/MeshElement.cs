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

    public struct FMeshElement : IComparable<FMeshElement>, IEquatable<FMeshElement>
    {
        public int lODIndex;
        public int matIndex;
        public int meshIndex;
        public int batchIndex;
        //public int InstanceGroupID;


        public FMeshElement(in int lODIndex, in int matIndex, in int meshIndex, in int batchIndex, in int instanceGroupID)
        {
            this.lODIndex = lODIndex;
            this.matIndex = matIndex;
            this.meshIndex = meshIndex;
            this.batchIndex = batchIndex;
            //this.InstanceGroupID = InstanceGroupID;
        }

        public bool Equals(FMeshElement Target)
        {
            //return InstanceGroupID.Equals(Target.InstanceGroupID);
            return lODIndex.Equals(Target.lODIndex) && matIndex.Equals(Target.matIndex) && meshIndex.Equals(Target.meshIndex);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshElement)obj);
        }

        public int CompareTo(FMeshElement Target)
        {
            //return InstanceGroupID;
            //return (MeshIndex + LODIndex + MatIndex).CompareTo(Target.MeshIndex + Target.LODIndex + Target.MatIndex);
            return ((meshIndex >> 16) + (lODIndex << 16 | matIndex)).CompareTo((Target.meshIndex >> 16) + (Target.lODIndex << 16 | Target.matIndex));
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
