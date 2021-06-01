using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    internal static class TreeShaderID
    {
        internal static int offset = Shader.PropertyToID("_TreeIndexOffset");
        internal static int indexBuffer = Shader.PropertyToID("_TreeIndexBuffer");
        internal static int primitiveBuffer = Shader.PropertyToID("_TreeBatchBuffer");
    }

    [Serializable]
    public unsafe class FTreeSector
    {
        public FMesh tree;
        public int treeIndex;
        public int cullDistance = 256;
        public List<FTransform> transforms;

        private NativeArray<int> m_ViewTreeBatchs;
        private NativeArray<int> m_TreeBatchIndexs;
        private NativeArray<float> m_TreeLODInfos;
        private NativeList<FMeshBatch> m_TreeBatchs;
        private NativeList<FMeshElement> m_TreeElements;
        private NativeList<FMeshElement> m_PassTreeElements;
        private NativeList<FMeshDrawCommand> m_TreeDrawCommands;

        private ComputeBuffer m_TreeBuffer;
        private ComputeBuffer m_IndexBuffer;

        public void Initialize()
        {
            m_TreeBatchs = new NativeList<FMeshBatch>(2048, Allocator.Persistent);
            m_TreeElements = new NativeList<FMeshElement>(4096, Allocator.Persistent);
        }

        public void Release()
        {
            m_TreeBuffer.Dispose();
            m_TreeBatchs.Dispose();
            m_IndexBuffer.Dispose();
            m_TreeLODInfos.Dispose();
            m_TreeElements.Dispose();
            m_ViewTreeBatchs.Dispose();
            m_TreeBatchIndexs.Dispose();
            m_PassTreeElements.Dispose();
            m_TreeDrawCommands.Dispose();
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
                m_TreeBatchs.Add(treeBatch);
            }

            m_TreeBuffer = new ComputeBuffer(m_TreeBatchs.Length, Marshal.SizeOf(typeof(FMeshBatch)));
            m_TreeBuffer.SetData(m_TreeBatchs.ToArray());

            m_TreeLODInfos = new NativeArray<float>(tree.lODInfo.Length, Allocator.Persistent);
            for (var j = 0; j < tree.lODInfo.Length; ++j)
            {
                m_TreeLODInfos[j] = tree.lODInfo[j].screenSize;
            }

            m_ViewTreeBatchs = new NativeArray<int>(m_TreeBatchs.Length, Allocator.Persistent);
            m_TreeDrawCommands = new NativeList<FMeshDrawCommand>(32, Allocator.Persistent);
            m_PassTreeElements = new NativeList<FMeshElement>(m_TreeBatchs.Length, Allocator.Persistent);
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
                        m_TreeElements.Add(treeElement);
                    }
                }
            }

            m_TreeElements.Sort();

            m_TreeBatchIndexs = new NativeArray<int>(m_TreeElements.Length, Allocator.Persistent);
            m_IndexBuffer = new ComputeBuffer(m_TreeBatchIndexs.Length, Marshal.SizeOf(typeof(int)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle InitView(in float cullDistance, in float3 viewPos, in float4x4 matrixProj, FPlane* planes)
        {
            var treeViewProcessJob = new FTreeBatchCullingJob();
            {
                treeViewProcessJob.planes = planes;
                treeViewProcessJob.numLOD = m_TreeLODInfos.Length - 1;
                treeViewProcessJob.viewOringin = viewPos;
                treeViewProcessJob.matrix_Proj = matrixProj;
                treeViewProcessJob.maxDistance = cullDistance;
                treeViewProcessJob.treeLODInfos = (float*)m_TreeLODInfos.GetUnsafePtr();
                treeViewProcessJob.viewTreeBatchs = m_ViewTreeBatchs;
                treeViewProcessJob.treeBatchs = (FMeshBatch*)m_TreeBatchs.GetUnsafeList()->Ptr;
            }
            return treeViewProcessJob.Schedule(m_ViewTreeBatchs.Length, 256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle DispatchSetup()
        {
            var treeDrawCommandBuildJob = new FTreeDrawCommandBuildJob();
            {
                treeDrawCommandBuildJob.maxLOD = tree.lODInfo.Length - 1;
                treeDrawCommandBuildJob.treeElements = m_TreeElements;
                treeDrawCommandBuildJob.treeBatchs = (FMeshBatch*)m_TreeBatchs.GetUnsafeList()->Ptr;
                treeDrawCommandBuildJob.viewTreeBatchs = m_ViewTreeBatchs;
                treeDrawCommandBuildJob.treeBatchIndexs = m_TreeBatchIndexs;
                treeDrawCommandBuildJob.passTreeElements = m_PassTreeElements;
                treeDrawCommandBuildJob.treeDrawCommands = m_TreeDrawCommands;
            }
            return treeDrawCommandBuildJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            m_IndexBuffer.SetData(m_TreeBatchIndexs);
            //cmdBuffer.SetComputeBufferData(m_IndexBuffer, m_TreeBatchIndexs);

            foreach (var treeDrawCmd in m_TreeDrawCommands)
            {
                Mesh mesh = tree.meshes[treeDrawCmd.lODIndex];
                Material material = tree.materials[treeDrawCmd.matIndex];

                propertyBlock.Clear();
                propertyBlock.SetInt(TreeShaderID.offset, treeDrawCmd.countOffset.y);
                propertyBlock.SetBuffer(TreeShaderID.indexBuffer, m_IndexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.primitiveBuffer, m_TreeBuffer);
                cmdBuffer.DrawMeshInstancedProcedural(mesh, treeDrawCmd.meshIndex, material, passIndex, treeDrawCmd.countOffset.x, propertyBlock);
            }

            m_PassTreeElements.Clear();
            m_TreeDrawCommands.Clear();
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool lodColorState = false, in bool showSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (var i = 0; i < m_TreeBatchs.Length; ++i)
            {
                var treeBatch = m_TreeBatchs[i];
                ref var color = ref Geometry.LODColors[treeBatch.lODIndex];
                if (m_ViewTreeBatchs[i] == 0)
                {
                    continue;
                }

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
