# Infinity Foliage 交付记录

## 2026-09-30 Tree CPU／GPU 双 Backend

基线 `29e3273`，工作树干净；Unity 6000.6.0f1／URP 17.6。仅扩展树颜色与主光 CSM，草保持 CPU 格剔除与连续 run；候选资产和矩阵格式不变，不 rebake。CPU compact 与 GPU 64 位 mask→计数→前缀→compact 为两条正式路径，退役旧混合搬运选择器。任务按阶段集中记录，正常帧不读回 GPU。

| 任务 | 依赖 | 代码 | 编译／断言 | Play／GPU | 结论 |
|---|---|---|---|---|---|
| B0 基线与控制契约 | 无 | Auto／CPU／GPU、组件覆盖、实际状态与独立遮挡控制 | Unity 6.6 Runtime／Editor 编译通过 | CPU／GPU／Auto、组件覆盖与 HZB 关开通过 | 本机功能通过 |
| B1 CPU 路径收敛 | B0 | Burst 剔除与 compact；CPU CSM 独立 compact；旧搬运链删除 | 正式断言随 Renderer Create 执行，无异常 | 3,604 可见；Distance 10 权重槽；Temporal 目标 5/303 起中末通过 | 通过 |
| B2 GPU 颜色 | B1 | GPU 格／实例、LOD、fade、64 位 mask／分层 prefix／compact | 当前 Metal 导入无目标 Shader 错误或警告 | 7,622 候选下 CPU／GPU 逐桶 index 相等；Distance index／weight 相等；HZB 3,533 为关闭时 3,604 的子集 | 核心与开关通过；已知遮挡者的单目标画面证据未关闭 |
| B3 GPU CSM | B2 | 每相机／级联独立最终 index／args | 托管编译通过 | 1／2／4 级联 CPU／GPU 完整索引相等；计数 5390、426/5271、20/79/495/5273；1／4 级联 Game 地面受影已观察 | 索引与当前画面通过；单独隔离镜头外 caster 未完成 |
| B4 切换与生命周期 | B2、B3 | 状态重置、失败回退、资源释放与相机清理 | 独立源码复核通过 | 两相机 3604/2292、10/8 权重槽相等；tree0 GPU 故障回退 CPU、361 index、仅一次提示；停用后释放、重入恢复 GPU | 本机功能通过 |
| B5 集中验收与交付 | B1–B4 | AGENTS／README 同步；临时入口与定位读回清除 | 最终源码终编与 Git 检查见发布记录 | 23 项诊断 PASS、0 FAIL；三路径各 120 帧；原设置已恢复 | 按用户最新要求直接提交；未测范围保留 |

本轮修复了 GPU compact 的 Metal 排名错误：高 32 位线程曾把整个位掩码的计数作为自身排名，覆盖同一槽位；原始 mask／count／offset 与候选 LOD 都正确。改为合法右移范围构造低位掩码后，CPU／GPU 最终 index 逐槽一致。临时原始 mask、rank 与 slot 读回已删除，正式冻结快照仍只按需执行。

Temporal 在现有 Tree 5／candidate 303 的 LOD 1→0 上实测：CPU alpha 起点 0.0040、中段 0.4560、终点 1；GPU 起点 0.0054、中段 0.4520、终点 1。实际目标从两个 fade 桶进入细档 stable；GPU 中段 Game 已观察。双相机、组件覆盖、关闭 fade、GPU 不可用回退和卸载重入均在同一 Editor 的收尾会话验证。自动验收结束恢复相机、组件、Renderer、原 URP 3 级联／128 米，销毁临时 Camera／RT，没有保存宿主资产。

性能为当前 Mac／Metal、原机位、1566×881、4 级联下单次顺序采样；每配置 120 个有效 FrameTiming 样本，读回关闭，不能直接与旧 1569×1874 基线比较，也不据此宣称 GPU 在所有设备更快。

| 配置 | CPU mean／P95 ms | GPU mean／P95 ms | 树 ComputeBuffer 容量 |
|---|---:|---:|---:|
| CPU | 16.677／17.709 | 27.056／47.327 | 3,001,680 bytes |
| GPU，HZB 关闭 | 16.668／17.356 | 16.486／24.569 | 3,001,680 bytes |
| GPU，HZB 开启 | 16.663／17.276 | 13.891／16.711 | 3,155,280 bytes |

DrawCalls recorder 全为 0，属于不可用数据，不能报告为零 Draw。容量是已分配 ComputeBuffer 的 count×stride，不是 VRAM 实测。最后 Frame Debugger 取得 263 个事件与最终树／地面画面，但未完成单事件资源和 atlas 隔离对照；已关闭 Frame Debugger。

发布边界：用户在上述功能与性能结果后明确要求「直接提交，不要查看 Unity」。据此停止继续 UI 验收并发布当前实现，保留 HZB 已知无遮挡／真实遮挡单目标、镜头外 caster 隔离及完整 Frame Debugger 资源对照的证据限制；提交不把这些项写成通过。旧 C4 转镜头突隐仍独立待办。草、VT、SH 不属于本轮新验收结论。

跨平台：Windows D3D11／D3D12、可选 Vulkan 未实测。对应平台用已有 Bake 场景对照 CPU／GPU、HZB 关闭的最终 index／LOD／Distance 权重；验证 GPU Temporal 起中末、切模式与开关重置、双相机、GPU 不可用回退、1／2／4 级联独立阴影索引及实际受影。重点检查 shader 编译变体资源、compute scan／compact 顺序与容量；Mac 证据不能替代该平台运行。本轮资产布局不变，无需 rebake。

发布记录：删除临时验收入口及其 `.meta`、全部临时 Compact 读回代码和本轮 18 个 AppleDouble 后，最终 Runtime／Editor Unity 6000.6.0f1 Managed 编译 **0 warning／0 error**，`git diff --check` 通过。宿主生成的 csproj 尚保留已删除入口条目，终编仅用 `/private/tmp` 的一次性 Remove target 排除该条目，未修改宿主项目文件；编译输出与 target 已精确删除。本节与正式实现按用户最新授权提交至 `main`。

## 2026-09-29 树 LOD 双模式回归收尾

基线：`main` 的 `ca4778cd`，本工程 Unity `6000.6.0f1`；进入本轮时工作树无未提交改动。现有 `Scene_PBR` 的 TreeComponent 有资产键和 15 个树种槽位。当前 Editor 日志第 1361、1364、1493、1494 行记录 Metal 因 `TreeLeave` / `TreeBrak` 缺 ComputeBuffer 而跳过 Draw；本轮须以修后新日志和 Game 画面重新判定。旧 C4 转镜头突隐仍是独立待办。

| 任务 | 依赖 | 代码 | 编译／断言 | Play／GPU | 结论 |
|---|---|---|---|---|---|
| R0 基线与复现 | 无 | 已核对提交、场景资产键与 Shader 缓冲绑定链 | 本工程 6000.6.0f1 | 修前 Play：资源载入后树消失，Metal 缺缓冲警告与代码对应 | 通过 |
| R1 默认树绘制恢复 | R0 | 实例 Forward 仅 Distance fade 变体声明权重；命令缓冲按桶开启并关闭关键词 | Unity 6.6 托管编译 0 警告／错误，Editor Tundra 和 ShaderImporter 成功 | 修后原相机 Temporal 与 fade 关闭树叶、树干可见；新日志无目标缺缓冲警告 | 通过 |
| R2 双模式运行安全 | R1 | 距离 emit Job 按依赖串联；模式状态由每相机持有；快照按最终 index 槽读权重 | 托管编译与 Editor Tundra 成功；真实 Job 安全断言修后导入无异常 | 单相机 Play 中 Temporal→Distance→关闭 fade 树均可见；Distance 的 HZB 关／开各读到 2 条有效配对、0 条无效权重 | 本轮直接回归通过；两相机和卸载重入未实测 |
| R3 画面与 GPU | R2 | 原 LOD 公式、三桶、独立阴影索引保留；仅距离 fade 桶要求权重 Shader 变体 | TreeLeave／TreeBrak 实例变体已在当前 Metal Editor 导入 | 默认 Temporal、fade 关闭、Distance 的 Game 画面均有树；宽度临时设 0.8 后 Tree 7 同机位 HZB off（frame 60833）与 on（frame 69045）均为 2 条配对、0 条无效权重；后者 15 树种均有 GPU 结果 | 无树回归与权重绑定通过；慢推拉连续像素、双相机、阴影单档与成本未取得本轮新证据，不能宣称完整 R3 通过 |
| R4 交付与平台 | R3 | AGENTS／README 已补资源与状态不变式 | Unity 6000.6.0f1 工程 Editor csproj 托管终编 0 warning、0 error；`git diff --check` 通过 | Mac 仅确认本轮目标画面和读回；外平台见下 | 待完整 R3；不以本轮局部通过冒充全量交付 |

首轮刷新后，新增断言在给 `lodDither` 赋值的循环中交替调度 Job，Unity 安全检查正确拒绝对正在读取的数组继续写入；把全部 dither 数据在调度前初始化后，第二次 Tundra 成功。首次 Play 仍使用旧 Shader，曾复现缺缓冲；执行 `Assets / Refresh` 后再次 Play，树叶和树干恢复，目标警告未在新日志重现。Unity 的 Component 菜单占用截图通道，取消菜单后截图恢复。最终 Play 已退出：组件覆盖回到 Temporal、fadeWidth 0.2、Terrain 遮挡，未保存临时设置。Console 中 `attempt to write a readonly database` 于退出 Play 后出现 29 次，是宿主既有错误，单列而不计入本包通过。

跨平台交接：Windows D3D11／D3D12 尚未运行；若项目启用 Vulkan，也尚未运行。在对应平台用同一已 Bake 场景检查默认 Temporal 树叶与树干 Draw、关闭 fade、Distance 带内两桶的 index／weight 同序与有效范围、HZB 开关、Metal 等价的 Compute／Shader 资源告警，以及切模式后的画面。Mac 的托管编译和 Metal Play 不能替代这些平台的实测。用户要求加速并跳过不相关测试，因此本轮未扩展旧 C4、VT、草、性能基准；旧 C4「转镜头突隐」仍独立待办，本轮没有把它判为已修复。

