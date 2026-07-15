# 图查询与切片引擎执行提案

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 将冻结 CPG 上的局部查询、结构连接和反向切片统一为预算化、确定性、低分配的查询引擎，并移除规则侧的全图重复扫描。

**Architecture:** 图完成后一次发布只读索引；查询只读该快照。每个 traversal state 使用 parent pointer，结果物化时才重建路径。查询 cache 的 key 包含图版本、方向、边种类、起止节点和全部预算。

**Tech Stack:** C# / .NET 10、现有 `RoslynCpgGraphIndex`、`RoslynCpgSliceQuery`、`MarkAnalysisSnapshot`、xUnit；不新增依赖。

---

## 现状与边界

- `RoslynCpgGraphIndex` 已提供 stable incoming/outgoing adjacency 与 `edgesByKind`。
- `RoslynCpgSliceQuery` 仍在每状态执行 `Where(...).ToArray()`，并以 `Append(...).ToArray()` 复制路径；`MaxCallDepth` 只被验证，尚未参与单过程查询行为。
- `RoslynCpgStructureViewBuilder` 会对 fragment pair 做无界全图无向 BFS，随后扫描全部节点和边；本提案先保留其语义基线，再逐条试点替换。
- 范围包含单 compilation 的 query engine 和索引；跨过程 state 属于跨过程数据流提案。

## 目标契约

```csharp
public sealed record RoslynCpgTraversalBudget(
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxVisitedNodes,
    int MaxVisitedEdges);

public sealed record RoslynCpgQueryTelemetry(
    long VisitedNodeCount,
    long VisitedEdgeCount,
    long MaterializedPathCount,
    bool WasTruncated,
    string? TruncationReason,
    long CacheHitCount,
    long CacheMissCount);
```

预算耗尽必须返回已排序的部分结果与唯一截断原因；禁止静默截断和依赖 HashSet 枚举顺序。

## Joern 对照后的具体执行蓝图

Joern 的 `Engine` 从每个 sink 创建 task，`TaskSolver` 做方法内反向扩展，结果表按 `(sink, callSiteStack, callDepth)` 缓存；重复 fingerprint 转为 held task，任务结束后统一补全并稳定去重。对应 `Engine.scala:29-129`、`TaskSolver.scala:82-123`、`TaskCreator.scala:15-28`。本计划只实现其单过程子集，禁止引入 Joern 的专用线程池。

```csharp
internal readonly record struct SliceStateKey(
    string GraphSnapshotVersion,
    string QuerySemanticFingerprint,
    string NodeId,
    RoslynCpgViewDirection Direction,
    int EdgeMaskId,
    int RemainingHops,
    int RemainingDefinitions);

internal sealed record SlicePathFragment(
    string NodeId,
    RoslynCpgEdgeKind? IncomingKind,
    SlicePathFragment? Next,
    bool IsTerminal);
```

`QuerySemanticFingerprint` 由已排序 edge kinds、所有预算、方向和查询算法版本组成。memo value 为 immutable fragment 或完整 terminal/truncation 结果；被预算截断的状态永远不能作为“完整”缓存命中。FIFO queue 保留 BFS hop 语义；每个 adjacency bucket 已按 graph ID 稳定排序，因此结果 materialization 后按 source/sink/path 进行最终排序。

## Task 1：锁定查询语义与性能基线

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

1. 写失败测试：环、多 source、edge-kind filter、hop/path/definition/node/edge 预算、冻结前拒绝查询、重复运行稳定排序。
2. 写 Structure View 基线：多 fragment 的 selected nodes/edges、无路径、共享节点和缓存命中。
3. 运行 `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~StructureViewBuilderTests"`。
4. 断言同一 query 的第二次执行只增加 cache hit；改变 edge mask、budget、graph snapshot version 时必须 miss。

## Task 2：扩展冻结索引为 QueryIndex v2

**Files:**
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

1. 先写失败测试：`(nodeId, direction, edgeKind)` bucket、`nodesByKind`、`symbol -> references`、`method -> owned callsites`、file/span 范围检索均稳定排序。
2. 冻结时从已有 nodes/edges 构造只读二级索引；严禁新增图边、改变 edge identity 或提前冻结。
3. 为范围索引定义半开 span `[start,end)` 与同 span 的 node-ID tie break。
4. 运行 graph/index 定向测试和 DOP graph snapshot 回归。
5. 建立 `EdgeMaskId`：冻结期将允许 edge-kind 集合规范化为 ordinal bit mask；超出 enum 位宽时使用排序 kind-name fingerprint，禁止依赖 HashSet identity。

## Task 3：重写切片执行状态

**Files:**
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQueryOptions.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`

1. 先写失败测试：同一结果路径序列和旧实现在所有边界形状相等，visited-node/edge 上限返回确定性截断。
2. 使用 edge-kind bucket 取邻接，移除每状态 LINQ filter/array allocation。
3. 用 `(nodeId, remainingHops)` visited state 与 parent-pointer 前驱表保存路径；仅在 sink/source terminal materialize node-ID sequence。
4. 将 query key 扩展为 graph snapshot version、方向、edge set、所有预算和起止节点；记录 hit/miss、visited、truncation telemetry。
5. 保持 `MaxCallDepth=0` 表示单过程；任何非零值在本计划阶段显式拒绝，避免产生未实现语义。
6. 按 Joern `PATH_CACHE_HITS/MISSES` 模式新增 local memo hit/miss；另记录 path materialization 数和每种 truncation 原因。

## Task 4：规则试点与 Structure View 收敛

**Files:**
- Modify: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `src/Rules/Implementations/Mark/DeleteUnreachableMethodRule.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. 先记录 Structure View 的 fragment 数、pair 数、visited nodes/edges、elapsed、cache hit/miss，并断言 telemetry 归属正确。
2. 以 feature flag 并行计算旧 connector 与预算化 connector；只在 nodes/edges 相同的固定规则试点切换。
3. 用 `method -> owned callsites` 和 `CallTargets` 取代 unreachable-method 的全 CallSite 扫描；保留 private-method/root 语义回归。
4. 规则输出、Mark、Decision、Rewrite 以及 DOP graph snapshot 均相等后，移除旧路径。
5. connector 试点必须使用明确白名单：syntax connector 只允许 `SyntaxChild/OpHasSyntax/OpChild`；数据依赖 connector 单独允许 `DataFlow/Cfg*`。禁止默认无向遍历全部 edge kind。

## Task 5：基准与迁移裁决

**Files:**
- Modify: `src/Host/RuntimeMetricsLog.cs`
- Modify: `progress.md`
- Modify: `feature_list.json`

1. 用同一输入、预热后至少三次独立运行，记录查询 wall-clock、allocation、visited nodes/edges、缓存命中和 Structure View pair 数。
2. 记录旧/new connector、旧/new unreachable-method 查询的中位数；规则语义相等是启用默认路径的前提。
3. 源文件缺失时记录基准缺口，保持 feature flag 默认关闭。

## 验收与停止条件

- 冻结后的查询不扫描全边集合重建邻接。
- 每次结果、路径、预算截断和 cache 统计可重现。
- Structure View 试点与既有规则结果完全相等。
- 任意二级索引导致冻结后 mutation、内存不可接受增长或 DOP 差异时停止。

## 提交边界

Task 2、3、4、5 各自独立提交；使用 Lore trailers 记录兼容性约束和真实基准证据。
