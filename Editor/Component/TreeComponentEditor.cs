using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TreeComponent))]
    public class TreeComponentEditor : UnityEditor.Editor
    {
        TreeComponent Foliage { get { return target as TreeComponent; } }


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

            //Foliage.Serialize();
        }
    }
}