发布边界：按用户 2026-09-29 的明确要求，将已经通过 Mac 直接回归的修复提交到 `main`，同时保留上表中 R3／R4 未关闭的验收项。提交与推送只交付当前修复，不代表距离淡化全动作、跨平台或旧 C4 已验收。包内本轮 AppleDouble、临时脚本与 `/private/tmp/foliage-lod-repair` 编译输出已精确清理；没有删除 Unity 正在使用的 Library／ShaderCache 或宿主 Scene 资产。

## 2026-09-28 树 LOD 双模式

颜色视口增加两种正式淡化。默认 `Temporal`，保持原来的双视点、共享 `alpha` 和 `fadeDuration`。`Distance` 按当前屏占比过渡带逐实例混合。`treeLodFade == false` 时两种都只画 stable。阴影仍硬切，不读主视口权重。

| 项 | 结果 |
|---|---|
| 距离互补第 8 节与 `k = 2` 的 `rsqrt`、compact 对齐 | 用 Hub `6000.5.8f1` 的 Mono 跑 `AssertDistanceFade`，打印 `AssertDistanceFade passed`，退出码 0。本机没有 `6000.6.0f1` 编辑器，源码树的 Debug Editor 没有 URP / Mathematics 程序集 |
| Runtime 编译 | 同一套 Hub 管理 DLL，`csc /unsafe`，退出码 0。唯一警告是未改动的 `WindComponent.m_selectedPreset`（CS0169） |
| Shader / Play / GPU | **未验收**。没有启动 Unity Hub 或 Editor。`TreeLeave` / `TreeBrak` 的程序化 Forward 和 `TreeVisibility.compute` 需要已打开的 Editor 重新导入 |

画面留到已打开的 Editor，按这些动作看，不要只看截图：

- 停：过渡带中间的树，颗粒不动，两档互补，没有洞。
- 慢推：过硬切换距离之前，粗档像素从少变多，细档变少。没有先整棵换成粗模再溶解。
- 慢拉：同一条带反向，细档像素变多。
- 来回：覆盖率跟着距离走，不重开一段固定时长。
- 两棵距离不同：更近的细档像素更多。
- 停在带外：只有 stable，不 dither。
- 最后一档：不和更粗的档重叠，到绘制距离才按原规则消失。
- 阴影：主视口正在过渡时，阴影只有一档。
- 传送：第一帧就是新距离的 stable 或带内 `x`，没有 0.5 秒尾巴。
- 未开 dither：仍然一档硬切。
- 切回时间窗：共享 0.5 秒仍在，差两档仍硬切。

## 2026-09-28 渲染控制与遮挡诊断任务板

本轮沿用现有 `main` 与原 `Scene_PBR`，不新建 Terrain、分支或 Editor。旧 T0–T7 结论是上一交付版本的证据；本轮的设置切换和突隐原因另行验收。以下为本轮实际结果，不能把可切换等同于误剔修复。

| 任务 | 依赖 | 代码验收 | 编译验收 | Play / GPU 验收 | 结论 |
|---|---|---|---|---|---|
| C1 设置权威与预算 | 无 | Renderer Feature 全局默认、组件局部覆盖；Terrain 密度和 URP 阴影仍为原真源；运行预算可调整 | Runtime / Editor Managed 编译 0 warning、0 error | 原场景 Play：预算 0 时 0 细节页，预算 9 时全草种合计 28 页；默认 2/8/12 Inspector 已核 | 代码和预算切换通过；2 Terrain 上限仍只具逻辑证据，未建新 Terrain |
| C2 树可见性开关 | C1 | 地形与 HZB 四模式独立，关闭 HZB 清旧状态；fade、迟滞、距离和主光开关传递 | 同上；本次 Editor Tundra 成功，未见目标 Metal warning | 同一候选 187、两机位四模式：机位 0 均 24/24；机位 1 两种 HZB 模式 39→33、两种非 HZB 模式 39→39；运行中切换正常 | 四模式通过；新 fade / 主光开关的独立画面 A/B 尚未完成，沿用旧 T4/T5 默认效果证据 |
| C3 Bound 与冻结快照 | C2 | 组件 / 格 / 实例或页三层线框；树颜色注明 CPU 与最终 GPU 阶段；指定相机一次异步读回 GPU 格、VisibleIndex、args | 同上 | 原 Play 对 `PlayerCamera` 快照：Tree 0 在 frame 3627 CPU 候选 228、最终 GPU index 217、HZB on；15 个树种均返回；旧 T6 的真实深度遮挡证据有效 | 冻结快照实测通过；本轮未新增 Frame Debugger 同帧截图，不能以线框单独判 GPU 可见 |
| C4 突隐根因 | C3 | 快照可列 GPU 剔除的 CPU 候选及世界盒位置 | 同上 | 山脊同机位 Y=160° 在 Both 与 Terrain-only 下树线无明显变化；另机位 HZB 剔除 6 个候选，但尚未证明误剔或真遮挡 | **未关闭**：三张图的具体树木未锁定候选 ID，同帧深度对照不足，不能宣称根因已修 |
| C5 总门与交付 | C1–C4 | AGENTS / README 已同步；临时验收入口和其 `.meta` 已删，包内 AppleDouble 已清 | 临时入口移除后 Unity Tundra 再次成功（日志 9738 行，1.93 秒）；Unity 6.6 Managed DLL 编译 0 warning、0 error；`git diff --check` 通过 | 宿主 Scene 与 Renderer 资产 SHA-256 与测试前一致；现有 Editor 后段无法取得截图，最终 UI 恢复状态未确认 | **验收未关闭**；按用户最新要求将已核验代码与本待办一同提交 `main`，不把提交当作 C4 / C5 通过 |

集中验收入口仅在 Play 内改变相机与 Feature 值并按结束逻辑恢复；运行中切换后，Unity UI 截图服务失效，未能确认最后一次山脊 hold 的恢复命令及 Play 退出。用户确认桌面已解锁后，UI 截图仍返回 `Screenshot unavailable`，系统 `screencapture` 也返回 `could not create image from display`。磁盘上 `Scene_PBR.unity` 和 `UniversalRenderer.asset` 的 SHA-256 与测试前相同，故没有保存临时相机或 Feature 值；再次操作 Editor 时应先退出 Play 或重新载入磁盘场景。临时入口已删，宿主 `.csproj` 最初尚保留旧编译条目，离线终编曾用一次性 `/private/tmp` MSBuild Remove target 排除；随后 Editor 自动刷新项目文件并完成 Tundra 成功导入，旧条目已消失，临时 target 和编译输出均已删。最终 UI 状态限制不记为验收通过。

## 2026-09-27 当前验收结论

此节是当前任务板；下方按日期保留的阶段记录只解释发现与修复过程，其中「待验」和「FD 阻塞」均为当时状态。唯一现有 `Scene_PBR` Editor 已完成集中 Play，Frame Debugger 已关闭。没有建立分支、新增 Terrain、启动第二个 Editor、batchmode 或 Player build。

| 任务 | 代码 | 编译 / 逻辑 | Play / GPU | 当前结论 |
|---|---|---|---|---|
| T0 七文档收敛与基线 | 同义项去重；被后续方案替代和明确排除项不计欠账 | 下方矩阵列出归属 | 原相机、1569×1874、300 帧基线：CPU Total P95 17.884 ms，GPU P95 44.522 ms | 通过 |
| T1 Bake 与迁移 | 草基础层及 `4×4` 细节页、独立树候选流；整批校验、变化文件暂存、失败回滚后才提交索引；重复 Mesh 仍保留独立 LOD 槽 | 故障注入与重复 Mesh 定向断言通过；最终 Runtime / Editor Unity 6.6 Managed 编译各 0 diagnostics | 原 `Scene_PBR`、`Scene_NPR` 已 rebake 并 Play；原 `Landscape` 的 `UpdateTerrainGrass` 无变化重跑后 153 个 `.bytes` 数量、大小、纳秒修改时间未变 | 通过；场景和生成资产在宿主工程，不在本包 Git |
| T2 草流式与驻留 | 每草 prefab 全局 8 细节页、2 Terrain 预算、确定性基础层、页代次与释放重入、运行时密度重建 | 预算 / 密度 / 释放逻辑断言通过；最终编译通过 | 现有 Terrain 多相机压力产生每草种 10 页需求，租约始终 ≤8，远页释放后可重入，快速重入 0；组件关开资源 `1/28/35/15/15 → 0/0/0/0/0 → 1/28/35/15/15`；密度 1→0→0.5→1 的 Draw 与实例数响应 | 当前允许的实景与预算门通过；三块独立 Terrain 实景未做，见限制 |
| T3 树可见性与 Metal | 可见格先于实例剔除；当前 RenderGraph depth 的 UV 原点驱动保守 HZB；三 lowering 写最终 VisibleIndex，CommandBuffer 顺序上传 | 当前 Editor 导入通过，最终源码编译通过；本次日志无 `HzbOccluded` 等目标 Metal 警告 | 三 lowering / 双 submesh args 各 70/70；相机转 120° 无成片误剔；候选 187 在真实遮挡前后 CPU 为 `True/True/True`、Metal index 为 `True/False/True` | 通过；多 Terrain 地形脊等未在现有场景单独构型 |
| T4 树 LOD fade | 每相机 hold / now、屏占比迟滞、邻档互补 dither、跨档及传送硬切；正交投影有独立口径 | 透视 / 正交、迟滞 / fade 断言通过；最终编译通过 | 同一候选 187 的 120 帧 Alpha 单调 `0→0.451→0.992`；中间帧 CPU 两桶 `1/1`，Metal args 两桶 `1/1`；[起点](Evidence/t4-fade-start.png)、[中段](Evidence/t4-fade-middle.png)、[终点](Evidence/t4-fade-end.png) 同机位画面未见整棵突现 | 当前画面门通过；截图保存异步，不能把每张实际帧的 Alpha 当成请求值 |
| T5 主光 CSM | 每相机每级联独立 index / args；fallback 角点改用正向世界视锥；实例与原生树偏置、软阴影按 URP 规则 | 最终源码与 Editor 导入通过 | 修后 3/3 球心在相机前，目标树入有效球；1/2/3/4 级联 atlas 均有实例投影，1/2/4 级联分别做最终地面强度 1→0→1 A/B；固定 TerrainCollider 地表点距相机 112/121/129 m，129 m 不再观察到该点实时影。Frame Debugger 地形渐隐常量 `z=0.0002959, w=-3.847756` 对应 128 m 截止 | 当前场景主光接影与 128 m 边界通过；原生 caster 并存、镜头外 caster 仅有代码链审查，未单独隔离 GPU 样本 |
| T6 VT / SH / Bound | VT 页有效性与普通地形回退、反馈数据自持；草树实例场景 `ambientProbe` 和逆转置法线；Bound 保留真实深度。相邻 VT shader 主光级联与软阴影变体已拆开 | 最终草树源码编译、VT ShaderImporter 成功；本次日志无反馈数组失效；修后 Terrain Forward 同时出现 `_MAIN_LIGHT_SHADOWS_CASCADE` / `_SHADOWS_SOFT` | VT 页表 alpha `0→255`、未就绪 / 越界 / 停用时地表有色；天空 Color / Skybox A/B 与同材质 Forward 间接光量级对照；转动 Play 相机后的 Bound depth / Gizmo 开关对照 | 三项独立验收通过；未做逐像素 VT 或 SH 数值读回。宿主 readonly database 报错另列，不归入植被通过项 |
| T7 性能与工程交付 | 架构与 README 对齐、仅保留正式运行路径；临时验收入口、编译产物和工作树 AppleDouble 已删 | 最终 Runtime / Editor Unity 6.6 Managed 编译各 0 error / 0 warning，Editor 删除临时入口后再次 Tundra 成功；`git diff --check` 与旧类型 / 全图草路径扫描通过 | VT 最后修复后、Frame Debugger 关闭，60 帧预热 + 300 连续有效帧：CPU Total P95 **17.767 ≤ 19.672 ms**，GPU P95 **11.980 ≤ 48.975 ms**；Draw 均值 202.5、RSS 2602.0→2601.9 MiB、图形驱动分配估计 856.8 MiB、驻留 1 Terrain / 28 页、页命中 / 驱逐 10080 / 0 | 当前必需工程门通过；本包由包含本记录的 `main` 提交交付，相邻 VT 修复见 `c81df93` |

