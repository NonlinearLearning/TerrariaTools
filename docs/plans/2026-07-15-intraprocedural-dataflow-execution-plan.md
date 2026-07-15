# 方法内数据流精度与成本执行提案

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 在保持节点、边、规则结果和 DOP 图快照稳定的前提下，将方法内数据流变为可预算、可测量的 def/use overlay，并减少大方法的重复工作。

**Architecture:** 保持当前 worker 只读候选、调用线程稳定提交的边界。每个方法先冻结 operation、CFG 邻接、definition seed 和 ownership 快照；分析只消费该 `MethodDataFlowPlan`。超限方法发布结构化降级结果，调用方显式看到原因。

**Tech Stack:** C# / .NET 10、Roslyn `IOperation` 与 `ControlFlowGraph`、现有 `DataFlowPass`、xUnit；不新增依赖。

---

## 现状与边界

- [DataFlowPass](../../src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs) 已将 method 的 operation tree 转为 `UsedFactRecord` 并按 method 分区，随后构建 CFG-sensitive plan。
- 现有 `RoslynCpgDataFlowPassTelemetry` 已包含 preparation、fact collection、fixpoint、candidate commit 等指标；本提案补齐预算与降级证据。
- 完整图的 node ID、edge identity、稳定排序、单写者物化和 DOP `1/8/12/14/16` 等价是不可突破的约束。
- 范围只包括单 compilation、方法内 local/parameter/member-family flow。跨调用边界由跨过程提案负责。

## 目标契约

```csharp
public sealed record RoslynCpgDataFlowOptions(
    int MaxDefinitionsPerMethod = 4000,
    int MaxFlowNodesPerMethod = 20000,
    int MaxCandidateEdgesPerMethod = 100000,
    RoslynCpgDataFlowOverflowBehavior OverflowBehavior = RoslynCpgDataFlowOverflowBehavior.SkipMethod);

public enum RoslynCpgDataFlowOverflowBehavior { SkipMethod, FailBuild }
```

`SkipMethod` 不产生该方法的 DataFlow 边，并在 telemetry 中以 method full name、超过的预算和原因记录；默认值只在基准和规则等价通过后启用。

## Joern 对照后的具体执行蓝图

Joern 的 `ReachingDefPass` 对每个 method 创建分析问题，在 fixpoint 前累计 transfer `GEN` 集中的 definitions，超过 4000 时跳过整个方法；求解后才由 DDG generator 为真实 use 物化边。对应源码：`joern-master/dataflowengineoss/.../ReachingDefPass.scala:14-51`、`ReachingDefProblem.scala:22-65,167-300`、`DdgGenerator.scala:81-168`。

本实现采用以下不可变 worker 输入，避免把 Roslyn symbol 判断降级为 Joern 的名称/代码字符串比较：

```csharp
internal sealed record MethodFlowSnapshot(
    string MethodNodeId,
    ImmutableArray<string> NodeIdsInReversePostOrder,
    ImmutableArray<int[]> PredecessorOrdinals,
    ImmutableArray<int[]> SuccessorOrdinals,
    ImmutableArray<DefinitionFact> DefinitionFacts,
    ImmutableArray<ImmutableBitArray> Gen,
    ImmutableArray<ImmutableBitArray> Kill,
    ImmutableArray<UsedFactRecord> Uses,
    ImmutableArray<int> UnreachableNodeOrdinals);
```

`DefinitionFact` 的 identity 继续以 Roslyn `ISymbol` / receiver-root / access-path key 为准。worker 执行 `OUT[n] = GEN[n] ∪ (IN[n] − KILL[n])`，`IN[n]` 为 predecessor `OUT` 的 union；直到 worklist 无变化。输出仅为 `(sourceNodeId, targetNodeId, DataFlow)` 候选，主线程按 method order、source ID、target ID 提交。

## Task 1：锁定现有方法内 flow 契约

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`

1. 写包含 assignment、分支 merge、loop、parameter、property/indexer accessor、return 的完整边快照测试。
2. 运行 `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests`；记录基线快照。
3. 为同一源在 DOP `1/8/12/14/16` 断言排序后 nodes/edges 完全相等。
4. 增加 invalid-flow 回归：控制结构、jump target、return shell、unknown operation 不能作为独立 DataFlow 端点；保留现有 operation/syntax 边。

## Task 2：显式化方法计划和预算

**Files:**
- Create: `src/MinimalRoslynCpg/Builder/Passes/MethodDataFlowPlan.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 先写失败测试：超过 definitions、flow nodes、candidate edges 时，`SkipMethod` 只跳过该方法并报告原因；`FailBuild` 抛出含方法稳定名的异常。
2. 在 `RoslynCpgBuilderOptions` 增加可选 DataFlow options，默认保留当前无限制行为。
3. 将 operation IDs、owner、CFG 邻接、parameter definition facts、return/exit nodes 收敛到不可变 `MethodDataFlowPlan`；禁止 worker 读取 builder 可变 cache。
4. 运行上述定向测试；确认默认 options 的完整图快照不变。
5. 将 budget 检查置于 fixpoint 之前：definitions 为 `GEN` cardinality 总和，flow nodes/candidate edges 为 snapshot 数量；禁止先计算再丢弃结果。

## Task 3：压缩 def/use 收集和 fixpoint 输入

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败测试：嵌套 invocation/binary/assignment 的每个 operation 只建立一个 `UsedFactRecord`，其聚合 facts 与基线边相等。
2. 用后序单遍遍历建立 direct facts 与 child-record 引用；禁止任何 candidate 阶段再次枚举 `DescendantsAndSelf()`。
3. 将 reaching-definition worklist 的输入限制为 method plan 内的 nodes；候选以稳定 method order 返回、调用线程提交。
4. 运行定向 DataFlow、property flow、call/return flow 测试和 DOP 快照测试。
5. 增加 RPO worklist 测试：直线 assignment、if merge、while loop、不可达 CFG block、field reassign kill、single-use identifier；断言边集和 `IterationCount` 有确定性。

## Task 4：补全可操作遥测与真实基准

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Modify: `src/Host/RuntimeMetricsLog.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 先写 telemetry 测试：method count、definition count、skipped count、各 overflow 原因、flow-node/candidate-edge 上限均可读且稳定排序。
2. 记录 wall-clock、allocated bytes、Gen2、method count、operation count、definitions、flow nodes、candidate edges；累计 worker 时间单独命名。
3. 每方法记录 `DefinitionCount`、`FixpointIterations`、`UnreachableNodeCount`、`GeneratedCandidateCount`、`SkipReason`；telemetry 数组按 method full name/ID 排序。
4. 对同一大源预热后跑三次 DOP 1 与目标 DOP，比较中位数；源缺失时只记录缺口。

## 验收与停止条件

- 默认 options 下完整 graph、rule output、rewrite 与 DOP 快照相等。
- 每个降级方法都有确定性原因和 telemetry，不产生半套边。
- 任何图边差异、跨 method 泄漏或 worker 写图都是停止条件。
- 基准同时证明目标 workload 未回归后，才允许启用有限预算默认值。

## 提交边界

每个 Task 单独提交。提交消息遵循仓库 Lore trailers，至少记录 `Constraint`、`Confidence`、`Scope-risk`、`Tested` 与 `Not-tested`。
