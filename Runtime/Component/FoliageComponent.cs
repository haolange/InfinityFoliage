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
        public FTree Tree;
        public GameObject TreePrefab;
        public TreeAsset TreeProfile;

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
            if (UnityTerrain.drawTreesAndFoliage == true)
            {
                UnityTerrain.drawTreesAndFoliage = false;
            }

            BuildTree();
            TreeSector = new FTreeSector();
            TreeSector.SetTree(Tree);
            TreeSector.Initialize();
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
        void BuildTree()
        {
            if (TreeProfile != null)
            {
                Tree = TreeProfile.Tree;
            }
        }
    }
}
