# Infinity Foliage 架构

本文件描述 `com.infinity.render-foliage` 的现行结构。用法见 [README.md](README.md)，修改约束见 [AGENTS.md](AGENTS.md)，按日期保留的验收记录见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

规格以本仓库源码为准。引擎类型、着色器路径和已经编译进资源的符号，按它们在源码中的名字书写。

## 1. 范围

本包在 Universal Render Pipeline 中接管 Terrain 的草与树绘制。草与树是两套组件、两套空间数据和两套绘制路径。场景可以只挂 `TreeComponent`。

宿主版本写在 `package.json`：Unity `6000.6`，Universal Render Pipeline 与 Core `17.6.0`，Burst `2.0.0`，Collections `6.6.0`，Mathematics `1.3.3`。

程序集：

| 程序集 | 位置 |
|---|---|
| `Infinity.Rendering.Foliage.Runtime` | `Runtime/`，允许 unsafe |
| `Infinity.Rendering.Foliage.Editor` | `Editor/` |
| `Infinity.Rendering.Foliage.Shader` | `Shader/` |

命名空间为 `Landscape.FoliagePipeline`，编辑器代码为 `Landscape.FoliagePipeline.Editor`。

运行数据只有两类流式资产：每种草一份基础密度层和 `4×4` 细节页，每种树一份候选流。场景组件保存资产键、空间格和网格引用。GPU 资源使用 `ComputeBuffer`。

`FoliageRenderer.Create` 会执行 `FoliageLogicAsserts.Evaluate()`。断言位于 Runtime 程序集。

## 2. 目录

```text
Runtime/Component/          GrassComponent、TreeComponent、FoliageComponent、WindComponent
Runtime/Renderer/           FoliageRenderer 与颜色、深度、主光 Pass
Runtime/Source/Foliage/     驻留、资产编解码、空间格、草页、树扇区、GPU 可见性
Runtime/Source/Mesh/        MeshAsset、FoliageMesh、剔除与撒点 Job
Runtime/Source/Utility/     几何、FoliageLogic、风参数
Runtime/Resources/          TreeVisibility.compute
Editor/                     烘焙菜单、资产事务、Inspector
Shader/Foliage/             Landscape/Grass、TreeLeave、TreeBrak 与 Include
Docs/                       验收记录与证据
```

`Shader/DummyFoliageShaders.cs` 是空类型，只为让着色器程序集被编辑器索引。

## 3. 术语

| 术语 | 含义 |
|---|---|
| 组件盒 | `FoliageComponent.boundSector.bound`，覆盖该 Terrain 植被范围的包围盒 |
| 空间格 | `BoundSection`。边数 `N = numSection`，下标 `CellIndex(x, y) = x * N + y` |
| 草页 | 一种草的一块密度。基础层页坐标为 `-1,-1`；细节页坐标为 `0..3 × 0..3` |
| 细节层序号 | `grassIndex`，当前 Terrain 上 `detailPrototypes` 的下标，用于取样和文件名 |
| 草种槽键 | `speciesKey`，细节原型 prefab 的实体标识。细节页预算按这个键跨 Terrain 计数 |
| 候选 | 一棵树实例。烘焙序列经 Morton 排序后进入该树种的数组 |
| 格段 | `TreeCell.offset` 与 `TreeCell.count`，指向排序后候选的连续区间 |
| 可见性中间表示 | 宽 64 的 chunk 位掩码。它是剔除结果，顶点着色器不读取它 |
| 可见索引 | 最终 `uint` 下标，绑定为 `_TreeIndexBuffer` |
| 淡化桶 | `LodBucket` 的 Stable、FadeOut、FadeIn，共 3 个绘制桶 |
| 绘制批次 | `TreeLodBatch`，键为网格、LOD、淡化桶、子网格 |
| 渲染标识 | `FoliageCameraData.renderId`。同一次相机渲染内的颜色、深度和阴影复用它 |

草组件的 `BoundSector` 带有完整空间格。树组件的 `BoundSector` 只保存组件盒；每种树的 `TreeSector.boundSector` 另有自己的 `N×N` 格和 `visibleMap`。

