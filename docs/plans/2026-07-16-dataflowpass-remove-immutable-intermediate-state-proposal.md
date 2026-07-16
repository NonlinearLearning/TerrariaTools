# `DataFlowPass` 去除 immutable 中间态以降低巨型文件瞬时内存峰值提案

**Goal:** 降低 `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs` 在 `NPC.cs`、`Player.cs` 这类超大文件上的瞬时分配和峰值保留内存，重点清理 `ToImmutableArray`、`ToImmutableDictionary`、多次 `ToArray` / `Distinct` / `ToDictionary` 造成的方法级中间态复制。

**Scope:** 只优化 `DataFlowPass` 及其直接依赖的数据流工作窗口/计划结构；不改规则语义、不改图边语义、不改默认 DOP、不把 capability 裁剪或目录级释放策略混进本提案。

**Non-goals:**

- 不改变 `DataFlow` 边结果
- 不改变 `InterproceduralDataFlow` 是否启用
- 不改 `RuleContext`、`DecisionModel`、`StructureView` 契约
- 不把本提案扩展成“所有大文件内存问题总修复”

---

## 1. 问题陈述

当前 `DataFlowPass` 已经避免了 worker 直接写图，但方法级计划仍然保留了大量不可变快照：

- `MethodDataFlowPlan.OrderedOperations: ImmutableArray<IOperation>`
- `MethodDataFlowPlan.OperationNodes: ImmutableArray<RoslynCpgNode>`
- `MethodDataFlowPlan.UsedFactsByOperation: ImmutableDictionary<...>`
- `MethodDataFlowPlan.ParameterDefinitionFacts: ImmutableDictionary<...>`
- `MethodDataFlowPlan.NodesByLegacyId: ImmutableDictionary<...>`
- `MethodDataFlowPlan.OperationNodesByOperation: ImmutableDictionary<...>`
- `MethodDataFlowPlan.Predecessors/Successors: ImmutableDictionary<..., ImmutableArray<...>>`

同时，构建这些 plan 前后还有多轮 materialization：

- `DescendantsAndSelf().ToList()`
- `DirectUsedFacts(...).ToArray()`
- `ChildOperations ... .ToArray()`
- `Values.Distinct().ToArray()`
- `ToDictionary(...).ToImmutableDictionary(...)`
- `pair.Value.ToArray()`

这在普通文件上问题不大，但在巨型文件上会放大成显著峰值：

- `NPC.cs`：`cpg-build=658547 ms`，`2108977 nodes / 5322409 edges`
- `Player.cs`：`cpg-build=247724 ms`，且规则阶段还会继续叠加内存

结论：当前 `DataFlowPass` 的主要问题不是算法错误，而是“为了稳定和只读性，保留了过多一次性不可变复制”，导致方法级中间态在 commit 前持续占用内存。

---

## 2. 设计目标

优化后应满足：

1. worker 仍然只读，不直接修改 `RoslynCpgGraph`
2. `DataFlow` 边集合、排序和 telemetry 语义不变
3. 方法级计划只保留执行所需的最小只读结构，不再为“防御性不可变”做整套复制
4. 每个方法的候选边、`in/out sets`、邻接快照在 commit 后尽快释放
5. 大文件上 peak private bytes 和/或 managed heap peak 至少出现可测下降，哪怕 wall-clock 只持平

---

## 3. 候选方案

### 方案 A：把 `Immutable*` 全部替换成数组和 `Dictionary`

做法：

- `MethodDataFlowPlan` 改成只持有 `IOperation[]`、`RoslynCpgNode[]`、`Dictionary`、`List`
- 保留现有“两阶段：先 plan，再并行算，再顺序 commit”的总体形状

优点：

- 改动集中
- 低风险
- 直接减少 immutable 包装和复制

缺点：

- 仍会保留完整 plan 列表直到所有方法 commit 完
- 只能解决“复制过多”，不能解决“生命周期过长”

### 方案 B：保留两阶段窗口，但把 `MethodDataFlowPlan` 收缩成轻量可释放结构

做法：

