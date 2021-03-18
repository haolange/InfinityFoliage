using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Landscape.FoliagePipeline;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Landscape.Editor.FoliagePipeline
{
    public class MeshAssetWizard : ScriptableWizard
    {
        private MeshAsset meshAsset;

        public GameObject target;


        void OnEnable()
        {

        }

        void OnWizardCreate()
        {
            MeshAsset.BuildMeshAsset(target, meshAsset);
        }

        void OnWizardOtherButton()
        {

        }

        void OnWizardUpdate()
        {
            
        }

        public void SetMeshAsset(MeshAsset meshAsset)
        {
            this.meshAsset = meshAsset;
            this.target = meshAsset.target != null ? meshAsset.target : null;
        }
    }
}
