using System;
using System.Reflection;
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
    public enum TreeOcclusionMode
    {
        None = 0,
        Terrain = 1,
        Hzb = 2,
        TerrainAndHzb = 3
    }

    public enum TreeLodFadeMode
    {
        Temporal = 0,
        Distance = 1
    }

    [Serializable]
    public class FoliageRenderSettings
    {
        [Header("Drawing")]
        public bool drawGrass = true;
        public bool drawTrees = true;
        public bool castTreeMainShadows = true;
        [Min(0f)] public float grassDistanceScale = 1f;
        [Min(0f)] public float treeDistanceScale = 1f;

        [Header("Tree Visibility")]
        public TreeOcclusionMode treeOcclusion = TreeOcclusionMode.TerrainAndHzb;
        public bool treeLodFade = true;
        public TreeLodFadeMode treeLodFadeMode = TreeLodFadeMode.Temporal;
        [Range(0.01f, 0.99f)] public float treeFadeWidth = 0.2f;
        [Range(0f, 0.5f)] public float treeLodHysteresis = 0.08f;

        [Header("Residency")]
        [Min(1)] public int residentTerrainBudget = 2;
        [Min(0)] public int grassDetailPageBudget = 8;
        [Min(0)] public int pageVisibilityHoldFrames = 12;
    }

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
        private readonly FoliageRenderSettings m_Settings;

        internal FoliagePass(FoliageRenderSettings settings)
        {
            m_Settings = settings;
        }

        private class PassData
        {
            internal FoliageRenderSettings settings;
            internal Camera camera;
            internal TextureHandle color;
            internal TextureHandle depth;
            internal TextureHandle cameraDepth;
            internal Matrix4x4 cameraProjection;
            internal Matrix4x4 cameraView;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (Application.isPlaying == false) { return; }

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            using (var builder = renderGraph.AddUnsafePass<PassData>("Foliage", out var passData))
            {
                passData.settings = m_Settings;
                passData.camera = camera;
                passData.color = resourceData.activeColorTexture;
                passData.depth = resourceData.activeDepthTexture;
                passData.cameraDepth = resourceData.cameraDepthTexture;
                passData.cameraProjection = cameraData.GetProjectionMatrix();
                passData.cameraView = cameraData.GetViewMatrix();
                builder.UseTexture(passData.color, AccessFlags.ReadWrite);
                builder.UseTexture(passData.depth, AccessFlags.ReadWrite);
                if (passData.cameraDepth.IsValid()) { builder.UseTexture(passData.cameraDepth, AccessFlags.Read); }
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmdBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmdBuffer.SetRenderTarget(data.color, data.depth);
                    RTHandle cameraDepth = data.cameraDepth.IsValid() ? (RTHandle)data.cameraDepth : null;
                    bool renderIntoTexture = cameraDepth != null && context.GetTextureUVOrigin(data.cameraDepth) == TextureUVOrigin.BottomLeft;
                    Matrix4x4 depthViewProj = GL.GetGPUProjectionMatrix(data.cameraProjection, renderIntoTexture) * data.cameraView;
                    ExecuteFoliage(cmdBuffer, data.camera, cameraDepth, depthViewProj, data.settings);
                });
            }
        }

        private static void ExecuteFoliage(CommandBuffer cmdBuffer, Camera camera, RTHandle cameraDepth, in Matrix4x4 depthViewProj, FoliageRenderSettings settings)
        {
            float3 viewOrigin = camera.transform.position;
            for (int i = 0; i < FoliageComponent.FoliageComponents.Count; ++i)
            {
                FoliageComponent component = FoliageComponent.FoliageComponents[i];
                if (component is TreeComponent tree) { tree.SetRenderSettings(settings); }
                else if (component is GrassComponent grass) { grass.SetRenderSettings(settings); }
            }
            FoliageResidency.UpdateView(camera, viewOrigin, settings);
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
            var matrixProj = Geometry.GetLodProjectionMatrix(camera);
            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            float invNear = Mathf.Approximately(near, 0f) ? 0f : 1f / near;
            float invFar = Mathf.Approximately(far, 0f) ? 0f : 1f / far;
            float zc0 = 1f - far * invNear;
            float zc1 = far * invNear;
            Vector4 zParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
            if (SystemInfo.usesReversedZBuffer)
            {
                zParams.y += zParams.x;
                zParams.x = -zParams.x;
                zParams.w += zParams.z;
                zParams.z = -zParams.z;
            }

            #region InitViewBound
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                FoliageComponent component = FoliageComponent.FoliageComponents[i];
                sectorsBound[i] = component == null || component.boundSector == null ? default : component.boundSector.bound;
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
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                FoliageComponent component = FoliageComponent.FoliageComponents[i];
                if (component == null || component.boundSector == null) { boundsVisible[i] = 0; }
            }
            #endregion

            #region InitViewFoliage
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (FoliageComponent.FoliageComponents[i] == null || FoliageComponent.FoliageComponents[i].boundSector == null) { continue; }
                if (boundsVisible[i] == 0 && FoliageComponent.FoliageComponents[i].foliageType != EFoliageType.Grass) { continue; }
                if (!ShouldDraw(FoliageComponent.FoliageComponents[i], settings)) { continue; }
                FoliageComponent.FoliageComponents[i].InitView(viewOrigin, matrixProj, planesPtr, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles.AsArray());
            taskHandles.Clear();
            #endregion

            #region InitViewCommand
            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (boundsVisible[i] == 0 || FoliageComponent.FoliageComponents[i] == null || FoliageComponent.FoliageComponents[i].boundSector == null) { continue; }
                if (!ShouldDraw(FoliageComponent.FoliageComponents[i], settings)) { continue; }
                FoliageComponent.FoliageComponents[i].DispatchSetup(camera, viewOrigin, matrixProj, taskHandles);
            }
            JobHandle.CompleteAll(taskHandles.AsArray());
            taskHandles.Clear();

            for (int i = 0; i < sectorsBound.Length; ++i)
            {
                if (FoliageComponent.FoliageComponents[i] == null || FoliageComponent.FoliageComponents[i].boundSector == null) { continue; }
                if (boundsVisible[i] == 0 && FoliageComponent.FoliageComponents[i].foliageType != EFoliageType.Grass) { continue; }
                FoliageComponent component = FoliageComponent.FoliageComponents[i];
                if (component is TreeComponent tree) { tree.SetDepthViewProjection(depthViewProj); }
                component.FlushPendingUploads(cmdBuffer, cameraDepth, zParams);
            }
            #endregion

            #region DispatchDraw
            using (new ProfilingScope(cmdBuffer, ProfilingSampler.Get(EFoliageSamplerId.FoliageBatch)))
            {
                for (int i = 0; i < sectorsBound.Length; ++i)
                {
                    if (boundsVisible[i] == 0 || FoliageComponent.FoliageComponents[i] == null || FoliageComponent.FoliageComponents[i].boundSector == null) { continue; }
                    if (!ShouldDraw(FoliageComponent.FoliageComponents[i], settings)) { continue; }
                    FoliageComponent.FoliageComponents[i].DispatchDraw(cmdBuffer, 1);
#if UNITY_EDITOR
                    if (FoliageComponent.FoliageComponents[i] is TreeComponent tree)
                    {
                        tree.CaptureVisibilitySnapshot(cmdBuffer, camera);
                    }
#endif
                }
            }
            #endregion

            planes.Dispose();
            taskHandles.Dispose();
            sectorsBound.Dispose();
            boundsVisible.Dispose();

        }

        private static bool ShouldDraw(FoliageComponent component, FoliageRenderSettings settings)
        {
            return component.foliageType == EFoliageType.Grass ? settings.drawGrass : settings.drawTrees;
        }
    }

    internal class FoliageMainShadowPass : ScriptableRenderPass
    {
        private readonly FoliageRenderSettings m_Settings;

        internal FoliageMainShadowPass(FoliageRenderSettings settings)
        {
            m_Settings = settings;
        }

        private static readonly Func<Light, bool, float> s_SoftShadowQuality = BindSoftShadowQuality();
        private static readonly int s_MainShadowTexture = Shader.PropertyToID("_MainLightShadowmapTexture");
        private static readonly int s_WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
        private static readonly int s_ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
        private static readonly int s_CascadeSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
        private static readonly int s_CascadeSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
        private static readonly int s_CascadeSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
        private static readonly int s_CascadeSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
        private static readonly int s_CascadeRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
        private static readonly int s_ShadowOffset0 = Shader.PropertyToID("_MainLightShadowOffset0");
        private static readonly int s_ShadowOffset1 = Shader.PropertyToID("_MainLightShadowOffset1");
        private static readonly int s_ShadowmapSize = Shader.PropertyToID("_MainLightShadowmapSize");

        private static Func<Light, bool, float> BindSoftShadowQuality()
        {
            MethodInfo method = typeof(ShadowUtils).GetMethod("SoftShadowQualityToShaderProperty", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) { throw new MissingMethodException("URP 17.6 ShadowUtils.SoftShadowQualityToShaderProperty"); }
            return (Func<Light, bool, float>)Delegate.CreateDelegate(typeof(Func<Light, bool, float>), method);
        }

        private class PassData
        {
            internal FoliageRenderSettings settings;
            internal Camera camera;
            internal TextureHandle shadow;
            internal ShadowSliceData[] slices;
            internal Plane[][] planes;
            internal Vector4[] biases;
            internal VisibleLight light;
            internal int cascadeCount;
            internal bool ownsShadowAtlas;
            internal bool supportsSoftShadows;
            internal float maxShadowDistance;
            internal float cascadeBorder;
            internal int atlasWidth;
            internal int atlasHeight;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!Application.isPlaying) { return; }
            if (!m_Settings.drawTrees) { return; }
            bool hasTreeCaster = false;
            for (int i = 0; i < FoliageComponent.FoliageComponents.Count; ++i)
            {
                TreeComponent tree = FoliageComponent.FoliageComponents[i] as TreeComponent;
                if (tree != null && tree.CastMainShadows(m_Settings)) { hasTreeCaster = true; break; }
            }
            if (!hasTreeCaster) { return; }
            var resourceData = frameData.Get<UniversalResourceData>();
            var shadowData = frameData.Get<UniversalShadowData>();
            var lightData = frameData.Get<UniversalLightData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            int cascadeCount = shadowData.mainLightShadowCascadesCount;
            int lightIndex = lightData.mainLightIndex;
            if (!shadowData.supportsMainLightShadows || cameraData.maxShadowDistance <= 0f || cascadeCount < 1 || cascadeCount > 4 ||
                lightIndex < 0 || lightIndex >= lightData.visibleLights.Length) { return; }

            VisibleLight light = lightData.visibleLights[lightIndex];
            if (light.lightType != LightType.Directional || light.light == null || light.light.shadows == LightShadows.None) { return; }
            int atlasWidth = shadowData.mainLightShadowmapWidth;
            int atlasHeight = cascadeCount == 2 ? shadowData.mainLightShadowmapHeight >> 1 : shadowData.mainLightShadowmapHeight;
            if (atlasWidth <= 0 || atlasHeight <= 0) { return; }
            int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(atlasWidth, atlasHeight, cascadeCount);
            var cullResults = renderingData.cullResults;
            bool ownsShadowAtlas = !cullResults.GetShadowCasterBounds(lightIndex, out _);
            if (!ownsShadowAtlas && !resourceData.mainShadowsTexture.IsValid()) { return; }
            ShadowSliceData[] slices = new ShadowSliceData[cascadeCount];
            Plane[][] planes = new Plane[cascadeCount][];
            Vector4[] biases = new Vector4[cascadeCount];
            for (int cascade = 0; cascade < cascadeCount; ++cascade)
            {
                if (ownsShadowAtlas)
                {
                    if (!BuildFoliageShadowSlice(cameraData.camera, light.light, cascade, cascadeCount,
                        shadowData.mainLightShadowCascadesSplit, cameraData.maxShadowDistance,
                        atlasWidth, atlasHeight, resolution, out slices[cascade], out planes[cascade])) { return; }
                }
                else
                {
                    if (!ShadowUtils.ExtractDirectionalLightMatrix(ref cullResults, shadowData, lightIndex, cascade,
                        atlasWidth, atlasHeight, resolution, light.light.shadowNearPlane, out _, out slices[cascade])) { return; }
                    ShadowSplitData split = slices[cascade].splitData;
                    planes[cascade] = new Plane[split.cullingPlaneCount];
                    for (int i = 0; i < planes[cascade].Length; ++i) { planes[cascade][i] = split.GetCullingPlane(i); }
                }
                biases[cascade] = ShadowUtils.GetShadowBias(ref light, lightIndex, shadowData, slices[cascade].projectionMatrix, resolution);
            }
            TextureHandle shadowTexture = resourceData.mainShadowsTexture;
            if (ownsShadowAtlas)
            {
                var descriptor = new RenderTextureDescriptor(atlasWidth, atlasHeight, RenderTextureFormat.Shadowmap, shadowData.shadowmapDepthBufferBits);
                shadowTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_FoliageMainLightShadowmapTexture", true, FilterMode.Bilinear);
                resourceData.mainShadowsTexture = shadowTexture;
            }

            using (var builder = renderGraph.AddUnsafePass<PassData>("Foliage Main Light Shadows", out var passData))
            {
                passData.settings = m_Settings;
                passData.camera = cameraData.camera;
                passData.shadow = shadowTexture;
                passData.slices = slices;
                passData.planes = planes;
                passData.biases = biases;
                passData.light = light;
                passData.cascadeCount = cascadeCount;
                passData.ownsShadowAtlas = ownsShadowAtlas;
                passData.supportsSoftShadows = shadowData.supportsSoftShadows;
                passData.maxShadowDistance = cameraData.maxShadowDistance;
                passData.cascadeBorder = shadowData.mainLightShadowCascadeBorder;
                passData.atlasWidth = atlasWidth;
                passData.atlasHeight = atlasHeight;
                builder.UseTexture(passData.shadow, ownsShadowAtlas ? AccessFlags.Write : AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(passData.shadow, s_MainShadowTexture);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmdBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmdBuffer.SetRenderTarget(data.shadow);
                    for (int i = 0; i < FoliageComponent.FoliageComponents.Count; ++i)
                    {
                        FoliageComponent component = FoliageComponent.FoliageComponents[i];
                        if (component is TreeComponent tree) { tree.SetRenderSettings(data.settings); }
                        else if (component is GrassComponent grass) { grass.SetRenderSettings(data.settings); }
                    }
                    FoliageResidency.UpdateView(data.camera, data.camera.transform.position, data.settings);
                    for (int cascade = 0; cascade < data.cascadeCount; ++cascade)
                    {
                        ShadowSliceData slice = data.slices[cascade];
                        cmdBuffer.SetViewport(new Rect(slice.offsetX, slice.offsetY, slice.resolution, slice.resolution));
                        cmdBuffer.SetViewProjectionMatrices(slice.viewMatrix, slice.projectionMatrix);
                        cmdBuffer.SetGlobalDepthBias(1f, 2.5f);
                        VisibleLight light = data.light;
                        ShadowUtils.SetupShadowCasterConstantBuffer(cmdBuffer, ref light, data.biases[cascade]);
                        for (int i = 0; i < FoliageComponent.FoliageComponents.Count; ++i)
                        {
                            TreeComponent tree = FoliageComponent.FoliageComponents[i] as TreeComponent;
                            if (tree != null && data.settings.drawTrees && tree.CastMainShadows(data.settings))
                            {
                                tree.DispatchShadow(cmdBuffer, data.camera, cascade, data.planes[cascade]);
                            }
                        }
                    }
                    if (data.ownsShadowAtlas) { SetupShadowReceiver(cmdBuffer, data); }
                    cmdBuffer.SetGlobalDepthBias(0f, 0f);
                    cmdBuffer.SetViewProjectionMatrices(data.camera.worldToCameraMatrix, data.camera.projectionMatrix);
                    cmdBuffer.SetViewport(data.camera.pixelRect);
                });
            }
        }

        private static bool BuildFoliageShadowSlice(Camera camera, Light light, in int cascadeIndex, in int cascadeCount,
            in Vector3 cascadeSplits, in float shadowDistance, in int atlasWidth, in int atlasHeight, in int resolution,
            out ShadowSliceData slice, out Plane[] planes)
        {
            slice = default;
            planes = null;
            if (camera == null || light == null) { return false; }

            float near = Mathf.Max(camera.nearClipPlane, 0.01f);
            float far = Mathf.Min(camera.farClipPlane, shadowDistance);
            if (far <= near) { return false; }
            float startSplit = cascadeIndex == 0 ? 0f : cascadeIndex == 1 ? cascadeSplits.x : cascadeIndex == 2 ? cascadeSplits.y : cascadeSplits.z;
            float endSplit = cascadeIndex == cascadeCount - 1 ? 1f : cascadeIndex == 0 ? cascadeSplits.x : cascadeIndex == 1 ? cascadeSplits.y : cascadeSplits.z;
            float nearSplit = Mathf.Lerp(near, far, startSplit);
            float farSplit = Mathf.Lerp(near, far, endSplit);
            if (farSplit <= nearSplit) { return false; }

            var nearCorners = new Vector3[4];
            var farCorners = new Vector3[4];
            Vector3 center = Vector3.zero;
            for (int i = 0; i < 4; ++i)
            {
                float u = (i & 1) != 0 ? 1f : 0f;
                float v = (i & 2) != 0 ? 1f : 0f;
                nearCorners[i] = camera.ViewportToWorldPoint(new Vector3(u, v, nearSplit));
                farCorners[i] = camera.ViewportToWorldPoint(new Vector3(u, v, farSplit));
                center += nearCorners[i] + farCorners[i];
            }
            center *= 0.125f;
            float radius = 0f;
            for (int i = 0; i < 4; ++i)
            {
                radius = Mathf.Max(radius, Vector3.Distance(center, nearCorners[i]), Vector3.Distance(center, farCorners[i]));
            }
            radius = Mathf.Ceil(radius * 16f) / 16f;
            float texel = 2f * radius / resolution;
            radius += texel;

            Vector3 direction = light.transform.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(direction, up);
            Vector3 lightCenter = Quaternion.Inverse(rotation) * center;
            lightCenter.x = Mathf.Round(lightCenter.x / texel) * texel;
            lightCenter.y = Mathf.Round(lightCenter.y / texel) * texel;
            Quaternion worldToLight = Quaternion.Inverse(rotation);
            float minDepth = float.PositiveInfinity;
            float maxDepth = float.NegativeInfinity;
            for (int i = 0; i < 4; ++i)
            {
                Vector3 nearPoint = worldToLight * nearCorners[i];
                Vector3 farPoint = worldToLight * farCorners[i];
                minDepth = Mathf.Min(minDepth, nearPoint.z, farPoint.z);
                maxDepth = Mathf.Max(maxDepth, nearPoint.z, farPoint.z);
            }
            for (int i = 0; i < FoliageComponent.FoliageComponents.Count; ++i)
            {
                TreeComponent tree = FoliageComponent.FoliageComponents[i] as TreeComponent;
                if (tree == null || tree.boundSector == null) { continue; }
                Aabb box = tree.boundSector.bound;
                Vector3 casterCenter = worldToLight * new Vector3(box.center.x, box.center.y, box.center.z);
                Vector3 right = rotation * Vector3.right;
                Vector3 upAxis = rotation * Vector3.up;
                Vector3 extents = new Vector3(box.extents.x, box.extents.y, box.extents.z);
                float extentX = Vector3.Dot(new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z)), extents);
                float extentY = Vector3.Dot(new Vector3(Mathf.Abs(upAxis.x), Mathf.Abs(upAxis.y), Mathf.Abs(upAxis.z)), extents);
                if (Mathf.Abs(casterCenter.x - lightCenter.x) > radius + extentX ||
                    Mathf.Abs(casterCenter.y - lightCenter.y) > radius + extentY) { continue; }
                float extentZ = Vector3.Dot(new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)), extents);
                minDepth = Mathf.Min(minDepth, casterCenter.z - extentZ);
                maxDepth = Mathf.Max(maxDepth, casterCenter.z + extentZ);
            }
            float margin = Mathf.Max(1f, light.shadowNearPlane);
            float eyeDepth = minDepth - margin;
            float depth = Mathf.Max(maxDepth - eyeDepth + margin, 1f);
            Vector3 eye = rotation * new Vector3(lightCenter.x, lightCenter.y, eyeDepth);
            slice.viewMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;
            slice.projectionMatrix = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0.01f, depth);
            slice.offsetX = (cascadeIndex % 2) * resolution;
            slice.offsetY = (cascadeIndex / 2) * resolution;
            slice.resolution = resolution;

            Matrix4x4 receiverProjection = slice.projectionMatrix;
            if (SystemInfo.usesReversedZBuffer)
            {
                receiverProjection.m20 = -receiverProjection.m20;
                receiverProjection.m21 = -receiverProjection.m21;
                receiverProjection.m22 = -receiverProjection.m22;
                receiverProjection.m23 = -receiverProjection.m23;
            }
            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = textureScaleAndBias.m11 = textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = textureScaleAndBias.m13 = textureScaleAndBias.m23 = 0.5f;
            slice.shadowTransform = textureScaleAndBias * receiverProjection * slice.viewMatrix;
            if (cascadeCount > 1) { ShadowUtils.ApplySliceTransform(ref slice, atlasWidth, atlasHeight); }

            ShadowSplitData splitData = default;
            splitData.cullingSphere = new Vector4(center.x, center.y, center.z, radius);
            slice.splitData = splitData;
            planes = GeometryUtility.CalculateFrustumPlanes(slice.projectionMatrix * slice.viewMatrix);
            return true;
        }

        private static void SetupShadowReceiver(CommandBuffer cmdBuffer, PassData data)
        {
            var matrices = new Matrix4x4[5];
            var spheres = new Vector4[4];
            for (int i = 0; i < data.cascadeCount; ++i)
            {
                matrices[i] = data.slices[i].shadowTransform;
                spheres[i] = data.slices[i].splitData.cullingSphere;
            }
            Matrix4x4 noOp = Matrix4x4.zero;
            noOp.m22 = SystemInfo.usesReversedZBuffer ? 1f : 0f;
            for (int i = data.cascadeCount; i < matrices.Length; ++i) { matrices[i] = noOp; }

            float distance = data.maxShadowDistance * data.maxShadowDistance;
            float border = data.cascadeBorder;
            float fadeScale;
            float fadeBias;
            if (border < 0.0001f)
            {
                fadeScale = 1000f;
                fadeBias = -distance * 1000f;
            }
            else
            {
                float fadeStart = (1f - border) * (1f - border) * distance;
                fadeScale = 1f / (distance - fadeStart);
                fadeBias = -fadeStart * fadeScale;
            }

            int width = data.atlasWidth;
            int height = data.atlasHeight;
            float halfWidth = 0.5f / width;
            float halfHeight = 0.5f / height;
            cmdBuffer.SetGlobalMatrixArray(s_WorldToShadow, matrices);
            bool softShadows = data.light.light.shadows == LightShadows.Soft && data.supportsSoftShadows;
            cmdBuffer.SetGlobalVector(s_ShadowParams, new Vector4(data.light.light.shadowStrength,
                s_SoftShadowQuality(data.light.light, softShadows), fadeScale, fadeBias));
            cmdBuffer.SetGlobalVector(s_CascadeSpheres0, spheres[0]);
            cmdBuffer.SetGlobalVector(s_CascadeSpheres1, spheres[1]);
            cmdBuffer.SetGlobalVector(s_CascadeSpheres2, spheres[2]);
            cmdBuffer.SetGlobalVector(s_CascadeSpheres3, spheres[3]);
            cmdBuffer.SetGlobalVector(s_CascadeRadii, new Vector4(spheres[0].w * spheres[0].w, spheres[1].w * spheres[1].w,
                spheres[2].w * spheres[2].w, spheres[3].w * spheres[3].w));
            cmdBuffer.SetGlobalVector(s_ShadowOffset0, new Vector4(-halfWidth, -halfHeight, halfWidth, -halfHeight));
            cmdBuffer.SetGlobalVector(s_ShadowOffset1, new Vector4(-halfWidth, halfHeight, halfWidth, halfHeight));
            cmdBuffer.SetGlobalVector(s_ShadowmapSize, new Vector4(1f / width, 1f / height, width, height));
            if (softShadows) { cmdBuffer.EnableShaderKeyword("_SHADOWS_SOFT"); }
            else { cmdBuffer.DisableShaderKeyword("_SHADOWS_SOFT"); }
            cmdBuffer.EnableShaderKeyword(data.cascadeCount == 1 ? "_MAIN_LIGHT_SHADOWS" : "_MAIN_LIGHT_SHADOWS_CASCADE");
            cmdBuffer.DisableShaderKeyword(data.cascadeCount == 1 ? "_MAIN_LIGHT_SHADOWS_CASCADE" : "_MAIN_LIGHT_SHADOWS");
        }
    }

    public class FoliageRenderer : ScriptableRendererFeature
    {
        public FoliageRenderSettings settings = new FoliageRenderSettings();
        private FoliagePass m_foliagePass;
        private FoliageMainShadowPass m_MainShadowPass;

        public override void Create()
        {
            if (settings == null) { settings = new FoliageRenderSettings(); }
            FoliageLogicAsserts.Evaluate();
            m_foliagePass = new FoliagePass(settings);
            m_foliagePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            m_foliagePass.ConfigureInput(ScriptableRenderPassInput.Depth);
            m_MainShadowPass = new FoliageMainShadowPass(settings);
            m_MainShadowPass.renderPassEvent = RenderPassEvent.AfterRenderingShadows;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_foliagePass);
            renderer.EnqueuePass(m_MainShadowPass);
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}
