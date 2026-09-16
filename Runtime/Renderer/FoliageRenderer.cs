using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
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

            Camera camera = renderingData.cameraData.camera;
            var planes = new NativeArray<FrustumPlane>(6, Allocator.TempJob);
            var taskHandles = new NativeList<JobHandle>(256, Allocator.Temp);
            var sectorsBound = new NativeArray<Aabb>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);
            var boundsVisible = new NativeArray<byte>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);

            renderingData.cameraData.camera.TryGetCullingParameters(false, out var cullingParams);
            for (var i = 0; i < 6; ++i)
            {
                planes[i] = cullingParams.cameraProperties.GetCameraCullingPlane(i);
            }

            FrustumPlane* planesPtr = (FrustumPlane*)planes.GetUnsafePtr();
            float3 viewOrigin = camera.transform.position;
            var matrixProj = Geometry.GetProjectionMatrix(camera.fieldOfView, camera.pixelWidth, camera.pixelHeight, camera.nearClipPlane, camera.farClipPlane);

            #region InitViewBound
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                sectorsBound[i] = FoliageComponent.FoliageComponents[i].boundSector.bound;
            }

            if (sectorsBound.Length < 8)
            {
                var sectorCullingJob = new BoundCullingJob();
                {
                    sectorCullingJob.planes = planesPtr;
                    sectorCullingJob.length = sectorsBound.Length;
                    sectorCullingJob.visibleMap = boundsVisible;
                    sectorCullingJob.sectorBounds = (Aabb*)sectorsBound.GetUnsafePtr();
                }
                sectorCullingJob.Run();
            }
            else
            {
                var sectorCullingJob = new BoundCullingParallelJob();
                {
                    sectorCullingJob.planes = planesPtr;
                    sectorCullingJob.visibleMap = boundsVisible;
                    sectorCullingJob.sectorBounds = (Aabb*)sectorsBound.GetUnsafePtr();
                }
                sectorCullingJob.Schedule(sectorsBound.Length, 8).Complete();
            }
            #endregion

            #region InitViewFoliage
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0 && FoliageComponent.FoliageComponents[i].foliageType != EFoliageType.Grass) { continue; }
                FoliageComponent.FoliageComponents[i].InitView(viewOrigin, matrixProj, planesPtr, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion

            #region InitViewCommand
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0) { continue; }
                FoliageComponent.FoliageComponents[i].DispatchSetup(camera, viewOrigin, matrixProj, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();

            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0 && FoliageComponent.FoliageComponents[i].foliageType != EFoliageType.Grass) { continue; }
                FoliageComponent.FoliageComponents[i].FlushPendingUploads();
            }
            #endregion

            #region DispatchDraw
            using (new ProfilingScope(cmdBuffer, ProfilingSampler.Get(EFoliageSamplerId.FoliageBatch)))
            {
                for (int i = 0; i < sectorsBound.Length; ++i)
                {
                    if (boundsVisible[i] == 0) { continue; }
                    FoliageComponent.FoliageComponents[i].DispatchDraw(cmdBuffer, 1);
                }
            }
            #endregion

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
            FoliageLogicAsserts.Evaluate();
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
