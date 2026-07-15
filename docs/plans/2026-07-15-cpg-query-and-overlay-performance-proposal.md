# CPG 查询、数据流与 Overlay 性能优化执行提案

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 将删除规则从“反复扫描完整 CPG 和语法祖先链”逐步转为基于冻结图索引、受限切片、控制依赖和按需 capability 的查询，使大文件分析的 CPG 建图与规则分析耗时可测量地下降。

**Architecture:** 保持 `RoslynCpgGraph` 的单线程物化和既有稳定 ID/边去重语义。建图结束时冻结不可变索引；查询层只读取该快照。DataFlow 继续以 method-local 分区运行，但消除重复操作树扫描和 method×全图节点查找。CFG、dominance、CDG 作为可声明依赖的 overlay，只有需要它们的规则才触发构建。

**Tech Stack:** C# / .NET 10、Roslyn `IOperation` 与 `ControlFlowGraph`、现有 `RoslynCpgBuilder`、xUnit；不新增 NuGet 依赖。

---

## 依据与决策

- `RoslynCpgGraph.ExtractLocalView` 每次局部查询都扫描完整边集，重建出入邻接并再次筛边。重复的 rule-local 查询会放大为 `O(queryCount × edgeCount)`。
- `DataFlowPass` 对已包含整棵方法操作树的 ordered operations，再对每个 operation 枚举 `DescendantsAndSelf()` 收集 used facts；并且每个 method plan 都全量遍历 operation-node 映射。大方法存在重复扫描和大分配风险。
- 当前 CPG 已拥有 Syntax、Symbol/Type、Operation、Call、MemberAccess、简化 CFG、method-local reaching-definition DataFlow，以及 DOP 稳定图回归。不得重写这些已验证部分。
- Joern 的可借鉴点是：冻结图上的 traversal、sink 到 source 的 `reachableBy` / `reachableByFlows`、结果缓存与工作预算、以及 CFG → dominator → CDG 的 overlay 依赖链。其 C# frontend 不使用 Roslyn semantic model，不能作为本项目 schema 或前端实现的直接模板。

**Decision:** 按下列顺序实施：先索引和按需切片，再修 DataFlow 的重复工作，再加入 dominance/CDG，最后引入 capability/overlay 调度。跨过程、上下文敏感数据流只有在某条规则有明确需求和基准数据时才进入范围。

## 兼容性与安全约束

- `RoslynCpgGraph`、builder 缓存、节点序号与边去重仍由稳定调用线程独占写入；查询快照只读。
- 现有 node ID、edge identity、导出顺序、规则决策和 rewrite 结果必须保持不变。
- 不把 `RoslynCpgGraph` 改为并发集合，不为图写入加锁，不建立新的专用线程池。
- 索引只能在图物化完成后发布；构建失败或取消时不得发布半成品索引。
- 查询必须有 `maxHops`、`maxPaths`、`maxDefinitions` 和可选 `maxCallDepth` 预算；预算耗尽是结构化结果，不是静默截断。
- 每个阶段先写回归测试，再写最小实现；任何 DOP `1/8/12/14/16` 图快照差异都是停止条件。
- 本提案与 `stable-graph-commit-parallelization`、`threadpool-memory-execution` 并行存在。实施时以它们已经落地的 single-writer / bounded-window 合同为基线，不回退其行为。

## 目标查询契约

新增内部只读服务 `RoslynCpgSliceQuery`，不向 CLI 暴露通用 DSL。

```csharp
internal sealed record RoslynCpgSliceQueryOptions(
    IReadOnlySet<RoslynCpgEdgeKind> AllowedEdgeKinds,
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxCallDepth = 0);

internal sealed record RoslynCpgSlicePath(
    string SourceNodeId,
    string SinkNodeId,
    IReadOnlyList<string> NodeIds);

internal sealed record RoslynCpgSliceResult(
    IReadOnlyList<RoslynCpgSlicePath> Paths,
    bool WasTruncated,
    string? TruncationReason,
    long VisitedNodeCount,
    long VisitedEdgeCount);
```

第一版仅支持单 compilation 内、无调用栈的反向 traversal：从 sink 沿 `DataFlow`、`Reference`、`Argument`、`Call` 等明确允许的边回溯 source。路径以 node ID 序列排序和去重，避免依赖 `HashSet` 枚举顺序。跨过程调用栈、field-sensitive 和 alias-sensitive 逻辑不在第一版实现。

