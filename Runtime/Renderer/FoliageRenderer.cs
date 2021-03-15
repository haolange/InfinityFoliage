using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using Landscape.FoliagePipeline;
using InfinityTech.Core.Geometry;
using UnityEngine.Rendering.Universal;
using Unity.Collections.LowLevel.Unsafe;

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

        #region InitViewContext
        var planes = new NativeArray<FPlane>(6, Allocator.TempJob);
        renderingData.cameraData.camera.TryGetCullingParameters(false, out var cullingParams);
        for (var i = 0; i < 6; ++i)
        {
            planes[i] = cullingParams.cameraProperties.GetCameraCullingPlane(i);
        }

        var cullHandles = new NativeList<JobHandle>(256, Allocator.Temp);
        var matrixProj = Geometry.GetProjectionMatrix(renderingData.cameraData.camera.fieldOfView, renderingData.cameraData.camera.pixelWidth, renderingData.cameraData.camera.pixelHeight, renderingData.cameraData.camera.nearClipPlane, renderingData.cameraData.camera.farClipPlane);
        FPlane* planesPtr = (FPlane*) planes.GetUnsafePtr();
        
        foreach (var foliageComponent in FoliageComponent.s_foliageComponents)
        {
            foliageComponent.InitViewFoliage(renderingData.cameraData.camera.transform.position, matrixProj, planesPtr, cullHandles);
        }

        JobHandle.CompleteAll(cullHandles);
        cullHandles.Dispose();
        #endregion //InitViewContext

        #region ExecuteViewContext
        var gatherHandles = new NativeList<JobHandle>(256, Allocator.Temp);

        foreach (var foliageComponent in FoliageComponent.s_foliageComponents)
        {
            foliageComponent.DispatchSetup(gatherHandles);
        }

        JobHandle.CompleteAll(gatherHandles);
        gatherHandles.Dispose();
        #endregion //ExecuteViewContext

        #region InitViewCommand
        cmdBuffer.BeginSample("TreePipeline");
        foreach (var foliageComponent in FoliageComponent.s_foliageComponents)
        {
            //foliageComponent.DrawBounds(true);
            foliageComponent.DispatchDraw(cmdBuffer);
        }
        cmdBuffer.EndSample("TreePipeline");
        #endregion //InitViewCommand

        #region ReleaseViewContext
        planes.Dispose();
        #endregion //ReleaseViewContext

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
        renderer.AddPass(m_foliagePass);
    }

    protected override void Dispose(bool disposing)
    {

    }
}


