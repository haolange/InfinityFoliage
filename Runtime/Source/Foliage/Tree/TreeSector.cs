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
        public FTree tree;
        public int treeIndex;
        public float cullDistance = 256;
        public List<FTransform> transforms;

        private NativeArray<int> m_viewTreeBatchs;
        private NativeArray<int> m_treeBatchIndexs;
        private NativeArray<float> m_treeLODInfos;
        private NativeList<FTreeBatch> m_treeBatchs;
        private NativeList<FTreeElement> m_treeElements;
        private NativeList<FTreeElement> m_passTreeElements;
        private NativeList<FTreeDrawCommand> m_treeDrawCommands;


        public void Initialize()
        {
            m_treeBatchs = new NativeList<FTreeBatch>(2048, Allocator.Persistent);
            m_treeElements = new NativeList<FTreeElement>(4096, Allocator.Persistent);
        }

        public void Release()
        {
            m_treeBatchs.Dispose();
            m_treeElements.Dispose();
            m_treeLODInfos.Dispose();
            m_viewTreeBatchs.Dispose();
            m_treeBatchIndexs.Dispose();
            m_passTreeElements.Dispose();
            m_treeDrawCommands.Dispose();
        }

        public void AddBatch(in FTreeBatch treeBatch)
        {
            m_treeBatchs.Add(treeBatch);
        }

        public void RemoveBatch(in FTreeBatch treeBatch)
        {
            var index = m_treeBatchs.IndexOf(treeBatch);
            if (index >= 0)
            {
                m_treeBatchs.RemoveAt(index);
            }
        }

        public void ClearBatch()
        {
            m_treeBatchs.Clear();
        }

        public void AddElement(in FTreeElement treeElement)
        {
            m_treeElements.Add(treeElement);
        }

        public void RemoveElement(in FTreeElement treeElement)
        {
            var index = m_treeElements.IndexOf(treeElement);
            if (index >= 0)
            {
                m_treeElements.RemoveAt(index);
            }
        }

        public void ClearElement()
        {
            m_treeElements.Clear();
        }

        public void BuildMeshBatch()
        {
            for (var i = 0; i < transforms.Count; ++i)
            {
                var mesh = tree.Meshes[0];
                var matrixWorld = float4x4.TRS(transforms[i].Position, quaternion.EulerXYZ(transforms[i].Rotation), transforms[i].Scale);

                FTreeBatch treeBatch;
                treeBatch.LODIndex = 0;
                treeBatch.Matrix_World = matrixWorld;
                treeBatch.BoundBox = Geometry.CaculateWorldBound(mesh.bounds, matrixWorld);
                treeBatch.BoundSphere = new FSphere(Geometry.CaculateBoundRadius(treeBatch.BoundBox), treeBatch.BoundBox.center);
                AddBatch(treeBatch);
            }

            m_treeLODInfos = new NativeArray<float>(tree.LODInfo.Length, Allocator.Persistent);
            for (var j = 0; j < tree.LODInfo.Length; ++j)
            {
                m_treeLODInfos[j] = tree.LODInfo[j].ScreenSize;
            }

            m_viewTreeBatchs = new NativeArray<int>(m_treeBatchs.Length, Allocator.Persistent);
            m_treeDrawCommands = new NativeList<FTreeDrawCommand>(32, Allocator.Persistent);
            m_passTreeElements = new NativeList<FTreeElement>(m_treeBatchs.Length, Allocator.Persistent);
        }

        public void BuildMeshElement()
        {
            for (var i = 0; i < transforms.Count; ++i)
            {
                FTreeElement treeElement;
                treeElement.BatchIndex = i;

                for (var j = 0; j < tree.Meshes.Length; ++j)
                {
                    treeElement.LODIndex = j;

                    for (var k = 0; k < tree.Meshes[j].subMeshCount; ++k)
                    {
                        treeElement.MeshIndex = k;
                        treeElement.MatIndex = tree.LODInfo[j].MaterialSlot[k];
                        //TreeElement.InstanceGroupID = (TreeElement.MeshIndex >> 16) + (TreeElement.LODIndex << 16 | TreeElement.MatIndex);
                        AddElement(treeElement);
                    }
                }
            }
            
            m_treeElements.Sort();

            m_treeBatchIndexs = new NativeArray<int>(m_treeElements.Length, Allocator.Persistent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle InitView(in float3 viewPos, in float4x4 matrixProj, FPlane* planes)
        {
            var treeViewProcessJob = new FTreeBatchCullingJob();
            {
                treeViewProcessJob.Planes = planes;
                treeViewProcessJob.NumLOD = m_treeLODInfos.Length - 1;
                treeViewProcessJob.ViewOringin = viewPos;
                treeViewProcessJob.Matrix_Proj = matrixProj;
                treeViewProcessJob.TreeLODInfos = (float*)m_treeLODInfos.GetUnsafePtr();
                treeViewProcessJob.ViewTreeBatchs = m_viewTreeBatchs;
                treeViewProcessJob.TreeBatchs = (FTreeBatch*)m_treeBatchs.GetUnsafeList()->Ptr;
            }
            return treeViewProcessJob.Schedule(m_viewTreeBatchs.Length, 256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle DispatchSetup()
        {
            var treeDrawCommandBuildJob = new FTreeDrawCommandBuildJob();
            {
                treeDrawCommandBuildJob.MaxLOD = tree.LODInfo.Length - 1;
                treeDrawCommandBuildJob.TreeElements = m_treeElements;
                treeDrawCommandBuildJob.TreeBatchs = (FTreeBatch*)m_treeBatchs.GetUnsafeList()->Ptr;
                treeDrawCommandBuildJob.ViewTreeBatchs = m_viewTreeBatchs;
                treeDrawCommandBuildJob.TreeBatchIndexs = m_treeBatchIndexs;
                treeDrawCommandBuildJob.PassTreeElements = m_passTreeElements;
                treeDrawCommandBuildJob.TreeDrawCommands = m_treeDrawCommands;
            }
            return treeDrawCommandBuildJob.Schedule();
        }

        /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer)
        {
            foreach (var treeElement in m_treeElements)
            {
                var viewTreeBatch = m_viewTreeBatchs[treeElement.BatchIndex];

                if(viewTreeBatch == 1)
                {
                    var treeBatch = m_treeBatchs[treeElement.BatchIndex];

                    if (treeElement.LODIndex == treeBatch.LODIndex)
                    {
                        var mesh = tree.Meshes[treeElement.LODIndex];
                        var material = tree.Materials[treeElement.MatIndex];
                        cmdBuffer.DrawMesh(mesh, treeBatch.Matrix_World, material, treeElement.MeshIndex, 0);
                    }
                }
            }

            m_passTreeElements.Clear();
            m_treeDrawCommands.Clear();
        }*/

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer)
        {
            foreach (var treeDrawCommand in m_treeDrawCommands)
            {
                var mesh = tree.Meshes[treeDrawCommand.LODIndex];
                var material = tree.Materials[treeDrawCommand.MatIndex];
                
                for (var instanceId = 0; instanceId < treeDrawCommand.CountOffset.x; ++instanceId)
                {
                    var index = m_treeBatchIndexs[treeDrawCommand.CountOffset.y + instanceId];
                    var treeBatch = m_treeBatchs[index];
                    cmdBuffer.DrawMesh(mesh, treeBatch.Matrix_World, material, treeDrawCommand.MeshIndex, 0);
                }
            }

            m_passTreeElements.Clear();
            m_treeDrawCommands.Clear();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseView()
        {
            //m_viewTreeBatchs.Dispose();
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool lodColorState = false, in bool showSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (var i = 0; i < m_treeBatchs.Length; ++i)
            {
                var treeBatch = m_treeBatchs[i];
                ref var color = ref Geometry.LODColors[treeBatch.LODIndex];

                Geometry.DrawBound(treeBatch.BoundBox, lodColorState ? color : Color.blue);

                if (showSphere)
                {
                    UnityEditor.Handles.color = lodColorState ? color : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(treeBatch.BoundSphere.center, Vector3.up, treeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeBatch.BoundSphere.center, Vector3.back, treeBatch.BoundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeBatch.BoundSphere.center, Vector3.right, treeBatch.BoundSphere.radius);
                }
            }
        }
#endif
    }
}
