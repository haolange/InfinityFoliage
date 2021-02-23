using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;

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

        [MenuItem("GameObject/Tool/Landscape/BuildTree", false, 3)]
        public static void CreatePrimitiveEntity(MenuCommand menuCommand)
        {
            GameObject LandscapeActor = menuCommand.context as GameObject;
            Terrain UTerrain = LandscapeActor.GetComponent<Terrain>();
            TerrainData UTerrainData = UTerrain.terrainData;
            FoliageComponent Foliage = LandscapeActor.GetComponent<FoliageComponent>();

            Foliage.InstancesTransfrom = new FTransform[UTerrainData.treeInstanceCount];

            for (int i = 0; i < UTerrainData.treeInstanceCount; i++)
            {
                Foliage.InstancesTransfrom[i].Scale = new float3(UTerrainData.treeInstances[i].widthScale, UTerrainData.treeInstances[i].heightScale, UTerrainData.treeInstances[i].widthScale);
                Foliage.InstancesTransfrom[i].Rotation = new float3(0, UTerrainData.treeInstances[i].rotation, 0);
                Foliage.InstancesTransfrom[i].Position = UTerrainData.treeInstances[i].position * (UTerrainData.heightmapResolution - 1);
            }
            Undo.RegisterCreatedObjectUndo(LandscapeActor, "BuildTree");
            //Selection.activeObject = MeshEntity;
        }
    }
}
