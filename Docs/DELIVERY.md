# Infinity Foliage 交付记录

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
