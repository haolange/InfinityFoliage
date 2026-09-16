# Infinity Foliage

URP 下替换 Unity Terrain 内置草 / 树的绘制。包名 `com.infinity.render-foliage`（文件夹仍是 `com.infinity.foliage`）。

Agent 工作台见 [AGENTS.md](AGENTS.md)。交付与宿主验收见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

## 使用

1. 在 Terrain 上挂 `GrassComponent` 和 / 或 `TreeComponent`。
2. 菜单 `GameObject / EntityAction / Landscape`：`BuildTerrainGrass` / `UpdateTerrainGrass`，`BuildTerrainTree` / `UpdateTerrainTree`。
3. 草的 `numSection` 控制切片（菜单默认按 32 世界单位切）。树有自己的 `numSection`（默认 16），不跟草走。
4. 改过数据布局或类型名后，**必须重新 Build/Update**。旧 Scene 与 MeshAsset 无兼容层。
5. Play 时组件会关掉 Terrain 内置草 / 树距离，停用后恢复。

URP Renderer 需挂 `FoliageRenderer`。
