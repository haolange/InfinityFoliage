# Agent 工作台

本包是 Unity Terrain 草 / 树 Runtime，没有 UI。`DESIGN.md` 是产品 / 视觉仓库的词，这里不准再建。架构不变式、C# 风格、禁止项、校验门都在本文件。

人读用法见 [README.md](README.md)。编译与宿主证据见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。不要把桌面长文、计划文件、别的仓库的 workbench / transcript 条款写进来。

## 角色

- 本 Agent：Orchestrator、Plan、Generator、编译与文档校验。
- 低杠杆检索（残留类型名、死引用）派 composer explore。不要为了搜一个符号自己扫全库。
- 不启动 Unity Editor 或 Unity Hub。编译只引用本机已有 Managed DLL。

## 架构不变式

`package.json` 名为 `com.infinity.render-foliage`，文件夹为 `com.infinity.foliage`。不改包名。

停在：烘焙密度 + CPU 粗格 + 草的 1D 窗口 / 树的 index compact。开放世界生成、整场景 GPU 驱动、阴影第二视口、Morton、Compute kernel 都是加层，不改格键。

两套系统，两份 `BoundSector` / `visibleMap`。只挂 `TreeComponent` 可运行。

| 单位 | 草 | 树 |
|---|---|---|
| 剔除 | 组件盒 → 自持格 → 种盒（可选） | 组件盒 → 自持格 → 实例 |
| 资源 | 一种草一张 packed `ComputeBuffer`，格 `{offset,count}` | 一种树 `bounds[]` + `matrices[]`，格 `{offset,count}` |
| 提交 | 1D 连续 run + `DrawMeshInstancedProcedural` | mesh × LOD × bucket + index + args + Indirect |
| 搬运 | 不每帧搬矩阵 | 矩阵只在重排后 upload 一次；每帧只写 uint index / args |

Pass 相位：组件盒粗剔 → `InitView` 入队后 `CompleteAll` → `DispatchSetup` 入队后 `CompleteAll` → `FlushPendingUploads` → `DispatchDraw`。`InitView` 只写 `visibleMap`。草 cull 可进共享 list；草 scatter 用组件私有 `JobHandle`，禁止 `taskHandles.Add`。Setup 后的 `CompleteAll` 只等树。草 `DispatchSetup` 禁止再对共享 `taskHandles` `CompleteAll` / `Clear`。草 Flush 只在私有 handle `IsCompleted` 后 `Complete` 一次，再 upload。草的 `InitView` / `Flush` 不受组件盒守卫；树的 InitView / Setup / Flush / Draw 仍按盒跳过。Draw 两边都按盒剔。

草：`sections` 保持 `N×N`；同一组件一份 `visibleMap`；断开仅 `count>0 && visible==0`；`count==0` 当桥。Play 的 `OnRegiste` 每种草一次 `IJobParallelFor` 扫全格；跨帧 `IsCompleted` 轮询，不对未完成 scatter `Complete`。`ComputeBuffer` 延到首次 `SetData`。每显示帧最多挑 16 格（`count>0` 且未 upload）：visible 优先，同级按格 pivot 到 `viewOrigin` 的 XZ 距离；相邻格（空格当桥）合成一段 packed `SetData`，允许多段。`m_Uploaded` 是格位图；`m_Counter` 只计已 upload 格数，不当 Draw 前缀。Draw = uploaded ∧ visible。CPU scatter 只做 XZ / 旋转 / 缩放，Y 由 VS 采 heightmap；只画 `meshes[0]`。

树：无「自有地形大 bound」；自持 `numSection` 默认 16；先按格重排再 upload 矩阵；已 cull 不算 LOD；fade 不进 payload，按当前渲染 `Camera` 分桶；`|lod0-lod1|==1` 才双几何；VS 只绑矩阵；DC = 种 × LOD × bucket × submesh。

Bake：density / transforms 写完之后才算格归属。`OnSave` 只在 `numSection` 变化时重建空间格。Play 中禁止把空 `transforms` 写回。旧 Scene + MeshAsset 必 rebake。

GPU 资源一律 `ComputeBuffer`，不引入 `GraphicsBuffer`。

命名：去掉 UE 式 `F` 前缀。`Foliage*` 保留。避撞：`Aabb`、`FrustumPlane`、`BoundSphere`、`InstanceTransform`、`FoliageMesh`。几何命名空间 `Landscape.FoliagePipeline`。

Shader 路径不改：`Landscape/Grass`、`Landscape/TreeLeave`、`Landscape/TreeBrak`。

Out of Scope：Compute kernel；Morton；阴影第二份 index；屏占比迟滞；内部 BVH；草 mesh LOD；可变 section；跨块双归属；改 package 名；剥 `WindSettings` 的 Gust 别名。

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
    taskHandles.Add(treeCullingJob.Schedule(cells.Length, 8));
}

// 错：对象初始化器；丢掉 in；再套一层 ShaderProperty
var job = new TreeCullLodJob { planes = planes };
```

- 4 空格；类型 / 方法 Allman 大括号。
- 热路径 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`，公开参数用 `in`。
- 私有字段 `m_`（`m_Counter`、`m_ElementBuffer`、`m_SubSectors`）。
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
- packed 与旧 per-section `ComputeBuffer` 双路径。
- 可见矩阵 compact 换 1 DC。
- 草 `DispatchSetup` 对 Pass 共享的 `taskHandles` 再 `CompleteAll` / `Clear`。
- 草 scatter 进共享 `taskHandles`，或对未完成 scatter `Complete`。
- 草 scatter 用 C# `Task` / `async` / `Thread`，或用 Compute kernel 做 scatter。
- 保留 `ScheduleScatter`、顺序前缀 `i < m_Counter`、或 `TryGetSetupSlice` 双路径。
- 把多段 packed run 写成旧 per-section buffer / 逐格 `SetData` 双路径。
- Play 模式把空 `transforms` 写回 scene。
- 新建测试 asmdef。断言进现有 Runtime。
- 改 Shader 资源路径字符串、改 `package.json` 名、剥 `WindSettings` 的 Gust 别名。
- 删除 `DummyFoliageShaders.cs`。

## 每步校验

1. 对照本文件架构不变式。
2. 改动文件 lints。
3. 无 Unity 进程编译；缺 DLL 则 DELIVERY 写未编译，不得假装通过。
4. composer 扫旧 `F` 类型、旧 section buffer、`DESIGN.md` 引用。
5. 失败回修，过门再下一步。
