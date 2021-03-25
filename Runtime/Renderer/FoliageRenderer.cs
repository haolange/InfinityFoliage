using Unity.Jobs;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using InfinityTech.Core.Geometry;
using UnityEngine.Rendering.Universal;
using Unity.Collections.LowLevel.Unsafe;
using Landscape.FoliagePipeline;
using Unity.Mathematics;

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

        FPlane* planesPtr = (FPlane*)planes.GetUnsafePtr();
        float3 viewPos = renderingData.cameraData.camera.transform.position;
        var matrixProj = Geometry.GetProjectionMatrix(renderingData.cameraData.camera.fieldOfView, renderingData.cameraData.camera.pixelWidth, renderingData.cameraData.camera.pixelHeight, renderingData.cameraData.camera.nearClipPlane, renderingData.cameraData.camera.farClipPlane);

        #region InitViewSectorBound
        NativeArray<int> boundsVisible = default;
        NativeArray<FBound> sectorsBound = default;
        var taskHandles = new NativeList<JobHandle>(256, Allocator.Temp);
        BoundComponent.InitSectorView(viewPos, planesPtr, ref boundsVisible, ref sectorsBound);
        #endregion //InitViewSectorBound

        for (int i = 0; i < sectorsBound.Length; ++i)
        {
            if(boundsVisible[i] == 0) { continue; }

            BoundComponent boundComponent = BoundComponent.s_boundComponents[i];

            #region InitViewSectionBound
            boundComponent.InitSectionView(viewPos, planesPtr, taskHandles);
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion //InitViewSectionBound

            #region InitViewFoliage
            boundComponent.treeComponent?.InitViewFoliage(viewPos, matrixProj, planesPtr, taskHandles);
            boundComponent.grassComponent?.InitViewFoliage(viewPos, matrixProj, planesPtr, taskHandles);
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion //InitViewFoliage

            #region InitViewCommand
            boundComponent.treeComponent?.DispatchSetup(taskHandles);
            JobHandle.CompleteAll(taskHandles);
            taskHandles.Clear();
            #endregion //InitViewCommand

            #region ExecuteViewCommand
            boundComponent.treeComponent?.DispatchDraw(cmdBuffer);
            boundComponent.grassComponent?.DispatchDraw(cmdBuffer);
            #endregion //ExecuteViewCommand
        }
        #endregion //InitViewContext

        #region ReleaseViewContext
        planes.Dispose();
        taskHandles.Dispose();
        sectorsBound.Dispose();
        boundsVisible.Dispose();
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


