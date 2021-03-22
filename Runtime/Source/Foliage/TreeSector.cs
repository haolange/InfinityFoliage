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
        public FMesh tree;
        public int treeIndex;
        public int cullDistance = 256;
        public List<FTransform> transforms;

        private NativeArray<int> m_viewTreeBatchs;
        private NativeArray<int> m_treeBatchIndexs;
        private NativeArray<float> m_treeLODInfos;
        private NativeList<FMeshBatch> m_treeBatchs;
        private NativeList<FMeshElement> m_treeElements;
        private NativeList<FMeshElement> m_passTreeElements;
        private NativeList<FMeshDrawCommand> m_treeDrawCommands;


        public void Initialize()
        {
            m_treeBatchs = new NativeList<FMeshBatch>(2048, Allocator.Persistent);
            m_treeElements = new NativeList<FMeshElement>(4096, Allocator.Persistent);
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

        public void AddBatch(in FMeshBatch treeBatch)
        {
            m_treeBatchs.Add(treeBatch);
        }

        public void RemoveBatch(in FMeshBatch treeBatch)
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

        public void AddElement(in FMeshElement treeElement)
        {
            m_treeElements.Add(treeElement);
        }

        public void RemoveElement(in FMeshElement treeElement)
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
                var mesh = tree.meshes[0];
                var matrixWorld = float4x4.TRS(transforms[i].position, quaternion.EulerXYZ(transforms[i].rotation), transforms[i].scale);

                FMeshBatch treeBatch;
                treeBatch.lODIndex = 0;
                treeBatch.matrix_World = matrixWorld;
                treeBatch.boundBox = Geometry.CaculateWorldBound(mesh.bounds, matrixWorld);
                treeBatch.boundSphere = new FSphere(Geometry.CaculateBoundRadius(treeBatch.boundBox), treeBatch.boundBox.center);
                AddBatch(treeBatch);
            }

            m_treeLODInfos = new NativeArray<float>(tree.lODInfo.Length, Allocator.Persistent);
            for (var j = 0; j < tree.lODInfo.Length; ++j)
            {
                m_treeLODInfos[j] = tree.lODInfo[j].screenSize;
            }

            m_viewTreeBatchs = new NativeArray<int>(m_treeBatchs.Length, Allocator.Persistent);
            m_treeDrawCommands = new NativeList<FMeshDrawCommand>(32, Allocator.Persistent);
            m_passTreeElements = new NativeList<FMeshElement>(m_treeBatchs.Length, Allocator.Persistent);
        }

        public void BuildMeshElement()
        {
            for (var i = 0; i < transforms.Count; ++i)
            {
                FMeshElement treeElement;
                treeElement.batchIndex = i;

                for (var j = 0; j < tree.meshes.Length; ++j)
                {
                    treeElement.lODIndex = j;

                    for (var k = 0; k < tree.meshes[j].subMeshCount; ++k)
                    {
                        treeElement.meshIndex = k;
                        treeElement.matIndex = tree.lODInfo[j].materialSlot[k];
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
                treeViewProcessJob.planes = planes;
                treeViewProcessJob.numLOD = m_treeLODInfos.Length - 1;
                treeViewProcessJob.viewOringin = viewPos;
                treeViewProcessJob.matrix_Proj = matrixProj;
                treeViewProcessJob.treeLODInfos = (float*)m_treeLODInfos.GetUnsafePtr();
                treeViewProcessJob.viewTreeBatchs = m_viewTreeBatchs;
                treeViewProcessJob.treeBatchs = (FMeshBatch*)m_treeBatchs.GetUnsafeList()->Ptr;
            }
            return treeViewProcessJob.Schedule(m_viewTreeBatchs.Length, 256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle DispatchSetup()
        {
            var treeDrawCommandBuildJob = new FTreeDrawCommandBuildJob();
            {
                treeDrawCommandBuildJob.maxLOD = tree.lODInfo.Length - 1;
                treeDrawCommandBuildJob.treeElements = m_treeElements;
                treeDrawCommandBuildJob.treeBatchs = (FMeshBatch*)m_treeBatchs.GetUnsafeList()->Ptr;
                treeDrawCommandBuildJob.viewTreeBatchs = m_viewTreeBatchs;
                treeDrawCommandBuildJob.treeBatchIndexs = m_treeBatchIndexs;
                treeDrawCommandBuildJob.passTreeElements = m_passTreeElements;
                treeDrawCommandBuildJob.treeDrawCommands = m_treeDrawCommands;
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
                var mesh = tree.meshes[treeDrawCommand.lODIndex];
                var material = tree.materials[treeDrawCommand.matIndex];
                
                for (var instanceId = 0; instanceId < treeDrawCommand.countOffset.x; ++instanceId)
                {
                    var index = m_treeBatchIndexs[treeDrawCommand.countOffset.y + instanceId];
                    var treeBatch = m_treeBatchs[index];
                    cmdBuffer.DrawMesh(mesh, treeBatch.matrix_World, material, treeDrawCommand.meshIndex, 0);
                }
            }

            m_passTreeElements.Clear();
            m_treeDrawCommands.Clear();
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool lodColorState = false, in bool showSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (var i = 0; i < m_treeBatchs.Length; ++i)
            {
                var treeBatch = m_treeBatchs[i];
                ref var color = ref Geometry.LODColors[treeBatch.lODIndex];

                Geometry.DrawBound(treeBatch.boundBox, lodColorState ? color : Color.blue);

                if (showSphere)
                {
                    UnityEditor.Handles.color = lodColorState ? color : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(treeBatch.boundSphere.center, Vector3.up, treeBatch.boundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeBatch.boundSphere.center, Vector3.back, treeBatch.boundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeBatch.boundSphere.center, Vector3.right, treeBatch.boundSphere.radius);
                }
            }
        }
#endif
    }
}
