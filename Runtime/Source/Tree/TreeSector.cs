using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public unsafe class FTreeSector
    {
        public FTree Tree;
        public int TreeIndex;
        public float CullDistance = 256;
        public List<FTransform> Transfroms;

        private NativeList<FTreeBatch> TreeBatchs;
        private NativeArray<int> ViewTreeBatchs;
        private NativeList<FTreeElement> TreeElements;


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
                TreeElement.BatchIndex = i;

                for (int j = 0; j < Tree.Meshes.Length; ++j)
                {
                    TreeElement.LODIndex = j;

                    for (int k = 0; k < Tree.Meshes[j].subMeshCount; ++k)
                    {
                        TreeElement.MeshIndex = k;
                        TreeElement.MatIndex = Tree.LODInfo[j].MaterialSlot[k];
                        AddElement(TreeElement);
                    }
                }
            }

            TreeElements.Sort();
        }

        public JobHandle InitView(FPlane* Planes)
        {
            ViewTreeBatchs = new NativeArray<int>(TreeBatchs.Length, Allocator.TempJob);

            FTreeBatchCullingParallelJob TreeBatchCullingParallelJob = new FTreeBatchCullingParallelJob();
            {
                TreeBatchCullingParallelJob.Planes = Planes;
                TreeBatchCullingParallelJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                TreeBatchCullingParallelJob.ViewTreeBatchs = ViewTreeBatchs;
            }
            JobHandle JobRef = TreeBatchCullingParallelJob.Schedule(ViewTreeBatchs.Length, 256);

            /*if(ViewTreeBatchs.Length >= 1024)
            {
                FTreeBatchCullingParallelJob TreeBatchCullingParallelJob = new FTreeBatchCullingParallelJob();
                {
                    TreeBatchCullingParallelJob.Planes = Planes;
                    TreeBatchCullingParallelJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                    TreeBatchCullingParallelJob.ViewTreeBatchs = ViewTreeBatchs;
                }
                JobRef = TreeBatchCullingParallelJob.Schedule(ViewTreeBatchs.Length, 256);
            } else if(ViewTreeBatchs.Length >= 512) {
                FTreeBatchCullingParallelJob TreeBatchCullingParallelJob = new FTreeBatchCullingParallelJob();
                {
                    TreeBatchCullingParallelJob.Planes = Planes;
                    TreeBatchCullingParallelJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                    TreeBatchCullingParallelJob.ViewTreeBatchs = ViewTreeBatchs;
                }
                JobRef = TreeBatchCullingParallelJob.Schedule(ViewTreeBatchs.Length, 128);
            } else if (ViewTreeBatchs.Length >= 256) {
                FTreeBatchCullingParallelJob TreeBatchCullingParallelJob = new FTreeBatchCullingParallelJob();
                {
                    TreeBatchCullingParallelJob.Planes = Planes;
                    TreeBatchCullingParallelJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                    TreeBatchCullingParallelJob.ViewTreeBatchs = ViewTreeBatchs;
                }
                JobRef = TreeBatchCullingParallelJob.Schedule(ViewTreeBatchs.Length, 64);
            } else {
                FTreeBatchCullingJob TreeBatchCullingJob = new FTreeBatchCullingJob();
                {
                    TreeBatchCullingJob.Length = ViewTreeBatchs.Length;
                    TreeBatchCullingJob.Planes = Planes;
                    TreeBatchCullingJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                    TreeBatchCullingJob.ViewTreeBatchs = ViewTreeBatchs;
                }
                JobRef = TreeBatchCullingJob.Schedule();
            }*/

            return JobRef;
        }

        public void DrawTree(CommandBuffer CmdBuffer)
        {
            FTreeBatch TreeBatch;

            for (int i = 0; i < TreeElements.Length; ++i)
            {
                FTreeElement TreeElement = TreeElements[i];
                int ViewTreeBatch = ViewTreeBatchs[TreeElement.BatchIndex];

                if(ViewTreeBatch == 1)
                {
                    TreeBatch = TreeBatchs[TreeElement.BatchIndex];

                    if (TreeElement.LODIndex == Tree.LODInfo.Length - 1)
                    {
                        Mesh Meshe = Tree.Meshes[TreeElement.LODIndex];
                        Material material = Tree.Materials[TreeElement.MatIndex];
                        CmdBuffer.DrawMesh(Meshe, TreeBatch.Matrix_World, material, TreeElement.MeshIndex, 0);
                    }
                }
            }
        }

        public void ReleaseView()
        {
            ViewTreeBatchs.Dispose();
        }

#if UNITY_EDITOR
        public static Color[] LODColors = new Color[7] { new Color(1, 1, 1, 1), new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) };

        public void DrawBounds(in bool LODColor = false, in bool DrawSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            FTreeBatch TreeBatch;

            for (int i = 0; i < TreeElements.Length; ++i)
            {
                TreeBatch = TreeBatchs[TreeElements[i].BatchIndex];
                Geometry.DrawBound(TreeBatch.BoundBox, LODColor ? LODColors[TreeElements[i].LODIndex] : Color.blue);

                if (DrawSphere)
                {
                    UnityEditor.Handles.color = LODColor ? LODColors[TreeElements[i].LODIndex] : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.up, TreeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.back, TreeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.right, TreeBatch.BoundSphere.radius);
                }
            }
        }
#endif
    }
}