- `UsedFactPartition` 保留数组/字典，不再转 immutable
- `MethodDataFlowPlan` 只保留：
  - `IOperation[] OrderedOperations`
  - `RoslynCpgNode[] FlowNodes`
  - `Dictionary<IOperation, RoslynCpgNode>`
  - `Dictionary<RoslynCpgNode, DefinitionFact>`
  - `Dictionary<RoslynCpgNode, RoslynCpgNode[]>` 或等价邻接数组
- `CfgSensitivePartition` commit 完即让大对象出作用域
- 避免 `Values.Distinct().ToArray()` 这类二次去重复制，直接在建 plan 时维持唯一 flow-node 列表

优点：

- 能同时降低复制成本和方法级滞留成本
- 仍保留现有 ordered window / telemetry / build safety 形状
- 风险可控，适合当前仓库

缺点：

- 需要重写 `BuildCfgSensitivePartitionPlans` 的多个辅助结构
- 需要更仔细地验证“结果不变、只是容器变”

### 方案 C：取消完整 plan 列表，改成“按方法即算即提交通道”

做法：

- method block 一进入窗口就生成最小输入
- worker 算完后立刻顺序 commit
- commit 后立即释放该方法全部状态

优点：

- 理论内存最好

缺点：

- 改执行形状最大
- 更容易破坏 telemetry 和 ordered commit 行为
- 本轮风险偏高

**Recommendation:** 采用 **方案 B**。它能明显降低分配和滞留，但不需要重写整个数据流执行模型。

---

## 4. 目标设计

### 4.1 结构收口

把 `MethodDataFlowPlan` 从 immutable 快照容器改成轻量 record/class：

```csharp
private sealed record MethodDataFlowPlan(
  int Order,
  string MethodFullName,
  IOperation[] OrderedOperations,
  RoslynCpgNode[] FlowNodes,
  RoslynCpgNode[] OperationNodes,
  Dictionary<IOperation, UsedFactRecord> UsedFactsByOperation,
  Dictionary<IOperation, RoslynCpgNode> OperationNodesByOperation,
  Dictionary<RoslynCpgNode, DefinitionFact> ParameterDefinitionFacts,
  Dictionary<RoslynCpgNode, RoslynCpgNode[]> Predecessors,
  Dictionary<RoslynCpgNode, RoslynCpgNode[]> Successors,
  RoslynCpgNode? ReturnNode,
  RoslynCpgNode? ExitNode);
```

关键点：

- 删除所有 `ImmutableArray` / `ImmutableDictionary`
- 删除 `NodesByLegacyId`；commit 只需要直接节点引用
- `FlowNodes` 在建 plan 时一次性唯一化，后续不再 `Distinct()`

### 4.2 建 plan 时避免重复 materialize

在 `BuildCfgSensitivePartitionPlans(...)` 中：

- `partition.OrderedOperations` 改为数组，不再反复 `ToImmutableArray()`
- `operationNodes` 改为数组
- `parameterNodes + operationNodes + return/exit` 直接填入预分配 `List<RoslynCpgNode>`，最后一次转数组
- 建邻接时直接输出 `Dictionary<RoslynCpgNode, RoslynCpgNode[]>`
- 去掉：
  - `nodesByLegacyId.ToImmutableDictionary(...)`
  - `operationNodesByOperation.ToImmutableDictionary(...)`
  - `predecessors.ToImmutableDictionary(... ToImmutableArray())`
  - `successors.ToImmutableDictionary(... ToImmutableArray())`

### 4.3 worker 分析阶段不再做二次唯一化

在 `AnalyzeCfgSensitivePartition(...)` 中：

- `flowNodes = plan.FlowNodes`
- `inSets/outSets` 直接按 `FlowNodes.Length` 初始化
- `CountUnreachableNodes(...)` 直接遍历 `plan.FlowNodes`
- 不再使用 `plan.NodesByLegacyId.Values.Distinct()`

如有需要，可增加节点到 ordinal 的局部映射：

```csharp
Dictionary<RoslynCpgNode, int> flowNodeOrdinals
```

