using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct FTreeLODInfo
    {
        public float ScreenSize;
    }

    [CreateAssetMenu(menuName = "Landscape/TreeAsset")]
    public class TreeAsset : ScriptableObject
    {
        [Header("Mesh")]
        public Mesh[] Meshes;

        [Header("Material")]
        public Material[] Materials;

        [Header("Culling")]
        public FTreeLODInfo[] LODInfo;


        public TreeAsset()
        {

        }
    }
}
