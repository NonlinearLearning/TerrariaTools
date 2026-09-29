# NLCPG WorkBatch 并发设计

> 状态：第一版已实现；project worker 的 local DOP1 synchronous adapter 已接入，Version4 级别的 warmed acceptance 仍需单独记录。
>
> 读者：维护 `NLCPG` 构图、Roslyn 分区分析、CPG 持久化和性能诊断的开发者。
>
> 关联：[最小 CPG 架构](cpg-architecture.md)、[日志与并发](日志与并发.md)。
>
> 项目级跨文档调度：项目级池**已删除**（2026-09-24），历史设计见 [NLCPG 项目级 WorkBatch 池](cpg-project-workbatch-pool.md)（已标注废弃）；当前项目导出由 `src/NLISSN.Infrastructure/ProjectJson/`（`NLCPG.ProjectJson` 组件）逐文档执行，并可由 `artifacts.projectJson` 在 NLISSN 运行内联触发。
>
> 本设计只处理 CPG 构图的调度、局部结果和归并，不改变节点/边语义，也不把 ECS 规则分析放进构图热路径。
>
> 更新时间：2026-09-21。

## 1. 结论

NLCPG 采用固定数量的长期 worker 执行 `WorkBatch`。一个 batch 连续分析多个方法或方法内部片段，生成不可变的 `LocalCpgFragment`；worker 不直接写共享的 `NLCPGGraph`。所有节点身份、边端点、跨 batch 关系和最终查询索引在 reducer 阶段确定。

```text
Roslyn 输入
  -> 方法发现和成本估算
  -> 按成本组装 WorkBatch
  -> P 个长期 worker
  -> 批内连续采集语法/操作/局部 CFG/数据流事实
  -> LocalCpgFragment
  -> 局部 reducer 和跨分片关系准备
  -> 稳定归并到 NLCPGGraph 或 shard store
  -> FreezeQueryIndex / catalog 发布
```

第一版的调度成本只使用方法源代码行数。行数是 scheduler hint，不是 CPG 语义，也不决定某个方法是否必须被分析。

## 2. 当前问题

当前 NLCPG 已经把一部分 Roslyn 事实采集放进并行路径，但仍存在三个结构性问题：

1. `PartitionedSyntaxPass` 和 `PartitionedOperationPass` 的采集可以并行，节点和边物化仍集中在有序提交回调中。
2. `BoundedConcurrencyPool.CommitOrdered` 和 `CommitTwoStageOrdered` 会为窗口内的工作创建多个 task。函数很多而单个函数很小时，task、continuation、排序和记录保留的成本可能超过分析成本。
3. 大文件中方法成本高度不均匀。大方法落在队列末端时会形成尾延迟；即使其他 worker 已空闲，最后一个重 batch 仍会阻塞 `FreezeQueryIndex` 和后续 CPG pass。

因此，目标不是给 `NLCPGGraph.AddNode` 和 `AddEdge` 简单加锁。共享图锁会把并行采集重新串行化，并引入锁竞争、顺序依赖和难以复现的节点身份问题。

## 3. 目标与非目标

### 3.1 目标

- 让固定数量的 worker 在整个 CPG 文件构建期间重复领取 batch，避免函数级 task 创建和销毁。
- 让语法、操作、局部 CFG、局部数据流和局部成员事实尽可能在 worker 内完成。
- 通过 fragment 和 reducer 隔离 Roslyn 只读分析与共享图写入。
- 让 batch 大小同时受 CPU 成本和内存上限约束，避免最后一个超大 batch 拖慢整个阶段。
- 保证 DOP 1、2、16 的图签名、查询结果、规则决策、改写源码和 diff 可比较且稳定。
- 允许在不改变 CPG 数据契约的情况下替换调度器、成本模型和 reducer 数量。

### 3.2 非目标

- 不承诺所有 CPG pass 都可以无 barrier 并行。
- 不在第一版实现完整跨方法数据流、虚派发或 dynamic dispatch。
- 不让 worker 直接修改 `NLCPGGraph`、全局 `NodeId` 分配器或共享查询索引。
- 不用协程替代 CPU 并行。协程可以协调队列和 I/O，但不能消除 CPU 计算中的线程竞争和调度成本。
- 不把 `EvidenceCatalog` 作为 CPG 的另一份节点/边存储。EvidenceCatalog 是下游证据整理层，消费已经完成或可证明的 CPG 事实。