### 当前 Todo 与验收边界

- [x] T0–T6：以上各门按代码、编译、适用 Play / GPU 分别核对；子门未被直接冒充父门。
- [x] T7 性能：最终 VT shader 变体生效后重采 300 帧；真实 Metal VRAM 无可靠计数，856.8 MiB 只是图形驱动分配估计。
- [x] T7 交付：临时入口、编译输出及两个包工作树 AppleDouble 均精确清理；相邻 VT 的 `c81df93` 已非强制推送至 `main`，本包以包含本记录的 `main` 提交交付。

场景中只有原有 `Landscape`。用户禁止新建 Terrain，故 2 Terrain 上限由预算逻辑验证，10 页压力由同 Terrain 多相机产生；三块**独立烘焙** Terrain 的实景画面不能写成通过。阴影的原生 caster 并存及镜头外 caster 代码链独立于颜色 VisibleIndex，但本场景未单独隔离它们的 GPU 结果。T4 画面序列来自同一目标树，截图异步，仅证明可见的渐变过程。`Scene_PBR.unity` 和宿主 `Assets/Generated/Foliage/Resources` 的 153 个 `.bytes` 不随本包 Git 推送；旧 Scene / MeshAsset 必须在目标工程 rebake。测试后重新载入磁盘上的 `Scene_PBR`，Editor 不再有未保存标记；场景和 URP 资产 SHA-256 与测试前相同。当前 Editor 退出 Play 后仍有 `attempt to write a readonly database`，发生于宿主 ShaderCache / AssetDB 过程，未见本包目标 NativeArray / Metal 警告；它不计作 Console 零错误。

### 历史阶段记录

以下 2026-09-26 与 2026-09-27 的中间态供复核根因，不再表示当前 Todo 或最终验收状态。

## 2026-09-26：七文档收敛

宿主 Unity 6000.6.0f1、URP 17.6、Metal。本包基线 `ce7b26d`，相邻 VT 包基线 `502515f`。只复用原有 `Scene_PBR` Editor，不建分支、不做 Player build。代码存在、编译通过和画面通过分别判断；子项通过不代表父项通过。用户已要求不再创建 Terrain，后续只使用原有的一个 `Landscape`。

### 任务板

| 任务 | 代码证据 | 编译 / 逻辑 | Play / GPU 证据 | 结论 |
|---|---|---|---|---|
| T0 七文档去重与基线 | 下表归并重复项，排除被替代路径 | 不适用 | 同相机 300 帧基线见下 | 完成 |
| T1 Bake 与资产迁移 | `FoliageAssetCodec`、`FoliageAssetWriter`：草基础层 + 4×4 页，树候选资产，删除旧 Scene 内嵌密度路径；草格计数先暂存，资产提交成功后才写回 Scene；树每 LOD 保留一个 mesh 槽，即使多个 LOD 引用同一 Mesh | 最新 Runtime / Editor 源码借本机 Unity 6.6 Managed 引用独立编译均 exit 0、0 diagnostics；重复 Mesh 的新 Bake 路径待 Editor 导入 | `Scene_PBR` 重新 Bake 并保存；1 Terrain、7 草种、15 树种、135 个 `.bytes`，Scene 文本无 `densityMap:`。`Scene_NPR` 在原 Terrain 执行 `BuildTerrainGrass` 并保存：旧 `densityMap` 0、新 `assetKey` 1、18 个 `.bytes`；Play 草可见，Console 两个错误均为宿主旧有 readonly database。`Scene_VT` 无草树组件 | 现有三场景资产迁移通过；**新异常安全路径和重复 Mesh Bake 尚待验收**；宿主 Scene 和生成资产不在本包 Git |
| T2 草页与驻留 | `FoliageResidency`、`GrassSector.GrassPage`、`GrassComponent.SetDensityScale`：2 Terrain / 每草 prefab 8 页预算、代次隔离、Burst scatter、卸载释放；修正跨 Terrain 草种键、先驱逐后加载、同帧视图 / 投影缓存及首次 Play 密度初始化；注册失败回滚原生草与 NativeArray，页构建失败释放部分资源；视锥边缘已驻留页保留 12 帧优先级；同 Terrain 树种共用一次 64×64 地形遮挡采样 | 最新 Runtime 源码借本机 Unity 6.6 Managed 引用独立编译 exit 0、0 diagnostics；此前 Editor 导入成功，最新高度采样与异常回滚 / 页迟滞尚待 Editor 导入 | 先前密度 1→0→0.5→1 时 Draw 173→116→171→173、实例 5754→93→2927→5754；原 Terrain 相机 X=80→800→406.5 时细节页 28→0→28，返回画面恢复；X=800 只证明细节页释放，单 Terrain 场景不能证明整地块卸载 | 草密度及单 Terrain 页路径有旧证据；最新回滚、页抖动、整地块释放与重入、三独立地块预算仍未验，父项不通过 |
| T3 树可见性 / Metal | `CommandBuffer.SetBufferData` 保证暂存与 dispatch 顺序；HZB 用 RenderGraph 相机 depth 的实际 `TextureUVOrigin` 构造投影，移除固定 Metal Y 翻转；保留格盒、保守 HZB、三 lowering 与多 submesh | 最新 HZB 路径 Tundra 编译及 Shader 导入成功，未见目标 Metal warning；最终 CSM 改动另见 T5 | 三 lowering、两 submesh 的 Metal args 各 70/70；新映射在原相机恢复密林、旋转 120° 无大片误剔。旧 10 树逐帧与真遮挡 A/B 基于被新映射替换的路径，不能直接继承为最终验证 | 新映射的真实遮挡、逐实例 GPU index、地形脊和多相机仍须定点复验；父项未过 |
| T4 双视点 LOD fade | Bake 读取 prefab `LODGroup` 阈值，实例 bound 合并所有 LOD mesh；透视 / 正交相机的屏半径各按对应投影计算，颜色与阴影 LOD 共用函数 | Unity 6.6 Editor 刷新 Tundra build success；透视 / 正交断言随 Renderer Feature 创建执行，独立 Managed DLL Runtime / Editor 编译各 0 warning / 0 error | 原场景近移路径 CPU 双桶 59 帧；在 Alpha=0.254 的同一 Game 相机帧，树种 9 的 fade-out / fade-in CPU 桶各 1，GPU Indirect args 的实例数也各 1。Frame Debugger 对 Indirect Draw 的零顶点显示不能用于实例计数 | **CPU 和 Metal GPU 双桶绘制通过**；正交相机画面、连续帧视觉平顺仍未验，父项不通过 |
| T5 主光 CSM | 每相机 / 级联独立 index / args；无原生 caster 时构建 atlas；软阴影 keyword / 质量与 URP 对齐。已将 fallback 级联角点改用 `ViewportToWorldPoint`，修复旧相机坐标基混用 | 最终角点代码仅本机 Unity 6.6 Managed DLL 离线编译 0 diagnostics；Editor 因 FD 耗尽中断导入 | 修前 1/2/3/4 级联 atlas 均有写入，但自定义树 `shadowAttenuation` 近 1、`ComputeCascadeIndex` 为 4，级联球在相机背后；修后尚无 Play/GPU 数据 | 级联方向修后受影、原生 caster 并存、128 米边界均未验；父项未过 |
| T6 VT / SH / Bound | VT 回退与反馈寿命、Grass/Tree `ambientProbe` 七向量及逆转置法线、Bound 真实深度路径分别处理 | VT 与共用 SH 源码曾成功导入；最终 TreeLeave/TreeBrak 原生法线改动尚待 Editor 重新导入 | Bound 转镜头同帧 depth/Gizmo 与开关对照通过；VT 未就绪及越界回退通过；天空 A/B 和同材质仅间接光 A/B 均为相近量级 | 缺页→驻留页交接和最终 Shader 导入未验；T6 总门未过 |
| T7 总门 | `AGENTS.md`、`README.md`、本记录按真实状态收敛；测试脚本与编译产物精确清理 | 当前 `git diff --check` 通过；最终 Runtime 独立编译 0 diagnostics；Editor 最新导入因 FD 耗尽中断 | 长会话 CPU Total 21.060ms 超 19.672ms 门；最终清洁 300 帧 P95 未取，真实 VRAM 不可得 | 未通过；全部必需门过前不提交、不推送 main |

