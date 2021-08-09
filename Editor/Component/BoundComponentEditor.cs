using UnityEditor;

namespace Landscape.FoliagePipeline.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(BoundComponent))]
    public class BoundComponentEditor : UnityEditor.Editor
    {
        /*BoundComponent boundTarget { get { return target as BoundComponent; } }


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
            if (boundTarget.gameObject.activeSelf == false) { return; }
            if (boundTarget.enabled == false) { return; }

            boundTarget.OnSave();
        }*/
    }
}
