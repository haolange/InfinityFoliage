# Agent 工作台

本包是 Unity Terrain 草 / 树 Runtime，没有 UI。`DESIGN.md` 是产品 / 视觉仓库的词，这里不准再建。架构不变式、C# 风格、禁止项、校验门都在本文件。

人读用法见 [README.md](README.md)。编译与宿主证据见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。不要把桌面长文、计划文件、别的仓库的 workbench / transcript 条款写进来。

## 角色

- 本 Agent：Orchestrator、Plan、Generator、编译与文档校验。
- 低杠杆检索（残留类型名、死引用）派 composer explore。不要为了搜一个符号自己扫全库。
- 只复用已打开的 Unity Editor，不启动第二个 Editor、Unity Hub 或 batchmode。编译与运行证据分别记录在 `Docs/DELIVERY.md`。

## 架构不变式

`package.json` 名为 `com.infinity.render-foliage`，文件夹为 `com.infinity.foliage`。不改包名。

正式路径：Bake 将草密度写成每种草一份基础层和 `4×4` 细节页、将树候选流写成独立资产；运行时先选驻留 Terrain，再按视点选细节页。草以每页 CPU scatter、页内连续 run 绘制；树以规则格、64 宽 Visibility IR 和 VisibleIndex 绘制。CPU Burst 与 GPU Compute 是颜色和主光 CSM 的两条完整 backend；GPU 的 HZB 是独立可选项。Morton 只排树 Candidate Stream。主光 CSM 有独立阴影索引，不复用相机可见索引。

两套系统，两份 `BoundSector` / `visibleMap`。只挂 `TreeComponent` 可运行。文档里的 Sector 是 mesh × LOD × fade bucket，对应 `TreeLodBatch`，不是 `BoundSector`。

| 单位 | 草 | 树 |
|---|---|---|
| 剔除 | 组件盒 → 自持格 → 基础层 / 细节页 | 组件盒 → 自持格 → 实例，格盒先做地形 / HZB 遮挡 |
| 资源 | 每种草一份基础层 + `4×4` 细节页资产；只为驻留页分配 packed `ComputeBuffer` | 一种树 `bounds[]` + `matrices[]`，格 `{offset,count}` |
| Draw | 页内 1D 连续 run + `DrawMeshInstancedProcedural` | mesh × LOD × bucket + VisibleIndex；CPU Procedural、GPU args + Indirect；主光每级联独立 index / count（GPU 为 args） |
| Pack | 页载入或运行密度变化后重新 scatter / upload；释放页即释放资源 | 矩阵与静态候选只在载入 / 重排后 upload；CPU compact 或 GPU 三桶一次 mask→计数→前缀→compact，VS 只吃 uint index |

Pass 相位：`FoliageResidency.UpdateView` → 组件盒粗剔 → `Cull` 入队后一次 `CompleteAll` → `Pack` → `Draw`。每次渲染相机通过共享 ContextItem 的 renderId 只准备一次。有效主光阴影先准备颜色／级联候选与 Pack，再画各级联；无阴影时在 PreZ 或 Forward 准备。`treePreZ` 默认开：`Draw(PreZ)` 写 `activeDepth`，颜色 Pass 只 `Draw(Forward)`；关闭时直接画颜色。草页 buffer 只在该相机准备阶段非阻塞换入，后续级联、PreZ、Forward 复用同一批页和可见 run。草页 scatter 用页私有 `JobHandle`，不进共享 `taskHandles`；未完成时不阻塞绘制。草的 Pack 在组件盒外仍推进驻留页；Draw 两边都按盒剔。主光阴影按级联 Pack / Draw，树独立过滤 caster index / args，不能借用颜色可见索引；草直接复用所属相机本次渲染的颜色可见页 / run，画外草不投影。

