using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;

namespace Landscape.Editor.FoliagePipeline
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(FoliageComponent))]
    public class FoliageComponentEditor : UnityEditor.Editor
    {
        FoliageComponent Foliage { get { return target as FoliageComponent; } }


        void OnEnable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += PreSave;
        }

        void OnValidate()
        {

        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            serializedObject.ApplyModifiedProperties();
        }

        void OnDisable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= PreSave;
        }

        void PreSave(UnityEngine.SceneManagement.Scene InScene, string InPath)
        {
            if (Foliage.gameObject.activeSelf == false) { return; }
            if (Foliage.enabled == false) { return; }

            Foliage.Serialize();
        }

        /*[MenuItem("GameObject/Tool/Landscape/Tree/InstancesFromTerrain", false, 10)]
        public static void GetTreeInstancesFromTerrain(MenuCommand menuCommand)
        {
            GameObject LandscapeActor = menuCommand.context as GameObject;
            Terrain UTerrain = LandscapeActor.GetComponent<Terrain>();
            TerrainData UTerrainData = UTerrain.terrainData;
            FoliageComponent Foliage = LandscapeActor.GetComponent<FoliageComponent>();

            Foliage.InstancesTransfrom = new FTransform[UTerrainData.treeInstanceCount];

            for (int i = 0; i < UTerrainData.treeInstanceCount; ++i)
            {
                Foliage.InstancesTransfrom[i].Scale = new float3(UTerrainData.treeInstances[i].widthScale, UTerrainData.treeInstances[i].heightScale, UTerrainData.treeInstances[i].widthScale);
                Foliage.InstancesTransfrom[i].Rotation = new float3(0, UTerrainData.treeInstances[i].rotation, 0);
                Foliage.InstancesTransfrom[i].Position = UTerrainData.treeInstances[i].position * (UTerrainData.heightmapResolution - 1);
            }
            Undo.RegisterCreatedObjectUndo(LandscapeActor, "BuildTree");
        }*/

        [MenuItem("GameObject/Tool/Landscape/TreesFromTerrain", false, 10)]
        public static void BuildTreeFromTerrainData(MenuCommand menuCommand)
        {
            GameObject[] SelectObjects = Selection.gameObjects;
            foreach (GameObject SelectObject in SelectObjects)
            {
                FoliageComponent[] foliageComponents = SelectObject.GetComponents<FoliageComponent>();
                if(foliageComponents.Length != 0)
                {
                    for (int i = 0; i < foliageComponents.Length; ++i)
                    {
                        Object.DestroyImmediate(foliageComponents[i]);
                    }
                }


                Terrain UTerrain = SelectObject.GetComponent<Terrain>();
                TerrainData UTerrainData = UTerrain.terrainData;

                foreach (TreePrototype treePrototype in UTerrainData.treePrototypes)
                {
                    List<Mesh> Meshes = new List<Mesh>();
                    List<Material> Materials = new List<Material>();

                    GameObject treePrefab = treePrototype.prefab;
                    LODGroup lodGroup = treePrefab.GetComponent<LODGroup>();
                    LOD[] lods = lodGroup.GetLODs();

                    FoliageComponent foliageComponent = SelectObject.AddComponent<FoliageComponent>();

                    //Collector Meshes&Materials
                    for (int j = 0; j < lods.Length; ++j)
                    {
                        ref LOD lod = ref lods[j];
                        Renderer renderer = lod.renderers[0];
                        MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();

                        Meshes.AddUnique(meshFilter.sharedMesh);
                        for (int k = 0; k < renderer.sharedMaterials.Length; ++k)
                        {
                            Materials.AddUnique(renderer.sharedMaterials[k]);
                        }
                    }

                    //Build LODInfo
                    FTreeLODInfo[] LODInfos = new FTreeLODInfo[lods.Length];
                    for (int l = 0; l < lods.Length; ++l)
                    {
                        ref LOD lod = ref lods[l];
                        ref FTreeLODInfo LODInfo = ref LODInfos[l];
                        Renderer renderer = lod.renderers[0];
                        MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();

                        LODInfo.MaterialSlot = new int[renderer.sharedMaterials.Length];
                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            ref int MaterialSlot = ref LODInfo.MaterialSlot[m];
                            MaterialSlot = Materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    foliageComponent.Tree = new FTree(Meshes.ToArray(), Materials.ToArray(), LODInfos);


                    //Build InstancesTransfrom
                    List<TreeInstance> treeInstances = new List<TreeInstance>(256);
                    for (int n = 0; n < UTerrainData.treeInstanceCount; ++n)
                    {
                        ref TreeInstance treeInstance = ref UTerrainData.treeInstances[n];
                        TreePrototype serchTreePrototype = UTerrainData.treePrototypes[treeInstance.prototypeIndex];
                        if (serchTreePrototype.Equals(treePrototype))
                        {
                            treeInstances.Add(treeInstance);
                        }
                    }

                    foliageComponent.InstancesTransfrom = new FTransform[treeInstances.Count];
                    for (int o = 0; o < treeInstances.Count; ++o)
                    {
                        foliageComponent.InstancesTransfrom[o].Scale = new float3(treeInstances[o].widthScale, treeInstances[o].heightScale, treeInstances[o].widthScale);
                        foliageComponent.InstancesTransfrom[o].Rotation = new float3(0, treeInstances[o].rotation, 0);
                        foliageComponent.InstancesTransfrom[o].Position = treeInstances[o].position * (UTerrainData.heightmapResolution - 1);
                    }
                    Undo.RegisterCreatedObjectUndo(foliageComponent, "BuildFoliage");
                }
            }
        }
    }
}