## 4. 成本估算和大小分类

### 4.1 第一版成本公式

对拥有方法体的 `IMethodSymbol`，以声明或 body 的源码行范围计算：

```text
cost(method) = max(1, endLine - startLine + 1)
```

行范围必须来自 `SyntaxTree.GetLineSpan`，不能通过字符串中的换行数猜测嵌套方法边界。没有可靠 body span 的方法使用最小成本 1，并在诊断中标记 `UnknownSpanCost`。

文件级初始化、类型声明和没有方法体的成员不应伪造方法成本；它们作为 file prelude 或 declaration work item 单独进入调度。

### 4.2 初始分类

| 类别 | 行数成本 | 默认调度策略 |
| --- | ---: | --- |
| 小型 | 1-40 | 与其他小型方法合并到同一 batch |
| 中型 | 41-200 | 按成本预算合并，避免单 batch 方法数过多 |
| 大型 | 201-800 | 通常一个方法一个 batch，或与极小方法合并 |
| 超大型 | >800 | 第一版单独 batch；后续按 CFG/basic block 或 operation subtree 分片 |

分类阈值必须是 `NLCPGBuilderOptions` 的调度配置，不得写死在 pass 中。阈值改变只影响调度，不应改变 CPG 语义。

### 4.3 Batch 预算

batch 不固定方法数量，而使用成本预算：

- `TargetBatchCost` 初始建议为 300-600 行成本。
- `MaxBatchCost` 初始建议为 800 行成本。
- 单个大型或超大型方法不能因为超过 `MaxBatchCost` 被强行和其他大型方法合并。
- batch 还必须受估计节点数、估计 fragment bytes 和最大方法数约束。
- 一个超大型方法在没有可靠内部边界时宁可保持单 batch，并记录尾延迟；不要按任意行号切断语义。

成本模型后续可以逐步增加权重，但必须保持可解释并通过真实工程校准。例如：

```text
cost = lines
     + 4 * branchCount
     + 2 * assignmentCount
     + 3 * invocationCount
     + 2 * memberAccessCount
```

权重不是第一版的默认值。增加权重前先通过现有 performance sink 证明它比单纯行数更能预测 collection、materialization 和 data-flow 时间。

## 5. WorkBatch 契约

WorkBatch 是调度器和 worker 之间的不可变值。建议字段如下，最终命名以现有 CPG 命名约束为准：

```csharp
public sealed record CpgWorkBatch(
  long BatchId,
  string SourceFilePath,
  int StableOrder,
  IReadOnlyList<CpgWorkItem> Items,
  int EstimatedCost,
  int EstimatedNodeCount,
  int EstimatedBytes,
  CpgWorkBatchKind Kind);

public sealed record CpgWorkItem(
  int StableOrder,
  string? MethodSymbolKey,
  int SpanStart,
  int SpanEnd,
  int EstimatedCost,
  CpgWorkItemKind Kind);
```

约束如下：

- `BatchId`、item 顺序和 span 必须来自稳定输入顺序；不能使用 worker 完成时间作为身份。
- 一个方法体只能由一个普通 work item 拥有。只有超大型方法采用内部分片时，才允许多个 item 共享 method identity，并且每个 item 必须声明 fragment boundary。
- `Items` 在入队后不可修改。worker 可以在本地建立临时数组，但不能回写调度器集合。
- `EstimatedBytes` 是背压估算，不是实际内存承诺。fragment 完成后记录实际 bytes。
- 不要求按源顺序执行分析；只要求最终 reducer 按 stable anchor 产生确定结果。

## 6. 固定 worker 和有界队列

每个文件构建或每个明确的 CPG build session 创建 `P` 个长期 worker：

```text
producer -> bounded Channel<CpgWorkBatch>
                 |      |      |
              worker worker worker ... P
                 \      |      /
                 LocalCpgFragment sink
```

实现约束：

