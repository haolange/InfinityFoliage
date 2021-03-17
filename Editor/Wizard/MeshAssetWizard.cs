using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    public class MeshAssetWizard : ScriptableWizard
    {
        private MeshAsset meshAsset;

        public GameObject buildTarget;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            BuildMeshAsset();
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {
            
        }

        public void SetMeshAsset(MeshAsset meshAsset)
        {
            this.meshAsset = meshAsset;
        }

        private void BuildMeshAsset()
        {
            if(buildTarget == null) 
            {
                Debug.LogWarning("source prefab is null");
                return; 
            }

            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();
            LOD[] lods = buildTarget.GetComponent<LODGroup>().GetLODs();

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
    }
}
