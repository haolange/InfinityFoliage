using UnityEditor;

namespace Landscape.FoliagePipeline.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(GrassComponent))]
    public class GrassComponentEditor : UnityEditor.Editor
    {
        GrassComponent grassTarget { get { return target as GrassComponent; } }


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

        void PreSave(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (grassTarget.gameObject.activeSelf == false) { return; }
            if (grassTarget.enabled == false) { return; }

            grassTarget.OnSave();
        }
    }
}
