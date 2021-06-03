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

        private ComputeBuffer m_TreeIndexBuffer;
        private ComputeBuffer m_TreeElementBuffer;
        private NativeArray<float> m_TreeLODInfos;
        private NativeList<FTreeElement> m_TreeElements;
        private NativeList<FTreeSection> m_TreeSections;
        private NativeArray<int> m_ViewTreeElements;
        private NativeArray<int> m_passTreeElements;
        private NativeList<FTreeSection> m_PassTreeSections;
        private NativeList<FMeshDrawCommand> m_TreeDrawCommands;

        public void Initialize()
        {
            m_TreeElements = new NativeList<FTreeElement>(2048, Allocator.Persistent);
            m_TreeSections = new NativeList<FTreeSection>(4096, Allocator.Persistent);
        }

        public void Release()
        {
            m_TreeLODInfos.Dispose();
            m_TreeElements.Dispose();
            m_TreeSections.Dispose();
            m_ViewTreeElements.Dispose();
            m_passTreeElements.Dispose();
            m_PassTreeSections.Dispose();
            m_TreeDrawCommands.Dispose();

            m_TreeIndexBuffer.Dispose();
            m_TreeElementBuffer.Dispose();
        }

        public void BuildTreeElement()
        {
            for (var i = 0; i < transforms.Count; ++i)
            {
                var mesh = tree.meshes[0];
                var matrixWorld = float4x4.TRS(transforms[i].position, quaternion.EulerXYZ(transforms[i].rotation), transforms[i].scale);

                FTreeElement treeElement;
                treeElement.meshIndex = 0;
                treeElement.matrix_World = matrixWorld;
                treeElement.boundBox = Geometry.CaculateWorldBound(mesh.bounds, matrixWorld);
                treeElement.boundSphere = new FSphere(Geometry.CaculateBoundRadius(treeElement.boundBox), treeElement.boundBox.center);
                m_TreeElements.Add(treeElement);
            }

            m_TreeLODInfos = new NativeArray<float>(tree.lODInfo.Length, Allocator.Persistent);
            for (var j = 0; j < tree.lODInfo.Length; ++j)
            {
                m_TreeLODInfos[j] = tree.lODInfo[j].screenSize;
            }

            m_TreeDrawCommands = new NativeList<FMeshDrawCommand>(6, Allocator.Persistent);
            m_ViewTreeElements = new NativeArray<int>(m_TreeElements.Length, Allocator.Persistent);
            m_PassTreeSections = new NativeList<FTreeSection>(m_TreeElements.Length, Allocator.Persistent);
        }

        public void BuildTreeSection()
        {
            for (var i = 0; i < transforms.Count; ++i)
            {
                FTreeSection treeSection;
                treeSection.batchIndex = i;

                for (var j = 0; j < tree.meshes.Length; ++j)
                {
                    treeSection.meshIndex = j;

                    for (var k = 0; k < tree.meshes[j].subMeshCount; ++k)
                    {
                        treeSection.sectionIndex = k;
                        treeSection.materialIndex = tree.lODInfo[j].materialSlot[k];
                        m_TreeSections.Add(treeSection);
                    }
                }
            }

            m_TreeSections.Sort();
            m_passTreeElements = new NativeArray<int>(m_TreeSections.Length, Allocator.Persistent);
        }

        public void BuildComputeBuffer()
        {
            m_TreeIndexBuffer = new ComputeBuffer(m_passTreeElements.Length, Marshal.SizeOf(typeof(int)));
            m_TreeElementBuffer = new ComputeBuffer(m_TreeElements.Length, Marshal.SizeOf(typeof(FTreeElement)));
            m_TreeElementBuffer.SetData(m_TreeElements.ToArray());
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
                treeViewProcessJob.treeElements = (FTreeElement*)m_TreeElements.GetUnsafeList()->Ptr;
                treeViewProcessJob.viewTreeElements = m_ViewTreeElements;
            }
            return treeViewProcessJob.Schedule(m_ViewTreeElements.Length, 256);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle DispatchSetup()
        {
            var treeDrawCommandBuildJob = new FTreeDrawCommandBuildJob();
            {
                treeDrawCommandBuildJob.maxLOD = tree.lODInfo.Length - 1;
                treeDrawCommandBuildJob.treeSections = m_TreeSections;
                treeDrawCommandBuildJob.treeElements = (FTreeElement*)m_TreeElements.GetUnsafeList()->Ptr;
                treeDrawCommandBuildJob.viewTreeElements = m_ViewTreeElements;
                treeDrawCommandBuildJob.passTreeElements = m_passTreeElements;
                treeDrawCommandBuildJob.passTreeSections = m_PassTreeSections;
                treeDrawCommandBuildJob.treeDrawCommands = m_TreeDrawCommands;
            }
            return treeDrawCommandBuildJob.Schedule();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            m_TreeIndexBuffer.SetData(m_passTreeElements);
            //cmdBuffer.SetComputeBufferData(m_IndexBuffer, m_TreeBatchIndexs);

            foreach (var treeDrawCmd in m_TreeDrawCommands)
            {
                Mesh mesh = tree.meshes[treeDrawCmd.meshIndex];
                Material material = tree.materials[treeDrawCmd.materialIndex];

                propertyBlock.Clear();
                propertyBlock.SetInt(TreeShaderID.offset, treeDrawCmd.countOffset.y);
                propertyBlock.SetBuffer(TreeShaderID.indexBuffer, m_TreeIndexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.primitiveBuffer, m_TreeElementBuffer);
                cmdBuffer.DrawMeshInstancedProcedural(mesh, treeDrawCmd.sectionIndex, material, passIndex, treeDrawCmd.countOffset.x, propertyBlock);
            }

            m_PassTreeSections.Clear();
            m_TreeDrawCommands.Clear();
        }

#if UNITY_EDITOR
        public void DrawBounds(in bool lodColorState = false, in bool showSphere = false)
        {
            if (Application.isPlaying == false) { return; }

            for (var i = 0; i < m_TreeElements.Length; ++i)
            {
                var treeBatch = m_TreeElements[i];
                ref var color = ref Geometry.LODColors[treeBatch.meshIndex];
                if (m_ViewTreeElements[i] == 0)
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
