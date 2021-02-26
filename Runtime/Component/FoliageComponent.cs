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

        [Header("Foliage")]
        //[HideInInspector]
        public FTree Tree;

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
            UnityTerrain = GetComponent<Terrain>();
            UnityTerrainData = GetComponent<TerrainCollider>().terrainData;
            if (UnityTerrain.drawTreesAndFoliage == true)
            {
                UnityTerrain.drawTreesAndFoliage = false;
            }

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

        }

        public void DrawBounds(in bool LODColor = false)
        {
            if (DisplayBounds && TreeSector != null)
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
