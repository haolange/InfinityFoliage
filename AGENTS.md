# 修改约束

本文件约束对本包的修改。现行结构见 [ARCHITECTURE.md](ARCHITECTURE.md)，使用步骤见 [README.md](README.md)，验收记录见 [Docs/DELIVERY.md](Docs/DELIVERY.md)。

架构事实与源码不一致时，先改文档或先改实现，使二者重新一致。验收记录中的历史结论保持原样，并在新日期下追加本轮范围。每次迭代结束后，已经存在的 `AGENTS.md`、`DESIGN.md`、`ARCHITECTURE.md` 只吸收本轮已经证实的变化：补上仍成立的内容，删除失效内容，合并重复表述。维护这三份文件时控制字数，本文件更短，只保留修改约束。

## 工作方式

- 修改需同时满足本文件和架构说明。
- 符号、残留类型和失效引用交给探索子任务检索。
- 只复用已经打开的 Unity Editor。不启动第二个 Editor、Unity Hub 或 batchmode。
- 编译结果与运行结果分开写入 `Docs/DELIVERY.md`。没有对应证据时写明未验收。
- 本包没有界面规格文件。不新增 `DESIGN.md`，也不把渲染结构写成界面流程条款。

## 交付

按用户给出的目标量级完成规划、实现、验证和收口。量级分为单点修复、局部功能、跨模块收敛、架构升级、全库清理，以及验证、审计或交接。确定之后保持该量级。完成是指调用链、负路径和真实运行验证都已闭合。计划、样品、只改主路径、只做局部补丁或只跑静态检查，都不算完成。

关键判断依据代码、文档、日志、测试、历史资产或用户的明确输入。复杂任务先写成可复查的状态摘要，再修改。只改与请求直接相关的代码、文档和相邻调用点。实现停在当前需求边界内，不加没有当前需求的设计、抽象或全库重构。本轮触及、并且能够安全证明已经废弃的实现、兼容层和双路径，收成一条正式路径。验证通过之后结束。

可能丢数据，破坏 API、ABI 或 schema，造成大范围不可逆改动，改变安全、权限或合规边界，改变外部可感知语义且无法安全回滚，或者现有证据不足以选出安全默认时，先停下确认。其余歧义写明默认选择、理由、风险和回滚方式，然后继续。

git 分支只在用户明确要求，或当前明确处于 Worktree 模式时创建。

## 必须保持的行为

包标识保持 `package.json` 的 `com.infinity.render-foliage` 与目录 `com.infinity.foliage`。命名空间保持 `Landscape.FoliagePipeline`。着色器资源名保持 `Landscape/Grass`、`Landscape/TreeLeave`、`Landscape/TreeBrak`。

草和树维持独立的数据与绘制。只挂 `TreeComponent` 必须能够运行。

一次相机渲染只准备一次颜色结果，键为 `FoliageCameraData.renderId`。顺序是驻留更新、组件盒粗剔、`Cull`、一次 `CompleteAll`、`Pack`、`Draw`。主光 Pass 执行时由它完成这次准备，再按级联绘制；它不执行时，`treePreZ` 开启则由深度 Pass 准备，关闭则由颜色 Pass 准备。主光每个级联单独打包和绘制树，使用该级联自己的索引和计数。草阴影复用本次颜色可见页和 run。

草页 scatter 使用页私有 `JobHandle`。未完成时继续使用旧缓冲，首次无缓冲则跳过该页。scatter 不进入 Pass 共享的 `taskHandles`。

草的运行数据是每种一份基础层加 `4×4` 细节页。基础层公式、页轴和版本保持 `FoliageAssetCodec` 的 `BaseDivisor`、`PageAxis`、`Version`。细节页预算按 prefab 实体标识计数，`grassIndex` 只表示单个 Terrain 的细节层序号。`GetDetailLayer` 按 `[y, x]` 读取。

树的运行数据是 Morton 排序后的候选流和 `{offset, count}` 格段。可见性中间表示宽 64。CPU 以 `DrawMeshInstancedProcedural` 提交已知计数，不分配 indirect 参数。GPU 以掩码、计数、前缀和、压缩写入索引与参数，再 `DrawMeshInstancedIndirect`。正常帧不读回数量来决定是否绘制。

LOD 淡化只有 `Temporal` 与 `Distance`。时间窗从已提交的 stable 档逐档追赶，视点不连续时硬切。距离权重与索引同槽，不写入矩阵或包围盒。阴影共用所属相机的 LOD 状态，并按级联可见位过滤。

`treeOcclusion` 是遮挡的保存值。层次 Z 缓冲只在 GPU 后端生效；关闭或视点不连续时清除有效状态。投影与构建该缓冲时的 `TextureUVOrigin` 一致。