## Task 1：冻结邻接、边类型与常用实体索引

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/GraphAnalyzerTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败测试：同一图在 finalize 前不提供查询快照；finalize 后按 node ID 查入/出边、按 edge kind 查边的结果稳定。
2. 写失败测试：`ExtractLocalView` 在索引存在时和现有 BFS 的 nodes、edges、方向、hop 限制结果完全相同。
3. 新增内部不可变 `RoslynCpgGraphIndex`：`outgoingByNodeId`、`incomingByNodeId`、`edgesByKind`，并保留确定性排序。
4. 在 builder 全部 pass 成功后调用 `FreezeQueryIndex()`；图 mutation 方法在冻结后拒绝写入，防止索引失效。
5. 将 `ExtractLocalView` 改为消费 index，删除每次调用的全边集邻接重建；保留原公开 API。
6. 运行 focused tests 和完整 DOP 图快照。记录 index 创建耗时、索引边数和 local-view visited-edge 数，不将其与累计 worker CPU 时间混淆。

## Task 2：按需反向切片查询与规则侧试点

**Files:**

- Create: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Create: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQueryOptions.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/GraphAnalyzerTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. 写失败测试：给定 assignment、return、method parameter 和 property flow，查询从 sink 反向返回期望 source 与稳定路径。
2. 写失败测试：环路、多个 source、超出 hop/path/definition 预算时结果稳定，并显式标识截断原因。
3. 以冻结索引实现非递归反向 BFS/DFS；visited key 在第一版为 `(nodeId, remainingHops)`，结果按 `(sourceId, sinkId, nodeId sequence)` 排序去重。
4. 将 query 实例放入 run-scoped `MarkAnalysisSnapshot`，使同一分析内相同 query key 复用结果；缓存 key 必须包含 source/sink、允许边集和全部预算。
5. 选择一个现有、只读且可精确断言的规则辅助路径作为试点，例如结构 view 的 connector 发现；先以 feature flag 或 internal path 与原实现并行比较，确认相等后再切换。
6. 验证 rule output、mark、decision、rewrite 和 DOP 图快照均未变化；添加 query cache hit/miss、visited nodes/edges、budget truncation telemetry。

## Task 3：消除 DataFlow 的重复 used-fact 与 method×全图索引扫描

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败测试：嵌套 binary/invocation/assignment operation 树的 DataFlow 边快照与现有基线相同，同时 telemetry 显示每个 operation fact 不被重复从 descendants 重算。
2. 将 `UsedFacts` 改为 method-local 单次操作树遍历：为每个 operation 建立可复用的 def/use 记录，父 operation 通过子记录组合，不再独立重扫 descendants。
3. 在 DataFlow 前冻结一次全局 `IOperation -> node ID` 与 method ownership 映射；method plan 只投影自身操作，禁止每个 plan 遍历全部 `_operationNodes`。
4. 将 CFG 邻接、operation-node IDs、definition seeds 组合成只读 method plan；保留当前 worker 只读 candidate + 调用线程稳定 commit 的边界。
5. 为 preparation、fact collection、reaching-definition、edge matching 分别写真实 elapsed telemetry；删除或实现当前永远为零的 preparation/neighbor 指标，不能继续只断言 `>= 0`。
6. 运行 DataFlow、Call/property flow、DOP `1/8/12/14/16` 完整图等价测试。对可用的大源文件记录 wall-clock、allocation、Gen2、method count、operation count 与每阶段耗时。

## Task 4：以 Roslyn CFG 构建 dominance、post-dominance 和 CDG overlay

**Files:**

- Create: `src/MinimalRoslynCpg/Builder/Passes/DominancePass.cs`
- Create: `src/MinimalRoslynCpg/Builder/Passes/ControlDependencePass.cs`
- Modify: `src/MinimalRoslynCpg/Contracts/RoslynCpgEdgeKind.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/docs/node-edge-catalog.md`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

1. 扩展 edge kind 时先写 schema 回归：增加 `Dominates`、`PostDominates`、`ControlDependence` 前，确认现有序列化、比较和 local-view 过滤对新 kind 保持稳定。
2. 从 Roslyn `ControlFlowGraph.BasicBlocks` 提取每个 method 的 block predecessor/successor；建立 operation 与 basic-block 的稳定映射。遇到无法映射的 synthetic block 只保留 block-level facts，不伪造 syntax node。
3. 写 if/else、while、for、switch、try/finally、return 的 dominator/post-dominator 测试。算法使用 reverse-postorder worklist；每个 method 独立计算。
4. 从 post-dominator frontier 推导 CDG；写 “条件控制语句 / 分支内语句 / finally” 的控制依赖边回归。
5. 在图上增加只读 query helpers：`Controls`、`ControlledBy`、`Dominates`、`PostDominates`；它们消费 Task 1 索引，不扫描完整边集。
6. 用一个逻辑条件或 if-structure 删除规则替换一次 syntax ancestor 回溯，先断言旧/new 规则产生相同 payload 和 rewrite，再移除重复扫描。
7. 跑完整图快照和规则管道回归；新 overlay 必须默认关闭，直到 capability 调度进入下一任务。

