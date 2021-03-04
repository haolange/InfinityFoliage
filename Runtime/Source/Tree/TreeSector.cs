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
        public FTree Tree;
        public int TreeIndex;
        public float CullDistance = 256;
        public List<FTransform> Transfroms;

        private NativeList<FTreeBatch> TreeBatchs;
        private NativeList<FTreeElement> TreeElements;


        public void DrawTree(CommandBuffer CmdBuffer)
        {
            for (int i = 0; i < TreeElements.Length; ++i)
            {
                FTreeElement TreeElement = TreeElements[i];

                if (TreeElement.LODIndex == Tree.LODInfo.Length - 1)
                {
                    Mesh Meshe = Tree.Meshes[TreeElement.LODIndex];
                    Material material = Tree.Materials[TreeElement.MatIndex];
                    CmdBuffer.DrawMesh(Meshe, TreeElement.Matrix_LocalToWorld, material, TreeElement.MeshIndex, 0);
                }
            }
        }

        public void Initialize()
        {
            TreeBatchs = new NativeList<FTreeBatch>(2048, Allocator.Persistent);
            TreeElements = new NativeList<FTreeElement>(4096, Allocator.Persistent);
        }

        public void Release()
        {
            TreeBatchs.Dispose();
            TreeElements.Dispose();
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

        public void AddElement(in FTreeElement TreeElement)
        {
            TreeElements.Add(TreeElement);
        }

        public void RemoveElement(in FTreeElement TreeElement)
        {
            int index = TreeElements.IndexOf(TreeElement);
            if (index >= 0)
            {
                TreeElements.RemoveAt(index);
            }
        }

        public void ClearElement()
        {
            TreeElements.Clear();
        }

#if UNITY_EDITOR
        public static Color[] LODColors = new Color[7] { new Color(1, 1, 1, 1), new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) };

        public void DrawBounds(in bool LODColor = false, in bool DrawSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (int i = 0; i < TreeElements.Length; ++i)
            {
                Geometry.DrawBound(TreeElements[i].BoundBox, LODColor ? LODColors[TreeElements[i].LODIndex] : Color.blue);

                if (DrawSphere)
                {
                    UnityEditor.Handles.color = LODColor ? LODColors[TreeElements[i].LODIndex] : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(TreeElements[i].BoundSphere.center, Vector3.up, TreeElements[i].BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeElements[i].BoundSphere.center, Vector3.back, TreeElements[i].BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeElements[i].BoundSphere.center, Vector3.right, TreeElements[i].BoundSphere.radius);
                }
            }
        }
#endif

        public void BuildMeshBatchs()
        {
            FTreeBatch TreeBatch;

            for (int i = 0; i < Transfroms.Count; ++i)
            {
                Mesh Meshe = Tree.Meshes[0];
                float4x4 Matrix_World = float4x4.TRS(Transfroms[i].Position, quaternion.EulerXYZ(Transfroms[i].Rotation), Transfroms[i].Scale);

                TreeBatch.Matrix_World = Matrix_World;
                TreeBatch.BoundBox = Geometry.CaculateWorldBound(Meshe.bounds, Matrix_World);
                TreeBatch.BoundSphere = new FSphere(Geometry.CaculateBoundRadius(TreeBatch.BoundBox), TreeBatch.BoundBox.center);
                AddBatch(TreeBatch);
            }
        }

        public void BuildMeshElements()
        {
            FTreeElement TreeElement;

            for (int i = 0; i < Transfroms.Count; ++i)
            {
                for (int j = 0; j < Tree.Meshes.Length; ++j)
                {
                    Mesh Meshe = Tree.Meshes[j];
                    float4x4 Matrix = float4x4.TRS(Transfroms[i].Position, quaternion.EulerXYZ(Transfroms[i].Rotation), Transfroms[i].Scale);

                    TreeElement.LODIndex = j;
                    TreeElement.Matrix_LocalToWorld = Matrix;
                    TreeElement.BoundBox = Geometry.CaculateWorldBound(Meshe.bounds, Matrix);
                    TreeElement.BoundSphere = new FSphere(Geometry.CaculateBoundRadius(TreeElement.BoundBox), TreeElement.BoundBox.center);

                    for (int k = 0; k < Meshe.subMeshCount; ++k)
                    {
                        TreeElement.MeshIndex = k;
                        TreeElement.MatIndex = Tree.LODInfo[j].MaterialSlot[k];
                        AddElement(TreeElement);
                    }
                }
            }
        }

        private void DoCulling()
        {

        }

        public void InitView()
        {

        }
    }
}
