using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    interface ITask
    {
        void Execute();
    }

    public struct FUpdateTreeTask : ITask
    {
        public int length;
        public float2 Scale;
        public TreePrototype TreePrototype;
        public TreeInstance[] TreeInstances;
        public TreePrototype[] TreePrototypes;
        public List<FTransform> TreeTransfroms;


        public void Execute()
        {
            FTransform Transform = new FTransform();

            for (int i = 0; i < length; ++i)
            {
                ref TreeInstance treeInstance = ref TreeInstances[i];
                TreePrototype serchTreePrototype = TreePrototypes[treeInstance.prototypeIndex];
                if (serchTreePrototype.Equals(TreePrototype))
                {
                    Transform.Rotation = new float3(0, treeInstance.rotation, 0);
                    Transform.Position = treeInstance.position * new float3(Scale.x, Scale.y, Scale.x);
                    Transform.Scale = new float3(treeInstance.widthScale, treeInstance.heightScale, treeInstance.widthScale);
                    TreeTransfroms.Add(Transform);
                }
            }
        }
    }

    public struct FUpdateTreeJob : IJob
    {
        public GCHandle TaskHandle;

        public void Execute()
        {
            ITask Task = (ITask)TaskHandle.Target;
            Task.Execute();
        }
    }

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


        [MenuItem("GameObject/ActorAction/Landscape/BuildTreesForTerrain", false, 10)]
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

                for (int TreeIndex = 0; TreeIndex < UTerrainData.treePrototypes.Length; ++TreeIndex)
                {
                    FoliageComponent foliageComponent = SelectObject.AddComponent<FoliageComponent>();
                    foliageComponent.TreeIndex = TreeIndex;

                    TreePrototype treePrototype = UTerrainData.treePrototypes[TreeIndex];
                    List<Mesh> Meshes = new List<Mesh>();
                    List<Material> Materials = new List<Material>();

                    GameObject treePrefab = treePrototype.prefab;
                    LODGroup lodGroup = treePrefab.GetComponent<LODGroup>();
                    LOD[] lods = lodGroup.GetLODs();

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

                        LODInfo.MaterialSlot = new int[renderer.sharedMaterials.Length];
                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            ref int MaterialSlot = ref LODInfo.MaterialSlot[m];
                            MaterialSlot = Materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    foliageComponent.Tree = new FTree(Meshes.ToArray(), Materials.ToArray(), LODInfos);


                    //Build InstancesTransfrom
                    /*List<TreeInstance> treeInstances = new List<TreeInstance>(256);
                    for (int n = 0; n < UTerrainData.treeInstanceCount; ++n)
                    {
                        ref TreeInstance treeInstance = ref UTerrainData.treeInstances[n];
                        TreePrototype serchTreePrototype = UTerrainData.treePrototypes[treeInstance.prototypeIndex];
                        if (serchTreePrototype.Equals(treePrototype))
                        {
                            treeInstances.Add(treeInstance);
                        }
                    }

                    FTransform Transform = new FTransform();
                    foliageComponent.InstancesTransfrom = new List<FTransform>(treeInstances.Count);
                    for (int o = 0; o < treeInstances.Count; ++o)
                    {
                        Transform.Scale = new float3(treeInstances[o].widthScale, treeInstances[o].heightScale, treeInstances[o].widthScale);
                        Transform.Rotation = new float3(0, treeInstances[o].rotation, 0);
                        Transform.Position = treeInstances[o].position * new float3(UTerrainData.heightmapResolution - 1, UTerrainData.heightmapScale.y, UTerrainData.heightmapResolution - 1);
                        foliageComponent.InstancesTransfrom.Add(Transform);
                    }*/
                    Undo.RegisterCreatedObjectUndo(foliageComponent, "BuildFoliage");
                }
            }
        }


        /*[MenuItem("GameObject/Tool/Landscape/UpdateTreesForTerrain", false, 11)]
        public static void UpdateTreeFromTerrainData(MenuCommand menuCommand)
        {
            GameObject[] SelectObjects = Selection.gameObjects;
            foreach (GameObject SelectObject in SelectObjects)
            {
                Terrain UTerrain = SelectObject.GetComponent<Terrain>();
                TerrainData UTerrainData = UTerrain.terrainData;

                FoliageComponent[] foliageComponents = SelectObject.GetComponents<FoliageComponent>();
                if (foliageComponents.Length != 0)
                {
                    for (int i = 0; i < foliageComponents.Length; ++i)
                    {
                        FoliageComponent foliageComponent = foliageComponents[i];
                        TreePrototype treePrototype = UTerrainData.treePrototypes[foliageComponent.TreeIndex];

                        //Build InstancesTransfrom
                        FTransform Transform = new FTransform();
                        foliageComponent.InstancesTransfrom = new List<FTransform>(512);

                        for (int j = 0; j < UTerrainData.treeInstanceCount; ++j)
                        {
                            ref TreeInstance treeInstance = ref UTerrainData.treeInstances[j];
                            TreePrototype serchTreePrototype = UTerrainData.treePrototypes[treeInstance.prototypeIndex];
                            if (serchTreePrototype.Equals(treePrototype))
                            {
                                Transform.Scale = new float3(treeInstance.widthScale, treeInstance.heightScale, treeInstance.widthScale);
                                Transform.Rotation = new float3(0, treeInstance.rotation, 0);
                                Transform.Position = treeInstance.position * new float3(UTerrainData.heightmapResolution - 1, UTerrainData.heightmapScale.y, UTerrainData.heightmapResolution - 1);
                                foliageComponent.InstancesTransfrom.Add(Transform);
                            }
                        }
                        Undo.RegisterCreatedObjectUndo(foliageComponent, "BuildFoliage");
                    }
                }
            }
        }*/


        [MenuItem("GameObject/ActorAction/Landscape/UpdateTreesForTerrain", false, 11)]
        public static void UpdateTreeFromTerrainDataParallel(MenuCommand menuCommand)
        {
            GCHandle TaskHandle;
            List<GCHandle> TasksHandle = new List<GCHandle>(32);
            List<JobHandle> JobsHandle = new List<JobHandle>(32);
            GameObject[] SelectObjects = Selection.gameObjects;

            foreach (GameObject SelectObject in SelectObjects)
            {
                Terrain UTerrain = SelectObject.GetComponent<Terrain>();
                TerrainData UTerrainData = UTerrain.terrainData;

                FoliageComponent[] foliageComponents = SelectObject.GetComponents<FoliageComponent>();
                if (foliageComponents.Length != 0)
                {
                    for (int i = 0; i < foliageComponents.Length; ++i)
                    {
                        FoliageComponent foliageComponent = foliageComponents[i];
                        foliageComponent.InstancesTransfrom = new List<FTransform>(512);
                        TreePrototype treePrototype = UTerrainData.treePrototypes[foliageComponent.TreeIndex];

                        //Build InstancesTransfrom
                        FUpdateTreeTask UpdateTreeTask = new FUpdateTreeTask();
                        {
                            UpdateTreeTask.length = UTerrainData.treeInstanceCount;
                            UpdateTreeTask.Scale = new float2(UTerrainData.heightmapResolution - 1, UTerrainData.heightmapScale.y);
                            UpdateTreeTask.TreePrototype = treePrototype;
                            UpdateTreeTask.TreeInstances = UTerrainData.treeInstances;
                            UpdateTreeTask.TreePrototypes = UTerrainData.treePrototypes;
                            UpdateTreeTask.TreeTransfroms = foliageComponent.InstancesTransfrom;
                        }
                        TaskHandle = GCHandle.Alloc(UpdateTreeTask);
                        TasksHandle.Add(TaskHandle);

                        FUpdateTreeJob UpdateTreeJob = new FUpdateTreeJob();
                        {
                            UpdateTreeJob.TaskHandle = TaskHandle;
                        }
                        JobsHandle.Add(UpdateTreeJob.Schedule());
                    }
                }
            }

            for (int j = 0; j < JobsHandle.Count; ++j)
            {
                JobsHandle[j].Complete();
                TasksHandle[j].Free();
            }
        }


        [MenuItem("GameObject/ActorAction/Landscape/RemoveTreeFormTerrain", false, 12)]
        public static void RemoveTreeForTerrain(MenuCommand menuCommand)
        {
            GameObject[] SelectObjects = Selection.gameObjects;
            foreach (GameObject SelectObject in SelectObjects)
            {
                FoliageComponent[] foliageComponents = SelectObject.GetComponents<FoliageComponent>();
                if (foliageComponents.Length != 0)
                {
                    for (int i = 0; i < foliageComponents.Length; ++i)
                    {
                        Object.DestroyImmediate(foliageComponents[i]);
                        Undo.RegisterCreatedObjectUndo(SelectObject, "RemoveTreeComponent");
                    }
                }
            }
        }

    }
}