PreZ：`FoliageDepth` 为 `ColorMask 0`，`ZTest LEqual`、`ZWrite On` 与主绘制一致。草与 `TreeLeave` 的 `AlphaToMask On` 且输出 albedo alpha，与颜色通道保持相同 MSAA 覆盖；`TreeBrak` 保持 `AlphaToMask Off`。草与树叶的普通 / 实例 Forward、PreZ 和阴影均先拒绝 alpha=0，再按材质阈值执行 alpha clip，不依赖 MSAA；阈值为 0 仍拒绝完全透明 texel，正 alpha 的阈值比较保持原规则；普通 `ShadowCaster` 与实例 `FoliageShadow` 显式 `AlphaToMask Off`，避免继承颜色状态并以零输出 alpha 清空覆盖。`TreeLeave` 的 `RenderType` 为 `TransparentCutout`，全部子网格进 PreZ；`TreeBrak` 只有 FadeOut / FadeIn 进；草全部进。`FoliageDepth` 与实例 Forward 共用 `FoliageLodFactor`，并声明同一套 `FOLIAGE_LOD_WEIGHT` / `FOLIAGE_GPU_TEMPORAL` 变体，clip 与 dither 逐位一致。HZB 用上一帧相机深度，按相机保存 viewProj；视点不连续则这一帧不用。投影仍匹配 RenderGraph 当前 camera depth 的 `TextureUVOrigin`，不可按 Metal 平台固定翻转 Y。

草：`sections` 保持 `N×N` 作为空间键；密度二进制资产是唯一运行数据真源，Scene 不再承载整张 `densityMap`。基础层按确定性抽样保留约 `1/8` 密度；每种草另有 `4×4` 细节页承载剩余密度。`FoliageRenderer.settings` 控制运行时驻留预算，默认最多 2 个 Terrain、每个草 prefab 跨 Terrain 最多 8 个细节页、离开视锥后保留 12 帧可见优先级；这些是可调预算，不改变 Bake 的 `4×4` 与 `1/8` 资产格式。不能用 Terrain 内的 detail prototype 序号当全局草种键。优先当前可见、近视点及已有驻留页；先释放未入选页的槽位，再申请新页，异步旧请求以代次隔离。页的 CPU `GrassPageScatterJob : IJobParallelFor` 跨帧完成后上传一张页内 packed `ComputeBuffer`；首次载入未就绪的页不绘制，密度重建期间继续使用旧 buffer，完成后替换，构建失败须归还页租约并释放部分分配。运行时 `terrain.detailObjectDensity` 或 `SetDensityScale` 变化触发驻留页重建，不回写 Bake 资产。页内 run 只画可见格；`count==0` 可作连续区间桥。CPU scatter 只做 XZ / 旋转 / 缩放，Y 由 VS 采 heightmap；只画 `meshes[0]`。

树：无「自有地形大 bound」；自持 `numSection` 默认 16；Bake 先 Morton 排 Candidate Stream，再 `CellFromLocal` 写 `{offset,count}`，格键公式不变。CPU chunk Job 可覆盖全量候选，但对不可见格尽早跳过；GPU 模式不调度 CPU 格 / 实例剔除与 LOD Job，格盒先做地形 / 可选 HZB 遮挡，实例再做精剔；HZB 读上一帧按相机保存的深度，视点不连续则跳过，投影须匹配保存时的 `TextureUVOrigin`；只对颜色可见候选与有效级联 caster 候选的并集计算 LOD，并集外不算 LOD。Visibility 是 IR（64 宽 chunk mask），不是绘制格式。CPU Burst 与树 Compute 是同一候选流的两种执行路径。CPU 直接按候选顺序 compact 最终 index / count，以 DrawMeshInstancedProcedural 提交，不分配或上传 args；GPU 首次使用时才创建 args。GPU 写每 chunk 的两个 uint mask，经计数与任意长度分层前缀扫描，按位排名写最终 index / args；Distance 权重与 index 同槽。禁止逐实例全局原子追加与 Append 实例列表，不依赖 Multi-Draw Indirect，不读回数量决定绘制。旧混合搬运选择器和 RunTransfer 已退役。同一视图的暂存 ComputeBuffer 按 CommandBuffer 顺序消费，不能在排队的 dispatch 消费前用即时 SetData 覆盖。颜色 LOD 淡化有两种正式模式，由 `treeLodFadeMode` 选择，不是旧路径兼容。时间窗按渲染 `Camera` 保存 hold / now 两个视点，共享 `alpha`；CPU 在 Burst 与相机状态中计算，GPU 通过自身状态与过渡归约闭合，不做正常帧读回；以屏占比迟滞稳定 LOD；Temporal 连续移动从已完成的 stable 档逐档追赶目标，每次只淡化相邻两档，不用旧视点重算替代已显示的 stable 档。传送／投影不连续仍硬切。距离互补只用当前视点，在硬切换点靠近的一侧按 `treeFadeWidth` 做过渡带，逐实例 `x` 只活在当帧 fade index 旁边，不进矩阵或 bounds。透视 / 正交相机按各自投影求屏半径，颜色与阴影使用同一口径；只有相邻 LOD 且实例 Forward pass 支持 dither 时双几何，fade 不进实例 payload。阴影两种模式都共享所属渲染相机的 LOD 历史、滞回、过渡档位与进度，级联不另推进状态；只按独立 caster 可见位过滤三桶，保留画外树投影。颜色 DC = 种 × LOD × bucket × submesh；主光 CSM 按相机 / 级联保留独立 index / count（GPU 为 args），fallback 级联球与投影必须用相机正前方的世界空间视锥角点。

