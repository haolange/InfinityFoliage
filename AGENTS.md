# Agent 工作台

本包是 Unity Terrain 草 / 树 Runtime，没有 UI。`DESIGN.md` 是产品 / 视觉仓库的词，这里不准再建。架构不变式、C# 风格、禁止项、校验门都在本文件。

人读用法见 [README.md](README.md)。编译与宿主证据见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。不要把桌面长文、计划文件、别的仓库的 workbench / transcript 条款写进来。

## 角色

- 本 Agent：Orchestrator、Plan、Generator、编译与文档校验。
- 低杠杆检索（残留类型名、死引用）派 composer explore。不要为了搜一个符号自己扫全库。
- 只复用已打开的 Unity Editor，不启动第二个 Editor、Unity Hub 或 batchmode。编译与运行证据分别记录在 `Docs/DELIVERY.md`。

## 架构不变式

`package.json` 名为 `com.infinity.render-foliage`，文件夹为 `com.infinity.foliage`。不改包名。

正式路径：Bake 将草密度写成每种草一份基础层和 `4×4` 细节页、将树候选流写成独立资产；运行时先选驻留 Terrain，再按视点选细节页。草以每页 CPU scatter、页内连续 run 绘制；树以规则格、64 宽 Visibility IR、lowering 和 VisibleIndex 绘制。树 Compute 负责保守 HZB、格与实例过滤、Visibility 展开；Morton 只排树 Candidate Stream。主光 CSM 有独立阴影索引，不复用相机可见索引。

两套系统，两份 `BoundSector` / `visibleMap`。只挂 `TreeComponent` 可运行。文档里的 Sector 是 mesh × LOD × fade bucket，对应 `TreeLodBatch`，不是 `BoundSector`。

| 单位 | 草 | 树 |
|---|---|---|
| 剔除 | 组件盒 → 自持格 → 基础层 / 细节页 | 组件盒 → 自持格 → 实例，格盒先做地形 / HZB 遮挡 |
| 资源 | 每种草一份基础层 + `4×4` 细节页资产；只为驻留页分配 packed `ComputeBuffer` | 一种树 `bounds[]` + `matrices[]`，格 `{offset,count}` |
| 提交 | 页内 1D 连续 run + `DrawMeshInstancedProcedural` | mesh × LOD × bucket + VisibleIndex + args + Indirect；主光每级联独立 index / args |
| 搬运 | 页载入或运行密度变化后重新 scatter / upload；释放页即释放资源 | 矩阵只在载入 / 重排后 upload；每帧 IR lowering，VS 只吃 uint index |

Pass 相位：`FoliageResidency.UpdateView` → 组件盒粗剔 → `InitView` 入队后 `CompleteAll` → `DispatchSetup` 入队后 `CompleteAll` → `FlushPendingUploads` → `DispatchDraw`。草页 scatter 用页私有 `JobHandle`，不进共享 `taskHandles`；未完成时不阻塞绘制。草的 Flush 在组件盒外仍推进驻留页；Draw 两边都按盒剔。主光阴影 Pass 按级联另算树 index / args，不能借用颜色相机的可见结果。

草：`sections` 保持 `N×N` 作为空间键；密度二进制资产是唯一运行数据真源，Scene 不再承载整张 `densityMap`。基础层按确定性抽样保留约 `1/8` 密度；每种草另有 `4×4` 细节页承载剩余密度。驻留预算为最多 2 个 Terrain、每个草 prefab 跨 Terrain 最多 8 个细节页；不能用 Terrain 内的 detail prototype 序号当全局草种键。优先当前可见、近视点及已有驻留页；已驻留页离开视锥时保留 12 帧可见优先级，抑制边缘反复驱逐；先释放未入选页的槽位，再申请新页，异步旧请求以代次隔离。页的 CPU `GrassPageScatterJob : IJobParallelFor` 跨帧完成后上传一张页内 packed `ComputeBuffer`；首次载入未就绪的页不绘制，密度重建期间继续使用旧 buffer，完成后替换，构建失败须归还页租约并释放部分分配。运行时 `terrain.detailObjectDensity` 或 `SetDensityScale` 变化触发驻留页重建，不回写 Bake 资产。页内 run 只画可见格；`count==0` 可作连续区间桥。CPU scatter 只做 XZ / 旋转 / 缩放，Y 由 VS 采 heightmap；只画 `meshes[0]`。

树：无「自有地形大 bound」；自持 `numSection` 默认 16；Bake 先 Morton 排 Candidate Stream，再 `CellFromLocal` 写 `{offset,count}`，格键公式不变。chunk Job 可覆盖全量候选，但对不可见格尽早跳过；格盒先做地形 / GPU HZB 遮挡，实例再做精剔；HZB 投影须匹配 RenderGraph 当前 camera depth 的 `TextureUVOrigin`，不可按 Metal 平台固定翻转 Y；已 cull 不算 LOD。Visibility 是 IR（64 宽 chunk mask），不是绘制格式。CPU Burst 与树 Compute 是同一候选流的两种执行路径。三种 lowering（CompactIndex / BitMaskTransfer / RunTransfer）按代价每桶每帧选一个，最终都写 `VisibleIndexBuffer`；Run/Mask 不进 VS。同一视图各 LOD / bucket 复用的暂存 ComputeBuffer，按 CommandBuffer 顺序上传、剔除、展开，不能在排队的 dispatch 消费前用即时 `SetData` 覆盖。Fade 状态按渲染 `Camera` 保存 hold / now 两个视点，以屏占比迟滞稳定 LOD；透视 / 正交相机按各自投影求屏半径，颜色与阴影使用同一口径；只有相邻 LOD 且实例 Forward pass 支持 dither 时双几何，fade 不进实例 payload。颜色 DC = 种 × LOD × bucket × submesh；主光 CSM 按相机 / 级联保留独立 index / args，fallback 级联球与投影必须用相机正前方的世界空间视锥角点。

