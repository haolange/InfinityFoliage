using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public class FTreeSector
    {
        internal FTree Tree;
        internal NativeList<FTreeBatch> TreeBatchs;
        public static List<FTreeSector> TreeSectors = new List<FTreeSector>(64);


        public FTreeSector()
        {

        }

        public void SetTree(FTree InTree)
        {
            Tree = InTree;
        }

        public void DrawTree(CommandBuffer CmdBuffer)
        {
            if (Application.isPlaying == false) { return; }

            for (int i = 0; i < TreeBatchs.Length; ++i)
            {
                FTreeBatch TreeBatch = TreeBatchs[i];

                if (TreeBatch.LODIndex == 2)
                {
                    Mesh Meshe = Tree.Meshes[TreeBatch.LODIndex];
                    Material material = Tree.Materials[TreeBatch.MaterialIndex];
                    CmdBuffer.DrawMesh(Meshe, TreeBatch.Matrix_LocalToWorld, material, TreeBatch.SubmeshIndex, 0);
                }
            }
        }

        public void Initialize()
        {
            TreeBatchs = new NativeList<FTreeBatch>(2048, Allocator.Persistent);
            TreeSectors.Add(this);
        }

        public void Release()
        {
            TreeBatchs.Dispose();
            TreeSectors.Remove(this);
        }

        public void AddBatch(in FTreeBatch TreeBatch)
        {
            TreeBatchs.Add(TreeBatch);
        }

        public void RemoveBatch(in FTreeBatch TreeBatch)
        {
            int index = TreeBatchs.IndexOf(TreeBatch);
            if (index >= 0)
            {
                TreeBatchs.RemoveAt(index);
            }
        }

        public void ClearBatch()
        {
            TreeBatchs.Clear();
        }

#if UNITY_EDITOR
        public static Color[] LODColors = new Color[7] { new Color(1, 1, 1, 1), new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) };

        public void DrawBounds(in bool LODColor = false, in bool DrawSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (int i = 0; i < TreeBatchs.Length; ++i)
            {
                Geometry.DrawBound(TreeBatchs[i].BoundingBox, LODColor ? LODColors[TreeBatchs[i].LODIndex] : Color.blue);

                if (DrawSphere)
                {
                    UnityEditor.Handles.color = LODColor ? LODColors[TreeBatchs[i].LODIndex] : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(TreeBatchs[i].BoundingSphere.center, Vector3.up, TreeBatchs[i].BoundingSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatchs[i].BoundingSphere.center, Vector3.back, TreeBatchs[i].BoundingSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatchs[i].BoundingSphere.center, Vector3.right, TreeBatchs[i].BoundingSphere.radius);
                }
            }
        }
#endif

        public void BuildMeshBatchs(List<FTransform> Instances)
        {
            FTreeBatch TreeBatch;

            for (int i = 0; i < Instances.Count; ++i)
            {
                for (int j = 0; j < Tree.Meshes.Length; ++j)
                {
                    Mesh Meshe = Tree.Meshes[j];
                    float4x4 Matrix = float4x4.TRS(Instances[i].Position, quaternion.EulerXYZ(Instances[i].Rotation), Instances[i].Scale);

                    TreeBatch.LODIndex = j;
                    TreeBatch.Matrix_LocalToWorld = Matrix;
                    TreeBatch.BoundingBox = Geometry.CaculateWorldBound(Meshe.bounds, Matrix);
                    TreeBatch.BoundingSphere = new FSphere(Geometry.CaculateBoundRadius(TreeBatch.BoundingBox), TreeBatch.BoundingBox.center);

                    for (int k = 0; k < Meshe.subMeshCount; ++k)
                    {
                        TreeBatch.SubmeshIndex = k;
                        TreeBatch.MaterialIndex = Tree.LODInfo[j].MaterialSlot[k];
                        AddBatch(TreeBatch);
                    }
                }
            }

            TreeBatchs.Sort();
        }
        
        private void DoCulling()
        {

        }

        public void InitView()
        {

        }
    }
}
