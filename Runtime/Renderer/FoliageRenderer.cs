using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.FoliagePipeline
{
    internal static class FoliageAmbientSH
    {
        private static readonly int[] s_Ids =
        {
            Shader.PropertyToID("_FoliageSHAr"), Shader.PropertyToID("_FoliageSHAg"), Shader.PropertyToID("_FoliageSHAb"),
            Shader.PropertyToID("_FoliageSHBr"), Shader.PropertyToID("_FoliageSHBg"), Shader.PropertyToID("_FoliageSHBb"),
            Shader.PropertyToID("_FoliageSHC")
        };

        internal static void Bind(MaterialPropertyBlock block)
        {
            SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
            for (int c = 0; c < 3; ++c)
            {
                block.SetVector(s_Ids[c], new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]));
                block.SetVector(s_Ids[c + 3], new Vector4(sh[c, 4], sh[c, 5], sh[c, 6] * 3f, sh[c, 7]));
            }
            block.SetVector(s_Ids[6], new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1f));
        }
    }

    internal unsafe class FoliagePass : ScriptableRenderPass
    {
        private class PassData
        {
            internal Camera camera;
            internal TextureHandle color;
            internal TextureHandle depth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (Application.isPlaying == false) { return; }

            var resourceData = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            using (var builder = renderGraph.AddUnsafePass<PassData>("Foliage", out var passData))
            {
                passData.camera = camera;
                passData.color = resourceData.activeColorTexture;
                passData.depth = resourceData.activeDepthTexture;
                builder.UseTexture(passData.color, AccessFlags.ReadWrite);
                builder.UseTexture(passData.depth, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmdBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmdBuffer.SetRenderTarget(data.color, data.depth);
                    ExecuteFoliage(cmdBuffer, data.camera);
                });
            }
        }

        private static void ExecuteFoliage(CommandBuffer cmdBuffer, Camera camera)
        {
            var planes = new NativeArray<FrustumPlane>(6, Allocator.TempJob);
            var taskHandles = new NativeList<JobHandle>(256, Allocator.Temp);
            var sectorsBound = new NativeArray<Aabb>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);
            var boundsVisible = new NativeArray<byte>(FoliageComponent.FoliageComponents.Count, Allocator.TempJob);

            camera.TryGetCullingParameters(false, out var cullingParams);
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
