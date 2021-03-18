using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    public class FoliageActorAction
    {
        #region Tree
        [MenuItem("GameObject/EntityAction/Landscape/BuildTerrainTree", false, 9)]
        public static void BuildTerrainTree(MenuCommand menuCommand)
        {
            GameObject[] SelectObjects = Selection.gameObjects;
            foreach (GameObject SelectObject in SelectObjects)
            {
                TreeComponent foliageComponent = SelectObject.GetComponent<TreeComponent>();
                if (foliageComponent == null)
                {
                    foliageComponent = SelectObject.AddComponent<TreeComponent>();
                }

                Terrain UTerrain = SelectObject.GetComponent<Terrain>();
                TerrainData UTerrainData = UTerrain.terrainData;
                foliageComponent.treeSectors = new FTreeSector[UTerrainData.treePrototypes.Length];

                for (int TreeIndex = 0; TreeIndex < UTerrainData.treePrototypes.Length; ++TreeIndex)
                {
                    foliageComponent.treeSectors[TreeIndex] = new FTreeSector();
                    foliageComponent.treeSectors[TreeIndex].treeIndex = TreeIndex;

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
                    FMeshLODInfo[] LODInfos = new FMeshLODInfo[lods.Length];
                    for (int l = 0; l < lods.Length; ++l)
                    {
                        ref LOD lod = ref lods[l];
                        ref FMeshLODInfo LODInfo = ref LODInfos[l];
                        Renderer renderer = lod.renderers[0];

                        LODInfo.screenSize = 1 - (l * 0.125f);
                        LODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            ref int MaterialSlot = ref LODInfo.materialSlot[m];
                            MaterialSlot = Materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    foliageComponent.treeSectors[TreeIndex].tree = new FMesh(Meshes.ToArray(), Materials.ToArray(), LODInfos);
                }
            }
        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainTree", false, 11)]
        public static void UpdateTerrainTree(MenuCommand menuCommand)
        {
            var tasksHandle = new List<GCHandle>(32);
            var jobsHandle = new List<JobHandle>(32);
            var selectObjects = Selection.gameObjects;

            foreach (var selectObject in selectObjects)
            {
                var terrain = selectObject.GetComponent<Terrain>();
                var terrainData = terrain.terrainData;

                var foliageComponent = selectObject.GetComponent<TreeComponent>();

                if (foliageComponent.treeSectors.Length != 0)
                {
                    for (var i = 0; i < foliageComponent.treeSectors.Length; ++i)
                    {
                        ref var treeSector = ref foliageComponent.treeSectors[i];
                        treeSector.transforms = new List<FTransform>(512);
                        var treePrototype = terrainData.treePrototypes[treeSector.treeIndex];

                        //Build Transforms
                        var updateTreeTask = new FUpdateTreeTask();
                        {
                            updateTreeTask.length = terrainData.treeInstanceCount;
                            updateTreeTask.scale = new float2(terrainData.heightmapResolution - 1, terrainData.heightmapScale.y);
                            updateTreeTask.treePrototype = treePrototype;
                            updateTreeTask.treeInstances = terrainData.treeInstances;
                            updateTreeTask.treePrototypes = terrainData.treePrototypes;
                            updateTreeTask.treeTransfroms = treeSector.transforms;
                        }
                        var taskHandle = GCHandle.Alloc(updateTreeTask);
                        tasksHandle.Add(taskHandle);

                        var updateTreeJob = new FUpdateTreeJob();
                        {
                            updateTreeJob.taskHandle = taskHandle;
                        }
                        jobsHandle.Add(updateTreeJob.Schedule());
                    }
                }
            }

            for (var j = 0; j < jobsHandle.Count; ++j)
            {
                jobsHandle[j].Complete();
                tasksHandle[j].Free();
            }
        }
        #endregion //Tree


        #region Grass
        [MenuItem("GameObject/EntityAction/Landscape/BuildTerrainGrass", false, 10)]
        public static void BuildTerrainGrass(MenuCommand menuCommand)
        {

        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainGrass", false, 12)]
        public static void UpdateTerrainGrass(MenuCommand menuCommand)
        {

        }
        #endregion //Grass
    }
}
