using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    public class FoliageAssetAction
    {
        #region MeshAsset
        [MenuItem("Assets/AssetActions/Landscape/BuildMeshAssetFromPrefab", priority = 32)]
        public static void BuildMeshAssetFromPrefab(MenuCommand menuCommand)
        {
            Object activeObject = Selection.activeObject;
            if (activeObject.GetType() != typeof(MeshAsset)) 
            {
                Debug.LogWarning("select asset type is not MeshAsset");
                return; 
            }

            MeshAssetWizard meshAssetWizard = ScriptableWizard.DisplayWizard<MeshAssetWizard>("Build MeshAsset", "Build");
            meshAssetWizard.SetMeshAsset((MeshAsset)activeObject);
        }
        #endregion //MeshAsset
    }
}
