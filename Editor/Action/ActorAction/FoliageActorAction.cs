using System;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.FoliagePipeline.Editor
{
    public unsafe class FoliageActorAction
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

                treeComponent.OnSave();

                TerrainData terrainData = UTerrain.terrainData;
                treeComponent.treeSectors = new FTreeSector[terrainData.treePrototypes.Length];

                for (int index = 0; index < terrainData.treePrototypes.Length; ++index)
                {
                    treeComponent.treeSectors[index] = new FTreeSector();
                    treeComponent.treeSectors[index].treeIndex = index;

                    TreePrototype treePrototype = terrainData.treePrototypes[index];
                    List<Mesh> meshes = new List<Mesh>();
                    List<Material> materials = new List<Material>();

                    GameObject treePrefab = treePrototype.prefab;
                    LODGroup lodGroup = treePrefab.GetComponent<LODGroup>();
                    LOD[] lods = lodGroup.GetLODs();

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
                    FMeshLODInfo[] lodInfos = new FMeshLODInfo[lods.Length];
                    for (int l = 0; l < lods.Length; ++l)
                    {
                        ref LOD lod = ref lods[l];
                        ref FMeshLODInfo lodInfo = ref lodInfos[l];
                        Renderer renderer = lod.renderers[0];

                        lodInfo.screenSize = 1 - (l * 0.03125f);
                        lodInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            ref int materialSlot = ref lodInfo.materialSlot[m];
                            materialSlot = materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    treeComponent.treeSectors[index].tree = new FMesh(meshes.ToArray(), materials.ToArray(), lodInfos);
                }

                EditorUtility.SetDirty(treeComponent);
            }

            UpdateTerrainTree(menuCommand);
        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainTree", false, 10)]
        public static void UpdateTerrainTree(MenuCommand menuCommand)
        {
            var tasksPtr = new List<long>(32);
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
                            updateTreeTask.size = new float2(terrainData.heightmapResolution - 1, terrainData.heightmapScale.y);
                            updateTreeTask.treePrototype = treePrototype;
                            updateTreeTask.treeInstances = terrainData.treeInstances;
                            updateTreeTask.treePrototypes = terrainData.treePrototypes;
                            updateTreeTask.treeTransfroms = treeSector.transforms;
                            updateTreeTask.terrainPosition = selectObject.transform.position;
                        }
                        GCHandle taskHandle = GCHandle.Alloc(updateTreeTask);
                        long taskPtr = ((IntPtr)taskHandle).ToInt64();
                        tasksPtr.Add(taskPtr);

                        var updateTreeJob = new FUpdateFoliageJob();
                        {
                            updateTreeJob.taskPtr = taskPtr;
                        }
                        jobsHandle.Add(updateTreeJob.Schedule());
                    }
                }

                EditorUtility.SetDirty(selectObject);
            }

            for (var j = 0; j < jobsHandle.Count; ++j)
            {
                jobsHandle[j].Complete();
                GCHandle.FromIntPtr((IntPtr)tasksPtr[j]).Free();
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

                TerrainData terrainData = terrain.terrainData;
                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null)
                {
                    grassComponent = selectObject.AddComponent<GrassComponent>();
                }

                grassComponent.numSection = (terrainData.heightmapResolution - 1) / 32;
                grassComponent.OnSave();

                grassComponent.grassSectors = new FGrassSector[terrainData.detailPrototypes.Length];

                for (int index = 0; index < terrainData.detailPrototypes.Length; ++index)
                {
                    grassComponent.grassSectors[index] = new FGrassSector(grassComponent.boundSector.sections.Length);
                    grassComponent.grassSectors[index].grassIndex = index;

                    DetailPrototype detailPrototype = terrainData.detailPrototypes[index];
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
                    FMeshLODInfo[] lodInfos = new FMeshLODInfo[1];
                    ref FMeshLODInfo lodInfo = ref lodInfos[0];

                    lodInfo.screenSize = 1;
                    lodInfo.materialSlot = new int[meshRenderer.sharedMaterials.Length];

                    for (int j = 0; j < meshRenderer.sharedMaterials.Length; ++j)
                    {
                        ref int materialSlot = ref lodInfo.materialSlot[j];
                        materialSlot = materials.IndexOf(meshRenderer.sharedMaterials[j]);
                    }
                    grassComponent.grassSectors[index].grass = new FMesh(meshes.ToArray(), materials.ToArray(), lodInfos);
                    grassComponent.grassSectors[index].grassIndex = index;

                    for (int k = 0; k < grassComponent.boundSector.sections.Length; ++k)
                    {
                        FGrassSection grassSection = new FGrassSection();
                        grassSection.boundIndex = k;
                        grassComponent.grassSectors[index].sections[k] = grassSection;
                    }
                }

                EditorUtility.SetDirty(selectObject);
            }

            UpdateTerrainGrass(menuCommand);
        }

        [MenuItem("GameObject/EntityAction/Landscape/UpdateTerrainGrass", false, 12)]
        public static void UpdateTerrainGrass(MenuCommand menuCommand)
        {
            var tasksPtr = new List<long>(32);
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

                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null)
                {
                    grassComponent = selectObject.AddComponent<GrassComponent>();
                }

                FBoundSector boundSector = grassComponent.boundSector;
                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    FGrassSector grassSector = grassComponent.grassSectors[index];
                    int grassIndex = grassSector.grassIndex;

                    for (int i = 0; i < grassSector.sections.Length; ++i)
                    {
                        FGrassSection grassSection = grassSector.sections[i];
                        FBoundSection boundSection = boundSector.sections[grassSection.boundIndex];

                        grassSection.densityMap = new byte[grassComponent.sectionSize * grassComponent.sectionSize];
                        //grassSection.heightmap = new float[grassComponent.SectionSize * grassComponent.SectionSize];

                        int2 sampleUV = (int2)boundSection.pivotPosition.xz - new int2((int)selectObject.transform.position.x, (int)selectObject.transform.position.z);
                        int[,] densityMap = terrainData.GetDetailLayer(sampleUV.x, sampleUV.y, grassComponent.sectionSize, grassComponent.sectionSize, grassIndex);
                        float[,] heightMap = terrainData.GetHeights(sampleUV.x, sampleUV.y, grassComponent.sectionSize, grassComponent.sectionSize);

                        //Build Density and Height and Normal
                        var updategrassTask = new FUpdateGrassTask();
                        {
                            updategrassTask.length = grassComponent.sectionSize;
                            updategrassTask.srcHeight = heightMap;
                            updategrassTask.srcDensity = densityMap;
                            updategrassTask.grassSection = grassSection;
                            updategrassTask.dscDensity = grassSection.densityMap;
                            //updategrassTask.dscHeight = grassSection.heightmap;
                        }
                        GCHandle taskHandle = GCHandle.Alloc(updategrassTask);
                        long taskPtr = ((IntPtr)taskHandle).ToInt64();
                        tasksPtr.Add(taskPtr);

                        var updateGrassJob = new FUpdateFoliageJob();
                        {
                            updateGrassJob.taskPtr = taskPtr;
                        }
                        //updateGrassJob.Execute();
                        jobsHandle.Add(updateGrassJob.Schedule());
                    }
                }

                EditorUtility.SetDirty(selectObject);
            }

            for (var j = 0; j < tasksPtr.Count; ++j)
            {
                jobsHandle[j].Complete();
                GCHandle.FromIntPtr( (IntPtr)tasksPtr[j] ).Free();
            }

            foreach (GameObject selectObject in selectObjects)
            {
                Terrain terrain = selectObject.GetComponent<Terrain>();
                TerrainData terrainData = terrain.terrainData;
                if (!terrain)
                {
                    Debug.LogWarning(selectObject.name + " doesn't have terrain component");
                    continue;
                }

                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null)
                {
                    grassComponent = selectObject.AddComponent<GrassComponent>();
                }
                
                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    FGrassSector grassSector = grassComponent.grassSectors[index];

                    for (int i = 0; i < grassSector.sections.Length; ++i)
                    {
                        FGrassSection grassSection = grassSector.sections[i];
                        if(grassSection.instanceCount == 0)
                        {
                            grassSection.densityMap = null;
                        }
                    }
                }
            }
        }
        #endregion //Grass
    }
}
