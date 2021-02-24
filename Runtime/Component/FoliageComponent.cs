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
        public TreeAsset TreeProfile;
        public GameObject TreePrefabs;

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
            TreeSector = new FTreeSector();
            TreeSector.Initialize();
            TreeSector.SetTreeAsset(TreeProfile);
            TreeSector.BuildMeshBatchs(InstancesTransfrom);

            if(UnityTerrain.drawTreesAndFoliage == true)
            {
                UnityTerrain.drawTreesAndFoliage = false;
            }
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
            TreeSector.Release();
        }

#if UNITY_EDITOR
        public void Serialize()
        {
            UnityTerrain = GetComponent<Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;
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
