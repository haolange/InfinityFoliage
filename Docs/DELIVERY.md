# Infinity Foliage 交付记录

## 2026-09-25：Bound、VT、植被 SH 分项修复与验收

本轮使用已打开的 Unity 6.6.0f1 Editor，未启动第二个 Editor、未建分支。保留了 foliage 与相邻 `com.infinity.virtual-texture` 包原有的未提交改动。

| 验收项 | 结果 | 本机证据及界限 |
|---|---|---|
| Bound 深度 | **转镜头复现并通过当前画面验收** | Play 中将 `PlayerCamera` Y 旋转从 -50.629° 改到 20° 后，Bound 在画面约 66% 宽处出现笔直明暗分界；该位置没有对应几何。`Assets/Profile/UniversalAsset.asset` 的 Render Scale 为 0.66；Frame Debugger 的场景目标 1033×1174、GameView 目标 1565×1780，且异常位于 URP `Final Depth Copy → DrawGizmos`。URP Asset Inspector 明确提示：GameView 启用 Upscaling 时 Camera depth 不受支持，会对该 pass 停用 depth test。临时改为 Render Scale 1，同机位分界消失；恢复 0.66 后分界回归。最终将 URP Asset Render Scale 保存为 **1**，Bound 继续使用真实深度。未修改 Foliage 深度附件或 `Debug.DrawLine`。代价是内部像素数相对 0.66 约为 2.30 倍；实际 GPU 耗时未测。 |
| VT 地形黑面 | **当前机位 Edit/Play/退出 Play 画面通过；边界全状态未验收** | `TerrainLitInclude.hlsl` 现在以 URP TerrainLit 片元路径回退，只有 `_VTReady`、覆盖范围和页表 alpha 有效时采样 VT。页表首次使用及反馈重建时清零，驻留条目标 alpha=1。现有 Editor 的同一 Game 相机在非 Play、Play、退出 Play 均显示正常地形颜色；尚未逐一跨越 VT 边界或验证每种缺页状态。 |
| VT 反馈寿命 | **当前连续 Play 未再出现目标异常** | `FVirtualTextureFeedback` 在 GPU 回调内复制为持久 NativeArray，只允许一个未完成请求，消费后释放，停用/销毁时撤销回调代次。Editor.log 最后一次 `FDecodeFeedbackJob.encodeDatas has been deallocated` 为第 20972 行（修复前）；本轮 Play 后日志超过 22200 行，未新增该异常。Editor 仍间歇报 `attempt to write a readonly database`，属于独立的资产数据库问题，本轮未修复。 |
| 植被 SH | **编译及当前天空画面通过；天空变化 A/B 未验收** | Grass、TreeLeave、TreeBrak 的实例 pass 删除七组固定系数，按当前 `RenderSettings.ambientProbe` 七向量打包至每次绘制的 MaterialPropertyBlock；Tree 在 `Clear()` 后重绑。实例法线改为逆转置等价的余子式变换。当前 Play 的实例树比非 Play 的静态显示明显受天空环境照亮；未对同材质普通 Forward 作数值测量，也未改变天空设置做控制实验。 |
| 工程 | **通过** | 使用本机 Unity 6.6 Roslyn `csc.dll` 和 `Library/Bee/artifacts/200b0aE.dag` 现有响应文件分别编译 `Infinity.Rendering.VirtualTexture.Runtime`、`Infinity.Rendering.Foliage.Runtime` 到 `/private/tmp`，均 exit 0；只有原有 API 废弃与序列化警告。改动路径 `git diff --check` 无新增空白错误；Editor 中无本轮新增 CS 或 shader 编译错误。 |

上述 Play/Frame Debugger 截图由当前 Editor 的交互工具观察，未导出为持久图像文件。VT 跨边界、天空变化和 Forward/实例数值对照仍未通过，不能用当前画面代替。URP 的缩放 GameView 深度限制仍在；Render Scale 1 是本项目的画面修复及成本选择，未来若恢复 Upscaling，需先验证所用 URP 版本已修复该限制。

日期：2026-09-17。本机未打开 Unity Editor / Unity Hub。Agent 工作台只在 [AGENTS.md](../AGENTS.md)，没有 `DESIGN.md`。

## 本步：树 Visibility IR