## 4. 渲染相位

`FoliageRenderer` 是 `ScriptableRendererFeature`。`treePreZ` 为真时入队深度 Pass，事件为 `AfterRenderingPrePasses`。颜色 Pass 始终入队，事件为 `AfterRenderingOpaques`，并声明需要深度。主光 Pass 始终入队，事件为 `AfterRenderingShadows`。非 Play 时不录制这些 Pass。

每次相机渲染有一份 `FoliageCameraData`。`renderId` 在该上下文首次使用时分配，`prepared` 表示这份颜色结果已经完成剔除和打包。

颜色或深度 Pass 的准备顺序：

1. `FoliageResidency.UpdateView` 按视点更新 Terrain 与草页驻留。
2. 用组件盒做视锥粗剔，得到本 Pass 的 `boundsVisible`。
3. 若尚未准备：各组件 `Cull`，随后一次 `JobHandle.CompleteAll`，再 `Pack`，然后把 `prepared` 置真。
4. 非仅准备的 Pass 对组件盒可见且允许绘制的组件调用 `Draw`。

草的 `Cull` 与 `Pack` 在组件盒之外仍然执行。草和树的颜色、深度 `Draw` 都要求组件盒可见。存在主光剔除平面时，树即使组件盒未进颜色视锥，仍会进入颜色阶段的 `Cull` 与 `Pack`，以便同一次准备生成级联所需的 LOD 状态。

主光 Pass 的事件早于深度 Pass 和颜色 Pass。它实际执行时，先把各级联平面写入 `FoliageCameraData.shadowPlanes`，再以仅准备方式完成共享的颜色剔除和打包。每个级联对通过组件盒测试的树执行 `Cull`、`CompleteAll` 和 `Pack`；`Draw(Shadow)` 只提交本 `renderId` 已经打包的树视图。草在级联中只 `Draw(Shadow)`，沿用本次 `renderId` 已经准备好的可见页和 run。之后的深度 Pass 与颜色 Pass 看到 `prepared`，只绘制。

主光 Pass 在没有草或树投射体、URP 未启用主光阴影、阴影距离不大于 0、级联数超出 1 到 4，或需要向已有阴影图追加绘制但该纹理无效时直接返回。此时 `treePreZ` 决定准备位置：开启时由深度 Pass 完成准备并绘制，颜色 Pass 只绘制；关闭时由颜色 Pass 完成准备并绘制。`treePreZ` 默认开启。

草页的 scatter 使用页私有 `JobHandle`。打包时只在该 Job 已完成的情况下取结果。未完成的新页保留上一份缓冲，第一次尚未就绪的页不绘制。这个 Job 不进入 Pass 共享的 `taskHandles`。

## 5. 设置

全局默认在 `FoliageRenderer.settings`（`FoliageRenderSettings`）。组件只覆盖本 Terrain 上列出的项。

| 字段 | 默认 | 覆盖 |
|---|---|---|
| `drawGrass` | 开 | 仅全局。关闭后草的颜色与投影都不画 |
| `drawTrees` | 开 | 仅全局 |
| `treePreZ` | 开 | 仅全局 |
| `castTreeMainShadows` | 开 | `TreeComponent.overrideMainShadows` / `castMainShadowsOverride` |
| `castGrassMainShadows` | 关 | `GrassComponent.overrideMainShadows` / `castMainShadowsOverride` |
| `grassDistanceScale` | 1 | 仅全局，乘进入 Play 时保存的 Terrain 草距离 |
| `treeDistanceScale` | 1 | 仅全局，乘进入 Play 时保存的 Terrain 树距离 |
| `treeBackend` | `Auto` | 树组件 `overrideBackend` / `backendOverride` |
| `treeOcclusion` | `TerrainAndHzb` | 树组件 `overrideOcclusion` / `occlusionOverride` |
| `treeLodFade` | 开 | 树组件 |
| `treeLodFadeMode` | `Temporal` | 树组件 |
| `treeFadeWidth` | 0.2，范围 0.01–0.99 | 树组件 |
| `treeLodHysteresis` | 0.08，范围 0–0.5 | 树组件 |
| `residentTerrainBudget` | 2，最小 1 | 仅全局 |
| `grassDetailPageBudget` | 8，最小 0 | 仅全局 |
| `pageVisibilityHoldFrames` | 12，最小 0 | 仅全局 |

