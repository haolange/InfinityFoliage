using UnityEngine;
using InfinityTech.Core.Geometry;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage/LOD Component")]
    public class LODComponent : MonoBehaviour
    {
        public MeshAsset meshAsset;


        void OnEnable()
        {
            GameObject.DestroyImmediate(this, true);
        }

        void Update()
        {
        
        }

        void OnDisable()
        {

        }
    }
}

