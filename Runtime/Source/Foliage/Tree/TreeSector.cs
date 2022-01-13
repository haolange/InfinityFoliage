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
        internal static int IndexBuffer = Shader.PropertyToID("_TreeIndexBuffer");
        internal static int ElementBuffer = Shader.PropertyToID("_TreeElementBuffer");
    }

    //[Serializable]
    internal class FTreeSubSector
    {
        public int meshIndex;
        public int[] sectionIndexs;
        public int[] materialIndexs;
        public ComputeBuffer indexBuffer;
        public NativeList<int> treeSections;

        public void Initialize(in int sectionCount)
        {
            this.treeSections = new NativeList<int>(sectionCount, Allocator.Persistent);
            this.indexBuffer = new ComputeBuffer(sectionCount, Marshal.SizeOf(typeof(int)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, ComputeBuffer elementBuffer, in FMesh tree, int count, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            Mesh mesh = tree.meshes[meshIndex];
            indexBuffer.SetData(treeSections.AsArray(), 0, 0, count);

            for (int i = 0; i < sectionIndexs.Length; ++i)
            {
                Material material = tree.materials[materialIndexs[i]];

                propertyBlock.Clear();
                propertyBlock.SetBuffer(TreeShaderID.IndexBuffer, indexBuffer);
                propertyBlock.SetBuffer(TreeShaderID.ElementBuffer, elementBuffer);
                cmdBuffer.DrawMeshInstancedProcedural(mesh, sectionIndexs[i], material, passIndex, count, propertyBlock);
            }
        }

        public void Dispose()
        {
            indexBuffer.Dispose();
            treeSections.Dispose();
        }
    }

    [Serializable]
    public class FTreeSector
    {
        public FMesh tree;
        public int treeIndex;
        public List<FTransform> transforms;

        private ComputeBuffer m_ElementBuffer;
        private NativeArray<int> m_ViewElements;
        private NativeArray<float> m_TreeLODInfos;
        private List<FTreeSubSector> m_SubSectors;
        private NativeArray<FTreeElement> m_TreeElements;

        public void Initialize()
        {
            m_SubSectors = new List<FTreeSubSector>(8);
            m_TreeElements = new NativeArray<FTreeElement>(transforms.Count, Allocator.Persistent);

            NativeArray<FTransform> nativeTransforms = transforms.ToNativeArray(Allocator.TempJob);
            var treeScatterJob = new FTreeScatterJob();
            {
                treeScatterJob.boundBox = tree.boundBox;
                treeScatterJob.transforms = nativeTransforms;
                treeScatterJob.treeElements = m_TreeElements;
            }
            treeScatterJob.Schedule(transforms.Count, 128).Complete();
            nativeTransforms.Dispose();
            
            /*for (int i = 0; i < transforms.Count; ++i)
            {
                float4x4 matrixWorld = float4x4.TRS(transforms[i].position, quaternion.EulerXYZ(transforms[i].rotation), transforms[i].scale);

                FTreeElement treeElement;
                treeElement.meshIndex = 0;
                treeElement.matrix_World = matrixWorld;
                treeElement.boundBox = Geometry.CaculateWorldBound(tree.boundBox, matrixWorld);
                treeElement.boundSphere = new FSphere(Geometry.CaculateBoundRadius(treeElement.boundBox), treeElement.boundBox.center);
                m_TreeElements[i] = treeElement;
            }*/

            transforms = null;
        }

        public void BuildRuntimeData()
        {
            m_TreeLODInfos = new NativeArray<float>(tree.lODInfos.Length, Allocator.Persistent);
            m_ViewElements = new NativeArray<int>(m_TreeElements.Length, Allocator.Persistent);

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

            m_ElementBuffer = new ComputeBuffer(m_TreeElements.Length, Marshal.SizeOf(typeof(FTreeElement)));
            m_ElementBuffer.SetData(m_TreeElements);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void InitView(in float cullDistance, in float3 viewOrigin, in float4x4 matrixProj, in FPlane* planes, in NativeList<JobHandle> taskHandles)
        {
            var treeCullingJob = new FTreeCullingJob();
            {
                treeCullingJob.planes = planes;
                treeCullingJob.viewOringin = viewOrigin;
                treeCullingJob.maxDistance = cullDistance;
                treeCullingJob.treeElements = (FTreeElement*)m_TreeElements.GetUnsafePtr();
                treeCullingJob.viewTreeElements = m_ViewElements;
            }
            taskHandles.Add(treeCullingJob.Schedule(m_ViewElements.Length, 256));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void DispatchSetup(in float3 viewOrigin, in float4x4 matrixProj, in NativeList<JobHandle> taskHandles)
        {
            var treeComputeJob = new FTreeComputeLODJob();
            {
                treeComputeJob.numLOD = m_TreeLODInfos.Length - 1;
                treeComputeJob.viewOringin = viewOrigin;
                treeComputeJob.matrix_Proj = matrixProj;
                treeComputeJob.treeLODInfos = (float*)m_TreeLODInfos.GetUnsafePtr();
                treeComputeJob.treeElements = (FTreeElement*)m_TreeElements.GetUnsafePtr();
            }
            JobHandle lodJobHandle = treeComputeJob.Schedule(m_ViewElements.Length, 256);
            
            foreach (var subSector in m_SubSectors)
            {
                subSector.treeSections.Clear();

                var treeSelectLODJob = new FTreeSelectLODJob();
                {
                    treeSelectLODJob.meshIndex = subSector.meshIndex;
                    treeSelectLODJob.treeElements = m_TreeElements;
                    treeSelectLODJob.viewTreeElements = m_ViewElements;
                    treeSelectLODJob.passTreeSections = subSector.treeSections;
                }
                taskHandles.Add(treeSelectLODJob.Schedule(lodJobHandle));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchDraw(CommandBuffer cmdBuffer, in int passIndex, MaterialPropertyBlock propertyBlock)
        {
            foreach (var subSector in m_SubSectors)
            {
                int count = subSector.treeSections.Length; if (count == 0)  { continue; }
                subSector.DispatchDraw(cmdBuffer, m_ElementBuffer, tree, count, passIndex, propertyBlock);
            }
        }

        public void Release()
        {
            m_TreeLODInfos.Dispose();
            m_TreeElements.Dispose();
            m_ViewElements.Dispose();
            m_ElementBuffer.Dispose();

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
                var treeElement = m_TreeElements[i];
                ref var color = ref Geometry.LODColors[treeElement.meshIndex];
                if (m_ViewElements[i] == 0)
                {
                    continue;
                }

                Geometry.DrawBound(treeElement.boundBox, lodColorState ? color : Color.blue);

                if (showSphere)
                {
                    UnityEditor.Handles.color = lodColorState ? color : Color.yellow;
                    UnityEditor.Handles.DrawWireDisc(treeElement.boundSphere.center, Vector3.up, treeElement.boundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeElement.boundSphere.center, Vector3.back, treeElement.boundSphere.radius);
                    UnityEditor.Handles.DrawWireDisc(treeElement.boundSphere.center, Vector3.right, treeElement.boundSphere.radius);
                }
            }
        }
#endif
    }
}