`TreeComponent.fadeDuration` 默认 0.5，范围 0.05–2，只驱动时间窗淡化。草密度来自 `Terrain.detailObjectDensity`，`GrassComponent.SetDensityScale` 写回该值。主光阴影距离、级联数量和分割由 URP 资产决定。

`TreeVisibilityBackend` 为 `Auto`、`CPU`、`GPU`。请求为 `Auto` 或 `GPU` 时尝试初始化 GPU，两条请求使用同一段初始化代码。初始化失败时实际后端为 CPU；请求为 `GPU` 且警告文本发生变化时记录一次。

`TreeOcclusionMode` 用位保存：`None = 0`，`Terrain = 1`，`Hzb = 2`，`TerrainAndHzb = 3`。Inspector 上的 Terrain Occlusion 与 GPU HZB 只映射这两位。

实际后端变化时释放该树种已有视图。淡化开关或淡化模式变化时，CPU 在当帧重置 LOD，并作废该相机的层次 Z 缓冲和编辑器快照；GPU 在当帧带上重置标记，同样作废层次 Z 缓冲和快照。工作缓冲在对应后端第一次使用时分配，随扇区驻留保留；卸载、停用和 Renderer 释放时整组释放。GPU 初始化失败时释放已经分配的部分。

## 6. 草

### 6.1 组件

`GrassComponent` 菜单名为 `HG/Foliage/Grass Component`。`numSection` 序列化默认值是 16。`BuildTerrainGrass` 会把它写成 `(heightmapResolution - 1) / 32`。`heightmapResolution - 1` 能被 32 整除时，每格恰好覆盖 32 个高度图采样；世界尺寸由 `TerrainData.size` 决定。`OnSave` 仅在空间格数量不等于 `N×N` 时重建包围盒。

进入 Play 且注册成功时，组件记下 `detailObjectDistance`，再把 Terrain 的该距离设为 0，从而停用引擎内置草。注销或注册失败时写回原距离。运行距离优先取组件覆盖值，否则取记下的距离乘 `grassDistanceScale`。

### 6.2 烘焙密度

`UpdateTerrainGrass` 按 `FoliageAssetCodec.PageAxis`（4）切分 `detailResolution`。`GetDetailLayer` 的返回数组以 `[y, x]` 读取，再映射回全局细节坐标。密度大于 255，或分辨率不能被 `numSection` 整除时，烘焙失败。

基础层密度由 `FoliageAssetCodec.BaseDensity` 决定：

```text
base = (density + (hash(x, y, grassIndex) & 7)) / 8
detail = density - base
```

细节页保存剩余密度。格上的 `count` 按原始密度累加，资产提交成功后才写回场景。基础层与全部细节页相加后等于原始密度。

### 6.3 驻留

`FoliageResidency` 先按视点选择最多 `residentTerrainBudget` 个 Terrain。已驻留 Terrain 在排序距离上乘 0.81。选中的 Terrain 调用 `SetResident`，基础层随 Terrain 加载，不占用细节页槽。

细节页按草种槽键分别保留最多 `grassDetailPageBudget` 个。排序依次考虑槽键、当前可见、距离、是否已经驻留、上次使用帧和页坐标；已驻留页的排序距离乘 0.81。离开视锥后，已驻留页在 `pageVisibilityHoldFrames` 内仍按可见优先级参与排序。预算为 0 时不申请细节页，基础层继续可用。

加载是异步的。页被取消或 Terrain 退出驻留时增加代次；过期回调只释放自己的资源和租约。构建失败时归还租约并释放已分配的缓冲。

运行中 `detailObjectDensity` 变化只重建已驻留页的 scatter，不改烘焙文件。

每个相机的 `GrassView` 另有 120 帧的缓存淘汰，与页的 12 帧可见优先级是两个计数。