1. `P = min(configuredDop, cpuBudget, admissionBudget)`，最小为 1。不能在每个方法或每个 batch 中再次创建线程。
2. 每个 worker 只创建一次执行 task，循环 `WaitToReadAsync`，领取一个 batch 后连续处理其所有 item，再领取下一个 batch。
3. channel 必须有界。初始容量建议为 `max(2 * P, 8)` 个 batch，并同时检查 queued estimated cost 和 queued estimated bytes。
4. producer 在达到内存水位时阻塞或异步等待，不能无限制生成 fragment 和保留 Roslyn 对象。
5. `Task.Run` 可以只用于启动固定 worker；不能在 worker 内为每个函数调用 `Task.Run`。
6. worker 只能并发读取 `SyntaxTree`、`SemanticModel` 和已经发布的只读符号事实。任何不确定是否线程安全的 Roslyn API 必须先做 DOP contract test。

现有 `BoundedConcurrencyPool` 可新增面向 batch 的长期 worker API，或由 NLCPG 创建 CPG 专用执行器。选择哪一种要以已有 admission、取消、telemetry 和异常语义为准；不能复制一份没有共享准入控制的线程池。

## 7. LocalCpgFragment

fragment 是 worker 的唯一结果边界。它应使用已经存在的稳定描述类型，而不是携带 Roslyn 对象：

```csharp
public sealed record LocalCpgFragment(
  long BatchId,
  string SourceFilePath,
  int StableOrder,
  IReadOnlyList<CpgNodeDescriptor> Nodes,
  IReadOnlyList<CpgEdgeCandidate> Edges,
  IReadOnlyList<CpgMethodSummary> MethodSummaries,
  IReadOnlyList<CpgBoundaryReference> BoundaryReferences,
  CpgFragmentMetrics Metrics,
  IReadOnlyList<CpgDiagnostic> Diagnostics);
```

其中：

- `CpgNodeDescriptor` 只描述 stable anchor、node kind、源码 span、符号摘要、隐式性和类型摘要，不包含最终 `NodeId`。
- `CpgEdgeCandidate` 使用 source/target stable anchor、edge kind、结构化 label、context 和 call-site 摘要，不直接使用只在局部图中有效的数字 id。
- `CpgMethodSummary` 保存 CFG、数据流、调用目标和成员访问的可跨阶段摘要，供后续 barrier 使用。
- `CpgBoundaryReference` 表示“端点在别的 fragment 或外部符号中”，解析失败时必须保留 unknown/unavailable，而不是猜一条边。
- `Metrics` 记录 collection 时间、materialization 前估计、实际节点/边、fragment bytes 和截断原因。
- fragment 完成后释放 Roslyn 临时集合；不能因为 reducer 尚未运行而长期持有完整语法树的额外副本。

## 8. Pass 并行边界

| 阶段 | worker 可以做什么 | 必须等待或归并的部分 |
| --- | --- | --- |
| Syntax | 在 batch 内采集声明、引用、类型和源码 span 事实 | stable anchor 去重、最终 node identity 和共享图写入 |
| Operation | 展开 operation tree、建立 operation 节点描述和局部 operation edge candidate | 跨 partition 的 syntax link、全局节点去重 |
| Method decoration | 建立方法、参数、返回值、entry/exit 的局部描述 | 与类型/符号节点的最终连接 |
| CFG | 方法内 basic block、`CfgNext`、true/false 分支 | 跨方法边和最终 CFG node identity |
| Dominance / ControlDependence | 在单方法局部 CFG 上计算摘要或候选边 | 需要完整 CFG 的全局提交 |
| DataFlow collect | 每个方法收集 definitions、uses、candidate edges 和预算统计 | 跨方法桥接和共享 graph mutation |
| DataFlow solve | 对已经准备好的方法摘要并行求解局部 reaching definitions | interprocedural overlay 前的 method barrier |
| CallGraph | 从调用点采集符号 key、候选目标和 unknown 状态 | 全局符号索引、候选去重、跨文件 resolve |
| MemberAccess | 收集 receiver/member/argument 关系 | 最终 member node identity 和跨方法连接 |
| Interprocedural | 只处理已发布的 call target、method summary 和 boundary reference | 完整局部 fragment、global symbol index 和 call graph barrier |
| Freeze/catalog | 可对独立 shard 做局部索引构建 | 最终发布仍需完整、稳定、不可变的 build snapshot |