- Visibility 是 IR：64 宽 `chunk mask`。`TreeCullLodJob` 按 chunk 写 mask + LOD，不再写全数组 `instanceVisible`。
- 已删除 `TreeCompactLodJob`（全量扫 compact）。`TreeEmitLodMasksJob` 只走 mask 里的 set bit。
- 三种 lowering：`CompactIndex` / `BitMaskTransfer` / `RunTransfer`。`PickVisibilityCodec` 按 uploadBytes + expandCost 每桶每帧选一个。CPU 无 compute 时固定 CompactIndex。结果只进每 LOD×bucket×submesh 的 `VisibleIndex` + 5-uint args。
- VS 仍只读 `_TreeIndexBuffer[SV_InstanceID]`。`Landscape/TreeLeave`、`Landscape/TreeBrak` 路径未改。
- Bake / Play SoA：Candidate Stream 先 Morton 排序，再 `CellFromLocal`。格键 `i = x * numSection + y` 未改。
- CPU OcclusionCull：64×64 地形高度场沿线坡度测试（`TreeComponent.SampleOcclusionHeight`）。无高度场则不做这条，不假装全可见。
- GPU：`Runtime/Resources/TreeVisibility.compute`（`Resources.Load("TreeVisibility")`）。kernel：`BuildHzb` / `BuildHzbMip` / `ExpandMaskToIndex` / `ExpandRunToIndex` / `FilterIndexHzb` / `CullInstances` / `CopyArgsCount`。资源只有 `ComputeBuffer`。compute 不可用则 CPU policy。
- 文档 Sector = `TreeLodBatch`。空间格仍是 `BoundSector`。草 packed / 1D / 16 格 upload 未改。
- `FoliageLogicAsserts` 本步用 `csc` 再跑通，新增 Morton / chunk / mask / run / codec / 地形遮挡。
- 改动文件 `ReadLints`：无诊断。
- composer explore：代码里无 `TreeCompactLodJob` / `instanceVisible` / `GraphicsBuffer` / `FindRun` / `TryGetSetupSlice` / `ScheduleScatter`；无 `DESIGN.md`；无 V1/V2 visibility；无草 per-section buffer 双路径。`WindSettings` 的 Gust `FormerlySerializedAs` 按不变式保留。`AGENTS.md` 里的同名只是禁令。

## 已落地（仍有效）

- 单位分离：草与树各持 `BoundSector` / `visibleMap`。只挂 `TreeComponent` 可走树路径。
- 整包去 `F` 前缀。几何进 `Landscape.FoliagePipeline`。`WindSettings` 的 `Gust*` 未剥。
- 草：一种草一张 packed `ComputeBuffer`；Play `OnRegiste` 每种草一次 `IJobParallelFor`；私有 `JobHandle` 跨帧 `IsCompleted`。每显示帧最多 16 格 visible 优先。Draw = uploaded ∧ visible。无 `TryGetSetupSlice` / `ScheduleScatter` / C# `Task`。
- Bake：density / transforms 写完后再算格归属。`OnSave` 只在 `numSection` 变化时重建空间格。Play 中不把空 `transforms` 写回。
- `DummyFoliageShaders.cs` 保留。`package.json` 名未改。

## 编译实况

本机：

1. `D:\Projects\Unity\LandscapeExample\Library\ScriptAssemblies` — **不存在**
2. Unity 2021.1 `Editor/Data/Managed` — **本机未装 2021.1**（Hub 只有 `6000.5.8f1`）
3. 2021.1 所需 `Unity.Burst` / `Unity.Collections` / `Unity.Mathematics` / `Unity.Jobs` / `Unity.RenderPipelines.*` — **均缺失**
4. 宿主 PackageCache — **不存在**
5. 不拿 Unity 6 DLL 冒充本包 Runtime 编译。

`FoliageLogicAsserts.Evaluate()` 用 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 与 `FoliageLogic.cs` 编临时 exe，输出 `FoliageLogicAsserts passed`。

| asmdef | 结果 |
|---|---|
| `Infinity.Rendering.Foliage.Runtime` | **本步未编译**（缺 2021.1 + Burst / Collections / Mathematics / Jobs / URP12） |
| `Infinity.Rendering.Foliage.Editor` | **本步未编译**（同上，且依赖 Runtime） |
| `Infinity.Rendering.Foliage.Shader` | 未在本步重编 |

不得把 Runtime / Editor 标成已通过 Unity 编译。

## 宿主机必做（Windows + Unity 2021.1 + URP 12）

1. 旧 Scene 与 MeshAsset **整表 rebake**（含树 Morton 序）。无兼容层。
2. PlayMode 草：与上轮相同（进 Play 即 Schedule、visible/近处优先、出锥仍 upload、共享 `CompleteAll` 不得卡住 scatter）。
3. PlayMode 树：
   - 只挂 `TreeComponent` 能画。
   - Frame Debugger：DC 仍是 种 × LOD × bucket × submesh；VS 只绑 `_TreeIndexBuffer`。
   - 邻级 dither、跨级硬切。
   - 地形脊后的树应被 CPU 高度场挡住；不透明物后的树在 `_CameraDepthTexture` 可用时应被 HZB 挡住。
   - compute 导入失败时应自动走 CPU CompactIndex，场景仍能画。
   - Indirect args 的 `count` 在 GPU lowering 后应对；多 submesh 的 instance count 应一致。
4. 多 Terrain 接缝：草缝本轮不修；邻块卸载掉边树是流式，不是 bug。
5. 运行时内置草 / 树距离被置 0，停用组件后应恢复。
6. 宿主若仍 `using InfinityTech.Core.Geometry` 会一起炸，需改成 `Landscape.FoliagePipeline`。

## 跨平台 / 下次在 Unity 机推进

- GPU HZB / `CullInstances` / args UAV 写 `IndirectArguments`：**本机未 Play**。到装了 Unity 2021.1 + URP12 的机器继续找本 Agent。
- 阴影第二份 index、屏占比迟滞、内部 BVH、草 mesh LOD、可变 section：仍不做。
- `package.json` 的 `com.infinity.render-foliage` 与文件夹 `com.infinity.foliage` 不一致，不改。