### 6.4 撒点与绘制

`GrassPageScatterJob` 是 `IJobParallelFor`。它在格内写 XZ、绕 Y 旋转和宽高缩放，矩阵的 Y 为 0。顶点着色器 `GrassInstancePosition` 采样 `_TerrainHeightmap` 得到高度。每页上传一份 `ComputeBuffer`，元素为 `GrassElement`（一个 `float4x4`），绑定 `_GrassElementBuffer`。

密度缩放使用 `FoliageLogic.ScaleGrassCount`。基础层与细节页使用不同的 layer 参与哈希，避免两条层选出同一批实例。

可见 run 由 `FoliageLogic.MergeVisibleRuns` 生成。一段 run 从可见且计数为正的格开始，沿同一行扩展。计数为 0 的格可以桥接前后实例区间。可见性为 0 且计数为正的格结束当前 run。绘制调用 `DrawMeshInstancedProcedural`，网格和材质取 `meshes[0]`、`materials[0]`，`_InstanceOffset` 为该 run 的起点。

Pass 名为 `ForwardLit-Instance`、`FoliageDepth`、`FoliageShadow`。

## 7. 树

### 7.1 组件与烘焙

`TreeComponent` 菜单名为 `HG/Foliage/Tree Component`。`numSection` 默认 16，与草的菜单计算结果互不影响。进入 Play 时记下 `treeDistance` 并把它设为 0，注销时恢复。

`BuildTerrainTree` 按 `treePrototypes` 建立 `TreeSector`，并从 prefab 的 `LODGroup` 读取网格与屏幕过渡。`meshes[i]` 与 `lODInfos[i]` 一一对应；多个 LOD 引用同一网格时仍各占一槽。材质用去重后的列表。LOD0 的 `screenSize` 为 1，其后各档取上一档的 `screenRelativeTransitionHeight`。

`UpdateTerrainTree` 收集 `treeInstances` 到 `treeTransfroms`，再 Morton 排序并用 `CellFromLocal` 写入格段。资产提交成功后把 `transforms` 置空。该字段标记为 `NonSerialized`。`OnSave` 只在空间格数量变化时重建格。

### 7.2 候选与可见性

一种树在内存中是排序后的包围盒、矩阵和实例所属格。CPU 与 GPU 读取同一份候选。

CPU 使用 Burst Job：格的地形遮挡、实例视锥与距离、LOD、三个桶的掩码，然后按候选顺序压缩成最终索引和计数。提交使用 `DrawMeshInstancedProcedural`，直接传入该桶计数。CPU 路径不创建、不上传 indirect 参数缓冲。

GPU 在第一次打包时为每个 LOD 创建 indirect 参数 `ComputeBuffer`。`TreeVisibility.compute` 的核包括 `BuildHzb`、`BuildHzbMip`、`PrepareView`、`CullCells`、`CullInstances`、`ReduceChanges`、`AdvanceFade`、`CommitLod`、`EmitMasks`、`ScanBlocks`、`AddOffsets`、`Compact`、`CopyArgsCount`。每个 64 宽 chunk、每个桶写入两个 `uint` 掩码，再经分块计数、分层前缀和与按位排名写入最终索引和参数。提交使用 `DrawMeshInstancedIndirect`。

同一视图的暂存缓冲按 `CommandBuffer` 顺序上传和消费。实例可见性的低位是颜色，级联 `c` 使用 `2u << c`。LOD 只对颜色可见候选与有效级联投射候选的并集计算。

GPU 路径不调度 CPU 的格、实例、LOD 和压缩 Job。正常帧不把索引读回 CPU。

### 7.3 遮挡

地形遮挡在两种后端使用同一保守测试：格盒采样高度图，比较盒底与地形。高度数据在该 Terrain 第一份树资产加载时批量采样一次，各种树复制到自己的数组。