这个表意味着“全部并行化”不可行，也不必要。可并行的是独立事实的计算；依赖身份、跨片段关系和可见性发布的阶段仍需 barrier。

## 9. Barrier 顺序

```text
B0  输入加载和 SyntaxTree/Compilation 准备
    |
B1  方法发现、成本估算、WorkBatch 组装
    |
B2  Syntax/Operation/Method local fragment 完成
    |
B3  局部 CFG、Dominance、MemberAccess、DataFlow fragment 完成
    |
B4  reducer 建立 stable anchor -> NodeId 和局部图索引
    |
B5  CallGraph 全局 resolve 和跨文件 boundary resolve
    |
B6  Interprocedural/DataFlow overlay 生成
    |
B7  最终 reducer、边去重、FreezeQueryIndex、catalog/shard 发布
```

每个 barrier 的输入都必须是前一阶段已经完成的不可变结果。未完成 build 对查询方不可见；取消或失败只能发布失败诊断和不完整状态，不能发布看起来完整的半张图。

## 10. Reducer、身份和确定性

### 10.1 NodeId

NodeId 不在 worker 中分配。归并阶段按以下数据生成或恢复 stable identity：

```text
project/source identity
  + stable anchor role
  + source span
  + semantic symbol/type summary
  + occurrence ordinal
```

现有 `StableNodeAnchor`、`CpgNodeDescriptor`、`FragmentOwnershipIndex` 和 stable identity factory 是优先复用的边界。batch 完成时间、worker 编号和队列顺序不能进入 identity。

### 10.2 Edge 去重

边的 dedup key 为：

```text
(source stable anchor, target stable anchor, edge kind,
 structured label, context id, call-site summary)
```

同一条边由多个 pass 或多个 partition 观察到时，只保留一条稳定边；语义不同的 label 不能因为 source/target 相同而合并。

### 10.3 归并策略

第一版可采用单一 reducer 线程按 `StableNodeAnchor` 排序提交，先验证语义和峰值内存。确认 reducer 成为瓶颈后，再按 stable anchor hash 分区执行局部 reducer，最后以固定分区顺序做轻量 final merge。不要以共享 graph lock 代替 reducer。

`NLCPGGraph.FreezeQueryIndex()` 只能在最终 graph 不再变化后执行。它是全量索引重排和 CSR 构建边界，不应被每个 batch 调用。

## 11. 超大型方法的内部切分

行数只能帮助识别超大型方法，不能安全决定切点。后续内部切分按以下顺序演进：

1. 先建立完整 method skeleton、参数/局部符号表和 method entry/exit。
2. 以 CFG basic block 或 operation subtree 作为切分单元，保留 parent/owner method anchor。
3. 对跨片段的 predecessor、successor、definition/use、call-site 只生成 boundary candidate。
4. 所有片段完成后，在 method barrier 合并 CFG 和数据流候选。
5. 无法证明切分安全时回退到单方法 batch，并记录 `UnsplittableLargeMethod`。

不能按每 N 行简单截断语法树；这会切断 lambda、local function、operation parent、异常边和数据流定义域。

## 12. 取消、失败和背压

- producer 观察取消令牌后停止生成新 batch，并完成 channel；worker 不领取新工作。
- worker 处理中的 batch 应在下一个安全边界检查取消，释放本地 fragment 和 Roslyn 临时对象。
- 一个 batch 的异常不能让其他 worker 静默成功后直接发布完整图。session 需要记录 batch failure，并按构建策略 fail build 或发布明确的 unavailable 状态。
- fragment sink 必须有界。至少同时限制 queued batch count、queued estimated cost、queued estimated bytes 和 completed-but-not-reduced fragment count。
- reducer 失败时停止发布后续 graph snapshot；已写出的临时 shard 只能由 session cleanup 删除或标记为 incomplete，不能作为可查询构建复用。
- 取消、异常、背压等待时间和 fragment 丢弃数量必须进入性能诊断，但不能进入 ECS 规则语义。

## 13. EvidenceCatalog 的关系

直接由 Roslyn 级节点和边形成 CPG catalog 是可行的，也是本设计的底层事实来源。但二者职责不同：

```text
Roslyn facts -> NLCPG nodes/edges -> stable catalog/shard index
                                  -> EvidenceCatalog
                                  -> ECS split evidence / rule decision
```