### 2026-09-27 剩余验收 Todo

只使用现有场景与当前单个 Editor；冻结没有失败证据支持的新结构改写。因旧会话的 FD 与 TreeCullLodJob 越界，上一阶段已按授权关闭 PID 96877，并仅重开同项目 Editor PID 28832。本阶段在该进程的描述符增至约 1184 后，使用最新用户指令允许的单次重开：先退出 PID 28832，再从 Hub 打开同一项目 PID 84242，未并行启动第二个 Editor。新进程初始约 532 个描述符，HZB 修复编译和定点 Play 后约 578；Tundra 导入成功，宿主仍报 `attempt to write a readonly database`，故不将 Console 写成 0 错误。**本阶段允许的重开已使用；新进程已再次因 FD 耗尽拒绝 Play。额外一次重开仅在用户新的明确授权后进行。**以下状态与阶段表共同追踪，不以子项通过代替父项通过。

依赖重排：先关闭 G0 并导入 T5 角点修复，立即在原相机检查级联球是否落在前方、实例是否实际受影；再在同一 Play 会话复核 T3 新 HZB 映射与 T4 同树连续 fade。T1 迁移和 T2 单 Terrain 页预算 / 释放重入沿用已有可靠证据，只补失败路径与第 8/9 页抖动；T6 只补 VT 缺页交接和最终 Shader 导入；最后 T5 补 1/2/4 级联、128 米边界，T7 在清洁会话取一次可比的性能样本。三独立 Terrain 的实景门与“不新建 Terrain”冲突：仅作资产和逻辑层验证，并保留实景证据限制。宿主 FD 无法解除前，所有依赖 Play / GPU 的父门均保持未通过。

| 优先级 / Todo | 依赖 | 本轮最小判定与通过条件 | 当前状态 |
|---|---|---|---|
| P0 · G0 单 Editor 工程门 | 无 | 确认仅一个同项目 Editor；本机 Unity 6.6 Runtime / Editor 编译 0 diagnostics，Console 无阻止 Play 的错误，Metal 不再报目标未初始化 warning。若 FD 再耗尽，先定位宿主资源状态，不盲目重编。 | **再次失败**：本阶段单次重开后约 532→1642 FD，其中 `ShaderCache.db` 约 1282；Tundra `Could not register to wait for file descriptor 1488` 中断，Play 拒绝。Unity 旧 issue tracker 有 exFAT shader DB 同类现象，当前库已由 sqlite3 `integrity_check=ok`、文件可读且路径可写，无法仅凭此断定当前 fd 泄漏的 Unity 内部根因。新 CSM 角点代码只能离线编译，Editor/GPU 门受阻 |
| P1 · T1 Bake / 迁移 | G0 | 复核已生成索引和现有 Scene 的资产键；原 `Scene_PBR`、`Scene_NPR` 进入 Play 后无缺页 / 旧密度读取；重复 Mesh LOD 只在现有 prefab 确有该布局时核对一一对应，不制造测试 Terrain。 | PBR 与 NPR 原场景均 Play 通过；NPR GrassComponent 开关在同机位改变提交与实例数，证明新路径实际运行；重复 Mesh 负例无现成实景 |
| P1 · T2 草页与释放重入 | T1 | 同一原 Terrain 上运行密度 1→0→0.5→1，实际 Draw / 容量响应；每草 prefab 同时租约≤8，快速转向不连续驱逐第 8/9 页；禁用再启用现有草树组件，页、NativeArray、ComputeBuffer 与原生草树距离释放 / 恢复，重入画面恢复。2 Terrain 上限由现有资产与逻辑断言判定；不创建第三 Terrain，三独立 Terrain 实景证据单列限制。 | 密度及单 Terrain 页往返旧证据保留；本轮停用→重入资源 28/35/15→0/0/0→28/35/15 直接读数通过；预算逻辑断言通过；2 Terrain 实景与长转向抖动尚有限制 |
| P1 · T3 Metal 可见性 | G0 | 沿用三 lowering / 双 submesh Metal 结果；当前同机位密林和 120° 转镜头无 HZB 误剔，且真实前景后仍能剔除。核对旧 10 树及地形脊、多相机的可用实景；无深度走保守保留。目标 Metal warning 为 0。 | 新 A/B 发现先前 10 树样本未覆盖大面积误剔：关闭 HZB 后密林恢复；去掉硬编码 Y 翻转亦恢复。已改为从 RenderGraph 深度纹理 UV 原点构造同一投影矩阵，Tundra 编译和同机位 Play 密林通过，120° 转向画面正常。真遮挡 / 旧 10 树须按新映射定点复验，父项仍开 |
| P2 · T4 LOD fade | T3 | 沿用同帧 CPU / GPU 双桶 1/1；原相机跨相邻 LOD 的连续画面平顺、无整棵突现；跨档 / 传送硬切，正交相机屏占比路径按现有断言及最小画面对照通过。只采必要关键帧。 | 非暂停 Play 已把原资产中同一棵树的 `hold=1 / now=0 / alpha=0.916 / fading=True / hardCut=False` 与目标画面对应；先前同帧 CPU / GPU 双桶各 1；正交相机移动 X392→406 后该树保持 LOD1。尚无能证明同一树冠像素连续的序列，父门仍开 |
| P0 · T5 主光 CSM | G0；完整门再依赖 T3、T4 | 先在原相机核对修后级联球心位于相机前方、树叶 `ComputeCascadeIndex` 在有效级联、实例 `shadowAttenuation` 和地面受影；再沿用已有 atlas 写入证据，只补 1 / 2 / 4 级联的最终受影、原生 caster 并存及 114–128 米渐隐 / 128–512 米无实时影。测试值恢复 3 级联。 | 修前同材质只输出 `mainLight.shadowAttenuation`：实例树叶几乎全白、原生有大量深灰 / 黑；`ComputeCascadeIndex/4` 显示实例落到级联外索引 4。旧 `CalculateFrustumCorners` 角向量与 `cameraToWorldMatrix` 的前向 Z 坐标基混用，使球心落在相机背后；现改用 `ViewportToWorldPoint` 世界角点。**最终修复仅独立 Unity 6.6 Managed 编译 exit 0；Editor 编译因 FD 中断，尚无修后 Play/GPU 证据。** 父门未过 |
| P2 · T6 三项独立画面 | G0 | VT 的 Edit / 刚进 Play / 缺页 / 已加载 / 越界 / 退出无地形黑面和反馈寿命异常；SH 天空 A/B 沿用旧图，同机位同材质 Forward 与实例的仅间接光量级须一致；Bound 沿用转镜头开关图与同机位 depth / Gizmo 归因，不把 VT 色面当作 Bound 证据。 | Bound 同帧 depth/Gizmo 与转镜头画面通过；VT 未就绪和缩小覆盖范围的真实边界画面通过，缺页到页到达的交接仍待验。TreeLeave 临时只输出间接光的同机位 A/B：实例与原生都呈暗色、处于相近量级，排除固定 SH 导致的一侧独有大亮度差；诊断 Shader 已撤销。七向量数值仍未直接读回；天空变化响应旧证据有效，T6 总门因 VT 未闭 |
| P3 · T7 性能与交付 | T1–T6 必需门 | 同场景 / 相机 / 分辨率的清洁会话 300 帧 P95 CPU Total≤19.672ms、GPU≤48.975ms，记录 Draw / RAM / 图形分配与页命中驱逐；缺真实 VRAM 读数则明确限制。集中更新 AGENTS / README / 本表，精确删除探针、编译产物与 AppleDouble，核对双包 Git；只有必需门有对应证据后才写提交说明并推送本包 main。 | 待前置项；长会话旧采样超 CPU 门，不沿用为清洁性能结果。本轮依赖顺序为 T5 受影根因→T4 连续画面及 T2/3/T6 未闭子项→清洁 T7，不复测已通过的级联写入 / lowering / Bound |

### 2026-09-27 集中功能 Play 阶段门（覆盖上方旧阻塞状态）

本阶段只运行 PID 41044 的原项目 Editor，`Scene_PBR` 仍为唯一 Terrain；文件句柄约 528→657，未重开或启动第二实例。临时诊断入口曾在写入中被 Unity 自动导入并出现 `CS0103`，最终脚本需再完成一次 Editor 导入后才能判编译门；旧 `attempt to write a readonly database` 仍存在，另记宿主日志。原 URP 资产的 128 米阴影、3 级联已恢复，测试前后 SHA-256 同为 `31978bcfb85c52c62396651f944b6cda04a39108d761ac17e8768b17f86cca34`。

