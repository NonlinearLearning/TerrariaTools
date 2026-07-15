# 跨过程数据流 Overlay 执行提案

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 为已解析的 C# 调用建立受预算、上下文受限、可解释的跨过程数据流 overlay，使删除规则能够从调用结果反向追到实参和返回定义。

**Architecture:** 复用现有 CallTargets、MethodParameter、MethodReturn、CallSite 和方法内 DataFlow。新增只读 `InterproceduralDataFlow` overlay，由调用边界候选在 worker 中生成、调用线程按稳定 method/callsite 顺序提交。查询以 `(nodeId, call-stack, remainingBudget)` 为状态，严格限制 call depth、definition、fan-out 与路径数。

**Tech Stack:** C# / .NET 10、Roslyn symbols/operations、`CallGraphPass`、`DataFlowPass`、`RoslynCpgSliceQuery`、xUnit；不新增依赖。

---

## 范围与保守性

- 第一阶段只处理有唯一、已解析 `CallTargets` 的普通方法、属性 getter/setter 和 indexer accessor。
- 支持三种桥接：actual argument → formal parameter、returned expression → `MethodReturn`、`MethodReturn` → call result。
- 递归、互调、dynamic、delegate、多候选虚调用、反射、async state machine、外部方法和别名/heap flow 均保持显式未覆盖状态。
- 默认关闭该 overlay；现有 `DataFlow` 边不改语义。

## 目标契约

```csharp
public sealed record RoslynCpgInterproceduralDataFlowOptions(
    int MaxCallDepth = 2,
    int MaxDefinitions = 4000,
    int MaxCallTargetsPerSite = 1,
    int MaxBoundaryEdgesPerMethod = 10000,
    RoslynCpgUnknownCallBehavior UnknownCallBehavior = RoslynCpgUnknownCallBehavior.Cut);

public enum RoslynCpgUnknownCallBehavior { Cut, TaintAllArgumentsAndReturn }
```

每条跨过程边记录 bridge kind 与 resolved target；`Cut` 产生 telemetry，`TaintAllArgumentsAndReturn` 仅用于显式要求 soundness 的规则，结果标记为低精度。

## Joern 对照后的具体执行蓝图

Joern 把方法内反向求解到达形参、内部调用结果或 output argument 的状态标为 partial；`TaskCreator` 再把它桥接到 caller argument、callee return 或 output parameter。task 创建时统一检查 call depth、重复 task stack、caller fan-out 和 output expansion。对应 `TaskSolver.scala:129-191`、`TaskCreator.scala:15-184`、`Engine.scala:29-129`。

MinimalRoslyn 的 query 不复刻 Joern 线程池，采用已有单次调用/稳定物化合同；跨过程状态为：

```csharp
internal readonly record struct InterproceduralSliceStateKey(
    string GraphSnapshotVersion,
    string SummaryFingerprint,
    string NodeId,
    ImmutableArray<string> CallSiteStack,
    int RemainingCallDepth,
    int RemainingHops,
    SliceValueRole ValueRole);

internal enum SliceValueRole { Normal, OutputArgument, MethodReturn }
```

反向 bridge 表：

| 到达状态 | 下一状态 | 预算/校验 |
|---|---|---|
| `MethodParameter` | 栈顶 callsite 的同 ordinal/named argument；空栈时由 parameter-to-callers 索引展开 | 消耗 depth；caller fan-out ≤ `MaxCallerFanout` |
| internal call result | 已解析 callee 的 `MethodReturn` 与 return expression | push callsite；唯一 target |
| output/ref-like argument | 对应 `MethodParameterOut` | `ValueRole=OutputArgument`，禁止混同普通 argument |
| external/stub call | `FlowSummary` 指定的 receiver/argument | summary fingerprint 必须进入 cache key |

同一 `NodeId` 在不同 `CallSiteStack` 下不可合并；重复 frame 立即以 `CallStackCycle` 截断。`MaxCallDepth=0` 禁止所有 bridge，`-1` 仅保留为内部调试选项，生产规则不得使用。

## Task 1：定义 schema 与基线

**Files:**
- Modify: `src/MinimalRoslynCpg/Contracts/RoslynCpgEdgeKind.cs`
- Modify: `src/MinimalRoslynCpg/Contracts/RoslynCpgCapability.cs`
- Modify: `src/MinimalRoslynCpg/docs/node-edge-catalog.md`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败测试：新增 `InterproceduralDataFlow` capability 自动要求 MethodModel、CallTargets、DataFlow 与 QueryIndex，默认 capability 集不含它。
2. 定义 `InterproceduralDataFlow` edge kind 与桥接标签：`ArgumentToParameter`、`ReturnToMethodReturn`、`MethodReturnToCallResult`。
3. 更新 catalog 的精度边界、未知调用策略和稳定排序契约。
4. 运行 schema/capability/DOP snapshot 定向测试。
5. capability plan 解析出 `InterproceduralDataFlow => DataFlow | CallTargets | MethodModel | QueryIndex`，并将执行 pass 名与 summary fingerprint 写入 build telemetry。

## Task 2：冻结调用边界计划