## Task 5：引入 capability/overlay 调度并按规则需求构图

**Files:**

- Create: `src/MinimalRoslynCpg/Contracts/RoslynCpgCapability.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleRegistry.cs`
- Modify: `src/Application/DeletionApplicationService.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 定义最小 capability 集：`SyntaxSemantic`、`MethodModel`、`CallTargets`、`Cfg`、`DataFlow`、`Dominance`、`ControlDependence`、`QueryIndex`。每个 pass 声明 requires/provides。
2. 写失败测试：只需要声明/符号事实的规则不执行 DataFlow；需要 structure query 的规则获得 QueryIndex；需要控制依赖的规则自动拉起 Cfg、Dominance、CDG。
3. `RoslynCpgBuilder` 依据有向依赖图选择 pass；默认兼容模式仍构建今天的完整能力集，避免意外改变现有调用方。
4. `RuleRegistry` 提供规则集合所需 capability 的确定性并集；`DeletionApplicationService` 在 build 前计算并传入该并集。
5. 为每个 skipped/executed pass、capability resolution 和 graph snapshot version 记录 telemetry。无效 capability 组合在构建前失败并说明缺失依赖。
6. 使用只声明型规则集、当前默认规则集、需要 CDG 的规则集分别跑管道回归；确认输出相等且只声明型规则跳过昂贵 pass。

## Task 6：基准、迁移判定与文档同步

**Files:**

- Modify: `progress.md`
- Modify: `feature_list.json`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 建立固定 benchmark helper：同一源文件、同一 SDK、预热后至少三次独立运行；输出 wall-clock、allocated bytes、Gen2、node/edge count、每 pass elapsed、query telemetry。
2. 分别比较：当前基线、索引+slice、DataFlow 去重、CDG overlay、按需 capability。不能只报告累计 worker duration。
3. 对 Terraria 或等量可复用大源集记录 WorldGen、Projectile、Main、Player 的中位数；源不存在时记录缺口，不能用小样本宣称收益。
4. 只有同时满足“规则/图等价”和“目标 workload 中位数不回归”时，才把试点查询或 capability 模式设为默认。
5. 更新 `progress.md` 与 `feature_list.json`：事实、未完成 benchmark、默认开关、已知风险。同步 quick start/developer guide 中的 capability 与 telemetry 使用说明。

## 验收标准

1. 同一冻结图上的 local-view 与 slice 查询不再为每次调用全量扫描 `_edges` 建邻接。
2. Slice 结果、路径、预算截断和缓存命中可复现；同一输入在重复运行和 DOP `1/8/12/14/16` 下稳定。
3. DataFlow 不再对每个 ordered operation 重扫嵌套 descendants，也不再为每个 method plan 遍历全局 operation map。
4. Dominance、post-dominance 与 CDG 能覆盖条件控制、循环、switch、try/finally 和 return 的测试形状。
5. 声明型规则集能跳过 DataFlow；依赖 CDG 的规则自动获得其完整依赖链。
6. 任何并行 worker 都不写图、builder cache 或 capability 状态；所有图 mutation 仍在稳定调用线程发生。
7. 所有变更通过 build、针对性测试、完整 DOP 图快照及三次独立大源基准；默认行为只在基准证明无回归后调整。

## 风险与停止条件

| 风险 | 控制与停止条件 |
| --- | --- |
| 冻结索引与后续图 mutation 脱节 | 在冻结后拒绝 mutation；任何 builder pass 需要继续写入时不得提前发布索引。 |
| Slice 路径在循环或多分支中指数增长 | 强制 hop/path/definition 预算，返回明确截断原因；不得无预算枚举所有路径。 |
| DataFlow 去重改变隐式/嵌套 operation 边 | 先锁定完整 DataFlow 边快照；任意差异停止迁移。 |
| Roslyn CFG 与现有手写 CFG 的语义差异 | 以 overlay 形式并存并比较；不删除旧 CFG，直到分支、finally、break/continue 兼容性有回归证据。 |
| capability 裁剪漏掉某条规则的隐式依赖 | 规则 capability 声明由测试覆盖；缺失依赖在 build 前显式失败。 |
| 索引内存抵消查询收益 | 基准同时记录 allocation 与 peak working set；内存回归时只保留按需索引或压缩索引。 |
| Joern 模式被错误地直接移植 | 保持 Roslyn 语义模型、当前 node ID 和单写者图合同；只借鉴 overlay、预算、缓存和 traversal 设计。 |