层次 Z 缓冲只在 GPU 后端且遮挡模式含 `Hzb` 时生效。它在颜色 Pass 末尾用当前相机深度构建，分辨率 64×32，并保存与该深度 `TextureUVOrigin` 一致的投影。下一帧只有帧号差为 1、且视点连续时才使用。正交投影、MSAA 或缺少深度时不构建。关闭该选项或视点不连续时作废本视图的有效状态，不沿用上一帧深度。缺少层次 Z 缓冲时保留其余 GPU 可见性结果。

### 7.4 LOD 淡化

屏幕半径按当前相机的投影计算。透视相机使用视野角，正交相机使用正交尺寸。颜色和阴影共用这个口径。`treeLodHysteresis` 只作用于时间窗的稳定档选择。

时间窗按渲染相机保存 hold 与 now 两个视点和一份 `alpha`。连续移动从已经提交的 stable 档向目标追赶，每个时间窗最多跨越相邻一档。`FoliageLogic.ViewDiscontinuous` 在单帧位移大于 `clamp(绘制距离 × 0.25, 1, 8)`，或投影矩阵分量差超过 0.1 时判定为不连续，当前过渡硬切。CPU 在 Job 中推进；GPU 在自己的状态缓冲里归约，核为 `AdvanceFade` 与 `CommitLod`。

距离模式只看当前视点。硬切换点附近由 `treeFadeWidth` 形成过渡带，逐实例权重与该帧 fade 索引同槽保存，写入 `_LodWeightBuffer`。权重不进入矩阵或包围盒。`DistanceFadeSnap` 为 0.02。

两种模式在 `treeLodFade` 关闭时只画 stable。只有相邻 LOD，且该网格的实例 Forward Pass 存在时，才同时提交 FadeOut 与 FadeIn。

阴影共用所属渲染相机的 LOD 历史、滞回、过渡档和进度，级联不另行推进。级联按自己的可见位过滤三个桶并压缩索引。画外但仍被级联平面接受的树保留投影。距离模式的级联权重按该级联自己的索引顺序压缩。

颜色绘制次数按树种、LOD、桶和子网格相乘。每个相机的每个级联保留独立索引；GPU 上同时保留独立参数缓冲。

### 7.5 主光级联体

没有场景里其他投射体时，主光 Pass 自建阴影图，并把它交给 URP 作为主阴影纹理。已有阴影图时，本包向该纹理追加绘制。级联球使用相机视口四角经 `ViewportToWorldPoint` 得到的世界坐标，近远距离沿相机前方。

## 8. 着色器

| 资源名 | 文件 | 子着色器 RenderType | AlphaToMask |
|---|---|---|---|
| `Landscape/Grass` | `Shader/Foliage/Grass.shader` | `Opaque` | 子着色器开启；`FoliageShadow` 与 `ShadowCaster` 关闭 |
| `Landscape/TreeLeave` | `Shader/Foliage/TreeLeave.shader` | `TransparentCutout` | 同上 |
| `Landscape/TreeBrak` | `Shader/Foliage/TreeBrak.shader` | `Opaque` | 保持关闭 |

深度 Pass `FoliageDepth` 使用 `ColorMask 0`、`ZTest LEqual`、`ZWrite On`。草的全部绘制进入深度 Pass。`TreeLeave` 因 RenderType 为 `TransparentCutout`，全部子网格进入深度 Pass。`TreeBrak` 的稳定桶不进入，淡化桶进入。

草、树叶的普通 Forward、实例 Forward、深度和阴影，以及树干的对应剪裁路径，使用：

```text
clip(alpha > 0.0 ? alpha - _AlphaThreshold : -1.0)
```

完全透明的纹素被拒绝。阈值比较只作用于正的 alpha。颜色与深度在开启 AlphaToMask 时输出 albedo alpha。阴影 Pass 关闭 AlphaToMask。

`TreeLeave` 与 `TreeBrak` 的实例 Forward、`FoliageDepth`、`FoliageShadow` 声明 `FOLIAGE_LOD_WEIGHT` 与 `FOLIAGE_GPU_TEMPORAL`。距离淡化桶在绘制前绑定权重并打开前一个关键字，GPU 时间窗淡化桶绑定 `_TreeFadeState` 并打开后一个关键字，绘制后关闭。稳定桶、CPU 时间窗和普通 Forward / ShadowCaster 不绑定这些资源。草着色器不声明这两套变体。