| 任务 | 代码 | 编译 | Play / GPU | 本阶段结论与剩余判定 |
|---|---|---|---|---|
| G0 | 本包最终路径未因诊断改写；临时入口只用于验收 | Unity 刷新 `Tundra build success` 后进入原场景 Play，目标 Metal warning 未在本次新日志出现；临时入口后续增补尚待最终重导入 | 原 Editor 可以连续运行；File Descriptor 未再耗尽 | 功能会话可用；最终编译门在删除临时入口后重验 |
| T1 | 批量写入器先校验 / 暂存、后提交索引，失败回滚；重复 Mesh LOD 保持两独立槽 | Runtime / Editor Unity 6.6 Managed 编译 0 diagnostics；离线写入故障注入通过 | 合并入口的现有 Mesh 复用定向断言 PASS；PBR/NPR 迁移 Play 旧证据有效。一次 `UpdateTerrainGrass` 后，153 个 `.bytes` 的文件数、大小与纳秒修改时间摘要未变 | Editor 中新事务路径是否真正执行到 no-op 提交仍需 Console / 选择状态确认；不重烘大量旧资产 |
| T2 | 8 页 / 草种、2 Terrain 预算与代次路径见代码 | 第 8 页成功、第 9 页拒绝及释放重入逻辑断言已有 | 单相机 242 帧扫视：每草种最大需求 4、快速重入 0、命中增量 6888、驱逐增量 0、租约上限 PASS | 不把“未触及第 9 页”写为压力通过；下一次只用原 Terrain 的临时 Game Camera 制造需求，三独立 Terrain 实景继续受禁止新建 Terrain 限制 |
| T3 | 当前 RenderGraph depth UV 原点进入 HZB，三 lowering 和双 submesh 路径保留 | 本阶段 Unity Editor 导入成功，无目标 `HzbOccluded` Metal warning | 候选 187 的 CPU chunk 和 Metal VisibleIndex 均可见，LOD=1；原相机 120° 转向旧证据有效 | 真实不透明遮挡的 1→0→1 和多相机还待定点复验 |
| T4 | hold / now、互补 dither 桶与透视 / 正交屏占比路径保留 | Unity 6.6 Managed 与 Editor 已编译 | 同一候选相邻 LOD 在 120 帧内 Alpha 单调：0.000→0.451→0.992；此前同帧 CPU / GPU 双桶各 1 | 仍需同一树冠起 / 中 / 末画面判连续性，不以状态数值代替画面 |
| T5 | fallback 级联角点用相机正前方世界角点，每相机 / 级联独立 index / args | 修后代码在本次 Editor 成功导入 | 3/3 级联球心位于相机前方，目标树落入有效球；Frame Debugger 中实例 `FoliageShadow` 写入 4096² D16 atlas。原机位阴影强度 1→0→1 的 Game A/B：树冠与林下地面随之明显变亮 / 恢复。1、2、4 级联切换均维持最终可见画面，随后恢复 3 | 修后实际受影子门通过；各级联最终地面受影和 114–128 米渐隐的定点证据尚需补足，不以 atlas 写入代替父门 |
| T6 | VT、SH、Bound 各自路径未混淆 | 本次相关源码与 shader 已在 Editor 导入 | VT 同一地形页表 alpha 从无效 0 到驻留 255；草树、地形 Play 画面可见，无本次反馈数组释放异常。天空 SH 与 Bound 的旧独立 A/B 继续有效 | VT 缺页交接子门通过；退出 Play 后日志仍有宿主 readonly database，未见 `FDecodeFeedbackJob.encodeDatas has been deallocated`；最终汇总前核对退出状态 |
| T7 | 文档与临时入口待收敛 | 最终源码编译仍待删除临时入口后执行 | 旧基线 Game 1569×1874；本次 Frame Debugger RT 1547×1742，尚不可比 | 关闭诊断并恢复相同像素后采连续 300 帧；不满足 CPU ≤19.672ms、GPU ≤48.975ms 则不能提交 / 推送 |

### 2026-09-27 阶段门：单 Editor、迁移与资源重入

| 任务 | 代码 | 编译 | Play / GPU | 结论与后续验收 |
|---|---|---|---|---|
| G0 | 未增建 Terrain / 分支 / 第二 Editor；关闭旧 PID 96877 后只由 Unity Hub 重开同一 LandscapeExample，新 PID 28832 | 新 Editor Tundra build success，导入本轮 Tree Job 修复；当前日志未见目标 Metal warning 或 C# 错误 | 旧进程约 673 个打开描述符，重开后约 527，短 Play / 树组件重入后约 520；切换原有 NPR / PBR 场景后约 755，其中约 451 个是同名 `Library/ShaderCache.db` 的不同 inode 句柄。工程卷是 exFAT，`attempt to write a readonly database` 从 Editor 启动、ShaderCompiler 连接前后就出现，早于本轮 Play。当前 Play 仍可运行，尚未再次报 FD 耗尽 | 文件描述符耗尽在重开后一度解除，但 ShaderCache 句柄增长说明宿主风险仍在；数据库错误与 ShaderCache 句柄相邻发生，尚无堆栈证明二者同根，更不能归为 Foliage 异常。唯一允许的 Editor 重启已用完 |
| T1 | Scene_PBR 与 Scene_NPR 均有新 assetKey，当前 Assets/Generated/Foliage/Resources 共 153 个 .bytes，对应此前记录的 135 + 18；Scene_PBR 不含旧 densityMap | 当前 Editor 导入成功；切换 Scene_NPR 并 Play 后 Editor.log 没有缺页、旧密度或 C# 加载异常 | Scene_PBR 原 Terrain 的草树在 Play 绘制。Scene_NPR 只有 GrassComponent；同机位关闭时 Game Stats 是 243 Draw / 54,356 instances，开启时是 95 Draw / 131,156 instances，草画面保持可见，证明迁移草组件实际接管绘制。测试后退出 Play 并恢复 Scene_PBR；没有新建 Terrain。重复 Mesh 的每 LOD 独立槽有代码路径，本场景未证明该负例 | 两个现有场景的迁移与 Play 路径通过；重复 Mesh 负例仍是代码审查结论，不能冒充画面实测 |
| T2 / T3 资源寿命 | TreeSector 现在持有格视图和剔除 JobHandle，释放高度图、格数据前等待完成；TreeCullLodJob 仅在高度数组达到固定格尺寸时启用地形遮挡，否则保守保留候选 | 修改文件 git diff --check 通过，新 Editor Tundra 导入成功 | 旧会话在 Play 中切换 / 刷新后产生大量 TreeCullLodJob.SampleHeight 的 NativeArray 长度 0 越界，Draw Calls 曾异常升至约 99,666；已立刻停止。新 Editor 同场景短 Play，树组件关→开后恢复约 212 Draw / 5,754 实例，新日志无该异常。同一 Play 的直接快照为开启 `1 Terrain / 28 detail leases / 35 grass ComputeBuffer / 15 tree matrix ComputeBuffer / 15 tree bounds NativeArray`，草树同时停用后依次为 `0 / 0 / 0 / 0 / 0`，重启后回到原值；快照所用临时菜单与 meta 已删除 | 单 Terrain 的实际释放与重入通过；每草种 8 页由租约上限与现有断言验证，2 Terrain 上限没有独立三地块实景证据，不能将父项扩大为该画面通过 |
| T3 · Metal lowering | `TreeVisibility.compute` 的 `CullInstances`、`ExpandMaskToIndex`、`ExpandRunToIndex`、`FilterIndexHzb`、`CopyArgsCount` 实际执行；原包仍使用统一 `VisibleIndex` | 当前唯一 Editor 导入临时验收入口成功，末次日志没有目标 `HzbOccluded` / `MaskBit` / `FrustumDistance` 未初始化警告 | 在 Metal 上对 70 个候选和满位尾 chunk 逐条运行三种 lowering；每条 GPU 读回排序后均为 0–69，两个不同 indexCount 的 submesh args 实例数均为 70 / 70；三条 PASS 均写入本轮 Editor.log。临时菜单、meta 和 AppleDouble 已删除 | 三种 lowering、64 位尾块、两 submesh args 的 GPU 功能门通过；真实镜头的 HZB 误剔保留此前 10 树/120°/120 帧和不透明遮挡 A/B 的有限证据，地形脊、多相机不扩写为通过 |
| T5 | 自定义 fallback 仍保留：无原生 caster 时 URP 的 ExtractDirectionalLightMatrix 实际返回失败，替换尝试已完整撤回 | 撤回后 Tundra 导入成功；本轮 Editor.log 无目标绑定异常 | 同机位自定义树 1 级联出现大块黑影，关自定义树后原生树在 1 级联亦偏暗；阴影强度 0 时明度恢复，故该暗区不是天空 SH 且尚不能判为本包独有错误。1 级联 atlas 有投影、3 级联最终 atlas 有树影；本轮 Frame Debugger 补得 2 级联 `87` 个 Foliage Shadow 事件及 4096×2048 两块树影，4 级联 `132` 个事件及 4096×4096 四块树影。测试后直接核对宿主资产恢复 `3` 级联、128 米、原始 split 及 border，草树启用 | 1 / 2 / 3 / 4 级联的 atlas 写入通过，3 级联地面强度响应通过；单级联暗区与原生阴影的差异、128 米附近视觉渐隐仍没有足够归因，**T5 视觉父门未通过**。不以 Draw 数替代地面受影或边界验证 |
| T6 · VT / SH / Bound | VT shader 的 `_VTReady`、范围及页表 alpha 门控回退到 URP `StandardSplatmapFragment`；草树实例从 `ambientProbe` 按 Unity 七向量打包，HLSL 采用 URP 同一 L0/L1、L2 及 Gamma 求值；Bound 保留真实深度 | 本轮唯一 Editor 末次导入 Tundra build success，日志无 `FDecodeFeedbackJob` 数据失效、数组越界或目标 Metal warning；宿主 readonly database 另列 | 原场景 Play 同机位 VT 启用时地表正常；停用 `VirtualTextureVolume` 后地表仍有颜色、没有黑面；缩小覆盖范围至 X256 并关→开后，边界内外地表都有颜色，退出 Play 恢复 X512。天空 Skybox / Color A/B 有既有图证。Bound 在复现角度的本轮 Frame Debugger：事件 298 `Copy Final Depth` 读取 1547×1742 `_CameraDepthAttachment` 并输出同尺寸 GameView RT；事件 299 后的 16 个 `DrawGizmos` 使用 `Hidden/Editor Gizmo`，`ZTest LessEqual`、`ZWrite Off`，同机位线框仅被真实前景遮挡，无原先笔直大块分界 | **Bound 当前场景画面与深度门通过**；Render Scale 1 已保留。VT 未就绪和缩小范围的真实边界回退通过，有效页与缺页交接尚未逐帧验；SH 七向量与 URP 公式可静态核对，但同材质 Forward 数值未采。T6 总门因 VT / SH 剩余项未关闭 |

