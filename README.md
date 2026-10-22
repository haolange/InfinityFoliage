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

URP Renderer 的 `FoliageRenderer / Settings` 给全场景提供默认值：草 / 树绘制隔离、`treePreZ`（默认开）、树 backend（Auto / CPU / GPU）、Terrain Occlusion 与 GPU HZB、LOD dither fade、淡化模式、过渡带宽度与迟滞、树 / 草主光投影、草 / 树绘制距离倍率，以及驻留 Terrain 数 / 每草种细节页数 / 页可见保留帧。每次相机渲染先准备一次 Cull / Pack。开启有效主光阴影时提前到首个级联绘制前，否则在 PreZ 或颜色通道准备。`treePreZ` 开启时，PreZ 只画带 clip 的子网格和正在淡化的桶，颜色通道复用同一份结果；关闭时直接画颜色。HZB 使用上一帧按相机保存的深度，视点不连续的那一帧不用。backend 默认 Auto：核心 Compute 可用时选 GPU，不可用时选 CPU。两种树遮挡默认开启，淡化模式为时间窗，过渡带宽度 `0.2`，迟滞 `0.08`，距离倍率 `1`，预算 `2 / 8 / 12`。预算可在 Play 调整；细节页预算设为 `0` 时草保留基础层。`4×4` 页格局与基础层 `1/8` 属于 Bake 格式，不能在运行时修改。时间窗在档位变化后共享一段 `fadeDuration`。距离互补按当前屏占比在过渡带里逐棵混合，相机停下混合就停下。

`TreeComponent / Rendering Overrides` 可为该 Terrain 单独覆盖 backend、遮挡、距离、LOD fade / 淡化模式 / 过渡带宽度 / 迟滞和主光投影。`fadeDuration` 只作用于时间窗，仍由组件决定。`GrassComponent` 可单独覆盖绘制距离和主光投影，草密度仍读取 Terrain。距离未覆盖时使用进入 Play 前的 Terrain 距离乘 Renderer 倍率；退出 Play 会恢复 Terrain 原值。实时阴影的距离及级联继续在 URP Asset 设置，树的 `castTreeMainShadows` 默认开；草的 `castGrassMainShadows` 默认关。草组件勾选 `Override Main Shadows` 后，由 `Cast Main Shadows Override` 决定本 Terrain 是否投影；关闭 `drawGrass` 会同时关闭草颜色和投影。草阴影复用所属渲染相机本次可见页与 run，画外草不投影。

选中组件并启用 `showBounds` 可看有真实深度遮挡的线框。草格红 / 绿只代表 CPU 视锥与距离；草页黄 / 绿表示仅基础层 / 有细节页驻留。CPU 树的实时线框只代表 CPU 阶段；GPU 实时线框不表示最终可见性。要判断 HZB 是否剔除了树，在 `TreeComponent` 指定 `debugCamera`（空值使用 Main Camera），点击 **Capture Visibility Snapshot**；冻结结果显示 backend、相机、帧号与最终 index 数，并按阶段给格和实例上色。可填写 `debugTreeIndex` 与 `debugCandidateIndex` 查看单个候选，随后点击 **Clear Visibility Snapshot** 恢复实时线框。该读回只在 Editor 按需执行。

距离模式的快照还列出最终 fade index／权重配对数与无效权重数；指定候选位于过渡带时显示该候选在两档中的权重。没有配对时，该机位可能没有树进入过渡带，需移动相机后重新捕获。

### Tree backend 选择

在 Renderer Settings 选择 **Tree Backend**；组件启用 Override Backend 可只改变该 Terrain。Inspector 显示请求值、实际 backend 和回退原因。CPU 使用 Burst 完成格 / 实例可见性、LOD 分桶与最终 compact；GPU 使用 Compute 完成同一工作，经 64 位 mask、计数、分层前缀扫描和 compact 输出最终 index / args。CPU 以 Procedural 提交最终 count，GPU 以 Indirect 提交 args；两者共享 Visibility IR 和 VisibleIndex 契约。草继续 CPU 格剔除与页内连续 run。

**Terrain Occlusion** 在两条 backend 都可开启；**GPU HZB** 只对 GPU 生效。选择 CPU 时 HZB 控件禁用，但保存选择不会被清除。没有有效相机深度或当前投影不适合 HZB 时，GPU 继续基础可见性并显示原因，保守保留候选。强制 GPU 初始化失败时回退 CPU，仅状态变化时警告。视锥与距离剔除始终保留。

树的 CPU backend 直接用已知桶数量调用 `DrawMeshInstancedProcedural`；GPU backend 继续用 args buffer 调用 `DrawMeshInstancedIndirect`。两者保留同一套 VisibleIndex、LOD 淡化和独立级联视图，CPU 不分配或上传 indirect args。Instance Bounds 在 CPU 模式下按 Debug Camera（未设时为 Main Camera）显示对应视点的 LOD 色；GPU 实时仍为灰色，按需冻结快照后才显示 GPU LOD 色。

backend、淡化模式或 fade 开关改变后，下次视图更新重置该相机的目标状态，首帧硬切，避免播完旧状态或消费旧 args。Temporal 使用双视点、共享 alpha 与 fadeDuration；连续快速移动从已完成的 stable 档逐档追赶目标，每次完成相邻两档的淡化后再推进，传送／投影不连续仍硬切。GPU 在自己的状态缓冲中闭合过渡。Distance 仍使用单视点过渡带，fade index 旁存同序权重。主光 CSM 跟随所选 backend，树对颜色候选与各级联 caster 候选的并集计算一次相机 LOD / fade，然后各视图独立生成 index / count（GPU 为 args）。级联与颜色使用相同过渡档位、Temporal 进度或 Distance 权重；级联权重跟各自 index 同槽 compact，不使用颜色索引。画外树仍按所属相机的投影选择 LOD 并投影，caster 不受颜色 HZB 拒绝结果限制。实时阴影距离及级联仍由 URP 设置。

该改动不改变 Bake 资产或矩阵布局，无需为切换 backend rebake。GPU 正常帧不读回结果；冻结诊断才读回。跨平台及实际验收状态以 [交付记录](Docs/DELIVERY.md) 为准。

草和树叶的普通 / 实例主绘制、PreZ、阴影都先拒绝完全透明 texel，再执行材质 `Alpha Threshold` 裁剪；阈值为 0 时仍不会绘制透明背景，无 MSAA 时同样有效；有 MSAA 时主绘制与 PreZ 继续使用 AlphaToMask 平滑覆盖。阴影显式关闭 AlphaToMask，以 alpha clip 决定轮廓；草的 Nature Renderer procedural Forward / ShadowCaster 使用相同规则。
