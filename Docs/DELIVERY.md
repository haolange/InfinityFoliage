# Infinity Foliage 交付记录

日期：2026-09-17。本机未打开 Unity Editor / Unity Hub。Agent 工作台只在 [AGENTS.md](../AGENTS.md)，没有 `DESIGN.md`。

## 文档与风格收口

- 已删除包内 `DESIGN.md`。架构不变式、C# 风格、禁止项都在 `AGENTS.md`。
- Runtime 外形按去 `F` 之前的本包写法收回（`in` / `MethodImpl` / Job 赋值块 / 无 `ShaderProperty` / 无 `sealed` 辅助类 / Renderer `#region`）。草树布局语义未改。
- `FoliageLogicAsserts` 在风格收回后再用 `csc` 跑通。
- 风格收口步：Runtime / Editor **仍未编译**（缺 2021.1 与 Burst / Collections / Mathematics / Jobs / URP12）。不得标成已通过 Unity 编译。

## 已落地

- 单位分离：草与树各持 `BoundSector` / `visibleMap`。只挂 `TreeComponent` 可走树路径。
- 整包去 `F` 前缀。几何进 `Landscape.FoliagePipeline`。`Foliage*` 保留。`WindSettings` 的 `Gust*` 序列化别名未剥。
- 草：一种草一张 packed `ComputeBuffer`；`sections` 保持 `N×N`；CPU 1D run（按行断开）；`_InstanceOffset`；空格 `count==0` 当桥。Play `OnRegiste` 每种草一次 `IJobParallelFor` 全格；私有 `JobHandle` 跨帧 `IsCompleted`。`ComputeBuffer` 延到首次 `SetData`。每显示帧最多 16 格：visible 优先，同级按距离；相邻格合成一段 packed `SetData`，允许多段。Draw = uploaded ∧ visible。草 InitView/Flush 出锥仍跑。无 `TryGetSetupSlice` / 顺序前缀 Draw / `ScheduleScatter` / C# `Task`。
- 树：SoA `bounds[]` / `matrices[]`；自持规则格一直开；先按格重排再 upload 矩阵；cull+LOD 合一 Job；按当前 `Camera` 分桶；`|lod|==1` 才双几何；index + 5-uint args + `DrawMeshInstancedIndirect`。
- Bake：density / transforms 写完后再算 `{offset,count}` 与格盒。`OnSave` 只在 `numSection` 变化时重建空间格。Play 中不把空 `transforms` 写回。
- 死代码：`TreeDrawCommand` / `MeshPassProcessor` / `MeshElementCollector` / `BoundComponent` / `FPSSync` 已不在工作区。`ListExtent.AddUnique` 仍被 Bake 使用，文件保留。`DummyFoliageShaders.cs` 保留。
- Shader 路径未改：`Landscape/Grass`、`Landscape/TreeLeave`、`Landscape/TreeBrak`。`package.json` 名未改。

## 静态验收

`FoliageLogicAsserts.Evaluate()` 用 .NET Framework `csc` 与 `FoliageLogic.cs` 编成临时 exe 后跑通，覆盖：

- `sections.Length == numSection²` 的前缀和
- 空格当桥；只有 `count>0 && visible==0` 断开
- 16×16 全可见 run 数 = `numSection`
- 棋盘不合
- 可见格优先于更近但不可见格；预算 16
- 选中 `{0,1,2}` 合并为 1 段；中间有实例的不相邻格为多段
- 空格当桥合成一段 upload；格 16 destOffset ≠ 0
- 已 cull 保持 LOD 哨兵 `-1`
- `|lod0-lod1|>1` 只进 `lodNow` stable
- `TreeDrawCount` 与可见格数无关

改动文件 `ReadLints`：无诊断。

