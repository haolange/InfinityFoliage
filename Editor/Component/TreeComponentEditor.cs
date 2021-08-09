using UnityEditor;

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
            if (treeTarget.gameObject.activeSelf == false) { return; }
            if (treeTarget.enabled == false) { return; }

            treeTarget.OnSave();
        }
    }
}