GPU 资源使用 `ComputeBuffer`。实例球谐和法线变换集中在 `Shader/Foliage/Include/Foliage.hlsl`。`FOLIAGE_LOD_WEIGHT` 与 `FOLIAGE_GPU_TEMPORAL` 只在对应淡化桶绘制期间打开。

`Landscape/Grass` 中的 `SetupNatureRenderer`、`_NatureRendererBuffer`、`_NatureRendererDistanceControl` 和 `NatureRendererInstancing` 保留在现有 Pass 上。运行时草绘制继续使用 `_GrassElementBuffer`。

风子系统保持现有文件版式。`WindSettings` 的 Gust 与 Shiver 序列化别名保留。

已有公开拼写保持不变：`OnRegiste`、`UnRegiste`、`sectionIndexs`、`CaculateBoundRadius`、`CaculateWorldBound`、`treeTransfroms`、`lODInfos`。

几何类型保持 `Aabb`、`FrustumPlane`、`BoundSphere`、`InstanceTransform`、`FoliageMesh`，避免与引擎同名类型冲突。

## C# 版式

草、树、渲染器和 `MeshPipelineJob` 使用下列版式。`WindComponent.cs`、`WindSettings.cs` 及其编辑器脚本保持两空格和现有属性写法。

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public unsafe void Cull(CommandBuffer cmdBuffer, in FoliageView view, in NativeList<JobHandle> taskHandles)
{
    var treeCullingJob = new TreeCullLodJob();
    {
        treeCullingJob.planes = view.planes;
        treeCullingJob.viewOrigin = view.origin;
    }
    taskHandles.Add(treeCullingJob.Schedule(m_ChunkMasks.Length, 4));
}
```

- 四空格。类型和方法的左大括号单独成行。
- 热路径标记 `AggressiveInlining`。公开的只读参数使用 `in`。
- 私有字段使用 `m_` 前缀。
- `GrassShaderID` 与 `TreeShaderID` 直接调用 `Shader.PropertyToID`。
- 内部辅助类型不标记 `sealed`。热路径函数保持 `unsafe` 与指针。
- `FoliageRenderer` 保留现有 `#region`。
- 属性访问器写成 `get { return ...; }`。
- 不添加 XML 文档，也不为输入层层嵌套空判断。
- `if (` 与 `foreach (` 的空格跟随所在文件的既有写法。

Job 字段在赋值块中写入。

## 禁止

- 未经明确要求，把截图、录屏、Game 画面、渲染对照图及其 `.meta` 加入 Git、Git LFS 或推送到远端。验证结论写入 `Docs/DELIVERY.md`；临时媒体放在仓库外，验证结束后删除。
- 为绕过体积限制而转换或压缩上述媒体。
- 新增 `DESIGN.md`，或用界面规范条款重写本包结构。
- 为类型名添加 `F` 前缀，或用 `FormerlySerializedAs` 迁移类型名。风参数上已经存在的字段别名除外。
- 让场景再保存整张密度图，或让全图缓冲与流式页同时成为运行路径。
- 把可见矩阵压缩成一次绘制来替代按桶提交。
- 把草页 scatter 放进共享 `taskHandles`，或为了绘制去等待未完成的 scatter。
- 用 `Task`、`async`、`Thread`、`Awaitable` 执行 scatter，或用它们包装热路径上的 `JobHandle`。
- 用计算着色器执行草 scatter。
- 在顶点着色器中查找 run 或掩码。
- 引入 `GraphicsBuffer`。
- 先写全量可见标记再扫描一遍来代替压缩。
- 恢复固定区间全图上传、顺序前缀计数或按空间格各持一份缓冲。
- 用颜色相机的可见索引绘制主光级联。
- 把屏幕占比迟滞放进实例矩阵或包围盒。
- 在 Play 模式把空的 `transforms` 写回场景。
- 新建测试程序集。断言留在现有 Runtime。
- 修改着色器资源路径、`package.json` 的 `name`，或删除风参数的序列化别名。
- 删除 `DummyFoliageShaders.cs`。
- 增加内部 BVH、草网格 LOD、运行时可变的 `numSection`、跨格双归属、草的 GPU 撒点、由顶点着色器寻址 run，或整场景 GPU 驱动的第二套路径。

## 每步核对

1. 对照架构说明和本文件。
2. 查看改动文件的诊断。
3. 用现有 Managed DLL 做编译检查。运行画面、Frame Debugger 和日志只在已经打开的 Editor 中采集。缺少哪一项，就在交付记录里把哪一项写成未验收。
4. 检索 `F` 前缀类型、场景密度图路径、`DESIGN.md` 和 `GraphicsBuffer`。
5. 核对失败则先修正，再进入下一步。
6. 按文首规则更新已有的 `AGENTS.md`、`DESIGN.md`、`ARCHITECTURE.md`，并控制字数。
