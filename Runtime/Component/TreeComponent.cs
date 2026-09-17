using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

namespace Landscape.FoliagePipeline
{
    [AddComponentMenu("HG/Foliage/Tree Component")]
    public unsafe class TreeComponent : FoliageComponent
    {
        [Header("Setting")]
        public int numSection = 16;
        [Range(0.05f, 2f)]
        public float fadeDuration = 0.5f;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool showBounds = false;
#endif
        [HideInInspector]
        public TreeSector[] treeSectors;

        private float drawDistance;
        private MaterialPropertyBlock m_PropertyBlock;
        private FrustumPlane* m_Planes;

        internal int sectorSize
        {
            get
            {
                return terrainData.heightmapResolution - 1;
            }
        }

        float[] SampleOcclusionHeight()
        {
            const int res = 64;
            float[] heights = new float[res * res];
            float3 pos = transform.position;
            float3 size = terrainData.size;
            for (int z = 0; z < res; ++z)
            {
                for (int x = 0; x < res; ++x)
                {
                    float u = x / (float)(res - 1);
                    float v = z / (float)(res - 1);
                    Vector3 world = new Vector3(pos.x + (u * size.x), pos.y, pos.z + (v * size.z));
                    heights[(z * res) + x] = terrain.SampleHeight(world);
                }
            }
            return heights;
        }

        protected override void OnRegiste()
        {
            terrain = GetComponent<Terrain>();
            foliageType = EFoliageType.Tree;
            terrainData = terrain.terrainData;
            drawDistance = terrain.treeDistance;
            terrain.treeDistance = 0;
            m_PropertyBlock = new MaterialPropertyBlock();

            Aabb terrainBound = terrainData.bounds;
            float3 terrainPosition = transform.position;
            if (treeSectors != null)
            {
                float[] heights = SampleOcclusionHeight();
                float3 terrainSize = terrainData.size;
                foreach (TreeSector treeSector in treeSectors)
                {
                    treeSector.Initialize(numSection, sectorSize, terrainPosition, terrainBound);
                    treeSector.BuildRuntimeData();
                    treeSector.SetHeightField(heights, 64, terrainPosition, terrainSize);
                }
            }
            EncapsulateComponentBound();
        }

        protected override void UnRegiste()
        {
            terrain.treeDistance = drawDistance;
            if (treeSectors == null) { return; }
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.Release();
            }
        }

#if UNITY_EDITOR
        public void OnSave()
        {
            if (Application.isPlaying) { return; }
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            int size = terrainData.heightmapResolution - 1;
            boundSector = new BoundSector(0, size, 0, transform.position, terrainData.bounds, false);
            if (treeSectors == null) { return; }
            int expected = numSection * numSection;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                if (treeSectors[i] == null) { continue; }
                if (treeSectors[i].boundSector != null && treeSectors[i].boundSector.sections != null && treeSectors[i].boundSector.sections.Length == expected)
                {
                    continue;
                }
                treeSectors[i].RebuildSpatialGrid(numSection, size, transform.position, terrainData.bounds);
            }
        }

        public void BakeAfterTransforms()
        {
            if (treeSectors == null) { return; }
            terrain = GetComponent<Terrain>();
            terrainData = terrain.terrainData;
            int size = terrainData.heightmapResolution - 1;
            Aabb terrainBound = terrainData.bounds;
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                treeSectors[i].BakeCells(numSection, size, transform.position, terrainBound);
            }
            EncapsulateComponentBound();
        }

        private void DrawBounds(in bool color = false)
        {
            if (showBounds == false || Application.isPlaying == false || this.enabled == false || this.gameObject.activeSelf == false) return;
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.DrawBounds(color);
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            DrawBounds(true);
        }
#endif

        public void EncapsulateComponentBound()
        {
            if (terrainData == null) { return; }
            if (boundSector == null)
            {
                int size = terrainData.heightmapResolution - 1;
                boundSector = new BoundSector(0, size, 0, transform.position, terrainData.bounds, false);
            }
            Aabb bound = boundSector.bound;
            if (treeSectors == null) { return; }
            for (int i = 0; i < treeSectors.Length; ++i)
            {
                TreeSector sector = treeSectors[i];
                if (sector == null || !sector.hasPackedBound) { continue; }
                bound.Encapsulate(sector.packedBound);
            }
            boundSector.bound = bound;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void InitView(in float3 viewOrigin, in float4x4 matrixProj, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            m_Planes = planes;
            if (treeSectors == null) { return; }
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.InitView(drawDistance, viewOrigin, matrixProj, planes, taskHandles);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchSetup(Camera camera, in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            if (treeSectors == null) { return; }
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.DispatchSetup(camera, drawDistance, viewOrigin, matrixProj, m_Planes, taskHandles);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void FlushPendingUploads()
        {
            if (treeSectors == null) { return; }
            float dt = Time.deltaTime;
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.FlushPendingUploads(fadeDuration, dt);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex)
        {
            if (treeSectors == null) { return; }
            foreach (TreeSector treeSector in treeSectors)
            {
                treeSector.DispatchDraw(cmdBuffer, passIndex, m_PropertyBlock);
            }
        }
    }
}