本轮定点补验：`Scene_PBR` 原 Camera `(406.5,16.4,229.5)` 面向有地表的同一视角，在 Play 内将现有 VT Volume 从 X512 临时改为 X256，并关→开使 `OnEnable` 重算覆盖矩形。相机 X=406.5 位于缩小范围右边界 X=384 外，画面同时可见边界两侧地表，均保有正常颜色，没有黑面或笔直暗界。退出 Play 后 Inspector 恢复 X512、组件启用；本测试证明越界回退，不等于证明有效页和缺页交接完全无色差。

T4 补验先从现有烘焙 `tree_9.bytes` 只读解析出候选 43 位于 `(430.01,18.94,237.89)`，树种 9 在 Scene 内为三档，阈值 `1/0.6/0.3`。原 Camera 在 Play 内朝该树设为 `(391,16.4,222)`、Yaw 69°、Pitch -15°，临时 Fade Duration 2 秒。第一次直接移到 X=400 跨 9 米，超过 `ViewDiscontinuous` 的 8 米传送阈值，属于预期硬切，不可作为 fade 证据；随后暂停逐帧改为 X391→396→400→406，每步不超过 6 米，目标树在首帧和之后 60 帧始终可见，未见整棵消失。由于普通 Game 图不能把这棵树在所有重叠树冠中逐像素分离，也未取得同一帧的候选 43 LOD 状态，**这批图仍不足以关闭连续 fade 视觉门**。退出 Play 后原 Camera、0.5 秒 Fade Duration 和原 debug 开关均由 Editor 恢复；没有保留中间帧。

本阶段没有进行 Player build、额外 Terrain 或性能判定。旧异常日志在 Logs/Editor-prev.log 约 148 MiB，已将可复查的堆栈入口和症状写在本表，并精确删除该轮转日志。T2 单 Terrain 资源门已有直接读数；T3 三种 lowering / 多 submesh 的 Metal 合成输入验收已通过，真实镜头范围仍受单场景限制；T4 连续画面仍待验。用原 Play 相机两段移动至此前 GPU 双桶点 `(395.62,16.4,238.43)`，稳态画面和日志正常，但普通 Editor 截图的延时大于 2 秒临时 fade 窗口，不能把这次截图当作连续过渡证据；退出 Play 后相机与 fadeDuration 均还原。T5 和 T6 各自维持独立画面门，T7 在前置门关闭后才运行。

T4 又在唯一 Editor 的 Play 中把根相机分两段移至上述 GPU 双桶点：先于 `(400,16.4,235)` 稳定，再暂停，将相机移到目标点并逐帧推进 16 帧。Play 画面没有整片空树，但所见树群中无法把特定树种 9 的那一棵与前后 LOD 像素一一对应；逐帧图不能证明该实例的视觉连续性。CPU / GPU 双桶 1 / 1 的既有证据保留，**T4 连续画面仍未通过**，不会用这批相似截图占用磁盘。

T4 定点复核只读原有 `tree_9.bytes`，用一次性 Editor 菜单把候选 43 绑定到运行时数组索引 187。暂停逐帧日志在第 2561 帧读到 `hold=now=stable=0, alpha=1`，编辑器重复渲染会推进状态，这组不作真实过渡证据。恢复正常 Play 后，第 3026 帧同一索引、相机 X406 为 `hold=1, now=0, stable=1, alpha=0.9161143, fading=True, hardCut=False`；目标树仍在 Game 画面。此前真实 Play 的 Alpha 0.254 帧，同树种两个桶的 CPU 和 Metal GPU args 都是 `1/1`，证明状态机与双几何提交实际运行。临时改为正交相机、尺寸 30 后，相机 X392 与 X406 时该索引均为 `hold=now=stable=1`，说明平移未触发错误的透视 LOD。退出 Play 后投影恢复 Perspective，原相机姿态和 Fade Duration 恢复。这些证据没有量测同一树冠的连续帧像素变化，T4 画面父门仍开。一次性菜单脚本和 `.meta` 将在下一次 Editor 刷新前删除。

T5 / T6 归因复核：同一原 Terrain、原 Camera `(406.5,16.4,229.5)` 的 Play 中，临时关闭 `TreeComponent` 让原生 Terrain 树接管，再打开；实时阴影启用时原生树冠与树下地表明显更暗，实例树较亮。保持同场景，临时关闭相机 `Render Shadows` 后，两种绘制的树冠都恢复较亮绿调，说明阴影是这次亮度差异的主要变量；无阴影对照只证明 SH / 直射光处于相近视觉量级，不能充当逐像素或系数数值相等。Frame Debugger 在实例 `Landscape/TreeLeave` 的 `ForwardLit-Instance` Draw 看到 `_MAIN_LIGHT_SHADOWS_CASCADE` keyword 与 4096×4096 `_MainLightShadowmapTexture`，因此问题不是完全缺阴影变体或贴图绑定。接收坐标、阴影图内容和偏置仍须定位；当前证据不支持修改 SH 系数。退出 Play 后相机 `Render Shadows`、树组件及原场景设置均恢复。这组 A/B 使 T5 最终画面门明确失败，不能仅凭 atlas 存在宣布通过。

T5 软阴影定点修复：URP 17.6 的 `SampleShadowmap` 只有在 `_SHADOWS_SOFT` 或各档专用 keyword 启用时才按 `_MainLightShadowParams.y` 进入 PCF；此前 fallback 虽传入 Medium 值 2，实例 `TreeLeave` 的 Frame Debugger keyword 列表只有 `_MAIN_LIGHT_SHADOWS_CASCADE`，实际走单点硬采样。`SetupShadowReceiver` 现在按本级联主光是否启用软阴影显式切换本包 shader 已编译的 `_SHADOWS_SOFT` 变体。现有 Editor 导入后 Tundra build success，原场景 Play 的同一实例 Draw 同时显示 `_MAIN_LIGHT_SHADOWS_CASCADE` 与 `_SHADOWS_SOFT`，并绑定 4096² atlas；退出 Play 后原设置恢复。这只关闭**软阴影变体绑定**子项。原生与实例阴影面积 / 亮度差、128 米边界仍未完成最终画面对照，T5 父门保持打开。

T5 后续定点复核：同相机、同 3 级联数下，原生 Terrain 树的 Frame Debugger 阴影图在左上象限树冠较密，自定义 fallback 阴影图投影分布较疏；两路径的级联投影不同，因此图形分布差尚不能直接解释为丢失 caster。代码真源另有明确差异：`TreeLeave` / `TreeBrak` 原生 `ShadowCaster` 固定深度偏置 -0.05、法线偏置 0，而实例 `FoliageShadow` 使用 URP 当前 `_ShadowBias`；原主光法线偏置为 0.4。现将两 shader 的原生 ShadowCaster 改为与实例同用 URP `ApplyShadowBias` 和逆转置世界法线，Shader 刷新后原场景 Play 成功、无目标 Shader 错误。同机位临时关 TreeComponent 后，原生树冠仍明显比实例树暗；重新启用并退出 Play 恢复现场。**偏置不一致已清理，但这不是受影差异的全部根因，T5 画面门继续失败。** 后续只针对级联投影、caster 覆盖及接收坐标定点诊断。该轮 Play 后 Editor 描述符已升至约 1184；下一次 GPU 诊断前使用用户本阶段允许的同一 Editor 单次重开，避免把宿主 FD 错误混入功能结论。

T3 新阶段门（代码 → 编译 → Play / GPU → 结论）：先精确撤销临时测试里传入 `null` depth 的 HZB 开关；该 A/B 在原相机使密林从稀疏恢复密集，说明大范围可见树被错误 HZB 剔除。随后仅去掉 `TreeVisibility.compute` 的固定 `uv.y = 1-uv.y`，保持 HZB 执行，同机位密林仍恢复，转动 Play 相机约 120° 后近树保持可见。URP 17.6 `RenderingUtils.ComputeInverseViewProjectionMatrix` 按深度纹理 `TextureUVOrigin` 决定 GPU 投影方向；本包之前使用 `GL.GetGPUProjectionMatrix(..., true)` 后又固定翻转 UV，标准 BottomLeft 中间纹理会二次翻转。最终路径现在在 `FoliagePass` 的 RenderGraph 回调查询该帧 depth handle 的 UV 原点，按相同规则从当前 `UniversalCameraData` 投影与视图算矩阵，再传给 `BuildHzb`、格盒和实例盒测试；无硬编码 Metal Y 翻转。新 Editor 导入 `Tundra build success (2.07 seconds), 25 items updated`，原相机 Play 密林与无 HZB 对照视觉一致、120° 转向未见大面积误剔，日志无目标 Metal warning / C# 错误。**根因修正和上述画面子项通过；新映射的真遮挡、旧 10 树逐帧 GPU index、地形脊 / 多相机仍待定点复验，T3 父项暂不通过。** 两次诊断均在 Play 外撤销临时修改，没有留下绕开 HZB 的开关或测试资产。

T6 SH 定点复核（代码 → 编译 → Play → 结论）：临时让 `TreeLeave` 的普通 Forward 与程序化实例 Forward 仅输出各自的 `indirectDiffuse`，不改变原相机、树材质、天空或地形；原生 / 实例两种模式的树冠均为相近的暗色量级，不再被直射光或实时阴影遮蔽比较。临时输出已在 Play 外恢复正式光照式；Grass / Tree 的七向量打包逐项符合 Unity Core `SHCoefficients`，Scene_PBR 树 prefab 均禁用局部 Light Probe，天空 Skybox / Color A/B 的实例响应已有证据。TreeLeave / TreeBrak 普通 Forward 法线同步改为 URP `TransformObjectToWorldNormal`，与程序化实例逆转置法线保持相同非等比缩放语义；该最终法线改动尚待 Editor 刷新。**SH 来源、公式及同机位间接光量级有代码和 Play 证据；没有七向量 GPU 数值读回，不能宣称逐系数完全相等。** 当前树冠暗色是场景 ambientProbe 的表现，先前正常光照里原生 / 实例的显著亮度差须在 T5 继续查阴影接收。

