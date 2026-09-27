using System;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
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

                        meshes.Add(meshFilter.sharedMesh);
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

                        lodInfo.screenSize = l == 0 ? 1f : lods[l - 1].screenRelativeTransitionHeight;
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
                            updateTreeTask.size = terrainData.size;
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

            List<FoliageAssetWriter.Request> treeRequests = new List<FoliageAssetWriter.Request>(selectObjects.Length);
            List<TreeComponent> bakedTrees = new List<TreeComponent>(selectObjects.Length);
            foreach (var selectObject in selectObjects)
            {
                var treeComponent = selectObject.GetComponent<TreeComponent>();
                if (treeComponent == null) { continue; }
                treeComponent.BakeAfterTransforms();
                string assetKey = FoliageAssetWriter.AssetKey(selectObject);
                List<FoliageAssetWriter.Entry> entries = new List<FoliageAssetWriter.Entry>(treeComponent.treeSectors.Length);
                for (int i = 0; i < treeComponent.treeSectors.Length; ++i)
                {
                    TreeSector sector = treeComponent.treeSectors[i];
                    if (sector == null || sector.transforms == null) { throw new InvalidOperationException("Tree candidates were not generated."); }
                    entries.Add(new FoliageAssetWriter.Entry("tree_" + sector.treeIndex, FoliageAssetCodec.EncodeTree(sector.treeIndex, sector.transforms)));
                }
                treeRequests.Add(new FoliageAssetWriter.Request(assetKey, "tree_", entries));
                bakedTrees.Add(treeComponent);
            }
            FoliageAssetWriter.CommitBatch(treeRequests);
            for (int i = 0; i < bakedTrees.Count; ++i)
            {
                TreeComponent treeComponent = bakedTrees[i];
                treeComponent.assetKey = treeRequests[i].assetKey;
                for (int j = 0; j < treeComponent.treeSectors.Length; ++j) { treeComponent.treeSectors[j].transforms = null; }
                EditorUtility.SetDirty(treeComponent);
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
            GameObject[] selectObjects = Selection.gameObjects;
            List<FoliageAssetWriter.Request> grassRequests = new List<FoliageAssetWriter.Request>(selectObjects.Length);
            List<GrassComponent> bakedGrass = new List<GrassComponent>(selectObjects.Length);
            List<int[][]> bakedCounts = new List<int[][]>(selectObjects.Length);
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
                if (grassComponent == null || grassComponent.grassSectors == null) { continue; }
                int resolution = terrainData.detailResolution;
                int numSection = grassComponent.numSection;
                if (resolution <= 0 || numSection <= 0 || resolution % numSection != 0)
                {
                    throw new InvalidOperationException("Grass detail resolution must divide the fixed section grid.");
                }
                string assetKey = FoliageAssetWriter.AssetKey(selectObject);
                List<FoliageAssetWriter.Entry> entries = new List<FoliageAssetWriter.Entry>(grassComponent.grassSectors.Length * 17);
                int[][] speciesCounts = new int[grassComponent.grassSectors.Length][];
                for (int index = 0; index < grassComponent.grassSectors.Length; ++index)
                {
                    GrassSector grassSector = grassComponent.grassSectors[index];
                    int grassIndex = grassSector.grassIndex;
                    byte[] baseDensity = new byte[resolution * resolution];
                    int[] sectionCounts = new int[grassSector.sections.Length];
                    speciesCounts[index] = sectionCounts;

                    for (int pageX = 0; pageX < FoliageAssetCodec.PageAxis; ++pageX)
                    {
                        int xStart = (pageX * resolution) / FoliageAssetCodec.PageAxis;
                        int xEnd = ((pageX + 1) * resolution) / FoliageAssetCodec.PageAxis;
                        for (int pageY = 0; pageY < FoliageAssetCodec.PageAxis; ++pageY)
                        {
                            int yStart = (pageY * resolution) / FoliageAssetCodec.PageAxis;
                            int yEnd = ((pageY + 1) * resolution) / FoliageAssetCodec.PageAxis;
                            int width = xEnd - xStart;
                            int height = yEnd - yStart;
                            int[,] source = terrainData.GetDetailLayer(xStart, yStart, width, height, grassIndex);
                            byte[] detailDensity = new byte[width * height];
                            for (int y = 0; y < height; ++y)
                            {
                                int cellY = ((yStart + y) * numSection) / resolution;
                                for (int x = 0; x < width; ++x)
                                {
                                    int density = source[x, y];
                                    if (density < 0 || density > byte.MaxValue)
                                    {
                                        throw new InvalidOperationException("Grass density exceeds the compact byte asset format.");
                                    }
                                    int globalX = xStart + x;
                                    int globalY = yStart + y;
                                    byte sample = (byte)density;
                                    byte baseSample = FoliageAssetCodec.BaseDensity(sample, globalX, globalY, grassIndex);
                                    baseDensity[(globalY * resolution) + globalX] = baseSample;
                                    detailDensity[(y * width) + x] = (byte)(sample - baseSample);
                                    int cellX = (globalX * numSection) / resolution;
                                    sectionCounts[(cellX * numSection) + cellY] += density;
                                }
                            }
                            GrassDensityPage detailPage = FoliageAssetCodec.CreateGrassPage(grassIndex, resolution, numSection, pageX, pageY, detailDensity);
                            string name = "grass_" + grassIndex + "_page_" + pageX + "_" + pageY;
                            entries.Add(new FoliageAssetWriter.Entry(name, FoliageAssetCodec.EncodeGrass(detailPage)));
                        }
                    }

                    GrassDensityPage basePage = FoliageAssetCodec.CreateGrassPage(grassIndex, resolution, numSection, -1, -1, baseDensity);
                    entries.Add(new FoliageAssetWriter.Entry("grass_" + grassIndex + "_base", FoliageAssetCodec.EncodeGrass(basePage)));
                }
                grassRequests.Add(new FoliageAssetWriter.Request(assetKey, "grass_", entries));
                bakedGrass.Add(grassComponent);
                bakedCounts.Add(speciesCounts);
            }
            FoliageAssetWriter.CommitBatch(grassRequests);
            for (int i = 0; i < bakedGrass.Count; ++i)
            {
                GrassComponent grassComponent = bakedGrass[i];
                grassComponent.terrain = grassComponent.GetComponent<Terrain>();
                grassComponent.terrainData = grassComponent.terrain.terrainData;
                int[][] speciesCounts = bakedCounts[i];
                for (int species = 0; species < speciesCounts.Length; ++species)
                {
                    GrassSector grassSector = grassComponent.grassSectors[species];
                    int[] sectionCounts = speciesCounts[species];
                    for (int section = 0; section < sectionCounts.Length; ++section)
                    {
                        grassSector.sections[section].count = sectionCounts[section];
                    }
                    grassSector.BuildPackedOffsets();
                    grassSector.BuildTightBound(grassComponent.boundSector);
                }
                grassComponent.assetKey = grassRequests[i].assetKey;
                EditorUtility.SetDirty(grassComponent);
            }
        }
        #endregion
    }
}
