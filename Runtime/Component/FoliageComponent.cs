using UnityEngine;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage Component")]
    public class FoliageComponent : InstanceMeshComponent
    {
#if UNITY_EDITOR
        [Header("Debug")]
        public bool DisplayBounds = false;
#endif

        [Header("Cull")]
        public float CullDistance;

        [Header("Tree")]
        public TreeAsset Tree;

        [Header("Instances")]
        public FTransform[] InstancesTransfrom;

        [HideInInspector]
        public Terrain UnityTerrain;
        [HideInInspector]
        public TerrainData UnityTerrainData;
        [HideInInspector]
        public FTreeSector TreeSector;


        public FoliageComponent() : base()
        {

        }

        protected override void OnRegister()
        {
            TreeSector.SetTreeAsset(Tree);
            TreeSector.BuildNativeCollection();
            TreeSector.BuildMeshBatchs(InstancesTransfrom);
        }

        protected override void OnTransformChange()
        {

        }

        protected override void EventPlay()
        {

        }

        protected override void EventTick()
        {

        }

        protected override void UnRegister()
        {
            TreeSector.ReleaseNativeCollection();
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            UnityTerrain = GetComponent<Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;

            TreeSector.ReleaseNativeCollection();

            TreeSector = new FTreeSector();
            TreeSector.SetTreeAsset(Tree);
            TreeSector.BuildNativeCollection();
            TreeSector.BuildMeshBatchs(InstancesTransfrom);
        }

        public void DrawBounds(in bool LODColor = false)
        {
            if (DisplayBounds)
            {
                TreeSector.DrawBounds(LODColor);
            }
        }

        void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif
    }
}
