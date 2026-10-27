# Infinity Foliage

在 Universal Render Pipeline 中绘制 Terrain 的草和树。包名是 `com.infinity.render-foliage`，目录名是 `com.infinity.foliage`。

架构见 [ARCHITECTURE.md](ARCHITECTURE.md)。修改约束见 [AGENTS.md](AGENTS.md)。验收记录见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

要求 Unity `6000.6` 与 Universal Render Pipeline `17.6`。场景里已有的草密度和树实例需要重新烘焙后才能被本包加载。

## 接入

1. 在 Terrain 物体上添加 `HG/Foliage/Grass Component`、`HG/Foliage/Tree Component`，或两者都加。在 URP Renderer 上添加 `FoliageRenderer`。
2. 在编辑模式选中带 Terrain 的物体，执行 `GameObject/EntityAction/Landscape` 下的烘焙命令，然后保存场景。
3. 进入 Play。组件会把对应的 Terrain 内置绘制距离记下来并设为 0，停用组件后恢复。

只添加树组件时，树路径可以独立运行。

## 烘焙

| 命令 | 作用 |
|---|---|
| `BuildTerrainGrass` | 补齐草组件。`numSection` 取 `(heightmapResolution - 1) / 32`；该差能被 32 整除时，每格对应 32 个高度图采样。接着执行更新。 |
| `UpdateTerrainGrass` | 读取细节层，写成每种草的基础层和 `4×4` 细节页。 |
| `BuildTerrainTree` | 补齐树组件，从 prefab 的 `LODGroup` 建立网格与 LOD。树的 `numSection` 保持组件值，默认 16。接着执行更新。 |
| `UpdateTerrainTree` | 把当前树实例写成每种树一份候选资产。 |

细节层按 `GetDetailLayer` 的 `[y, x]` 读取。基础层密度为 `(density + (hash & 7)) / 8`，细节页保存其余密度。资产位于宿主工程的 `Assets/Generated/Foliage/Resources/{assetKey}/`，不随本包仓库分发。场景未保存时不能生成 `assetKey`。

更新草或树的源数据后，执行对应的 Update 并保存场景。进入 Play 后树候选列表会清空，场景保存也不会在 Play 里重建空间格，因此烘焙放在编辑模式完成。

`Assets/AssetActions/Landscape/BuildMeshAssetFromPrefab` 与 `UpdateMeshAssetFromPrefab` 维护可复用的 `MeshAsset`。创建菜单为 `Landscape/MeshAsset`。

## 运行控制

`FoliageRenderer` 的 Settings 是全局默认。下表括号内是序列化字段。

**Drawing**

| 控件 | 默认 | 说明 |
|---|---|---|
| Draw Grass（`drawGrass`） | 开 | 同时控制草的颜色和投影 |
| Draw Trees（`drawTrees`） | 开 | 控制树绘制 |
| Tree Pre Z（`treePreZ`） | 开 | 深度 Pass 先写入当前深度，颜色 Pass 复用本次准备结果 |
| Cast Tree Main Shadows | 开 | 树的主光投影 |
| Cast Grass Main Shadows | 关 | 草的主光投影 |
| Grass / Tree Distance Scale | 1 | 乘进入 Play 前的 Terrain 绘制距离 |

**Tree Visibility**

| 控件 | 默认 | 说明 |
|---|---|---|
| Tree Backend | Auto | 显式 CPU 走 Burst。Auto 与 GPU 在计算着色器可用时走 GPU，初始化失败时实际回到 CPU |
| Terrain Occlusion | 开 | CPU 与 GPU 使用同一套地形格测试 |
| GPU HZB | 开 | 只在实际 GPU 后端生效。CPU 后端下控件不可用，已保存的选择保留 |
| Tree Lod Fade | 开 | 关闭后只画稳定 LOD |
| Tree Lod Fade Mode | Temporal | 时间窗，或 Distance 距离过渡带 |
| Tree Fade Width | 0.2 | 距离模式的过渡带宽度 |
| Tree Lod Hysteresis | 0.08 | 时间窗的屏幕占比迟滞 |

**Residency**

| 控件 | 默认 | 说明 |
|---|---|---|
| Resident Terrain Budget | 2 | 同时驻留的 Terrain 数，最小 1 |
| Grass Detail Page Budget | 8 | 每个草 prefab 跨 Terrain 的细节页数。0 表示只保留基础层 |
| Page Visibility Hold Frames | 12 | 细节页离开视锥后继续参与选择的帧数 |

预算可以在 Play 中调整。`4×4` 页划分和基础层公式属于资产格式，运行时不能改。

`TreeComponent` 的 Rendering Overrides 可以覆盖本 Terrain 的距离、后端、遮挡、淡化、过渡带、迟滞和主光投影。`Fade Duration` 默认 0.5 秒，只用于时间窗。`GrassComponent` 可以覆盖绘制距离和主光投影。草组件勾选 Override Main Shadows 后，由 Cast Main Shadows Override 决定本 Terrain 是否投影。

主光的阴影距离和级联仍在 URP 资产中设置。树投影按每个级联单独收集投射体，颜色画面之外的树可以投影。草投影复用所属相机这次已经可见的页和 run。

时间窗在档位变化后使用一段共享的 `fadeDuration`，连续移动时每次只在相邻两档之间过渡。距离模式按当前屏幕占比混合，相机停下后混合结果保持不变。切换后端、淡化模式或淡化开关时，当帧按新状态硬切。

## 诊断

选中组件并打开 Show Bounds。草的 Cells 表示视锥与距离，Pages 表示基础层或细节页是否驻留。树的 CPU 实例线表示该调试相机的 CPU LOD；GPU 实时实例线保持灰色。

判断 GPU 剔除时，在 `TreeComponent` 指定 Debug Camera，空值使用 Main Camera。进入 Play 后使用 **Capture Visibility Snapshot**。结果绑定后端、相机和帧号，并给出最终索引数。Debug Tree Index 与 Debug Candidate Index 用来查看单个候选。**Clear Visibility Snapshot** 恢复实时线框。读回只在编辑器中按请求执行。

距离模式的快照会列出权重配对。指定候选落在过渡带内时，可以看到它在两档上的权重。

## 透明剪裁

草和树叶先拒绝 alpha 为 0 的纹素，再按材质的 Alpha Threshold 剪裁。阈值为 0 时仍然不绘制完全透明的纹素。颜色和深度 Pass 用 AlphaToMask 保持与覆盖蒙版一致；阴影 Pass 关闭 AlphaToMask，轮廓由剪裁决定。树干着色器不开启 AlphaToMask，稳定 LOD 不写入深度 Pass，正在淡化的桶会写入。

## 风

把 `WindComponent` 放到场景中即可向草写入全局风参数。它可以从预设或 `WindZone` 取值。树着色器不采样这套风参数。
