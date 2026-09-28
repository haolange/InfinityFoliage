# Infinity Foliage

URP 下替换 Unity Terrain 内置草 / 树的绘制。包名 `com.infinity.render-foliage`（文件夹仍是 `com.infinity.foliage`）。

Agent 工作台见 [AGENTS.md](AGENTS.md)。交付与宿主验收见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

## 使用

1. 在 Terrain 上挂 `GrassComponent` 和 / 或 `TreeComponent`，在 URP Renderer 中启用 `FoliageRenderer`。
2. 退出 Play，选中 Terrain 所在对象，从 `GameObject / EntityAction / Landscape` 执行 `BuildTerrainGrass` 和 `BuildTerrainTree`；Build 会继续执行对应 Update。更改 Terrain 密度或树实例后可执行 `UpdateTerrainGrass` / `UpdateTerrainTree`，然后保存场景。
3. 草的 `numSection` 控制空间格（Build 菜单默认按 32 世界单位切）；树有自己的 `numSection`（默认 16），不跟草走。
4. 当前 Bake 将草写成每种草一份基础密度资产和 `4×4` 细节页，将树候选流写成独立资产。旧 Scene、MeshAsset 和旧密度布局没有兼容路径，必须重新 Build/Update；Play 前确认流式资产已生成。
5. Play 时默认最多驻留 2 个 Terrain；同一草 prefab 跨 Terrain 默认最多驻留 8 个细节页。基础层随 Terrain 驻留，细节页按可见性和距离加载。`GrassComponent.SetDensityScale` 或 Terrain 的 `detailObjectDensity` 可调整运行密度。组件会关掉 Terrain 内置草 / 树距离，停用后恢复。

## 渲染与诊断控制

URP Renderer 的 `FoliageRenderer / Settings` 给全场景提供默认值：草 / 树绘制隔离、树遮挡模式（关闭、仅 CPU 地形、仅 GPU HZB、两者）、LOD dither fade、淡化模式、过渡带宽度与迟滞、树主光投影、草 / 树绘制距离倍率，以及驻留 Terrain 数 / 每草种细节页数 / 页可见保留帧。默认值保持此前画面：两种树遮挡开启，淡化模式为时间窗，过渡带宽度 `0.2`，迟滞 `0.08`，距离倍率 `1`，预算 `2 / 8 / 12`。预算可在 Play 调整；细节页预算设为 `0` 时草保留基础层。`4×4` 页格局与基础层 `1/8` 属于 Bake 格式，不能在运行时修改。时间窗在档位变化后共享一段 `fadeDuration`。距离互补按当前屏占比在过渡带里逐棵混合，相机停下混合就停下。

`TreeComponent / Rendering Overrides` 可为该 Terrain 单独覆盖遮挡、距离、LOD fade / 淡化模式 / 过渡带宽度 / 迟滞和主光投影。`fadeDuration` 只作用于时间窗，仍由组件决定。`GrassComponent` 可单独覆盖绘制距离，草密度仍读取 Terrain。距离未覆盖时使用进入 Play 前的 Terrain 距离乘 Renderer 倍率；退出 Play 会恢复 Terrain 原值。实时阴影的距离及级联继续在 URP Asset 设置，本包主光开关只控制树的投影。

选中组件并启用 `showBounds` 可看有真实深度遮挡的线框。草格红 / 绿只代表 CPU 视锥与距离；草页黄 / 绿表示仅基础层 / 有细节页驻留。树的实时线框也只代表 CPU 阶段。要判断 HZB 是否剔除了树，在 `TreeComponent` 指定 `debugCamera`（空值使用 Main Camera），点击 **Capture Visibility Snapshot**；冻结结果显示相机、帧号、CPU 候选与最终 GPU index 数，并按阶段给格和实例上色。可填写 `debugTreeIndex` 与 `debugCandidateIndex` 查看单个候选，随后点击 **Clear Visibility Snapshot** 恢复实时 CPU 线框。该读回只在 Editor 按需执行。

距离模式的快照还列出最终 fade index／权重配对数与无效权重数；指定候选位于过渡带时显示该候选在两档中的权重。没有配对时，该机位可能没有树进入过渡带，需移动相机后重新捕获。

树的颜色绘制由可见格、地形 / HZB 遮挡和当前淡化模式生成 Visibility IR，再 lowering 到 `_TreeIndexBuffer`。时间窗使用双视点与屏占比迟滞；距离互补使用单视点过渡带，fade 桶另带一条与 index 对齐的权重。主光 CSM 使用独立级联索引，不读这份权重。代码路径的编译、Play 画面和 Metal GPU 验收状态以 [交付记录](Docs/DELIVERY.md) 为准。