T5 级联球根因定位（代码 → 编译 → Play / GPU → 结论）：为避免直接光与 SH 互相掩盖，短暂把同材质 `TreeLeave` 两个 Forward pass 分别改为仅输出 `mainLight.shadowAttenuation` 和 `ComputeCascadeIndex/4`，每次只在现有 Play 的同一原相机临时开关 `TreeComponent`。阴影衰减图中实例叶片几乎全白（接近 1），原生树叶有大量深灰 / 黑；级联编号图中实例几乎全白（索引 4，落在 0–2 实际级联外），原生为级联内的灰度。Frame Debugger 在自定义实例 Draw 显示球 0/1/2 中心 X 约 `408.97 / 415.92 / 463.00`，而当前相机 X=406.5、Yaw=-50.629°，视线向 X 减小方向；球心沿相机背后增长。`BuildFoliageShadowSlice` 原用 `CalculateFrustumCorners` 返回的视空间向量经 `cameraToWorld.MultiplyPoint3x4`，与当前相机正向不一致。现改用 `Camera.ViewportToWorldPoint` 直接取得 0/1 视口四角在 near/far 距离的世界位置，再计算球与光空间投影；Unity API 明确其 z 为从相机起的正向距离。修改后的本机 Unity 6.6 Runtime Managed DLL 离线编译 exit 0、0 diagnostics，`git diff --check` 通过。**Editor 在刷新这项修改时 fd 1488 注册失败，Tundra 中断且 Play 被拒，修后级联编号、atlas、地面受影、128 米边界均未验，T5 父项未过。** 诊断 shader 已在磁盘恢复正式光照式；当前未成功重导入的 Editor 内存里可能仍是旧诊断变体，不能当作最终画面。未创建 Terrain、分支或测试资产。

### Play 旋转后的复核

用户再次观察到阴影距离异常、近远植被闪现、看不到 LOD fade。相机在 Play 中由 Y=-50.629° 转至 20° 对照后已恢复原角度；这次未锁定同一棵树的首次消失帧，因此不能单凭截图指认 HZB 或 URP。代码有两个确定的 LOD 缺陷：`BuildTerrainTree` 将每个 prefab 的 LODGroup 阈值替换为 `1 - lod * 0.03125`，场景实际序列化为 `1 / 0.96875 / 0.9375` 等；`Geometry.GetProjectionMatrix` 对输入直接取 `tan`，而颜色 / 阴影调用方传入的是 `Camera.fieldOfView` 的角度值，且并非半视角弧度。8% 的迟滞还宽于相邻阈值的约 3.1% 间距，容易跨档硬切，使双桶 dither 难以被触发。旧断言没有覆盖 Bake 阈值与角度单位，故原 T4“代码通过”结论撤回。

本场景 `Terrain.treeDistance=512`、URP `Shadow Distance=128`、`Cascade Border=0.10909091`；实时阴影约在 114–128 米渐隐，树仍可绘制到 512 米。用户明确选择保留该距离与性能预算，不能将 128–512 米没有实时影判为缺陷。自定义 CSM fallback 只验过 atlas 写入与地面光强响应，不能推定 128 米边界或级联稳定。旋转时对象本体闪现另走颜色可见性链：CPU 格盒 / 地形遮挡、GPU HZB / VisibleIndex、最后 Indirect Draw；须在同物体、同机位逐层对照。树包围盒原来只取 LOD0 mesh 的 bounds，本轮已改为合并所有 LOD 并重烘；是否足以消除原先观测到的误剔仍待连续帧复验。以上均不归因于 VT 色面或 Bound Gizmo。

本轮修正：`BuildTerrainTree` 和通用 `MeshAsset` Bake 读取 prefab 的实际 LODGroup 前档阈值；`Geometry` 与 CPU / 阴影 LOD 使用角度单位正确的投影和屏半径；树实例盒合并所有 LOD mesh 的 bounds。只在原有 `Landscape` 执行 `BuildTerrainTree` 并保存，场景仍是 1 Terrain、生成资产仍 135 个；旧的固定阈值已从 Scene 消失。另发现同一树种多个 LOD / bucket 共用 `m_Chunks`、`m_RwChunks`、`m_Runs`、`m_SrcIndex` 暂存 buffer：即时 `SetData` 可在前一项排队的 GPU dispatch 消费前覆盖数据。改为同一 `CommandBuffer` 的 `SetBufferData` 顺序上传，并把颜色和主光 index / args 上传排进对应的命令流。Unity 官方 API 明确它会添加 buffer 数据命令；此修正解决了代码上的时序缺陷，但还要在 Frame Debugger 核对每桶结果与旋转连续帧，不能据此宣布闪现已消失。

同机 Play 复核时先在固定坐标把 Y 角转至 -30°、0°，再沿 Z 每次前进 5 米；稳态画面无空白树群，Frame Debugger 捕获了当前相机深度输入及树实例 Draw。为捕捉 fade 曾在 Play 临时把 `Fade Duration` 调至 Inspector 上限 2 秒，并暂停前移帧；没有可靠记录到相邻 LOD 两个 bucket 同时有非零实例，故 T4 仍不通过。退出 Play 后相机、`Fade Duration` 与 debug 开关由 Editor 还原，未保存测试值。

再用现有 Editor 的一次性菜单按固定路径复现：根 `PlayerCamera` 是活跃相机，场景中 `PlayerController` 子相机的 `m_IsActive: 0`，因此先前“可能切到另一台相机”的猜测撤回。Frame Debugger 同帧显示 `DrawOpaqueObjects` 只画地形，后续 `Foliage` 自定义 pass 才加入 Play 树草。旋转 121 帧没有 LOD 变化；固定朝向向树群近移并返回 121 帧时有 88 帧同一树视图持有 fade，且 fade-out / fade-in CPU 桶同时非零。首次双桶帧已暂停并观察 Play 画面；启用 Frame Debugger 后采样路径继续推进，因此该捕获不能当作同一帧两个 GPU 实例桶的严格证据。诊断脚本、`.meta` 和 AppleDouble 已精确删除，Play 相机回到原坐标且未保存测试姿态。转镜头闪现仍须固定同一实例，对照 CPU chunk、GPU VisibleIndex / args 和相机 depth 才能判定根因。

随后固定原场景根 `PlayerCamera`，按同一 120° 往返路径逐帧采 10 棵树；只统计树顶处于画面中央且其视线未被地形挡住的帧。修复前其中 3 棵 CPU chunk 持续可见，GPU VisibleIndex 却分别缺失 34/34、15/15、21/21 帧。只在诊断期间关闭 HZB，三棵全部恢复；在 HZB 开启的正式路径把投影 UV 的 Y 转成 Metal depth `Load` 的行原点后，三棵也全部恢复。另把临时不透明盒放到本来可见的树前，盒存在时 GPU 正常剔除，移走后恢复。由此确认了这批近远树消失的深度坐标根因，同时保留真实遮挡。诊断开关与菜单脚本均已删除，盒未保存到 Scene；这组有限路径不能替代地形脊和所有相机的画面验收。

后续代码复核修了三个边界：驻留缓存原只比较帧号和视点位置，现同帧还比较视图 / 投影矩阵，防止只旋转相机却沿用旧页需求；草组件在首次注册时先读取 `Terrain.detailObjectDensity`，防止第一帧等待页 Flush 时把新页误设成零密度；草 Bake 先编码并提交所有页，再把新格计数和资产键写入 Scene 内存，失败时不留下半迁移状态。三项均在现有 Editor 刷新编译并 Play 冒烟，容量、故障注入和资源释放仍需专门验收。

LOD 增补了正交相机的固定屏半径投影，颜色与主光阴影均走同一计算；主光 fallback 改用锁定 URP 17.6 内部质量函数，当前帧 `_MainLightShadowParams.y=2` 与宿主 Medium 设置一致。Frame Debugger 对 Indirect Draw 的实例数显示为横线 / 零顶点，事件列表还会刷新，故改在现有 Play 的 `endCameraRendering` 后直接读取 GPU args。原 `PlayerCamera` 从 `(406.5,16.4,229.5)` 沿前方每帧 0.16 米走至 14.4 米后返回；树种 9 在 Alpha=0.2536368 时，fade-out / fade-in 的 CPU 计数为 1/1，GPU args 实例数也为 1/1，相机位置 `(395.62,16.40,238.43)`。这一帧证实两档确实提交到 Metal；连续帧画面平顺仍须单独判断。临时探针随后删除，Play 测试值不保存。

### 七份文档去重矩阵

原文：`Foliage_Build优化Plan`、`Foliage_Upload优化Plan`、`Foliage改进Plan`、`Grass内存与流式加载需求`、`Tree结构与可见性优化Plan`、`Foliage结构优化需求`、`InfinityFoliage-植被系统架构报告`。同义需求只计一次。