这能让 `inSets/outSets/predecessors/successors` 改成按索引数组访问，进一步减少字典开销；但如果第一轮风险控制优先，可以先保留字典 keyed by node。

### 4.4 生命周期控制

保持 `BoundedPartitionWorkWindow.RunOrdered(...)` 不变，但要求：

- `CfgSensitivePartition` commit 后不再被任何集合持有
- `MethodDataFlowPlan` 不保留多余节点查找表
- 大数组只在方法粒度上存活，不在 pass 结束前额外挂载 immutable 副本

如果第一轮完成后峰值下降不明显，再追加第二轮：

- 把 `CfgSensitivePartition.Edges` 从 `List<DataFlowEdgeCandidate>` 改成数组池或分块列表
- 把 `UsedFactRecord.ChildRecords` 从数组改为切片视图

---

## 5. 实施步骤

### Phase 0：先锁住回归面

**Files:**

- Read/verify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

**Goal:**

- 明确当前 `DataFlow` / `InterproceduralDataFlow` / delete pipeline 回归入口

### Phase 1：替换 `MethodDataFlowPlan` immutable 容器

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`

**Changes:**

- `MethodDataFlowPlan` 改为数组 + `Dictionary`
- 删除 `NodesByLegacyId`
- 删除 `ToImmutableArray` / `ToImmutableDictionary`

### Phase 2：去掉二次复制和二次去重

**Files:**

- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`

**Changes:**

- `BuildCfgSensitivePartitionPlans` 中一次性生成唯一 `FlowNodes`
- `AnalyzeCfgSensitivePartition` / `CountUnreachableNodes` 改成直接走 `FlowNodes`
- 删除 `Distinct().ToArray()` 这类后置复制

### Phase 3：验证峰值与结果

**Files:**

- Modify: `progress.md`
- Modify: `feature_list.json` only if this work becomes a tracked feature or sub-feature

**Changes:**

- 记录新旧日志对比
- 明确内存收益是否成立

---

## 6. 验证命令

先跑 focused：

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests"
```

再跑全量：

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
```

最后跑真实源码对比：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff --runtime-metrics-log .\Build\dataflow-lite-runtime-<stamp>.jsonl --per-file-memory-diagnostics-log .\Build\dataflow-lite-memory-<stamp>.jsonl --per-file-phase-timing-log-directory .\Build\dataflow-lite-phases-<stamp>
```

重点比较：

- total wall-clock
- runtime peak managed heap
- runtime peak working set
- `NPC.cs` / `Player.cs` 的 `privateBytes`
- `NPC.cs` / `Player.cs` 的 `cpg-build`
- 图节点数/边数是否完全一致

---

## 7. 完成条件

- `DataFlowPass` 不再持有方法级 immutable 快照副本
- `MethodDataFlowPlan` 和邻接快照只保留执行必要结构
- focused + full tests 全通过
- `NPC.cs` / `Player.cs` 的内存或 `cpg-build` 至少有一项出现可复现改善
- 图节点数、边数和规则结果不变

---

## 8. 风险

### 风险 1：容器变化引入结果顺序漂移

缓解：

- 保留现有 ordered commit
- 所有对外可见结果仍按既有排序出口断言

### 风险 2：为了省内存误删查找表，导致 worker 里反复线性扫描

缓解：

- 只删除 commit 不需要的表
- 保留 `OperationNodesByOperation` 和邻接字典

### 风险 3：内存下降不明显，因为真正峰值来自图本体而不是 data-flow 中间态

缓解：

- 明确这是针对 `DataFlowPass` 的局部提案
- 若收益不足，再单开 capability 裁剪 / lazy index / directory release 提案

---

## 9. 建议

建议把这项工作作为一个**局部、可独立验证的性能提案**执行，而不是和 capability 裁剪、目录级释放、structure-view cache 优化一起打包。

原因：

- 它只改 `DataFlowPass`
- 回归面清晰
- 真实收益可以直接从 `NPC.cs` / `Player.cs` 日志中测出来
- 即便收益有限，也能明确排除一类高噪音中间态复制问题