`EvidenceCatalog` 的职责是把完整 CPG 上的节点、边、来源 span、规则能力、unknown/cut 状态和证据 provenance 组织成下游可消费的证据。它不应参与 worker 热路径中的节点分配，也不应在局部 fragment 尚未完成时把局部事实提升为全局结论。

因此，WorkBatch 只输出 CPG fragment 和可解释的摘要；在 B7 之后再构建或更新 EvidenceCatalog。这样既可以直接利用 Roslyn 级 catalog，也不会让 ECS 拆分的证据规则污染 CPG 构图并发模型。

## 14. 可观测性和验收指标

每个 batch 至少记录：

- estimated cost、actual node/edge count、actual fragment bytes；
- queue wait、collection、local solve、reducer wait、commit 和 total elapsed；
- worker id、stage、method count、largest item cost；
- backpressure count、cancelled count、failure kind 和 data-flow truncation；
- active worker count、queue high-water mark 和 completed-not-reduced high-water mark。

验收不以“worker 数量增加”作为完成条件，而以同一输入的对比为准：

1. DOP 1、2、16 的 graph signature、节点/边去重结果和查询结果一致。
2. 需要规则的场景中，decision、rewrite source 和 diff 一致；unknown、预算截断和编译错误仍保持原语义。
3. 函数级 task 数量不再随方法数量线性增长；长期 worker 数量接近配置的 P。
4. 大文件的最后一个 batch 不再因为大量小 task 排队而产生额外尾延迟；P95/P99、queue wait 和最大 worker elapsed 必须有基线对比。
5. 内存峰值、fragment bytes 和 catalog/freeze 时间不能因并发改造无界增长。

在真实 Version4 输入完成 warmed benchmark 前，不把某个固定倍数写成性能承诺；先保存基线，再以 measured delta 决定默认阈值。

当前第一版已经落地成本受限的 `CpgWorkBatch`、固定长期 worker、有界 batch/result
channel、`LocalCpgFragment` 和 stable-anchor reducer。`CpgWorkBatchPerformanceEvent`
通过 `NLCPGBuildMetrics.WorkBatchPerformanceEvents` 和可选的
`ICpgWorkBatchPerformanceEventSink` 发布；sink fail-open，遥测不参与图语义。
实际字段覆盖 estimated cost/bytes、输出节点/边/fragment bytes、queue wait、worker
processing、reducer wait、worker index、active worker 峰值、queue high-water 和
completed-not-reduced high-water。

## 15. 迁移顺序

1. 保留现有 DOP 1 路径作为语义基线，先抽取成本模型和 WorkBatch contract。
2. 在不改变 pass 输出的前提下，把 syntax/operation 的收集结果从 partition result 适配为 LocalCpgFragment。
3. 增加固定 worker、有界 channel、取消和异常排空，先让 reducer 仍按源序单线程提交。
4. 把局部 CFG、data-flow collect/solve 和 member facts 接到 batch worker；跨方法 pass 继续等待 barrier。
5. 逐步减少 `CommitOrdered` 中每分片 task 的使用，仅保留兼容调用方或非 CPG 场景。
6. 实现 stable anchor 去重、跨 fragment resolve 和最终 catalog/freeze。
7. 在 DOP 1/2/16 和真实大文件上完成等价性、内存和尾延迟验收后，才考虑启用默认 WorkBatch 调度。

## 16. 当前实现与验收边界

Task 1-12 的成本模型、batch builder、固定 worker、局部 fragment、稳定 reducer、
局部/跨方法 barrier、取消/失败语义和 DOP 等价契约已实现。Task 13 的遥测契约已
由 small、mixed、large 三类固定输入的 DOP 1/2/16 测试覆盖，原始数据见
[CPG WorkBatch 遥测基线](../../benchmarks/cpg-workbatch-baseline.md)。

剩余的 Version4 acceptance 仍需要同一源快照下的多次 warmed 样本，比较 graph/query/
rule/rewrite/diff、峰值内存、freeze/catalog、queue wait 和 P95/P99 尾延迟。没有这组
配对数据前，WorkBatch 遥测只能证明边界和可观察性，不能证明默认调度应切换或性能一定提升。