Bake：草密度、树 transforms 写完之后才算格归属，并生成带版本的流式资产；树 `meshes[i]` 与 `lODInfos[i]` 一一对应，重复引用同一 Mesh 的 LOD 仍各占一个槽，只有 Material 可去重；`OnSave` 只在 `numSection` 变化时重建空间格。Play 中禁止把空 `transforms` 写回。旧 Scene、MeshAsset 和旧密度布局必须重新 Build/Update；缺失或版本错误的资产不能静默走旧路径。

GPU 资源一律 `ComputeBuffer`，不引入 `GraphicsBuffer`。

草树实例的天空 SH 求值与非均匀缩放法线变换统一放在 `Shader/Foliage/Include/Foliage.hlsl`；普通 Forward pass 使用 URP 场景 SH。

命名：去掉 UE 式 `F` 前缀。`Foliage*` 保留。避撞：`Aabb`、`FrustumPlane`、`BoundSphere`、`InstanceTransform`、`FoliageMesh`。几何命名空间 `Landscape.FoliagePipeline`。

Shader 路径不改：`Landscape/Grass`、`Landscape/TreeLeave`、`Landscape/TreeBrak`。

Out of Scope：内部 BVH；草 mesh LOD；可变 section；跨块双归属；改 package 名；剥 `WindSettings` 的 Gust 别名；草 Compute scatter；VS 寻址 run；整场景 GPU 驱动；版本史 / legacy 双路径。

## C# 风格

真源是本包去 `F` 之前的 Runtime（`GrassComponent` / `TreeSector` / `FoliageRenderer` / `MeshPipelineJob`）。**不是** `WindComponent.cs`（2 空格第三方风，保持不动）。

```csharp
// 对：热路径 in + AggressiveInlining；Job 用赋值块
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public unsafe void InitView(in float cullDistance, in float3 viewOrigin, in FrustumPlane* planes, in NativeList<JobHandle> taskHandles)
{
    var treeCullingJob = new TreeCullLodJob();
    {
        treeCullingJob.planes = planes;
        treeCullingJob.viewOrigin = viewOrigin;
    }
    taskHandles.Add(treeCullingJob.Schedule(m_ChunkMasks.Length, 4));
}

// 错：对象初始化器；丢掉 in；再套一层 ShaderProperty
var job = new TreeCullLodJob { planes = planes };
```

- 4 空格；类型 / 方法 Allman 大括号。
- 热路径 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`，公开参数用 `in`。
- 私有字段 `m_`（`m_ChunkMasks`、`m_BuildHandle`、`m_PageLastUsed`）。
- Shader ID：`internal static class GrassShaderID` / `TreeShaderID` 直接 `Shader.PropertyToID`。不准再套 `ShaderProperty`。
- 内部辅助类不 `sealed`；热函数保持 `unsafe` + `*`。
- `FoliageRenderer` 保留 `#region`。
- 属性用 `get { return ...; }`，不要为了新风格改成 `=>`。
- 已有拼写当 API：`OnRegiste`、`sectionIndexs`、`Caculate*`、`treeTransfroms`。不准顺手修对。
- 不加 XML 文档、不加计划口吻长注释、不为防御叠三层 null 金字塔。
- `foreach (` / `if (` 空格跟周围旧文件走，不统一成另一种 formatter。

## 禁止

- 再建 `DESIGN.md`，或把本包架构写成 UI/UX Workbench 条款。
- 类型名加回 `F` 前缀，或加 `FormerlySerializedAs` 渡旧类型名。
- 流式页与旧全图 packed `ComputeBuffer` / Scene `densityMap` 双路径。
- 可见矩阵 compact 换 1 DC。
- 草页 scatter 进 Pass 共享的 `taskHandles`，或对未完成 scatter `Complete`。
- 草 scatter 用 C# `Task` / `async` / `Thread` / `Awaitable`，或用 Compute kernel 做 scatter。
- 热路径用 `async` / `Task` / `Awaitable` 包 `JobHandle`。
- VS 里查找 run / mask；`GraphicsBuffer`；全数组 `instanceVisible` 再扫一遍当 compact。
- 恢复固定 `[16k,16k+16)` 全图上传、顺序前缀 `i < m_Counter`、`TryGetSetupSlice` 或旧 per-section buffer。
- 用颜色相机 index 绘制主光级联阴影，或把屏占比迟滞放进实例 payload。
- Play 模式把空 `transforms` 写回 scene。
- 新建测试 asmdef。断言进现有 Runtime。
- 改 Shader 资源路径字符串、改 `package.json` 名、剥 `WindSettings` 的 Gust 别名。
- 删除 `DummyFoliageShaders.cs`。

## 每步校验

1. 对照本文件架构不变式。
2. 改动文件 lints。
3. 用已有 Managed DLL 做编译检查，并在已打开的 Editor 验证 Game 画面、Frame Debugger、日志；缺证据则 DELIVERY 写未验收，不得假装通过。
4. composer 扫旧 `F` 类型、旧全图草 buffer / Scene 密度路径、`DESIGN.md` 引用。
5. 失败回修，过门再下一步。
