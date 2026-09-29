using UnityEditor;
using UnityEngine;

namespace Landscape.FoliagePipeline.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(TreeComponent))]
    public class TreeComponentEditor : UnityEditor.Editor
    {
        TreeComponent treeTarget { get { return target as TreeComponent; } }

        void OnEnable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += PreSave;
        }

        void OnValidate()
        {

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (property.name == "occlusionOverride")
                {
                    bool overridden = serializedObject.FindProperty("overrideOcclusion").boolValue;
                    using (new EditorGUI.DisabledScope(!overridden))
                    {
                        TreeOcclusionControls.DrawLayout(property, treeTarget.usesCpuBackend);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                    {
                        EditorGUILayout.PropertyField(property, true);
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
            if (targets.Length == 1) { EditorGUILayout.HelpBox(treeTarget.BackendStatusSummary(), MessageType.None); }
            EditorGUILayout.HelpBox("Live bounds: red = CPU rejection; LOD colors = CPU candidate. Frozen cells: magenta = terrain rejection, cyan = GPU cell rejection. Frozen instances: cyan = absent from final GPU indices. Lines keep scene depth.", MessageType.Info);
            if (targets.Length != 1) { return; }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Capture Visibility Snapshot")) { treeTarget.RequestVisibilitySnapshot(); }
                if (GUILayout.Button("Clear Visibility Snapshot")) { treeTarget.ClearVisibilitySnapshot(); }
            }
            if (Application.isPlaying) { EditorGUILayout.HelpBox(treeTarget.VisibilitySnapshotSummary(), MessageType.None); }
        }

        void OnDisable()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= PreSave;
        }

        void PreSave(UnityEngine.SceneManagement.Scene InScene, string InPath)
        {
            if (treeTarget.gameObject.activeSelf == false) { return; }
            if (treeTarget.enabled == false) { return; }
            treeTarget.OnSave();
        }
    }
}
