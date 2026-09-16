using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct MeshLodInfo : IEquatable<MeshLodInfo>
    {
        public float screenSize;
        public int[] materialSlot;

        public bool Equals(MeshLodInfo target)
        {
            return screenSize.Equals(target.screenSize) && materialSlot.Equals(target.materialSlot);
        }

        public override bool Equals(object target)
        {
            return Equals((MeshLodInfo)target);
        }

        public override int GetHashCode()
        {
            return screenSize.GetHashCode() + (materialSlot != null ? materialSlot.GetHashCode() : 0);
        }
    }

    [Serializable]
    public struct FoliageMesh : IEquatable<FoliageMesh>
    {
        public bool isCreated;
        public float numLOD;
        public Bounds boundBox;
        public int[] numSections;
        public Mesh[] meshes;
        public Material[] materials;
        public MeshLodInfo[] lODInfos;

        public FoliageMesh(Mesh[] meshes, Material[] materials, MeshLodInfo[] lODInfos)
        {
            this.isCreated = true;
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

        public bool Equals(FoliageMesh target)
        {
            return isCreated.Equals(target.isCreated) && meshes.Equals(target.meshes) && lODInfos.Equals(target.lODInfos) && materials.Equals(target.materials);
        }

        public override bool Equals(object target)
        {
            return Equals((FoliageMesh)target);
        }

        public override int GetHashCode()
        {
            return new float4(isCreated ? 0 : 1, meshes.GetHashCode(), lODInfos.GetHashCode(), materials.GetHashCode()).GetHashCode();
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
        public MeshLodInfo[] lODInfos;

        [Header("Proxy")]
        [HideInInspector]
        public FoliageMesh tree;

#if UNITY_EDITOR
        void BuildMeshAsset(Mesh[] buildMeshes, Material[] buildMaterials, MeshLodInfo[] buildLodInfos)
        {
            this.meshes = buildMeshes;
            this.materials = buildMaterials;
            this.lODInfos = buildLodInfos;
            this.tree = new FoliageMesh(buildMeshes, buildMaterials, buildLodInfos);
        }

        internal static void BuildMeshAssetFromLODGroup(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshList = new List<Mesh>();
            List<Material> materialList = new List<Material>();
            LOD[] lods = cloneTarget.GetComponent<LODGroup>().GetLODs();

            for (int j = 0; j < lods.Length; ++j)
            {
                ref LOD lod = ref lods[j];
                Renderer renderer = lod.renderers[0];
                MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();

                meshList.AddUnique(meshFilter.sharedMesh);
                for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
                {
                    materialList.AddUnique(renderer.sharedMaterials[k]);
                }
            }

            MeshLodInfo[] lodInfos = new MeshLodInfo[lods.Length];
            for (int l = 0; l < lods.Length; ++l)
            {
                ref LOD lod = ref lods[l];
                ref MeshLodInfo lodInfo = ref lodInfos[l];
                Renderer renderer = lod.renderers[0];

                lodInfo.screenSize = 1 - (l * 0.125f);
                lodInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                {
                    lodInfo.materialSlot[m] = materialList.IndexOf(renderer.sharedMaterials[m]);
                }
            }

            meshAsset.BuildMeshAsset(meshList.ToArray(), materialList.ToArray(), lodInfos);
            EditorUtility.SetDirty(meshAsset);
        }

        internal static void BuildMeshAssetFromMeshRenderer(GameObject cloneTarget, MeshAsset meshAsset)
        {
            List<Mesh> meshList = new List<Mesh>();
            List<Material> materialList = new List<Material>();

            Renderer renderer = cloneTarget.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = cloneTarget.GetComponent<MeshFilter>();

            meshList.AddUnique(meshFilter.sharedMesh);
            for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
            {
                materialList.AddUnique(renderer.sharedMaterials[k]);
            }

            MeshLodInfo[] lodInfos = new MeshLodInfo[1];
            lodInfos[0].screenSize = 1;
            lodInfos[0].materialSlot = new int[renderer.sharedMaterials.Length];
            for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
            {
                lodInfos[0].materialSlot[m] = materialList.IndexOf(renderer.sharedMaterials[m]);
            }

            meshAsset.BuildMeshAsset(meshList.ToArray(), materialList.ToArray(), lodInfos);
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

            if (cloneTarget.GetComponent<LODGroup>() != null)
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