composer 扫工作区：无 `TryGetSetupSlice` / `FlushUploadRange` / `ScheduleScatter` / `i < m_Counter` Draw 前缀；无 `ScheduleBuild` 内 `Random`；无 `Init` 立刻 `new ComputeBuffer`；无 `DESIGN.md` 文件。`BoundComponent` / `FPSSync` / `TreeDrawCommand` / `MeshPassProcessor` / `MeshElementCollector` 磁盘上已不在。

## 编译实况

可见优先 + 点火步再次搜索：

1. `D:\Projects\Unity\LandscapeExample\Library\ScriptAssemblies` — **不存在**
2. Unity 2021.1 `Editor/Data/Managed` — **本机未装 2021.1**（Hub 只有 `6000.5.8f1`）
3. 2021.1 所需 `Unity.Burst` / `Unity.Collections` / `Unity.Mathematics` / `Unity.Jobs` / `Unity.RenderPipelines.*` — **均缺失**
4. 宿主 PackageCache — **不存在**
5. Unity 6 模板 libcache 里有 Burst / Collections / Mathematics / URP Runtime，**没有**独立 `Unity.Jobs.dll`，且版本不是 2021.1 / URP12。不拿来冒充本包 Runtime 编译。

`FoliageLogicAsserts` 本步用 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 与 `FoliageLogic.cs` 编临时 exe，输出 `FoliageLogicAsserts passed`。

| asmdef | 结果 |
|---|---|
| `Infinity.Rendering.Foliage.Runtime` | **本步未编译**（缺 2021.1 + Burst / Collections / Mathematics / Jobs / URP12） |
| `Infinity.Rendering.Foliage.Editor` | **本步未编译**（同上，且依赖 Runtime） |
| `Infinity.Rendering.Foliage.Shader` | 未在本步重编；先前仅用 Unity 6 `UnityEngine.dll` 编过 `DummyFoliageShaders.cs`，**不是** 2021.1/URP12 正式编译 |

不得把 Runtime / Editor 标成已通过 Unity 编译。PlayMode 必须在装了 Unity 2021.1 + URP12 的机器上继续找本 Agent 推进。

## 宿主机必做（Windows + Unity 2021.1 + URP 12）

1. 旧 Scene 与 MeshAsset **整表 rebake**（`BuildTerrainGrass` / `UpdateTerrainGrass`，`BuildTerrainTree` / `UpdateTerrainTree`）。无兼容层。
2. PlayMode：进 Play 即 Schedule，Enable 不立刻建大 `ComputeBuffer`。build 完成前无草。出生在对角应先出脚下（visible/近处优先，不是格序 0 起）。出锥回来 upload 应已推进。同一显示帧多相机不得双倍 upload。Draw 只画已 upload 且可见的格。Frame Debugger 看 run 数（满视野约一行一次）。共享 `CompleteAll` 不得卡住草 scatter。
3. PlayMode：树 fade（邻级 dither、跨级硬切）；只挂 `TreeComponent` 的场景能画。
4. 多 Terrain 接缝：草缝本轮不修；邻块卸载掉边树是流式，不是 bug。
5. Frame Debugger：换成 Indirect **不得单独**让 DC 下降。树 DC = 种 × LOD × bucket × submesh。
6. 运行时内置草 / 树距离被置 0，停用组件后应恢复。
7. 宿主若仍 `using InfinityTech.Core.Geometry` 会一起炸，需改成 `Landscape.FoliagePipeline`。

## 本轮明确未做（下次在 Unity 机推进）

- Compute kernel（只许写该组件 `visibleMap` 或 `args.count`）
- Morton 换键（草树一起换）
- 阴影 / 多视口第二份 index（`FoliagePass` 仍 `DispatchDraw(..., passIndex=1)`，不进 ShadowCaster）
- 屏占比迟滞
- 多游戏相机各持完整 fade：目前只保证 **当前渲染相机** 正确
- 内部 BVH / 草 mesh LOD / 可变 section / 世界流式
- `package.json` 的 `com.infinity.render-foliage` 与文件夹 `com.infinity.foliage` 不一致，本轮不改
