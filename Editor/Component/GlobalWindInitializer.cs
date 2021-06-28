using UnityEditor;
using UnityEngine;

namespace Landscape.FoliagePipeline.Editor
{
  internal class GlobalWindInitializer
  {
    [InitializeOnLoadMethod]
    private static void Initialize() => EditorApplication.update += new EditorApplication.CallbackFunction(GlobalWindInitializer.OnEditorUpdate);

    private static void OnEditorUpdate()
    {
      if (Application.isPlaying || !((Object) WindComponent.Instance != (Object) null))
        return;
      WindComponent.Instance.UpdateTime(EditorApplication.timeSinceStartup);
    }

    public static Texture2D LoadGustNoise()
    {
      string assetPath = AssetDatabase.GUIDToAssetPath("b564ef71400a25f48a92b590802a9b99");
      return assetPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) : (Texture2D) null;
    }
  }
}
