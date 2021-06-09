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
        internal static int indexBuffer = Shader.PropertyToID("_TreeIndexBuffer");
        internal static int elementBuffer = Shader.PropertyToID("_TreeElementBuffer");
    }

    //[Serializable]
    internal class FTreeSubSector
    {
        public int meshIndex;
        public int[] sectionIndexs;
        public int[] materialIndexs;
        public NativeList<int> treeSections;
        public ComputeBuffer treeIndexBuffer;

        public void Initialize(in int sectionCount)
        {
            this.treeSections = new NativeList<int>(sectionCount, Allocator.Persistent);
            this.treeIndexBuffer = new ComputeBuffer(sectionCount, Marshal.SizeOf(typeof(int)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, ComputeBuffer treeElementBuffer, in FMesh tree, int count, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            Mesh mesh = tree.meshes[meshIndex];
            treeIndexBuffer.SetData(treeSections.AsArray(), 0, 0, count);

            for (int i = 0; i < sectionIndexs.Length; ++i)
            {
                Material material = tree.materials[materialIndexs[i]];

                propertyBlock.Clear();
                propertyBlock.SetBuffer(TreeShaderID.indexBuffer, treeIndexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.elementBuffer, treeElementBuffer);
                cmdBuffer.DrawMeshInstancedProcedural(mesh, sectionIndexs[i], material, passIndex, count, propertyBlock);
            }
        }

        public void Dispose()
        {
            treeSections.Dispose();
            treeIndexBuffer.Dispose();
        }
    }

    [Serializable]
    public unsafe class FTreeSector
    {
        public FMesh tree;
        public int treeIndex;
        public List<FTransform> transforms;

        private ComputeBuffer m_TreeElementBuffer;
        private List<FTreeSubSector> m_SubSectors;
        private NativeArray<float> m_TreeLODInfos;
        private NativeArray<int> m_ViewTreeElements;
        private NativeArray<FTreeElement> m_TreeElements;

        public void Initialize()
        {
            m_SubSectors = new List<FTreeSubSector>(8);
            m_TreeElements = new NativeArray<FTreeElement>(transforms.Count, Allocator.Persistent);

            for (int i = 0; i < transforms.Count; ++i)
            {
                float4x4 matrixWorld = float4x4.TRS(transforms[i].position, quaternion.EulerXYZ(transforms[i].rotation), transforms[i].scale);

                FTreeElement treeElement;
                treeElement.meshIndex = 0;
                treeElement.matrix_World = matrixWorld;
                treeElement.boundBox = Geometry.CaculateWorldBound(tree.boundBox, matrixWorld);
                treeElement.boundSphere = new FSphere(Geometry.CaculateBoundRadius(treeElement.boundBox), treeElement.boundBox.center);
                m_TreeElements[i] = treeElement;
            }
        }

        public void BuildRuntimeData()
        {
            m_TreeLODInfos = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
            m_ViewTreeElements = new NativeArray<int>(m_TreeElements.Length, Allocator.Persistent);

            for (int i = 0; i < tree.lODInfos.Length; ++i) 
            { 
                Mesh mesh = tree.meshes[i];
                ref FMeshLODInfo meshLODInfo = ref tree.lODInfos[i];

                m_TreeLODInfos[i] = meshLODInfo.screenSize;

                FTreeSubSector subSector = new FTreeSubSector();
                subSector.meshIndex = i;
                subSector.sectionIndexs = new int[mesh.subMeshCount];
                subSector.materialIndexs = new int[mesh.subMeshCount];

                for (int j = 0; j < mesh.subMeshCount; ++j) 
                {
                    subSector.sectionIndexs[j] = j;
                    subSector.materialIndexs[j] = meshLODInfo.materialSlot[j];    
                }
                m_SubSectors.Add(subSector);
            }

            foreach (var subSector in m_SubSectors)
            {
                subSector.Initialize(m_TreeElements.Length);
            }

            m_TreeElementBuffer = new ComputeBuffer(m_TreeElements.Length, Marshal.SizeOf(typeof(FTreeElement)));
            m_TreeElementBuffer.SetData(m_TreeElements);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InitView(in float cullDistance, in float3 viewPos, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            var treeElementCullingJob = new FTreeElementCullingJob();
            {
                treeElementCullingJob.planes = planes;
                treeElementCullingJob.numLOD = m_TreeLODInfos.Length - 1;
                treeElementCullingJob.viewOringin = viewPos;
                treeElementCullingJob.matrix_Proj = matrixProj;
                treeElementCullingJob.maxDistance = cullDistance;
                treeElementCullingJob.treeLODInfos = (float*)m_TreeLODInfos.GetUnsafePtr();
                treeElementCullingJob.treeElements = (FTreeElement*)m_TreeElements.GetUnsafePtr();
                treeElementCullingJob.viewTreeElements = m_ViewTreeElements;
            }
            taskHandles.Add(treeElementCullingJob.Schedule(m_ViewTreeElements.Length, 256));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchSetup(in NativeList<JobHandle> taskHandles)
        {
            foreach (var subSector in m_SubSectors)
            {
                subSector.treeSections.Clear();

                var treeLODSelectJob = new FTreeLODSelectJob();
                {
                    treeLODSelectJob.meshIndex = subSector.meshIndex;
                    treeLODSelectJob.treeElements = m_TreeElements;
                    treeLODSelectJob.viewTreeElements = m_ViewTreeElements;
                    treeLODSelectJob.passTreeSections = subSector.treeSections;
                }
                taskHandles.Add(treeLODSelectJob.Schedule());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            foreach (var subSector in m_SubSectors)
            {
                int count = subSector.treeSections.Length; if (count == 0)  { continue; }
                subSector.DispatchDraw(cmdBuffer, m_TreeElementBuffer, tree, count, passIndex, propertyBlock);
            }
        }

        public void Release()
        {
            m_TreeLODInfos.Dispose();
            m_TreeElements.Dispose();
            m_ViewTreeElements.Dispose();
            m_TreeElementBuffer.Dispose();

            foreach (var subSector in m_SubSectors)
            {
                subSector.Dispose();
            }
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
