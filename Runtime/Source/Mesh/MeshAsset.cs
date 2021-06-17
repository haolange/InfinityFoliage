using System;
using UnityEditor;
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
        public float numLOD;
        public Bounds boundBox;
        public int[] numSections;
        public Mesh[] meshes;
        public Material[] materials;
        public FMeshLODInfo[] lODInfos;

        public FMesh(Mesh[] meshes, Material[] materials, FMeshLODInfo[] lODInfos)
        {
            this.IsCreated = true;
            this.meshes = meshes;
            this.materials = materials;

            this.lODInfos = lODInfos;
            this.numLOD = meshes.Length;
            this.boundBox = meshes[0].bounds;
            this.numSections = new int[meshes.Length];

            for (int i = 0; i < numSections.Length; ++i)
            {
                this.numSections[i] = meshes[i].subMeshCount;
            }
        }

        public bool Equals(FMesh Target)
        {
            return IsCreated.Equals(Target.IsCreated) && meshes.Equals(Target.meshes) && lODInfos.Equals(Target.lODInfos) && materials.Equals(Target.materials);
        }

        public override bool Equals(object obj)
        {
            return Equals((FMesh)obj);
        }

        public override int GetHashCode()
        {
            return new float4(IsCreated ? 0 : 1, meshes.GetHashCode(), lODInfos.GetHashCode(), materials.GetHashCode()).GetHashCode();
        }
    }

    [CreateAssetMenu(menuName = "Landscape/MeshAsset", order = 256)]
    public class MeshAsset : ScriptableObject
    {
#if UNITY_EDITOR
        [Header("Target")]
        [HideInInspector]
        public GameObject target;
#endif

        [Header("Mesh")]
        public Mesh[] meshes;

        [Header("Material")]
        public Material[] materials;

        [Header("Culling")]
        public FMeshLODInfo[] lODInfos;

        [Header("Proxy")]
        [HideInInspector]
        public FMesh tree;


        public MeshAsset()
        {

        }

        void Awake()
        {
            //Debug.Log("Awake");
            //BuildMeshProxy();
        }

        void Reset()
        {
            //Debug.Log("Reset");
            //BuildMeshProxy();
        }

        void OnEnable()
        {
            //Debug.Log("OnEnable");
            //BuildMeshProxy();
        }

        void OnValidate()
        {
            //Debug.Log("OnValidate");
            //BuildMeshProxy();
        }

        void OnDisable()
        {
            //Debug.Log("OnDisable");
        }

        void OnDestroy()
        {
            //Debug.Log("OnDestroy");
        }

#if UNITY_EDITOR
        void BuildMeshAsset(Mesh[] meshes, Material[] materials, FMeshLODInfo[] lODInfos)
        {
            this.meshes = meshes;
            this.materials = materials;
            this.lODInfos = lODInfos;
            this.tree = new FMesh(meshes, materials, lODInfos);
        }

        internal static void BuildMeshAssetFromLODGroup(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();
            LOD[] lods = cloneTarget.GetComponent<LODGroup>().GetLODs();

            //Collector Meshes&Materials
            for (int j = 0; j < lods.Length; ++j)
            {
                ref LOD lod = ref lods[j];
                Renderer renderer = lod.renderers[0];
                MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();

                meshes.AddUnique(meshFilter.sharedMesh);
                for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
                {
                    materials.AddUnique(renderer.sharedMaterials[k]);
                }
            }

            //Build LODInfo
            FMeshLODInfo[] lODInfos = new FMeshLODInfo[lods.Length];
            for (int l = 0; l < lods.Length; ++l)
            {
                ref LOD lod = ref lods[l];
                ref FMeshLODInfo lODInfo = ref lODInfos[l];
                Renderer renderer = lod.renderers[0];

                lODInfo.screenSize = 1 - (l * 0.125f);
                lODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                {
                    ref int MaterialSlot = ref lODInfo.materialSlot[m];
                    MaterialSlot = materials.IndexOf(renderer.sharedMaterials[m]);
                }
            }

            meshAsset.BuildMeshAsset(meshes.ToArray(), materials.ToArray(), lODInfos);
            EditorUtility.SetDirty(meshAsset);
        }

        internal static void BuildMeshAssetFromMeshRenderer(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();

            //Collector Meshes&Materials
            Renderer renderer = cloneTarget.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = cloneTarget.GetComponent<MeshFilter>();

            meshes.AddUnique(meshFilter.sharedMesh);
            for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
            {
                materials.AddUnique(renderer.sharedMaterials[k]);
            }

            //Build LODInfo
            FMeshLODInfo[] lODInfos = new FMeshLODInfo[1];

            ref FMeshLODInfo lODInfo = ref lODInfos[0];
            lODInfo.screenSize = 1;
            lODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

            for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
            {
                ref int MaterialSlot = ref lODInfo.materialSlot[m];
                MaterialSlot = materials.IndexOf(renderer.sharedMaterials[m]);
            }

            meshAsset.BuildMeshAsset(meshes.ToArray(), materials.ToArray(), lODInfos);
            EditorUtility.SetDirty(meshAsset);
        }

        public static void BuildMeshAsset(GameObject cloneTarget, MeshAsset meshAsset)
        {
            if (cloneTarget == null)
            {
                Debug.LogWarning("source prefab is null");
                return;
            }

            bool buildOK = false;

            if(cloneTarget.GetComponent<LODGroup>() != null)
            {
                buildOK = true;
                meshAsset.target = cloneTarget;
                BuildMeshAssetFromLODGroup(cloneTarget, meshAsset);
            }

            if (cloneTarget.GetComponent<MeshFilter>() != null && cloneTarget.GetComponent<MeshRenderer>() != null)
            {
                buildOK = true;
                meshAsset.target = cloneTarget;
                BuildMeshAssetFromMeshRenderer(cloneTarget, meshAsset);
            }

            if (!buildOK) { Debug.LogWarning("source prefab doesn't have LODGroup or MeshRenderer"); }
        }
#endif
    }
}