设置权威：`FoliageRenderer.settings` 给绘制、`treePreZ`、Tree backend、遮挡、LOD 淡化模式、过渡带宽度、LOD 迟滞、主光投影和预算提供全局默认；`TreeComponent` 只覆盖本 Terrain 的 backend、遮挡、fade、淡化模式、过渡带宽度、迟滞、距离与主光投影，`GrassComponent` 覆盖距离与主光投影。`castGrassMainShadows` 全局默认关，组件以 `overrideMainShadows` / `castMainShadowsOverride` 覆盖；`drawGrass` 为草绘制及投影总开关。`fadeDuration` 只驱动时间窗。草密度仍由 Terrain 管，主光阴影距离和级联仍由 URP 管。Tree backend 默认 Auto：核心 Compute / 资源可用选 GPU，否则 CPU；强制 GPU 失败时仅状态变化提示并回退 CPU。Terrain Occlusion 在两 backend 执行同一保守算法，GPU HZB 仅在 GPU backend 生效。现有 treeOcclusion 枚举是遮挡保存真源，Inspector 两开关只映射其位，不另建 bool。核心 GPU 与 HZB 能力分别判断，缺深度或 HZB 不适用不关闭 GPU 基础可见性；关闭 HZB 必须清除本视图的有效状态，不能消费上一帧深度。组件 `showBounds` 仍是 Editor 线框总开关且保留真实深度；CPU 实时线框只代表 CPU 阶段，GPU 实时线框不能冒充最终可见性。按需冻结快照才能读取 GPU 格过滤和最终 VisibleIndex，快照必须绑定 backend、具体相机与帧。

距离模式多 LOD emit Job 共写当帧权重数组，必须按 `JobHandle` 依赖顺序调度；每个渲染相机分别记录 backend、上次模式与淡化开关，切换时重置该相机目标状态、首帧硬切，清除旧 args、HZB 和过期快照。工作资源按 backend 首次使用分配，随驻留保留；卸载、停用和 Renderer 释放须完整释放，初始化失败释放部分 GPU 资源并回退 CPU。树实例 Forward、`FoliageDepth` 与 `FoliageShadow` 的 `FOLIAGE_LOD_WEIGHT` 编译变体仅用于距离 fade 桶：绘制前绑定权重 buffer 并启用关键词，绘制后关闭；stable、Temporal、普通 Forward 与普通 ShadowCaster 不声明该资源。GPU Temporal fade 使用 FOLIAGE_GPU_TEMPORAL 编译变体读取共享 fade 状态；声明与读取同时受宏保护，CPU Temporal 和 stable 不依赖该资源，桶绘制后关闭关键词。Editor 冻结快照的距离权重和 VisibleIndex 同槽读回，只在请求时执行。

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
public unsafe void Cull(CommandBuffer cmdBuffer, in FoliageView view, in NativeList<JobHandle> taskHandles)
{
    var treeCullingJob = new TreeCullLodJob();
    {
        treeCullingJob.planes = view.planes;
        treeCullingJob.viewOrigin = view.origin;
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

- 未经用户明确要求上传具体证据媒体，禁止将截图、录屏、Game 画面、渲染对照图等验证媒体及其 `.meta` 添加到 Git / Git LFS、提交或上传远端仓库。“提交代码”“上传改动”不构成上传这些文件的授权。
- 禁止为上传验证媒体而转换格式、压缩或绕过 Git LFS 限制。验证结果默认只在 `Docs/DELIVERY.md` 记录必要的文字结论；临时媒体放在仓库外，验证结束后清理，不作为代码交付附件入库。
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
