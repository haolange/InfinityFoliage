using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FMeshLODInfo : IEquatable<FMeshLODInfo>
    {
        public float screenSize;
        public int[] materialSlot;

        public bool Equals(FMeshLODInfo Target)
        {
            return screenSize.Equals(Target.screenSize) && materialSlot.Equals(Target.materialSlot);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshLODInfo)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = screenSize.GetHashCode();
            hashCode += materialSlot.GetHashCode();

            return hashCode;
        }
    }

    [Serializable]
    public struct FMesh : IEquatable<FMesh>
    {
        public bool IsCreated;

        public Mesh[] meshes;

        public Material[] materials;

        public FMeshLODInfo[] lODInfo;
        

        public FMesh(Mesh[] meshes, Material[] materials, FMeshLODInfo[] lODInfo)
        {
            this.IsCreated = true;
            this.meshes = meshes;
            this.materials = materials;
            this.lODInfo = lODInfo;
        }

        public bool Equals(FMesh Target)
        {
            return IsCreated.Equals(Target.IsCreated) && meshes.Equals(Target.meshes) && lODInfo.Equals(Target.lODInfo) && materials.Equals(Target.materials);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMesh)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = IsCreated ? 0 : 1;
            hashCode += meshes.GetHashCode();
            hashCode += lODInfo.GetHashCode();
            hashCode += materials.GetHashCode();

            return hashCode;
        }
    }

    [CreateAssetMenu(menuName = "Landscape/MeshAsset", order = 256)]
    public class MeshAsset : ScriptableObject
    {
        [Header("Mesh")]
        public Mesh[] meshes;

        [Header("Material")]
        public Material[] materials;

        [Header("Culling")]
        public FMeshLODInfo[] lODInfo;

        /*[HideInInspector]
        public FMesh Tree;*/


        public MeshAsset()
        {

        }

        void Awake()
        {
            //Debug.Log("Awake");
        }

        void Reset()
        {
            //Debug.Log("Reset");
        }

        void OnEnable()
        {
            //Debug.Log("OnEnable");
        }

        void OnValidate()
        {
            //Debug.Log("OnValidate");
        }

        void OnDisable()
        {
            //Debug.Log("OnDisable");
        }

        void OnDestroy()
        {
            //Debug.Log("OnDestroy");
        }

        public void BuildMeshAsset(Mesh[] meshes, Material[] materials, FMeshLODInfo[] lODInfo)
        {
            this.meshes = meshes;
            this.materials = materials;
            this.lODInfo = lODInfo;
        }
    }
}
