using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FTreeLODInfo : IEquatable<FTreeLODInfo>
    {
        public float ScreenSize;
        public int[] MaterialSlot;

        public bool Equals(FTreeLODInfo Target)
        {
            return ScreenSize.Equals(Target.ScreenSize) && MaterialSlot.Equals(Target.MaterialSlot);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTreeLODInfo)obj);
        }

        public override int GetHashCode()
        {
            int hashCode = ScreenSize.GetHashCode();
            hashCode += MaterialSlot.GetHashCode();

            return hashCode;
        }
    }

    [Serializable]
    public struct FTree : IEquatable<FTree>
    {
        public bool IsCreated;

        public Mesh[] Meshes;

        public Material[] Materials;

        public FTreeLODInfo[] LODInfo;



        /*public FTree()
        {
            this.IsCreated = true;
            this.Meshes = null;
            this.LODInfo = null;
            this.Materials = null;
        }*/

        public FTree(Mesh[] Meshes, Material[] Materials, FTreeLODInfo[] LODInfo)
        {
            this.IsCreated = true;
            this.Meshes = Meshes;
            this.Materials = Materials;
            this.LODInfo = LODInfo;
        }

        public bool Equals(FTree Target)
        {
            return IsCreated.Equals(Target.IsCreated) && Meshes.Equals(Target.Meshes) && LODInfo.Equals(Target.LODInfo) && Materials.Equals(Target.Materials);
        }

        public override bool Equals(object obj)
        {
            return Equals((FTree)obj);
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

    [CreateAssetMenu(menuName = "Landscape/TreeAsset")]
    public class TreeAsset : ScriptableObject
    {
        [Header("Mesh")]
        public Mesh[] Meshes;

        [Header("Material")]
        public Material[] Materials;

        [Header("Culling")]
        public FTreeLODInfo[] LODInfo;

        [HideInInspector]
        public FTree Tree;


        public TreeAsset()
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
            BuildTree();
        }

        void OnValidate()
        {
            //Debug.Log("OnValidate");
            BuildTree();
        }

        void OnDisable()
        {
            //Debug.Log("OnDisable");
        }

        void OnDestroy()
        {
            //Debug.Log("OnDestroy");
        }

        public void BuildTree()
        {
            Tree = new FTree(Meshes, Materials, LODInfo);
        }
    }
}
