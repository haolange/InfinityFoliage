using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public unsafe class FTreeSector
    {
        public FTree Tree;
        public int TreeIndex;
        public float CullDistance = 256;
        public List<FTransform> Transfroms;

        private NativeArray<int> ViewTreeBatchs;
        private NativeArray<int> TreeBatchIndexs;
        private NativeArray<float> TreeLODInfos;
        private NativeList<FTreeBatch> TreeBatchs;
        private NativeList<FTreeElement> TreeElements;
        private NativeList<FTreeElement> PassTreeElements;
        private NativeList<FTreeDrawCommand> TreeDrawCommands;


        public void Initialize()
        {
            TreeBatchs = new NativeList<FTreeBatch>(2048, Allocator.Persistent);
            TreeElements = new NativeList<FTreeElement>(4096, Allocator.Persistent);
        }

        public void Release()
        {
            TreeBatchs.Dispose();
            TreeElements.Dispose();
            TreeLODInfos.Dispose();
            ViewTreeBatchs.Dispose();
            TreeBatchIndexs.Dispose();
            PassTreeElements.Dispose();
            TreeDrawCommands.Dispose();
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

                TreeBatch.LODIndex = 0;
                TreeBatch.Matrix_World = Matrix_World;
                TreeBatch.BoundBox = Geometry.CaculateWorldBound(Meshe.bounds, Matrix_World);
                TreeBatch.BoundSphere = new FSphere(Geometry.CaculateBoundRadius(TreeBatch.BoundBox), TreeBatch.BoundBox.center);
                AddBatch(TreeBatch);
            }

            TreeLODInfos = new NativeArray<float>(Tree.LODInfo.Length, Allocator.Persistent);
            for (int j = 0; j < Tree.LODInfo.Length; ++j)
            {
                TreeLODInfos[j] = Tree.LODInfo[j].ScreenSize;
            }

            ViewTreeBatchs = new NativeArray<int>(TreeBatchs.Length, Allocator.Persistent);
            TreeDrawCommands = new NativeList<FTreeDrawCommand>(32, Allocator.Persistent);
            PassTreeElements = new NativeList<FTreeElement>(TreeBatchs.Length, Allocator.Persistent);
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
                        //TreeElement.InstanceGroupID = (TreeElement.MeshIndex >> 16) + (TreeElement.LODIndex << 16 | TreeElement.MatIndex);
                        AddElement(TreeElement);
                    }
                }
            }
            TreeElements.Sort();

            TreeBatchIndexs = new NativeArray<int>(TreeElements.Length, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle InitView(in float3 ViewOringin, in float4x4 Matrix_Proj, FPlane* Planes)
        {
            FTreeBatchCullingJob TreeBatchCullingJob = new FTreeBatchCullingJob();
            {
                TreeBatchCullingJob.Planes = Planes;
                TreeBatchCullingJob.NumLOD = TreeLODInfos.Length - 1;
                TreeBatchCullingJob.ViewOringin = ViewOringin;
                TreeBatchCullingJob.Matrix_Proj = Matrix_Proj;
                TreeBatchCullingJob.TreeLODInfos = (float*)TreeLODInfos.GetUnsafePtr();
                TreeBatchCullingJob.ViewTreeBatchs = ViewTreeBatchs;
                TreeBatchCullingJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
            }
            return TreeBatchCullingJob.Schedule(ViewTreeBatchs.Length, 256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle DispatchSetup()
        {
            FTreeDrawCommandBuildJob TreeDrawCommandBuildJob = new FTreeDrawCommandBuildJob();
            {
                TreeDrawCommandBuildJob.MaxLOD = Tree.LODInfo.Length - 1;
                TreeDrawCommandBuildJob.TreeElements = TreeElements;
                TreeDrawCommandBuildJob.TreeBatchs = (FTreeBatch*)TreeBatchs.GetUnsafeList()->Ptr;
                TreeDrawCommandBuildJob.ViewTreeBatchs = ViewTreeBatchs;
                TreeDrawCommandBuildJob.TreeBatchIndexs = TreeBatchIndexs;
                TreeDrawCommandBuildJob.PassTreeElements = PassTreeElements;
                TreeDrawCommandBuildJob.TreeDrawCommands = TreeDrawCommands;
            }
            return TreeDrawCommandBuildJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer CmdBuffer)
        {
            FTreeBatch TreeBatch;

            for (int i = 0; i < TreeElements.Length; ++i)
            {
                FTreeElement TreeElement = TreeElements[i];
                int ViewTreeBatch = ViewTreeBatchs[TreeElement.BatchIndex];

                if(ViewTreeBatch == 1)
                {
                    TreeBatch = TreeBatchs[TreeElement.BatchIndex];

                    if (TreeElement.LODIndex == TreeBatch.LODIndex)
                    {
                        Mesh Meshe = Tree.Meshes[TreeElement.LODIndex];
                        Material material = Tree.Materials[TreeElement.MatIndex];
                        CmdBuffer.DrawMesh(Meshe, TreeBatch.Matrix_World, material, TreeElement.MeshIndex, 0);
                    }
                }
            }

            PassTreeElements.Clear();
            TreeDrawCommands.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseView()
        {
            //ViewTreeBatchs.Dispose();
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool UseLODColor = false, in bool DrawSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            FTreeBatch TreeBatch;

            for (int i = 0; i < TreeBatchs.Length; ++i)
            {
                TreeBatch = TreeBatchs[i];
                ref Color LODColor = ref Geometry.LODColors[TreeBatch.LODIndex];

                Geometry.DrawBound(TreeBatch.BoundBox, UseLODColor ? LODColor : Color.blue);

                if (DrawSphere)
                {
                    UnityEditor.Handles.color = UseLODColor ? LODColor : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.up, TreeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.back, TreeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(TreeBatch.BoundSphere.center, Vector3.right, TreeBatch.BoundSphere.radius);
                }
            }
        }
#endif
    }
}