实例草和实例树的天空球谐由 `Foliage.hlsl` 的 `SampleFoliageSH` 计算，系数来自 `RenderSettings.ambientProbe`，只在颜色 Pass 绑定。普通 Forward 使用 URP 的 `SampleSH`。非均匀缩放的法线由 `TransformFoliageNormal` 计算。

`Landscape/Grass` 的非实例 `ForwardLit` 与 `ShadowCaster` 带有程序化实例入口 `SetupNatureRenderer`，缓冲名为 `_NatureRendererBuffer`，子着色器标签含 `NatureRendererInstancing`。本包运行时绘制使用 `ForwardLit-Instance` 与 `_GrassElementBuffer`。两套入口编译在同一份着色器资源中。

草的距离淡出和风动画在 `ViewFade.hlsl`、`Wind.hlsl`。当前 `TreeLeave` 与 `TreeBrak` 不包含风函数。

## 9. 资产事务

编辑器把流式文件写到 `Assets/Generated/Foliage/Resources/{assetKey}/`。`assetKey` 由已保存场景的 GUID 与 Terrain 对象标识组成。场景未保存时不能生成键。

`FoliageAssetCodec` 容器魔数为 `0x474C4F46`，版本为 1。种类值：草页 1，树候选 2，清单 3。载荷经 Deflate 压缩。文件名：

```text
grass_{grassIndex}_base.bytes
grass_{grassIndex}_page_{pageX}_{pageY}.bytes
tree_{treeIndex}.bytes
index.bytes
```

运行时用去掉扩展名的 Resources 路径加载。版本或键不匹配时拒绝该资产。

`FoliageAssetWriter.CommitBatch` 先把新内容写入临时文件，再用替换进入目标路径，成功后刷新导入并删除本类别中不再引用的旧文件。失败时按事务回滚。草的格计数在提交成功之后写入场景内存。树的候选编码在写盘前完成，场景在提交成功后只保留资产键。

这些生成文件属于宿主工程，不在本包仓库内。

`MeshAsset` 是可复用的网格描述，菜单 `Landscape/MeshAsset`。`BuildMeshAsset` 在有 `LODGroup` 时按 LOD 取渲染器网格，否则在同时存在 `MeshFilter` 与 `MeshRenderer` 时建立单档。

## 10. 风

风是本包内的独立子系统，源文件为 `WindComponent`、`WindSettings`、`Wind.hlsl` 以及对应编辑器脚本。它通过全局着色器参数 `g_WindDirection`、`g_Wind`、`g_Turbulence`、`g_GustNoise`、`g_SmoothTime`、`g_PerlinNoise` 驱动草。可选的 `WindZone` 只作为这些参数的来源。

`WindSettings` 保留 `GustDirection`、`GustStrength`、`GustSpeed`、`ShiverStrength` 到现行字段的序列化别名。该子系统使用两空格缩进，与草树运行时的版式分开维护。

## 11. 诊断

组件的 `showBounds` 在选中时绘制带深度测试的线框。

草的格线表示 CPU 视锥和距离结果。页线表示该页处于基础层驻留还是细节页驻留。树的 CPU 实例线在 CPU 打包之后，按调试相机保存的 LOD 上色；未指定 `debugCamera` 时使用 Main Camera。GPU 实时实例线保持灰色。

`Capture Visibility Snapshot` 只在编辑器 Play 中、对指定相机和当前帧读回 GPU 格过滤、最终可见索引、参数计数和距离权重。快照记录请求的后端与实际后端。`Clear Visibility Snapshot` 回到实时线框。

## 12. 释放

停用组件时注销驻留、释放 Native 集合和 GPU 缓冲，并恢复 Terrain 内置绘制距离。树扇区等待进行中的 Job 结束后再释放高度、格数据和计算缓冲。Renderer 释放时释放层次 Z 缓冲和各树组件持有的视图。

页或扇区在分配中途失败时释放已经拿到的缓冲和租约，不留下半套资源。
