using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using Landscape.FoliagePipeline;
using InfinityTech.Core.Geometry;
using UnityEngine.Rendering.Universal;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    internal unsafe class FoliagePass : ScriptableRenderPass
    {
        public override void OnCameraSetup(CommandBuffer cmdBuffer, ref RenderingData renderingData)
        {

        }

        public override void Execute(ScriptableRenderContext renderContext, ref RenderingData renderingData)
        {
            if (Application.isPlaying == false) { return; }

            var cmdBuffer = CommandBufferPool.Get();
            cmdBuffer.Clear();
            renderContext.ExecuteCommandBuffer(cmdBuffer);

            var planes = new NativeArray<FPlane>(6, Allocator.TempJob);
            var taskHandles = new NativeList<JobHandle>(256, Allocator.Temp);
            var sectorsBound = new NativeArray<FAABB>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);
            var boundsVisible = new NativeArray<byte>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);

            renderingData.cameraData.camera.TryGetCullingParameters(false, out var cullingParams);
            for (var i = 0; i < 6; ++i)
            {
                planes[i] = cullingParams.cameraProperties.GetCameraCullingPlane(i);
            }

            FPlane* planesPtr = (FPlane*)planes.GetUnsafePtr();
            float3 viewOrigin = renderingData.cameraData.camera.transform.position;
            var matrixProj = Geometry.GetProjectionMatrix(renderingData.cameraData.camera.fieldOfView, renderingData.cameraData.camera.pixelWidth, renderingData.cameraData.camera.pixelHeight, renderingData.cameraData.camera.nearClipPlane, renderingData.cameraData.camera.farClipPlane);

            #region InitViewBound
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                sectorsBound[i] = FoliageComponent.FoliageComponents[i].boundSector.bound;
            }

            if(sectorsBound.Length < 8)
            {
                FBoundCullingJob sectorCullingJob;
                sectorCullingJob.planes = planesPtr;
                sectorCullingJob.length = sectorsBound.Length;
                sectorCullingJob.visibleMap = boundsVisible;
                sectorCullingJob.sectorBounds = (FAABB*)sectorsBound.GetUnsafePtr();
                sectorCullingJob.Run();
            } else {
                FBoundCullingParallelJob sectorCullingJob;
                sectorCullingJob.planes = planesPtr;
                sectorCullingJob.visibleMap = boundsVisible;
                sectorCullingJob.sectorBounds = (FAABB*)sectorsBound.GetUnsafePtr();
                sectorCullingJob.Schedule(sectorsBound.Length, 8).Complete();
            }
            #endregion //InitViewBound

            #region InitViewFoliage
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0) { continue; }

                FoliageComponent foliageComponent = FoliageComponent.FoliageComponents[i];
                foliageComponent.InitView(viewOrigin, matrixProj, planesPtr, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion //InitViewFoliage

            #region InitViewCommand
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0) { continue; }
                FoliageComponent foliageComponent = FoliageComponent.FoliageComponents[i];
                foliageComponent.DispatchSetup(viewOrigin, matrixProj, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion //InitViewCommand

            #region InitViewCommand
            using (new ProfilingScope(cmdBuffer, ProfilingSampler.Get(EFoliageSamplerId.FoliageBatch)))
            {
                for (int i = 0; i < sectorsBound.Length; ++i)
                {
                    if (boundsVisible[i] == 0) { continue; }
                    FoliageComponent foliageComponent = FoliageComponent.FoliageComponents[i];
                    foliageComponent.DispatchDraw(cmdBuffer, 1);
                }
            }
            #endregion //InitViewCommand

            planes.Dispose();
            taskHandles.Dispose();
            sectorsBound.Dispose();
            boundsVisible.Dispose();

            renderContext.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();
            CommandBufferPool.Release(cmdBuffer);
        }

        public override void OnCameraCleanup(CommandBuffer cmdBuffer)
        {

        }
    }

    public class FoliageRenderer : ScriptableRendererFeature
    {
        private FoliagePass m_foliagePass;

        public override void Create()
        {
            m_foliagePass = new FoliagePass();
            m_foliagePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_foliagePass);
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}


