# Infinity Foliage

URP 下替换 Unity Terrain 内置草 / 树的绘制。包名 `com.infinity.render-foliage`（文件夹仍是 `com.infinity.foliage`）。

Agent 工作台见 [AGENTS.md](AGENTS.md)。交付与宿主验收见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

## 使用

1. 在 Terrain 上挂 `GrassComponent` 和 / 或 `TreeComponent`，在 URP Renderer 中启用 `FoliageRenderer`。
2. 退出 Play，选中 Terrain 所在对象，从 `GameObject / EntityAction / Landscape` 执行 `BuildTerrainGrass` 和 `BuildTerrainTree`；Build 会继续执行对应 Update。更改 Terrain 密度或树实例后可执行 `UpdateTerrainGrass` / `UpdateTerrainTree`，然后保存场景。
3. 草的 `numSection` 控制空间格（Build 菜单默认按 32 世界单位切）；树有自己的 `numSection`（默认 16），不跟草走。
4. 当前 Bake 将草写成每种草一份基础密度资产和 `4×4` 细节页，将树候选流写成独立资产。旧 Scene、MeshAsset 和旧密度布局没有兼容路径，必须重新 Build/Update；Play 前确认流式资产已生成。
5. Play 时最多驻留 2 个 Terrain；同一草 prefab 跨 Terrain 最多驻留 8 个细节页。基础层随 Terrain 驻留，细节页按可见性和距离加载。`GrassComponent.SetDensityScale` 或 Terrain 的 `detailObjectDensity` 可调整运行密度。组件会关掉 Terrain 内置草 / 树距离，停用后恢复。

树的颜色绘制由可见格、地形 / HZB 遮挡、双视点 LOD 与屏占比迟滞生成 Visibility IR，再 lowering 到 `_TreeIndexBuffer`；主光 CSM 使用独立级联索引。代码路径的编译、Play 画面和 Metal GPU 验收状态以 [交付记录](Docs/DELIVERY.md) 为准。
