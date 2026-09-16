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
                Terrain terrain = SelectObject.GetComponent<Terrain>();
                if (!terrain)
                {
                    Debug.LogWarning("select GameObject doesn't have terrain component");
                    continue;
                }

                TerrainData terrainData = terrain.terrainData;
                TreeComponent treeComponent = SelectObject.GetComponent<TreeComponent>();
                if (treeComponent == null)
                {
                    treeComponent = SelectObject.AddComponent<TreeComponent>();
                }
                treeComponent.terrain = terrain;
                treeComponent.terrainData = terrainData;
                treeComponent.OnSave();
                treeComponent.treeSectors = new TreeSector[terrainData.treePrototypes.Length];

                for (int index = 0; index < terrainData.treePrototypes.Length; ++index)
                {
                    treeComponent.treeSectors[index] = new TreeSector();
                    treeComponent.treeSectors[index].treeIndex = index;
                    treeComponent.treeSectors[index].RebuildSpatialGrid(treeComponent.numSection, terrainData.heightmapResolution - 1, SelectObject.transform.position, terrainData.bounds);

                    TreePrototype treePrototype = terrainData.treePrototypes[index];
                    List<Mesh> meshes = new List<Mesh>();
                    List<Material> materials = new List<Material>();

                    GameObject treePrefab = treePrototype.prefab;
                    LODGroup lodGroup = treePrefab.GetComponent<LODGroup>();
                    LOD[] lods = lodGroup.GetLODs();

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

                    MeshLodInfo[] lodInfos = new MeshLodInfo[lods.Length];
                    for (int l = 0; l < lods.Length; ++l)
                    {
                        ref LOD lod = ref lods[l];
                        ref MeshLodInfo lodInfo = ref lodInfos[l];
                        Renderer renderer = lod.renderers[0];

                        lodInfo.screenSize = 1 - (l * 0.03125f);
                        lodInfo.materialSlot = new int[renderer.sharedMaterials.Length];

                        for (int m = 0; m < renderer.sharedMaterials.Length; ++m)
                        {
                            lodInfo.materialSlot[m] = materials.IndexOf(renderer.sharedMaterials[m]);
                        }
                    }
                    treeComponent.treeSectors[index].tree = new FoliageMesh(meshes.ToArray(), materials.ToArray(), lodInfos);
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
                if (!terrain)
                {
                    Debug.LogWarning(selectObject.name + " doesn't have terrain component");
                    continue;
                }

                var terrainData = terrain.terrainData;
                var treeComponent = selectObject.GetComponent<TreeComponent>();
                treeComponent.terrain = terrain;
                treeComponent.terrainData = terrainData;

                if (treeComponent.treeSectors != null && treeComponent.treeSectors.Length != 0)
                {
                    for (var i = 0; i < treeComponent.treeSectors.Length; ++i)
                    {
                        var treeSector = treeComponent.treeSectors[i];
                        treeSector.transforms = new List<InstanceTransform>(512);
                        var treePrototype = terrainData.treePrototypes[treeSector.treeIndex];

                        var updateTreeTask = new UpdateTreeTask();
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

                        var updateTreeJob = new UpdateFoliageJob();
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

            foreach (var selectObject in selectObjects)
            {
                var treeComponent = selectObject.GetComponent<TreeComponent>();
                if (treeComponent == null) { continue; }
                treeComponent.BakeAfterTransforms();
                EditorUtility.SetDirty(selectObject);
            }
        }
        #endregion

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
                grassComponent.terrain = terrain;
                grassComponent.terrainData = terrainData;
                grassComponent.numSection = (terrainData.heightmapResolution - 1) / 32;
                grassComponent.OnSave();

                grassComponent.grassSectors = new GrassSector[terrainData.detailPrototypes.Length];

                for (int index = 0; index < terrainData.detailPrototypes.Length; ++index)
                {
                    grassComponent.grassSectors[index] = new GrassSector(grassComponent.boundSector.sections.Length);
                    grassComponent.grassSectors[index].grassIndex = index;

                    DetailPrototype detailPrototype = terrainData.detailPrototypes[index];
                    GameObject grassPrefab = detailPrototype.prototype;

                    List<Mesh> meshes = new List<Mesh>();
                    List<Material> materials = new List<Material>();

                    MeshFilter meshFilter = grassPrefab.GetComponent<MeshFilter>();
                    MeshRenderer meshRenderer = grassPrefab.GetComponent<MeshRenderer>();

                    meshes.AddUnique(meshFilter.sharedMesh);
                    for (int i = 0; i < meshRenderer.sharedMaterials.Length; ++i)
                    {
                        materials.AddUnique(meshRenderer.sharedMaterials[i]);
                    }

                    MeshLodInfo[] lodInfos = new MeshLodInfo[1];
                    ref MeshLodInfo lodInfo = ref lodInfos[0];
                    lodInfo.screenSize = 1;
                    lodInfo.materialSlot = new int[meshRenderer.sharedMaterials.Length];

                    for (int j = 0; j < meshRenderer.sharedMaterials.Length; ++j)
                    {
                        lodInfo.materialSlot[j] = materials.IndexOf(meshRenderer.sharedMaterials[j]);
                    }
                    grassComponent.grassSectors[index].grass = new FoliageMesh(meshes.ToArray(), materials.ToArray(), lodInfos);

                    for (int k = 0; k < grassComponent.boundSector.sections.Length; ++k)
                    {
                        GrassSection grassSection = new GrassSection();
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
                if (!terrain)
                {
                    Debug.LogWarning(selectObject.name + " doesn't have terrain component");
                    continue;
                }

                TerrainData terrainData = terrain.terrainData;
                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                grassComponent.terrain = terrain;
                grassComponent.terrainData = terrainData;

                BoundSector boundSector = grassComponent.boundSector;
                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    GrassSector grassSector = grassComponent.grassSectors[index];
                    int grassIndex = grassSector.grassIndex;

                    for (int i = 0; i < grassSector.sections.Length; ++i)
                    {
                        GrassSection grassSection = grassSector.sections[i];
                        BoundSection boundSection = boundSector.sections[grassSection.boundIndex];
                        grassSection.count = 0;
                        grassSection.offset = 0;
                        grassSection.densityMap = new byte[grassComponent.sectionSize * grassComponent.sectionSize];

                        int2 sampleUV = (int2)boundSection.pivotPosition - new int2((int)selectObject.transform.position.x, (int)selectObject.transform.position.z);
                        int[,] densityMap = terrainData.GetDetailLayer(sampleUV.x, sampleUV.y, grassComponent.sectionSize, grassComponent.sectionSize, grassIndex);
                        float[,] heightMap = terrainData.GetHeights(sampleUV.x, sampleUV.y, grassComponent.sectionSize, grassComponent.sectionSize);

                        var updategrassTask = new UpdateGrassTask();
                        {
                            updategrassTask.length = grassComponent.sectionSize;
                            updategrassTask.srcHeight = heightMap;
                            updategrassTask.srcDensity = densityMap;
                            updategrassTask.grassSection = grassSection;
                            updategrassTask.dscDensity = grassSection.densityMap;
                        }
                        GCHandle taskHandle = GCHandle.Alloc(updategrassTask);
                        long taskPtr = ((IntPtr)taskHandle).ToInt64();
                        tasksPtr.Add(taskPtr);

                        var updateGrassJob = new UpdateFoliageJob();
                        {
                            updateGrassJob.taskPtr = taskPtr;
                        }
                        jobsHandle.Add(updateGrassJob.Schedule());
                    }
                }

                EditorUtility.SetDirty(selectObject);
            }

            for (var j = 0; j < tasksPtr.Count; ++j)
            {
                jobsHandle[j].Complete();
                GCHandle.FromIntPtr((IntPtr)tasksPtr[j]).Free();
            }

            foreach (GameObject selectObject in selectObjects)
            {
                Terrain terrain = selectObject.GetComponent<Terrain>();
                if (!terrain)
                {
                    Debug.LogWarning(selectObject.name + " doesn't have terrain component");
                    continue;
                }

                GrassComponent grassComponent = selectObject.GetComponent<GrassComponent>();
                if (grassComponent == null) { continue; }

                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    GrassSector grassSector = grassComponent.grassSectors[index];
                    grassSector.BuildPackedOffsets();
                    grassSector.BuildTightBound(grassComponent.boundSector);
                    for (int i = 0; i < grassSector.sections.Length; ++i)
                    {
                        GrassSection grassSection = grassSector.sections[i];
                        if (grassSection.count == 0)
                        {
                            grassSection.densityMap = null;
                        }
                    }
                }
                EditorUtility.SetDirty(selectObject);
            }
        }
        #endregion
    }
}
