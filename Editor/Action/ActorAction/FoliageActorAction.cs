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
                Terrain UTerrain = SelectObject.GetComponent<Terrain>();
                if (!UTerrain)
                {
                    Debug.LogWarning("select GameObject doesn't have terrain component");
                    continue;
                }

                TreeComponent treeComponent = SelectObject.GetComponent<TreeComponent>();
                if (treeComponent == null)
                {
                    treeComponent = SelectObject.AddComponent<TreeComponent>();
                }

                TerrainData UTerrainData = UTerrain.terrainData;
                treeComponent.treeSectors = new FTreeSector[UTerrainData.treePrototypes.Length];

                for (int TreeIndex = 0; TreeIndex < UTerrainData.treePrototypes.Length; ++TreeIndex)
                {
                    treeComponent.treeSectors[TreeIndex] = new FTreeSector();
                    treeComponent.treeSectors[TreeIndex].treeIndex = TreeIndex;

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

                        LODInfo.screenSize = 1 - (l * 0.0625f);
                        LODInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            ref int MaterialSlot = ref LODInfo.materialSlot[m];
                            MaterialSlot = Materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    treeComponent.treeSectors[TreeIndex].tree = new FMesh(Meshes.ToArray(), Materials.ToArray(), LODInfos);
                }
            }
        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainTree", false, 10)]
        public static void UpdateTerrainTree(MenuCommand menuCommand)
        {
            var tasksHandle = new List<GCHandle>(32);
            var jobsHandle = new List<JobHandle>(32);
            var selectObjects = Selection.gameObjects;

            foreach (var selectObject in selectObjects)
            {
                var terrain = selectObject.GetComponent<Terrain>();
                var terrainData = terrain.terrainData;

                var treeComponent = selectObject.GetComponent<TreeComponent>();

                if (treeComponent.treeSectors.Length != 0)
                {
                    for (var i = 0; i < treeComponent.treeSectors.Length; ++i)
                    {
                        ref var treeSector = ref treeComponent.treeSectors[i];
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
                            updateTreeTask.terrainPosition = selectObject.transform.position;
                        }
                        var taskHandle = GCHandle.Alloc(updateTreeTask);
                        tasksHandle.Add(taskHandle);

                        var updateTreeJob = new FUpdateFoliageJob();
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
        [MenuItem("GameObject/EntityAction/Landscape/BuildTerrainGrass", false, 11)]
        public static void BuildTerrainGrass(MenuCommand menuCommand)
        {
            GameObject[] selectObjects = Selection.gameObjects;
            foreach (GameObject selectObject in selectObjects)
            {
                Terrain terrain = selectObject.GetComponent<Terrain>();
                if (!terrain) 
                {
                    Debug.LogWarning("select GameObject doesn't have terrain component");
                    continue; 
                }

                BoundComponent boundComponent = selectObject.GetComponent<BoundComponent>();
                if (boundComponent == null)
                {
                    boundComponent = selectObject.AddComponent<BoundComponent>();
                }

                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null)
                {
                    grassComponent = selectObject.AddComponent<GrassComponent>();
                }

                TerrainData UTerrainData = terrain.terrainData;
                grassComponent.grassSectors = new FGrassSector[UTerrainData.detailPrototypes.Length];

                for (int index = 0; index < UTerrainData.detailPrototypes.Length; ++index)
                {
                    grassComponent.grassSectors[index] = new FGrassSector(boundComponent.BoundSector.sections.Length);
                    grassComponent.grassSectors[index].grassIndex = index;

                    DetailPrototype detailPrototype = UTerrainData.detailPrototypes[index];
                    GameObject grassPrefab = detailPrototype.prototype;

                    List<Mesh> meshes = new List<Mesh>();
                    List<Material> materials = new List<Material>();

                    //Collector Meshes&Materials
                    MeshFilter meshFilter = grassPrefab.GetComponent<MeshFilter>();
                    MeshRenderer meshRenderer = grassPrefab.GetComponent<MeshRenderer>();

                    meshes.AddUnique(meshFilter.sharedMesh);
                    for (int i = 0; i < meshRenderer.sharedMaterials.Length; ++i)
                    {
                        materials.AddUnique(meshRenderer.sharedMaterials[i]);
                    }

                    //Build LODInfo
                    FMeshLODInfo[] LODInfos = new FMeshLODInfo[1];
                    ref FMeshLODInfo LODInfo = ref LODInfos[0];

                    LODInfo.screenSize = 1;
                    LODInfo.materialSlot = new int[meshRenderer.sharedMaterials.Length];

                    for (int j = 0; j < meshRenderer.sharedMaterials.Length; ++j)
                    {
                        ref int MaterialSlot = ref LODInfo.materialSlot[j];
                        MaterialSlot = materials.IndexOf(meshRenderer.sharedMaterials[j]);
                    }
                    grassComponent.grassSectors[index].grass = new FMesh(meshes.ToArray(), materials.ToArray(), LODInfos);
                    grassComponent.grassSectors[index].grassIndex = index;


                    for (int k = 0; k < boundComponent.BoundSector.sections.Length; ++k)
                    {
                        FGrassSection grassSection = new FGrassSection();
                        grassSection.boundIndex = k;
                        grassComponent.grassSectors[index].sections[k] = grassSection;
                    }
                }
            }
        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainGrass", false, 12)]
        public static void UpdateTerrainGrass(MenuCommand menuCommand)
        {
            var tasksHandle = new List<GCHandle>(32);
            var jobsHandle = new List<JobHandle>(32);
            GameObject[] selectObjects = Selection.gameObjects;

            foreach (GameObject selectObject in selectObjects)
            {
                Terrain terrain = selectObject.GetComponent<Terrain>();
                TerrainData terrainData = terrain.terrainData;
                if (!terrain)
                {
                    Debug.LogWarning(selectObject.name + " doesn't have terrain component");
                    continue;
                }

                BoundComponent boundComponent = selectObject.GetComponent<BoundComponent>();
                FBoundSector boundSector = boundComponent.BoundSector;
                if (boundComponent == null)
                {
                    boundComponent = selectObject.AddComponent<BoundComponent>();
                }

                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null)
                {
                    grassComponent = selectObject.AddComponent<GrassComponent>();
                }

                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    FGrassSector grassSector = grassComponent.grassSectors[index];
                    int grassIndex = grassSector.grassIndex;

                    for (int i = 0; i < grassSector.sections.Length; ++i)
                    {
                        FGrassSection grassSection = grassSector.sections[i];
                        FBoundSection boundSection = boundSector.sections[grassSection.boundIndex];

                        grassSection.densityMap = new int[boundComponent.SectionSize * boundComponent.SectionSize];
                        int2 uv = (int2)boundSection.PivotPosition.zx - new int2((int)selectObject.transform.position.z, (int)selectObject.transform.position.x);
                        int[,] densityMap = terrainData.GetDetailLayer(uv.x, uv.y, boundComponent.SectionSize, boundComponent.SectionSize, grassIndex);

                        //Build Densitys
                        var updategrassTask = new FUpdateGrassTask();
                        {
                            updategrassTask.srcMap = densityMap;
                            updategrassTask.dscMap = grassSection.densityMap;
                            updategrassTask.length = boundComponent.SectionSize;
                        }
                        var taskHandle = GCHandle.Alloc(updategrassTask);
                        tasksHandle.Add(taskHandle);

                        var updateGrassJob = new FUpdateFoliageJob();
                        {
                            updateGrassJob.taskHandle = taskHandle;
                        }
                        jobsHandle.Add(updateGrassJob.Schedule());
                    }
                }
            }

            for (var j = 0; j < jobsHandle.Count; ++j)
            {
                jobsHandle[j].Complete();
                tasksHandle[j].Free();
            }
        }
        #endregion //Grass
    }
}
