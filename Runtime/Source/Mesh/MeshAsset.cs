using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FMeshLODInfo : IEquatable<FMeshLODInfo>
    {
        public float ScreenSize;
        public int[] MaterialSlot;

        public bool Equals(FMeshLODInfo Target)
        {
            return ScreenSize.Equals(Target.ScreenSize) && MaterialSlot.Equals(Target.MaterialSlot);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMeshLODInfo)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = ScreenSize.GetHashCode();
            hashCode += MaterialSlot.GetHashCode();

            return hashCode;
        }
    }

    [Serializable]
    public struct FMesh : IEquatable<FMesh>
    {
        public bool IsCreated;

        public Mesh[] Meshes;

        public Material[] Materials;

        public FMeshLODInfo[] LODInfo;
        

        public FMesh(Mesh[] Meshes, Material[] Materials, FMeshLODInfo[] LODInfo)
        {
            this.IsCreated = true;
            this.Meshes = Meshes;
            this.Materials = Materials;
            this.LODInfo = LODInfo;
        }

        public bool Equals(FMesh Target)
        {
            return IsCreated.Equals(Target.IsCreated) && Meshes.Equals(Target.Meshes) && LODInfo.Equals(Target.LODInfo) && Materials.Equals(Target.Materials);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMesh)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = IsCreated ? 0 : 1;
            hashCode += Meshes.GetHashCode();
            hashCode += LODInfo.GetHashCode();
            hashCode += Materials.GetHashCode();

            return hashCode;
        }
    }

    [CreateAssetMenu(menuName = "Landscape/MeshAsset", order = 10)]
    public class MeshAsset : ScriptableObject
    {
        [Header("Mesh")]
        public Mesh[] Meshes;

        [Header("Material")]
        public Material[] Materials;

        [Header("Culling")]
        public FMeshLODInfo[] LODInfo;

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
    }
}