**Files:**
- Create: `src/MinimalRoslynCpg/Builder/Passes/InterproceduralDataFlowPass.cs`
- Create: `src/MinimalRoslynCpg/Builder/Passes/InterproceduralDataFlowPlan.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 先写失败测试：静态 method、instance method、overload、property getter/setter、indexer 各生成确定性 bridge；多 target 或 external 调用按 options 被记录为 cut。
2. 从已冻结 operation/callsite/call target/parameter/return 映射创建不可变 boundary plan；不得再次全图扫描或在 worker 中访问可变 builder cache。
3. 对每个 method 和 callsite 定义稳定顺序：caller method full name、source span、callsite ID、target full name、argument ordinal。
4. worker 只生成边候选和 diagnostics；调用线程按该顺序提交。
5. 计划中保存 `ArgumentOrdinal` 和 `ArgumentName`；Roslyn named argument 先绑定 parameter ordinal，再排序。禁止用语法 child order 替代 parameter identity。

## Task 3：方法内结果与调用边界组合

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/InterproceduralDataFlowPass.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败测试：`A(x) -> B(p) -> return p -> y` 的跨过程边加上既有方法内边，可从 `y` 回溯到 `x`。
2. 先提交已覆盖的 method-local DataFlow，再执行跨过程 pass；缺失 method-local facts 时记录 bridge skip，禁止伪造边。
3. 对 property/indexer 复用当前 accessor callsite 与 parameter/return nodes，避免创建第二套节点模型。
4. 用递归和 mutual recursion 测试确认建图阶段有限，不依赖 query call depth 停止。
5. bridge edge 只在 target 唯一且内部定义存在时生成；每个 cut 记录 `UnresolvedTarget`、`ExternalTarget`、`AmbiguousTarget` 或 `MissingIntraFacts`。

## Task 4：跨过程反向查询与预算

**Files:**
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQueryOptions.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`

1. 写失败测试：maxCallDepth `0/1/2`、递归环、多个 callsite、path/definition/fan-out 预算、稳定截断原因和 cache key 隔离。
2. 将 traversal state 扩为 `(nodeId, call-stack fingerprint, remainingCallDepth, remainingHops)`；只有跨 `InterproceduralDataFlow` 边时消耗 call depth。
3. call stack 使用稳定 target/callsite ID 序列，重复帧拒绝扩展；不得只按 node ID 去重而丢失上下文。
4. telemetry 记录边界展开数、cut/unknown 数、最大调用深度、cache hit/miss 和截断原因。
5. 建立两级缓存：query-local memo 保存 immutable path fragments；run-wide result table 按完整 state key 保存 terminal/truncated result。truncated fragment 不能复用为 complete result。
6. 容量限制单独建 `MaxCachedStates`；达到上限时停止写入 cache 并报告 `CacheCapacityBypass`，不得改变查询结果。

## Task 5：Flow summary registry 与规则试点

**Files:**
- Create: `src/MinimalRoslynCpg/Analysis/FlowSummaries/RoslynCpgFlowSummary.cs`
- Create: `src/MinimalRoslynCpg/Analysis/FlowSummaries/RoslynCpgDefaultFlowSummaries.cs`
- Modify: `src/Application/DeletionRulePipeline.cs`
- Modify: `src/Rules/Implementations/Propagate/DeleteClassSymbolReferencePropagationRule.cs`
- Test: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. 先写 summary 测试：argument ordinal 使用 Roslyn parameter ordinal；receiver 为 `0`、return 为 `-1`；未列出的流被 cut。
2. 第一批只加入已验证的内部 accessor 与少量 BCL pure-wrapper summary；禁止猜测 LINQ、reflection 和 mutable collection 流向。
3. 选一个 helper-return 删除规则作为 feature-flag 试点，旧 Roslyn 扫描与新 slice 同时计算并断言 Mark/Decision/Rewrite 相等。
4. `RuleDefinition.RequiredCapabilities` 仅为试点规则声明 `InterproceduralDataFlow`；其余规则保持当前能力，避免扩大建图成本。
5. `FlowSummary` 的 key 使用 assembly identity、containing metadata name、method name、generic arity、parameter ref-kind/type；优先级为 project override、framework summary、unknown-call policy。mapping 支持 `Receiver`、`Parameter(ordinal)`、`Return`、`PassThrough`、`Block`。
6. summary 先在 query bridge 阶段解释；主图物化 summary edge 仅在 overlay epoch 与 summary fingerprint 可分离后进入范围。

## Task 6：基准、精度与默认开关

**Files:**
- Modify: `src/Host/RuntimeMetricsLog.cs`
- Modify: `progress.md`
- Modify: `feature_list.json`

1. 预热后至少三次测量：intra-only、interproc enabled、规则试点 enabled；记录 wall-clock、allocation、edge count、bridge count、cuts、截断和 false-positive/false-negative 回归样例。
2. 同时运行完整 graph/DOP snapshots、规则管道回归与试点真实目录 dry-run。
3. 默认开关保持关闭，直到规则等价、预算安全和中位数无回归均有证据。
4. 测试矩阵必须包含：两个 caller 的同一 callee 不串线、depth `0/1/2`、129 caller 超过 fan-out、257 bridge task 超过 expansion、project summary 覆盖 framework summary、receiver/named/ref/out argument、图 epoch 或 summary version 改变导致 cache miss。

## 验收与停止条件

- 每条跨过程边可追溯到唯一 resolved call target 与 bridge label。
- maxCallDepth 只作用于跨过程边，递归查询必然终止。
- 默认 capability、原有 DataFlow 边和非试点规则结果不变。
- 多目标动态调用、未知 API、alias/heap flow 无法满足保守性时停止扩展并保留 cut telemetry。

## 提交边界

Schema、boundary plan、query、summary/试点、benchmark 分别提交；Lore trailers 必须说明 soundness/precision 选择、预算和未覆盖语言形状。
