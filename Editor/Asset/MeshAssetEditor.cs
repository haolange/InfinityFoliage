using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.FoliagePipeline.Editor
{
    [CustomEditor(typeof(MeshAsset))]
    public class MeshAssetEditor : UnityEditor.Editor
    {
        MeshAsset assetTarget { get { return target as MeshAsset; } }


        void OnEnable()
        {
            
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
            
        }
    }
}