| 归并能力 | 本轮前 | 最终归属与证据 |
|---|---|---|
| 草 packed、私有 JobHandle 撒点、可见优先多 run 上传 | 已实现、运行成本未验 | 收敛为页内单一路径；密度改变时 GPU 容量、Draw、实例数响应已验 |
| 草基础层 + 4×4 页、2 地块 / 每草种 8 页 | 未实现 | 迁移、单 Terrain 释放重入和 10 页需求 / 8 页租约已验；三独立 Terrain 实景按禁建限制保留 |
| 树 Morton、64 宽 Visibility IR、三 lowering、Indirect | 已实现、GPU 行为未验 | 三 lowering、双 submesh 的 Metal args 各 70/70；只写 VisibleIndex，无旧 VS mask 寻址 |
| 可见格候选、CPU 地形格盒遮挡、GPU HZB 格盒 / 实例 | 部分实现 | RenderGraph 当前 depth 的 UV 原点和真实遮挡 `True/False/True` 已验，120° 转向无成片误剔 |
| 双视点 LOD fade、屏占比迟滞、传送硬切 | 部分实现 | Bake 阈值、FOV 单位、全 LOD bounds 已修并重烘；状态断言、CPU/GPU 双桶和同树冠三张画面已验；截图异步限制见 T4 |
| 每相机 / 每 CSM 级联独立树阴影索引 | 未实现 | 1/2/3/4 级联 atlas 与最终地面接影已验；128 m 边界定点验收见 T5，原生 caster 并存 / 镜头外 GPU 样本未单独隔离 |
| VT 缺页回退、天空 SH、Bound 深度 | 已修、验收不全 | 三问题独立验收；VT 主光级联与软阴影变体额外修复见 T6 |
| 固定顺序 `[16k,16k+16)`、顺序前缀、旧全图 buffer | 被较新 Upload 方案替代 | 删除旧路径，不恢复 |
| 内部 BVH、草 mesh LOD、可变格、跨块双归属、点 / 聚光灯阴影 | 明确排除 | 不计欠账 |

### 性能与驻留

`PlayerCamera` 固定 (406.5,16.4,229.5)、Y=-50.629°，Game 1569×1874，每次连续 300 帧；单位 ms，表中为 P95。初始基线 CPU Total 17.884、CPU Main 1.931、GPU 44.522；110% 总帧 / GPU 门为 **19.672 / 48.975**。基线 Draw Profiler 计数全零与 Game Stats 不符，判无效；后续用 `UnityStats.drawCalls`。图形驱动分配估计不是 Metal VRAM。

| 采样 | CPU Total | CPU Main | GPU | Draw | Editor RSS MiB | 图形驱动分配 MiB | 驻留地块 / 页 |
|---|---:|---:|---:|---:|---:|---:|---|
| 最初全功能、原相机 | 17.768 | 3.496 | 21.387 | 157 | 2432 | 829 | 1 / 28 |
| 长时间 Editor 会话后、原相机 | 21.060 | 3.410 | 27.613 | 175 | 5716 | 1136 | 1 / 28 |
| 离开原 Terrain（X=800） | 20.793 | 2.330 | 25.515 | 43 | 5721 | 1146 | 1 / 0 |

后两次 CPU Total 超门，RSS 未随页卸载下降，不能宣称性能 / 内存总门通过。页持有的 `NativeArray` / `ComputeBuffer` 有释放路径，进程 RSS 还包括 Editor 缓存与分配器；需要同一清洁会话的内存快照证明资源回收。`PageEvictions` 原未计距离卸载，已补计；上表采样是修复前口径。

主光阴影和颜色 Foliage pass 原来在同一相机、同一帧各调用一次 `FoliageResidency.UpdateView`，重复计算地块距离、排序草页并把同一命中计两次。本轮按相机 / 帧 / 视点跳过重复调用；Runtime / Editor Managed DLL 再编译均 exit 0、0 diagnostics。新代码尚未重采 300 帧 P95，也不能仅凭这项优化把 T7 性能门改为通过。

驻留审计又发现原预算以各 Terrain 的 `grassIndex` 作全局草种键：不同 prefab 只要序号相同就争同一组 8 页，相同 prefab 序号不同却能越过预算。改为 prefab 的 Unity `EntityId`，与页加载申请使用同一键。更新选页改为先驱逐所有未入选页，再申请新页；取消的异步加载立即归还租约，旧回调只做代次检查与资源释放。此前顺序会使新页暂时申请失败，镜头转向时可能表现为细节闪现。该根因修正已通过托管编译，但 Mac 锁屏期间未完成 Editor Play 复验。

曾在 Play 内短暂克隆两个未独立 Bake 的 Terrain，只检查 3 对象时驻留 2 的预算计数。克隆资产仍指向原 Terrain，**不构成三地块画面验收**。副本未保存、已退出 Play；后续不再新增 Terrain。原场景仍只有一个 `Landscape`。

驻留负路径复核发现：草组件以前在关闭 Terrain 原生草、分配 NativeArray 后才调用各草种 `Init`，prototype 无效或后续分配失败可能令 `m_RuntimeReady=false` 却留下距离为 0 和资源；现在先校验所有 prototype，再在注册失败时释放已初始化页与 NativeArray、恢复原生草距离。草页在解码后撒点构建中途失败、或 GPU 上传失败时，统一清理 scratch、密度、buffer 和页租约，避免失败页持有半分配。视锥边缘的第 8 / 9 页原可能因可见标志每帧互换而反复驱逐；现已驻留页在最后一次可见后保留 12 帧可见优先级，仍不增加每草种 8 页预算。以上最新代码通过本机 Unity 6.6 Managed DLL 的 Runtime / Editor 独立编译，均 0 diagnostics；真实 Editor 页抖动和故障注入尚待解锁后验证。单 Terrain 移到 X=800 不能测试整地块释放，因为预算为 2，该 Terrain 仍驻留；须在 Play 中禁用 / 启用原有草树组件验证资源释放和重入，不再创建 Terrain。

树资产驻留时原先每个树种都调用 4096 次 `Terrain.SampleHeight`；`Scene_PBR` 的 15 个树种因此可能重复 15 次相同采样。现在同一 `TreeComponent` 首个资产加载时通过 `TerrainData.GetInterpolatedHeights` 批量采样一次，卸载时丢弃共享托管数组，各 `TreeSector` 继续复制到自己的 NativeArray。Unity 文档明确该 API 的写入已有数组重载不再分配托管内存；采样值加 Terrain 的 Y 位移，与世界空间的相机、树盒比较一致。当前原 Terrain 的 Y 为 0，画面效果不应改变。该改动的 Runtime Managed DLL 编译 exit 0、0 diagnostics；驻留重入耗时与非零 Y Terrain 的画面尚待 Editor 验证。

树的 LOD batch 原在 GPU / Native 资源分配中途失败时尚未加入 `m_Batches`，外层 `Release` 无法找到它；现由 batch 在初始化失败时清理已分配部分。阴影 batch 同样清理部分创建的 args，索引 buffer 扩容改为新 buffer 创建成功后再释放旧 buffer、更新容量。Runtime Managed DLL 编译 exit 0、0 diagnostics；这是失败路径的静态闭环，尚未通过真实 GPU 资源故障注入，不能据此宣称 T2/T5 父项通过。

### 画面证据

- [主光 atlas](Evidence/t5-shadow-atlas.png)、[阴影强度 0](Evidence/t5-shadow-strength-zero.png)、[阴影强度 1](Evidence/t5-shadow-strength-one.png)：当前 3 级联写入与地面响应。
- [Bound 开](Evidence/t6-bound-on-y20.png)、[Bound 关](Evidence/t6-bound-off-y20.png)：Play 相机转至 Y=20° 的同机位对照。异常笔直分界来自宿主 URP Render Scale 0.66 下 GameView depth-copy / Gizmo 尺寸不一致；宿主 `UniversalAsset.asset` 已保存 Render Scale 1。真实前景遮挡保留。
- [天空 Color](Evidence/t6-sh-color.png)、[天空 Skybox](Evidence/t6-sh-skybox.png)：实例草树视觉 A/B，已恢复 Skybox / 强度 1；没有同材质 Forward 数值对照。
- VT Terrain 色面与 Bound 线框是独立问题。VT 包位于本包 Git 外；当前机位正常，缺页 / 越界六状态还未全部通过。

### 工程与交付边界

`FoliageLogicAsserts.Evaluate()` 在现有 Editor 显示 `FoliageLogicAsserts passed`。用于此测试的临时菜单采样器、原始采样 JSON 和场景备份已删除。修复后 Editor.log 未出现新的目标 `TreeVisibility` Metal 未初始化警告。Unity CLI 的 `status` 返回 `STATUS_NO_INSTANCES`（当前项目没有 CLI Pipeline 连接），因此不能用它代替现有 Editor 的交互验收。第三方 deprecated 警告和 `attempt to write a readonly database` 与本项区分记录。

宿主 `Scene_PBR.unity`、135 个生成 `.bytes` 与相邻 VT 包变更均不属于本包 Git；单独推送本包 `main` 无法携带它们。旧 Scene / MeshAsset 必须重新 Bake，无兼容读取路径。当前完整验收门尚未通过，不把 `main` 推送描述为完整交付。

补充迁移审计：工程现有 `Scene_PBR`、`Scene_NPR`、`Scene_VT` 各有且仅有一个 Terrain。`Scene_NPR` 原有草组件序列化 16 个 `densityMap`，没有新资产键；恢复现有 Editor 后对其原 `Landscape` 执行 `BuildTerrainGrass` 并保存。场景文本由约旧密度内嵌变为 `densityMap: 0`、`assetKey: 1`；生成目录包含 `grass_0_base`、16 个细节页与 `index`，共 18 个 `.bytes`。Play 后前景草可见，退出 Play 后回到 Edit；Console 的两个错误均为重复的 `attempt to write a readonly database`，没有新的 foliage asset 缺失或 C# 编译错误。`Scene_VT` 没有 foliage 组件，不需草树 Bake。

最新一次 Editor 刷新导入了共用 `Foliage.hlsl` 及三个草树 shader，Play 草树仍可见；此后 Editor 对测试探针刷新时发生宿主级 `NotSupportedException: Could not register to wait for file descriptor 1080`，Burst gRPC 和 Visual Studio integration socket 也报同源错误。只读 `lsof` 显示该 Unity 进程约 1,244 个打开描述符，其中约 812 个指向 `Library/ShaderCache.db`；不能把这次 Burst 失败归因于 foliage C# 或 Metal compute。解锁后在同一个 Editor 点击 Play，Console 明确提示 `All compiler errors have to be fixed before you can enter playmode!`，实际未进入 Play。临时探针及 `.meta` 已删，独立 Roslyn 编译它为 0 diagnostics。当前正在请求只重启同一项目的单个 Editor 以释放描述符；在清洁会话重新通过之前，不能把此轮 Play、shader 和 P95 写成总门通过。
