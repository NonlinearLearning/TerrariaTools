# 统一工作调度内核设计：四层并发设施合并为一

**状态：** 设计已定稿，待执行（2026-09-24）。本文**不含任何生产代码改动**。
**执行计划：** [统一调度内核执行计划](2026-09-24-unified-work-scheduler-execution.md)

**目标：** 把当前四层并发设施（通用池、准入控制器、CPG 度预算、WorkBatch 执行器）
合并为**一个内核 + 一套词汇 + 一套遥测**，并在同一内核上实现小/中文件成批与
大文件分片三类提交，不改变任何 CPG 图语义、节点集、边序或持久化格式。

**读者：** 维护 `NL.Concurrency`、CPG 构图并发、目录分析调度与运行时并发配置的开发者。

**责任边界：** 本页定义内核契约、调用方迁移边界、删除清单与验收条件；
不重新定义 CPG 节点/边语义、规则 DAG 语义或 rewrite 结果。

---

## 0. 一句话结论

四层不是四个问题，是**同一个问题的四种方言**：

| 层 | 它说的那句话 | 位置 |
| --- | --- | --- |
| 通用池 | 「给一组 source 和一个 DOP，按序返回结果」 | `BoundedConcurrencyPool.cs`（8 个成员） |
| 准入控制器 | 「最多 N 个操作 / N 个 item / N 字节在途」 | `ConcurrencyAdmissionController.cs` |
| CPG 度预算 | 「DOP 本身是可排队领取的资源」 | `CpgBuildAdmissionBudget.cs` |
| WorkBatch 执行器 | 「固定 P worker + 有界队列 + 稳定序归并」 | `CpgWorkBatchExecutor.cs` |

四句都在描述同一件事：**把工作放进有界预算，用固定 worker 跑掉，按稳定序交回。**
本文把它实现为一个对象：`WorkScheduler`。

---

## 1. 当前状况（事实基础，全部已核实）

### 1.1 调用链

```
AnalysisRuntime                                    ExecutionRuntime.cs:63-84
├── ConcurrencyPool = BoundedConcurrencyPool       :79（注入 admissionController）
├── CpgBuildAdmissionBudget                        :96（独立字段，与上面互不知情）
└── ExecutionOptions

DirectoryAnalysisUseCase.AnalyzeFiles              :267
└── ConcurrencyPool.SelectOrderedAsync(directoryDOP)
    └── 每文件: CpgBuildAdmissionBudget.AcquireAsync(cpgDOP)   :273
        └── ApplicationService.Analyze
            └── NLCPGBuilder → 8 个 pass 各自 _workBatchExecutor.ExecuteAsync
                └── ConcurrencyPool.ExecuteWithAdmissionAsync   CpgWorkBatchExecutor.cs:358
                    └── AcquireAdmissionAsync 发现已有租约 → 返回 null  :973-976
```

**这不是一条链。** 通用池「持有」准入控制器，但不「经过」它；CPG 度预算是同级的
第二个字段。第四层回落到第一层时，准入被静默短路。

### 1.2 四个已核实的关键事实

**事实 A：文件级并行实际被串行化。**
`MaxDegreePerLease = TotalDegree`（`CpgBuildAdmissionBudget.cs:18`），而每份租约请求
`EffectiveCpgMaxDegreeOfParallelism`（=12）（`DirectoryAnalysisUseCase.cs:274`）。
`_availableDegree` 初始 12，第一份租约拿满，第二份必须等 `Dispose()`（`:139-142`）。
且该 scope 包住**整个 `AnalyzeFile`**（含规则管线与改写），不只是构图。
⇒ 目录 DOP=12 是名义值。与 `Context/progress.md` 轮次 14 实测
（`sum(CPG.Build.wall)/wall = 0.70`、`threadsRunning` 71.8% 为 1）一致。

**事实 B：所有字节预算参数都是死的。**

| 参数 | 声明处 | 生产消费者 |
| --- | --- | --- |
| `ConcurrencyAdmissionOptions.MaxReservedByteCount`（128 MB） | `ExecutionRuntime.cs:228` | **无** |
| `ConcurrencyWindowOptions.EstimatedRetainedBytesPerItem` | `ConcurrencyWindowOptions.cs:16` | **仅测试** |
| `CpgWorkBatchExecutorOptions.AdmissionReservedByteCount` | `CpgWorkBatchExecutor.cs:22` | 默认 0，`NLCPGBuilder.cs:136-142` 不传 |
| `QueueCapacity`/`FragmentSinkCapacity`/`MaxQueuedEstimatedCost`/`MaxQueuedEstimatedBytes` | `CpgWorkBatchExecutor.cs:71-77` | 走 `?? int.MaxValue`（`:89-95`）⇒ 队列预算不生效 |

**事实 C：`IConcurrencyPool` 8 个成员中 4 个生产死代码。**

| 成员 | 生产调用点 |
| --- | --- |
| `SelectOrderedAsync<TSource,TResult>` | `DirectoryAnalysisUseCase.cs:267`、`DecisionModel.cs:486`、`RewritePlanReplayService.cs:25` |
| `ForEachAsync` | `CpgShardBuildCoordinator.cs:98`、`ParameterShrinkAnalyzer.cs:1351` |
| `RunDependencyGraphAsync` | `RuleGraphExecutor.cs:235` |
| `ExecuteWithAdmissionAsync` | `CpgWorkBatchExecutor.cs:358`（**本设计要删的调用方**） |
| `SelectOrderedAsync<TResult>`（按索引） | **无** |
| `SelectCpuBoundOrdered` | **无**（仅测试与测试假实现） |
| `CommitOrdered` | 仅 `PartitionedOperationPass.cs:66`——位于**零引用的** `RunPartitionedOperationPassOrderedCompatibility` |
| `CommitTwoStageOrdered` | **无** |

**事实 D：迁移从未清理。** `2026-09-20` 计划的 WorkBatch 迁移本应保留 ordered 路径
作为 DOP1 语义对照（`cpg-workbatch-concurrency.md` §15.5），但：

- `RunPartitionedOperationPassOrderedCompatibility`（`PartitionedOperationPass.cs:62`）**全仓零引用**；
- `ShouldUsePartitionedOperationBuild` 恒 `return true`（`:435-438`）；
- `_workBatchExecutor` 被 **8 个 pass** 调用（Syntax/Operation/CallGraph/MemberAccess/
  ControlFlow/DataFlow/Dominance/ControlDependence）。

### 1.3 文件级大小分类不存在

`LargeFileLineThreshold`、`LargeFileMethodThreshold`、`LargeMethodLineSpanThreshold`、
`SyntaxLargeFileLineThreshold`（`NLCPGBuilderOptions.cs:20-22,26`）在 `src/` 内**零消费者**；
`OperationBuildStrategy.SourceLineCount` 只赋值不用（`PartitionedOperationPass.cs:48`）。
唯一活着的大小分类是 `CpgWorkBatchCostModel` 的 40/200/800 行（`CpgWorkBatchCostModel.cs:58-63`），
作用对象是**方法**，不是文件。

### 1.4 大文件分片合并的现状

- **`NLCPGGraph.AddNode` / `AddEdge` 全文无锁**（`NLCPGGraph.cs:115/144/334/359`）
  ⇒ 分片**不能**并发写同一张图，必须「worker 采集 → 单写入 reducer 合并」。
- **`CpgFragmentReducer` 已存在**（`CpgFragmentReducer.cs:14`），但只在
  `DominancePass.cs:377`、`ControlDependencePass.cs:69` 两处使用，**不在主构图路径**。
- 但现有 WorkBatch 路径**已经是**「worker 采集 + 单 reader reduce」结构：
  `PartitionedOperationPass.cs:177-222` 的 reduce 回调在单读者线程上
  `MaterializeOperationPartition` → `AddNode/AddEdge`。
  ⇒ 分片架构**保留**，只换调度单位。

### 1.5 大文件是成本主体

| 口径 | 值 | 来源 |
| --- | --- | --- |
| TOP3 文件占构图耗时 | **93.1%**（61.3 min 中的 57.1 min） | `Context/progress.md` |
| `NPC.cs` + `Item.cs` 占全部边 | **55.1%**（63.6 M 条中的 35.0 M） | 同上 |
| 单文件平均边成本 | `Item.cs` 18.87 边/节点 = `WorldGen.cs` 的 **11 倍** | 同上 |

⇒ 分桶不是「平均优化」，是**把 93.1% 的耗时对象隔离处理**。

---

## 2. 目标与非目标

### 2.1 目标

1. **一个内核**：`WorkScheduler` 是唯一的调度实现、唯一的预算记账处、唯一的遥测来源。
2. **一套词汇**：`WorkItem` / `WorkSubmission` / `WorkPriority` 取代
   `ConcurrencyAdmissionRequest` / `ConcurrencyWorkType` / `ConcurrencyWindowOptions` /
   `DependencyWorkItem` / `CpgWorkBatch` / `CpgWorkItem` 六种表述。
3. **一套遥测**：`ConcurrencyOperationTelemetry` 与 `CpgWorkBatchPerformanceEvent`
   合并为一个 `WorkTelemetry`。
4. **长期 worker**：worker 在全 run 生命周期内存在，不再每次调用 `Task.Run` 起 P 个。
5. **支持三类提交**：小文件成批、中文件成批、大文件分片——由**外部算法**决定划分。
6. **无嵌套**：全部工作扁平化提交，内核只有一套不可重入的领取逻辑。
7. **行为等价**：DOP 1/2/16 的图签名、查询结果、规则决策、改写源码、diff 可比较且稳定。

### 2.2 非目标

- **不保留函数级并行**（已决策）。⚠️ **精确化（2026-09-26，见 §13b.9）**：放弃的是
  **函数级并行**（每方法一个并行项），**不是**方法级**批次组装**。
  `CpgWorkBatchBuilder` 的「按 cost 组装标准大小批次」能力**保留**，
  作用域由「单文件」放宽到「可跨文件的文件组」（需求 D1/D2）。
- 不改变 CPG 节点/边语义、`NLCPGGraph` 公开形状、持久化格式或 rewrite 结果。
- 不改变 6 个 `execution.*MaxDegreeOfParallelism` YAML 字段的名称与必填性
  （Schema 2 契约保持）。
- 不引入 `InternalsVisibleTo`（`ArchitectureBoundaryTests.cs` 禁止）。
- 不用协程替代 CPU 并行。
- 不对未测量的吞吐/内存收益做任何声称。

---

## 3. 内核契约

```csharp
// src/NLISSN.Infrastructure/Concurrency/WorkScheduler.cs
namespace NL.Concurrency;

/// 全 run 唯一实例。拥有 P 个长期 worker，P 由配额推导；worker 惰性启动。
public sealed class WorkScheduler : IAsyncDisposable
{
    public WorkScheduler(WorkSchedulerOptions options, IWorkTelemetrySink? telemetry = null);

    /// 本内核实际使用的配额；长期 worker 数 = Options.WorkerCount。
    /// 调用方要报告「生效值」时必须读这里，而不是把外部配置再映射一遍。
    public WorkSchedulerOptions Options { get; }

    /// 唯一入口。有序选择 / 遍历 / 依赖图 / 长任务都是它的参数差异。
    public Task<IReadOnlyList<TResult>> RunAsync<TResult>(
        WorkSubmission<TResult> submission,
        CancellationToken cancellationToken = default);

    /// 与 RunAsync 同一实现，额外返回调度峰值。
    /// 供规则图上报 PeakReadyNodeCount / PeakConcurrentNodeCount 使用。
    /// RunAsync 就是它的薄包装（单一入口不变）。
    public Task<WorkExecutionOutcome<TResult>> RunWithMetricsAsync<TResult>(
        WorkSubmission<TResult> submission,
        CancellationToken cancellationToken = default);
}
```

⚠️ **为什么必须有 `RunWithMetricsAsync`**（S2 实施时发现，计划外）：
`RuleGraphExecutionResult` 需要 `RuleGraphExecutionMetrics(PeakReadyNodeCount,
PeakConcurrentNodeCount)`（`RuleGraphExecutor.cs:189,271`），而只返回结果列表的
`RunAsync` 无法提供。若为此让规则图直连 `Submission` 内部状态，才是真的开后门；
让 `RunAsync` 调用 `RunWithMetricsAsync` 并丢弃额外字段，保持了**单一入口**这一不变量。

```csharp
/// 一次提交的结算结果。
public sealed record WorkExecutionOutcome<TResult>(
    IReadOnlyList<TResult> Results,
    int PeakReadyCount,
    int PeakActiveCount,
    TimeSpan Elapsed);
```

```csharp
// 一次提交。取代 ConcurrencyWindowOptions + maxDegreeOfParallelism 参数。
public sealed record WorkSubmission<TResult>
{
    public required IReadOnlyList<WorkItem<TResult>> Items { get; init; }
    public string Category { get; init; } = WorkCategories.Default;  // 取代 6 个 DOP 字段的作用域
    public bool PreserveOrder { get; init; } = true;                  // false = 纯 ForEach
    public int? MaxConcurrency { get; init; }                         // 覆盖类别在途上限
    public long MaxInFlightBytes { get; init; }                       // M1 字节额度；0 = 不限
}

// 一个工作项。取代 DependencyWorkItem + CpgWorkBatch + CpgWorkItem。
public sealed record WorkItem<TResult>
{
    public required long StableOrder { get; init; }                   // 唯一顺序权威
    public required Func<IReadOnlyDictionary<long, TResult>, CancellationToken, Task<TResult>>
        ExecuteAsync { get; init; }                                   // 首参为已完成前置项结果
    public IReadOnlyList<long> Dependencies { get; init; } = [];      // 空 = 无依赖
    public WorkPriority Priority { get; init; } = WorkPriority.Throughput;
    public int EstimatedCost { get; init; }                           // 取代 ReservedItemCount
    public long EstimatedBytes { get; init; }                         // 取代 ReservedByteCount（今天全死）
}

public enum WorkPriority { LatencySensitive, Throughput }

public static class WorkCategories
{
    public const string Default = "default";
    /// <summary>
    /// 不是提交类别，而是由分片计划器查询的「窗口内同时存活的文件数」上限（§4.4 / §7）。
    /// </summary>
    public const string Directory = "directory";
    public const string Cpg = "cpg";
    public const string RuleGroup = "rule-group";
    public const string Helper = "helper";
    public const string Replay = "replay";
}
```

⚠️ **`Directory` 的角色与其他类别不同**：目录文件分析不再是「一个提交类别」，
而是构成扁平 DAG 的**输入**（§4.2）。文件的分片与阶段就是 DAG 节点本身，
它们的**提交** `Category` 是 `cpg`（受 CPG 额度约束）；
`directory` 常量只被 `DocumentShardPlanner` 查询，用于决定**分片窗口能容纳多少文件**
（§7）。它保留在 `WorkCategories` 里是为了让 6 个 YAML 字段的映射一处可见，
但它**不出现在任何 `WorkSubmission.Category` 上**。

### 3.1 为什么 8 个成员能塌成 1 个

| 今天 | 合并后 |
| --- | --- |
| `SelectOrderedAsync(sources, dop, f)` | `Items = sources.Select(...)`，`PreserveOrder = true` |
| `ForEachAsync(sources, dop, f)` | 同上，`PreserveOrder = false` |
| `RunDependencyGraphAsync(items, dop, cmp)` | `Items[i].Dependencies = ...`，`Priority = LatencySensitive` |
| `CommitOrdered(sources, opts, work, commit)` | `RunAsync` 返回有序结果，调用方自己按序 commit |
| `CommitTwoStageOrdered(...)` | 两阶段是调用方的 `ExecuteAsync` 内部实现 |
| `SelectCpuBoundOrdered(sources, dop, f)` | `ExecuteAsync = (_, t) => Task.FromResult(f(x))` |
| `SelectOrderedAsync<TResult>(count, ...)` | `Items = Enumerable.Range(0, count).Select(...)` |
| `ExecuteWithAdmissionAsync(request, op)` | 长任务就是一个 `WorkItem`，其 `ExecuteAsync` 是 op |

**没有一种语义丢失。** 唯一真正新增的是 `Category`（见 §7）。

### 3.2 worker 模型：全异步单循环

```text
构造时：只记录 P = WorkSchedulerOptions.WorkerCount，不启动线程
首次提交时：启动 P 个长期 worker（Task.Run），此后常驻
        每个 worker 循环：等待信号门 → 领取一个就绪项 → await ExecuteAsync
提交时：把 WorkItem 放入就绪集（Dependencies 为空者立刻就绪），并脉冲信号门
        worker 完成后：递减后继依赖计数，归零者入就绪队列，并脉冲信号门
结算时：全部完成后按 StableOrder 归并返回，然后从活动集合移除该提交（再次脉冲）
释放时：停止领取新提交，但**继续服务在途提交**，二者皆空后 worker 退出
```

⚠️ **惰性启动**：构造时不建线程，避免「从未使用的内核占用 P 个线程」。
⚠️ **三次脉冲都不能少**：入队、完成一项、移除提交。**移除提交那次最易漏**——
worker 的退出条件是「已释放且无在途提交」，而移除提交正是让该条件成立的事件；
少了它，`DisposeAsync` 会永久挂起（这是 S1 实现中实测踩到的第一个缺陷）。

**同步 CPU 批如何进入异步循环**（已决策 = 全异步单循环）：
调用方给 `ExecuteAsync` 传一个返回已完成 `Task` 的包装：

```csharp
ExecuteAsync = (_, _) => Task.FromResult(ProcessShard(shard))
```

代价是每项一次 `Task.FromResult`（无分配，`Task<T>` 有缓存或单次堆分配）。

**R3 已实测（2026-09-24）**：200 批 × 500 行合成 CPU 工作，
直接循环 vs 经 `RunAsync` + `Task.FromResult` 包装，各取 5 轮最优值：

| 口径 | 最优耗时 |
| --- | --- |
| 直接调用 | 31.8 ms |
| 内核包装 | 29.8 ms |

差值 −6.26%，但**单轮离散度约 10%**（直接 31.8–34.3，包装 29.8–68.3 首轮含 JIT）。
⇒ 结论只能诚实地写为：**包装开销落在噪声内，未观察到可归因于包装的回归**。
**不得**据此声称精确的 ≤1%，也不得声称有任何加速。
探针脚本测完即删（未进仓库），本结论仅为量级判断。

**为什么不用 work-stealing**：全异步循环 + 单层扁平提交 ⇒ worker 永不等待另一个提交，
不存在「worker 被等待项占满」的死锁面。这正是「等待时帮忙」不需要实现的理由。

---

## 4. 无嵌套：单一扁平 DAG 是架构的核心决定

### 4.1 嵌套实际发生在哪（本设计的第一个关键修正）

⚠️ **初稿曾断言「目录层扁平化后提交深度恒为 1」，该断言是错的。**
嵌套的根源不在目录层，而在**每个文件的分析内部**，共两处：

| # | 内层提交 | 位置 |
| --- | --- | --- |
| 1 | `NLCPGBuilder` 的 8 个 pass 各自提交 WorkBatch | `PartitionedOperationPass.cs:158` 等 8 处 |
| 2 | **规则 DAG** | `RuleGraphAnalysisExecutor.cs:51`、`MarkingEngine.cs:34`、`DecisionModel.cs:455`、`MarkLiftingEngine.cs:103`（**4 个构造点**） |

即 `ApplicationService.RunAnalysis`（`ApplicationService.cs:112`）→
`RuleGraphAnalysisExecutor.Run` → `RuleGraphExecutor.ExecuteAsync` 是**在文件级工作项
内部**发起的第二次提交。无论目录层如何扁平化，这两处嵌套都依然存在。

今天的代码**绕过了**它，但方式是错的：`AcquireAdmissionAsync` 发现
`AsyncLocal` 已有租约就返回 `null`（`BoundedConcurrencyPool.cs:973-976`）。
⇒ **今天没有嵌套调度，只有嵌套静默失效。**

### 4.2 解法：把整条流水线的阶段全部表达为一个扁平 DAG

「单写入」约束的**粒度是每张图，不是全局**（正确性前提 1 只说同一张图不能并发写）。
⇒ 不同文件的图可以并发写。于是整条流水线可以摊平为**一次提交 + 依赖边**：

```text
一次 scheduler.RunAsync，包含全部文件的全部分片与阶段：

  shard(file F, i=0..K-1)          ← 只读，产出 LocalCpgFragment
      │  (全部 K 片完成后)
      ▼
  reduce(F)                        ← 单写入者：把 F 的 fragments 归并进 F 的图
      ▼
  interprocedural(F)               ← 屏障后一次，写 F 的图
      ▼
  rules(F)                         ← 规则 DAG + 改写，写 F 的图
```

- **提交深度恒为 1**，所有阶段都是同一个 DAG 里的节点。
- 内核的 `Dependencies` 字段（§3）正好表达这些箭头——它不是为规则 DAG 特设的，
  而是整条流水线的通用机制。
- 跨文件天然并行：F1 的 `rules` 与 F2 的 `reduce` 可同时在跑。
- 内核不需要可重入领取、不需要 work-stealing、不需要 `AsyncLocal` 租约状态。

**规则 DAG 的处置（诚实标注的行为变更）**：`rules(F)` 这一项内部**不再二次提交**。
规则 DAG 的并行由 `groupParallelism` 控制，而它**默认为 `false`**
（`YamlConfigurationLoader.cs:938`）⇒ **默认配置下规则 DAG 本来就是串行的**，
本设计不改变默认行为。`groupParallelism: true` 的配置会退化为串行，
代价上限可估：`Rule.*` 合计 **78.3 s，占 Run 的 1.5%**（`Context/progress.md`）。
该退化**必须**在 runtime log 中显式告警，不得静默。

### 4.3 为什么不用 work-stealing

扁平 DAG 后，worker 永不「等待另一个提交」——它等待的是**同一 DAG 里的其他节点**，
而这些节点由同一批 worker 领取，就绪集非空即会被领取，不存在全被等待项占满的稳态。
⇒ 「等待时帮忙」是无用的复杂度。
**S1-5 的嵌套回归测试正是用来固定这一点的。**

### 4.4 本设计最大的内存张力（必须正面处理）

扁平 DAG 意味着**全部文件的分片结果可能同时存活**，直到各自的 `reduce` 消费掉它们。
这与「降低峰值 committed」的目标直接冲突（`Context/progress.md` 轮次 14 的换页机理）。

**处理方式**：分片**分批窗口**提交——一次提交覆盖窗口内的文件子集，
窗口全部完成后归并、释放，再提交下一窗口：

```text
for window in windows(allFiles):        # 窗口大小由字节额度决定
    await scheduler.RunAsync(该窗口的完整扁平 DAG)
    # 窗口内所有图已归并、规则已跑、结果已抽出 → 图与 fragments 可释放
```

- 窗口内提交深度仍是 1，只是分多次提交。
- 窗口大小 = `MaxInFlightBytes` 决定的文件数，由 `DocumentShardPlanner` 计算。
- **这是 M1 字节额度（§5.3）的真正用途**，也是它必须先于分片上线的原因。

⚠️ **窗口大小与吞吐的权衡未实测**：窗口过小则并行度不足，过大则峰值内存上升。
执行计划 S3-1c Step 2 与 S5-4 必须逐档实测，**不得推断**。

### 4.5 分片计划由外部算法决定

分片计划器位于应用层（`src/NLISSN.Application/Analysis/`），**不在内核、不在 NLCPG**：

```csharp
public sealed record DocumentShardPlan(
    string FilePath,
    FileSizeClass SizeClass,          // Small / Medium / Large
    IReadOnlyList<DocumentShard> Shards,
    long EstimatedBytes);

public sealed record DocumentShard(
    long StableOrder,                 // 全局单调，跨文件唯一
    int ShardIndex,
    IReadOnlyList<int> MethodOrders,  // 该分片负责的方法（Order 来自 GetOperationRootPlans）
    int EstimatedCost,
    long EstimatedBytes);
```

**为什么必须外部**：方法根发现（`GetOperationRootPlans`，`OperationPass.cs:57`）需要
`SemanticModel`，而 `SemanticModel` 在目录层已经为每个文件构造（`DirectoryAnalysisUseCase.cs:248`
的 `compilation.GetSemanticModel(tree)`）。外部计划器可以在**构图前**一次拿到全部方法边界，
且该结果可被 8 个 pass 复用（现有 `_operationRootPlanRoot` 缓存即为此）。

---

## 5. 分桶：外部算法 + 内核数值

### 5.1 桶不是内核概念（已决策）

内核不知道「小/中/大桶」是什么。三桶是外部计划器按阈值分类后，给工作项贴上的
`EstimatedCost` / `EstimatedBytes` / `Category` 数值的结果。

| 桶 | 外部划分 | 提交形态 | 内核看到的 |
| --- | --- | --- | --- |
| 小文件 | 行数 ≤ 阈值 | 若干文件**合并为一个** `WorkItem`，`ExecuteAsync` 内顺序处理 | 1 项，低 cost/bytes |
| 中文件 | 阈值之间 | 同上，不同阈值 | 1 项，中 cost/bytes |
| 大文件 | 行数 ≥ 阈值 | **拆成 K 个** `WorkItem`，共享文件身份 | K 项，高 cost/bytes |

⚠️ **两条硬约束（2026-09-26 需求，详见 §13b）**：

1. **原子单位是完整函数**，拆分与合并**都不得在函数体内部下刀**；
   函数长度不确定时**允许超出标准大小**。该约束是**结构性天然满足**的
   （`CpgWorkItemKind` 只有 `{FilePrelude, Declaration, Method}`），
   并**排除**了「按基本块拆分」等代价极高的方案（§13b.2）。
2. **小/中文件的合并必须能跨文件边界**（需求 D1）。上表「若干文件合并为一个」
   在今日实现下**不可能达成**——`CpgWorkBatchBuilder.Build` 强制单文件
   （`:55-58` 显式 `throw`），故小文件的方法只能与**同文件**的方法凑批，
   一个 30 行的文件永远凑不到 `TargetBatchCost = 400`。跨文件凑批是
   **G4b 范围内的新增能力**，其障碍与基础设施盘点见 §13b.6 / §13b.7。

⚠️ **并行度不受合并影响**：并行度 = `min(P, 就绪项数)`（§13b.3 源码核实），
批化后项数仍 ≫ P ⇒ **并行度守恒**。合并的真实收益是**消除 O(N²) 派发与逐项分配**
（§13b.4），**不是**抵消线程切换（CPU 密集路径上无线程切换）。

阈值必须是 `DocumentShardPlannerOptions` 的配置，**不得写死在 pass 中**。
建议初值（待实测校准，见执行计划 S5-4）：

| 参数 | 建议初值 | 依据 |
| --- | --- | --- |
| `SmallFileMaxLines` | 200 | 与现有 `CpgWorkBatchCostOptions.MediumMaxCost=200` 同量级 |
| `MediumFileMaxLines` | 800 | 与现有 `LargeMaxCost=800`、`SyntaxLargeFileLineThreshold=800` 一致 |
| `LargeFileShardTargetLines` | 1500 | 待校准；目标是把 1.72 M 节点的 `NPC.cs` 切成可并行的 K 片 |

### 5.2 桶间公平性来自内核，不来自队列隔离

由于桶不是内核概念，**大文件不会被天然隔离保护**。内核必须提供：

1. **成本降序就绪序**（LPT）：同优先级就绪项按 `EstimatedCost` 降序领取，
   让巨片**最先**启动以与大量小片重叠。这与
   `2026-09-23-freezequeryindex-single-thread-tail-research.md:1091` 的既有建议一致。
2. **老化提升**：吞吐项等待超过 `MaximumThroughputWait` 时提升，避免长尾饿死。
   现有 `ConcurrencyAdmissionController` 已实现该机制（`AdmissionReason.AgingPromotion`），
   迁入内核即可，不新增概念。

⇒ 「小文件把大文件饿死」由就绪序 + 老化解决；如果实测证明不够，
再考虑把桶升为一等概念（**明确记为备选，不预先实现**）。

### 5.3 「内存池」的两种解释，两种都实现

你提的「大文件拆分进入内存池」有两种解读，本设计**两种都覆盖**，因为它们的成本差异大：

| 解释 | 实现 | 位置 |
| --- | --- | --- |
| **M1 调度侧额度**（限制同时在途/存活的字节） | 内核按 `EstimatedBytes` 累计记账；超过提交的字节上限即暂停发放新项（信号量式背压） | `WorkScheduler` 记账处 |
| **M2 分配侧缓冲区复用** | `ArrayPool<byte>` / `ArrayPool<int>` 复用分片瞬态数组 | NLCPG 分片 adapter 内部 |

**M1 是本次的核心**，因为它是两件事的共同前提：

1. 它把已被实测定位的换页机理（`Context/progress.md` 轮次 14 §9）变成**可控**：
   今天所有字节参数都是死的（事实 B），M1 把它们**变成活的**。
2. 它是**分片窗口大小的决定者**（§4.4）：窗口内全部 fragments 同时存活，
   窗口多大由字节额度算出。没有 M1，扁平 DAG 会把峰值内存推高。

⇒ **M1 必须先于 S5 分片上线**，否则分片会直接放大已定位的内存问题。

**M2 的边界必须诚实标注**：仓库已在 `PartitionedOperationPass.cs:265`、
`CpgShardStore.cs:459+`、`NLCPGGraphIndex.cs:905` 使用 `ArrayPool`。但 `Create()`
的巨型瞬态数组里 `CanonicalEdges` 是**冻结后长期存活**的（`NLCPGBuilder.cs:291,461`）
⇒ 池化必须先解决所有权/归还时机，否则只是把 LOH 数组换成永不归还的池数组。
**M2 不做为本次目标**，只在分片 adapter 的瞬态缓冲上顺带使用。

---

## 6. 大文件分片：S1 合并契约

已决策 **S1：K 份并行后合并为同一张图**。

### 6.1 为什么可行

现有 WorkBatch 路径已经是「worker 采集 → 单 reader reduce → `AddNode/AddEdge`」：

```text
PartitionedOperationPass.cs:160  worker: AnalyzeOperationPartition → OperationPartitionResult（只读事实）
PartitionedOperationPass.cs:177  reducer（单线程）: MaterializeOperationPartition → AddNode/AddEdge
```

`ControlFlowPass.cs:46-51`、`DominancePass.cs:357-379`、`ControlDependencePass.cs:61-69`
已用 `LocalCpgFragment` + `CpgFragmentReducer` 完成同样的形状。

⇒ **架构不需要重新发明，只需要保证所有 pass 都遵守「worker 产出候选 → 单写入物化」。**

### 6.2 分片必须遵守的四条契约

1. **节点身份是稳定锚，不是 NodeId。** 分片产出 `CpgNodeDescriptor`（带 `StableNodeAnchor`），
   归并时由 reducer 统一分配 NodeId（`CpgFragmentReducer.cs:51-53`）。分片内**不得**分配全局 NodeId。
2. **跨片引用必须显式化为 boundary reference。** `LocalCpgFragment` 已强制校验
   （`LocalCpgFragment.cs:33-42`：边的两端必须命中本地节点或显式 boundary）。
3. **interprocedural pass 必须等全部片完成。** 现有 `_interproceduralBarrierCompleted`
   标志位（`NLCPGBuilder.cs:81`）语义保留，只是 barrier 从「方法批之间」变成「分片之间」。
4. **`FreezeQueryIndex()` 只在全部归并后执行一次**（`NLCPGBuilder.cs:390`）。
   分片内不得调用（今天的 `ControlFlowPass.cs:66` 在**局部图**上调用是合法的，
   因为那是 `localGraph` 不是 `context.Graph`）。

### 6.3 明确的不确定（诚实标注）

⚠️ **本设计未验证**：把一个文件的方法集合切成 K 片后，各片的 Roslyn 语义查询
（`GetTypeInfo`/`GetSymbolInfo`/`GetOperation`）与不切片是否**逐节点等价**。
`SemanticModel` 是只读且线程安全的，但分片会改变 `GetOrCreateOperationNode` 等
**跨方法共享缓存**（`_syntaxNodes`/`_symbolNodes`/`_methodNodes`，见
`NLCPGBuilder.ReleaseTransientBuilderState:466-479`）的命中顺序。

**这是本设计最大的风险点**，必须在 Stage 4 用 DOP 等价契约测试正面证明，
未通过前**不得**开启大文件分片。执行计划把它单列为 Gate G4。

---

## 7. 配额：6 个 YAML 字段的重新解释

Schema 2 的 `execution` 要求 6 个正整数（`nlissn.schema.2.json:32`），契约测试已存在。
本设计**保留字段名与必填性**，把它们从「各层各自的 DOP」重新解释为
「同一内核上的类别在途上限」：

| YAML 字段 | 今天 | 合并后 |
| --- | --- | --- |
| `directoryMaxDegreeOfParallelism` | 目录池 DOP | **窗口内同时存活的文件数上限**（§4.4） |
| `cpgMaxDegreeOfParallelism` | CPG builder 请求的度 | 类别 `cpg` 在途上限；分片 DAG 的并发来源 |
| `groupMaxDegreeOfParallelism` | 规则 DAG DOP | 类别 `rule-group` 在途上限（`rules` 节点用） |
| `helperMaxDegreeOfParallelism` | helper 扫描 DOP | 类别 `helper` 在途上限 |
| `replayMaxDegreeOfParallelism` | replay DOP | 类别 `replay` 在途上限 |
| `maxConcurrentOperations` | 通用 admission 操作上限 | **全局**在途上限 |

`directoryMaxDegreeOfParallelism` 的语义变化**最大**：它不再控制「目录池同时调度的
文件数」（那个池已不存在），而是控制**分片窗口内同时存活的文件数**。
这是同一意图（限制目录级并行）在新架构下的落点，但**用户按旧语义配置会得到不同行为**，
必须文档明写 + runtime log 打印实际生效值（风险 R4）。

**worker 数 P 的定义（本设计新增的推导规则，不改 Schema）：**

```text
P = max(directory, cpg, group, helper, replay, maxConcurrentOperations)
```

理由：任一类别上限若大于 P 就无法被满足；取最大值使配置意图可达成，
且**不需要新增 YAML 字段**（`additionalProperties: false`，加字段会破坏 Schema 契约）。

三个布尔开关（`directoryParallelism` / `groupParallelism` / `helperParallelism`）
语义不变：false 时对应类别退化为串行（上限 = 1）。

---

## 8. 删除清单

| 目标 | 位置 | 前置条件 |
| --- | --- | --- |
| `BoundedConcurrencyPool` | `BoundedConcurrencyPool.cs`（1185 行） | 全部调用方迁移完成 |
| `ConcurrencyAdmissionController` + `ConcurrencyAdmissionOptions` + `ConcurrencyAdmissionRequest` | 同目录 | 老化提升迁入内核 |
| `ConcurrencyOperationTelemetry` + `IConcurrencyPoolTelemetrySink` | `ConcurrencyOperationTelemetry.cs` | 合并为 `WorkTelemetry` |
| `ConcurrencyWindowOptions` / `ConcurrencyWorkType` / `ConcurrencyExecutionPolicy` | 同目录 | 折叠进 `WorkSubmission` |
| `IConcurrencyPool` / `DependencyWorkItem` / `DependencyExecutionResult` | 同目录 | 折叠进内核 API |
| `CpgBuildAdmissionBudget` | `NLCPG/Builder/` | DOP 改由 `DocumentShardPlan` 传递 |
| `CpgWorkBatchExecutor` + `CpgWorkBatchExecutorOptions` | `NLCPG/Builder/Concurrency/` | 8 个 pass 改走内核 |
| `CpgWorkBatchBuilder` + `CpgWorkBatchCostModel` + `CpgWorkItem` + `CpgWorkBatch` | 同上 | ⚠️ **仅当** D1/D2（§13b）改由 `DocumentShardPlanner` 一并接管「标准大小批次组装」才可删；**否则保留**——「函数级并行放弃」**不**蕴含「批次组装放弃」（§13b.9） |
| `CpgWorkBatchQueueBudget` | `CpgWorkBatchExecutor.cs:690-729` | 字节记账上移内核 |
| `RunPartitionedOperationPassOrderedCompatibility` | `PartitionedOperationPass.cs:62` | **零引用，可直接删** ✅ 已于 2026-09-26 删除 |
| `ShouldUsePartitionedOperationBuild`（恒 true） | `PartitionedOperationPass.cs:435` | 直接内联 ✅ 已于 2026-09-26 删除（调用点已硬编码 `true`） |
| 4 个死参数（事实 B） | 见 §1.2 | 复活为 M1 或删除 |
| `NLCPGBuilderOptions` 4 个死阈值（事实 1.3） | `NLCPGBuilderOptions.cs:20-22,26` | 由 `DocumentShardPlannerOptions` 取代 |

**保留**：`LocalCpgFragment`、`CpgFragmentReducer`、`CpgNodeDescriptor`、
`CpgEdgeCandidate`、`CpgFragmentMetrics`、`FragmentOwnershipIndex`
（它们是 S1 合并契约的实现，不是要删的层）。

---

## 9. 统一遥测

`ConcurrencyOperationTelemetry`（14 字段）与 `CpgWorkBatchPerformanceEvent`（17 字段）
覆盖高度重叠：queue wait、峰值 active、峰值 buffer、elapsed、run id。合并为一个类型。

**已实现的 `WorkTelemetry`（10 字段，已随 S1-8 落地）**：

```csharp
public sealed record WorkTelemetry(
    string Category,
    int InputCount,
    int MaxConcurrency,
    int PeakActiveCount,
    int PeakReadyCount,
    long PeakInFlightBytes,
    TimeSpan MaxQueueWait,
    TimeSpan Elapsed,
    bool WasCanceled,
    string? RunId = null);
```

⚠️ **比初稿的 17 字段少**，这是有意的：初稿沿用了 `PeakBufferedResultCount`、
`PeakQueuedEstimatedCost`、`ReducerWait`、`AdmissionReason`、`FailureDrainElapsed`
等字段，但它们描述的是**被本设计删掉的概念**——缓冲是 `CommitOrdered` 的产物、
`AdmissionReason` 属于被删的准入控制器、`ReducerWait` 属于被删的 reducer 等待。
为一个已删除的层保留遥测字段会让「唯一遥测来源」自相矛盾。
`InputCount` 与 `MaxConcurrency` 保留了原 `SourceCount` /
`RequestedMaxDegreeOfParallelism` 的信息。

`RuntimeMeasurementLog.CreatePoolFields`（`:192-208`）与
`EmitPoolOperations`（`:210-233`）在 **S4 改为消费本类型**；
现有 `poolPeakActive`/`poolQueueWaitMaxMs` 等字段名**保持不变**，
以免破坏运行日志的既有消费方。新增字段以 `pool` 前缀追加。

**不可协商的约束（沿用现状）**：遥测写入失败不得改变调度、成功或取消语义。
已实现为 `WorkScheduler.EmitTelemetry` 内的 try/catch 吞掉，
并有 `RunAsync_WhenSinkThrows_SubmissionStillSucceeds` 正面固定。

---

## 10. 失败与取消语义

### 10.1 内核契约

| 场景 | 行为 |
| --- | --- |
| 单 WorkItem 抛出 | 记录首个失败；取消同一提交内的剩余项；**不影响其他提交** |
| 调用方 token 取消 | 贯穿排队与执行；已启动项按现有契约排空 |
| 内核 Dispose | 停止领取，等待在途项结束，释放 worker |
| 归并阶段失败 | 视为提交失败，向上抛出首个异常 |

### 10.2 per-file 隔离（新增，修正今天的 fail-fast）

今天 `SelectOrderedAsync` 首个异常即取消剩余全部文件
（`BoundedConcurrencyPool.cs:186-204`）。分桶后这是**退化**：一个坏文件杀掉整桶。

改为：`DirectoryAnalysisUseCase` 对每个文件项捕获异常，产出
`DirectoryFileAnalysisResult` 的失败变体（新增 `Failure` 字段或诊断码），
目录级结果记为降级而非崩溃。**这是行为变更，必须单列验收。**

### 10.3 嵌套契约（已实现，必须保留回归测试）

由于扁平化提交，理论上不存在嵌套死锁。但必须有**回归测试**固定这一点。

**已选实现：立即拒绝并给可诊断异常。** 内核用 `AsyncLocal<WorkScheduler>` 标记
「当前正在执行本内核的工作项」，在 `RunAsync` 入口检测到同内核重入时抛
`InvalidOperationException`，消息指明应改用单次提交内的 `Dependencies`。

为什么不做「内层提交在当前 worker 之外排队」：那需要 work-stealing 或额外线程，
是设计 §4.3 明确不引入的复杂度；而且它会把一个**结构性错误**静默变成
「能跑但慢」的形态，掩盖调用方违反扁平契约的事实。
**回归测试的作用是防止未来有人重新引入嵌套。**

---

## 11. 迁移阶段

每一阶段结束后仓库必须**可编译、测试全绿**。

| 阶段 | 内容 | 行为变更 |
| --- | --- | --- |
| **S1** | 内核独立实现（新文件），无人调用 | 无 |
| **S2** | 4 个活调用方改走内核（含规则 DAG 的 **4 个构造点**）；`IConcurrencyPool` 降为适配器 | 无（除修准入分片语义，见 §12 R1）— **S2-1/S2-2 已于 2026-09-24 完成（规则 DAG 路径，零回归）；S2-3/S2-4 未做** |
| **S3** | 8 个 CPG pass 迁移；规则阶段内联；**摊平为跨文件扁平 DAG**；删 `CpgWorkBatchExecutor` | **有**：`groupParallelism: true` 退化为串行（默认 false，故默认行为不变） |
| **S4** | 删死层与死参数；统一遥测；统一 DOP 解释；**Gate G4 分片等价性验证** | 无 |
| **S5** | 外部分片计划器 + 分片 adapter + per-file 隔离 + 分窗口提交 | **有**（Gate G4 通过后才开） |

⚠️ **S3 比初稿重得多**：扁平 DAG（§4.2）要求文件级工作项内部**不再有任何提交**。
仅迁移 8 个 pass 不够——`ApplicationService.RunAnalysis` → `RuleGraphAnalysisExecutor`
→ `RuleGraphExecutor` 是第二处内层提交（§4.1），必须一并内联（S3-1b），
然后才能摊平（S3-1c）。

---

## 12. 风险与不确定（诚实标注）

| # | 风险 | 严重度 | 缓解 |
| --- | --- | --- | --- |
| **R1** | 修准入分片语义会**真正开启文件级并行**，峰值内存可能上升 | **高** | 与 M1 字节额度同批上线；先用 DOP=1 建立内存基线，再逐档放大 |
| **R2** | 分片后 Roslyn 语义查询是否逐节点等价（§6.3） | **高** | Gate G4：DOP 等价契约测试未通过不得开启 |
| **R2b** | **扁平 DAG 使全部 fragment 同时存活**，与降峰值目标冲突（§4.4） | **高** | M1 字节额度 + 分窗口提交；窗口大小逐档实测 |
| **R3** | 全异步包装给同步 CPU 批引入开销 | 中 | **已实测（2026-09-24）**：直接 31.8 ms vs 包装 29.8 ms（5 轮最优），**离散度 ~10% ⇒ 只可称"落在噪声内"**；**未**证明 ≤1%，不得声称加速。探针已删 |
| **R4** | 6 个 DOP 字段重新解释后，用户按旧语义配置得到不同行为（`directory` 字段变化最大） | 中 | 文档明写 + runtime log 打印实际生效的类别上限与 P |
| **R4b** | `groupParallelism: true` 的配置在 `rules` 节点内**退化为串行** | 中 | 默认值为 false 故默认行为不变；非默认配置必须 runtime log 显式告警 |
| **R5** | 删除 `CpgWorkBatchExecutor` 时遗漏某个 pass 的 barrier | 中 | 8 个 pass 逐个迁移，每步跑该 pass 的定向契约测试 |
| **R5b** | 规则 DAG 有 **4 个**构造点（`RuleGraphAnalysisExecutor.cs:51`、`MarkingEngine.cs:34`、`DecisionModel.cs:455`、`MarkLiftingEngine.cs:103`），只改一处会漏 | 中 | 4 处全部迁移；grep `new RuleGraphExecutor` 必须归零 |
| **R6** | `EstimatedBytes = 行数 × 64`（`CpgWorkBatchBuilder.cs:7,224-227`）是纯估算，直接当硬额度会误限流 | 中 | M1 初版只做**观测与告警**，额度校准后再生效；`EstimatedBytes=0` 与单超项必须放行 |
| **R7** | 大文件分片后 `NPC.cs` 型的 interprocedural 边界边数量可能爆炸 | 中 | 沿用 `MaxBoundaryEdgesPerMethod`（`NLCPGBuilderOptions.cs:161`）上限 |
| **R8** | `RuleGraphExecutor` 用 `readyOrder` 比较器（`GraphOrderComparer`，`:238`）决定多就绪节点的执行序；内核就绪序若不同，**规则决策可能改变** | **高** | **已解除（2026-09-24）**：内核就绪序为 `Priority 升 → EstimatedCost 降 → StableOrder 升`，而规则项 `EstimatedCost` 全为 0，同优先级内退化为 `StableOrder` 升序，与原比较器等价。证据不是这段推理，而是 **HostTests 全量与 HEAD 干净基线的逐条比对：本树 7 条失败 = 基线同样 7 条 ⇒ 零回归**（见执行计划「S2 完成记录」）。`GraphOrderComparer` 已删除 |
| **R9** | **跨文件凑批（D1）** 需全局 `StableOrder`，而当前 `StableOrder` 是文件内局部序号；两个文件的同名方法会**碰撞**，内核**直接抛异常**（`WorkScheduler.cs:623`） | **高** | 先做全局单调序号（§13b.10 第 2 步），并有专属用例钉住「跨文件不碰撞」；**未完成前不得开启跨文件批次** |
| **R10** | **归并按项路由（D1）** 触 8 个 pass 的 `Commit*` 步；遗漏一处会把 B 文件的事实写进 A 文件的图（**静默错误，非崩溃**） | **高** | 逐 pass 迁移 + 每步跑该 pass 的定向契约测试；补「跨文件批次后每文件图签名与单文件路径逐字一致」的等价性断言 |
| **R11** | **`TakeReady` 的 O(N²) 派发**（`WorkScheduler.cs:283-326`，全程持 `_stateGate`）在扁平 DAG 无依赖项下暴露；批化能否压到可忽略区间**尚无数据** | 中 | §13b.10 第 1 步先实测常数；若不足则给就绪集换优先队列（与批化互补，非替代） |

---

## 11b. S2 实施记录（2026-09-24）

**已完成**：S2-1（接线到 `AnalysisRuntime`）、S2-2（规则 DAG 的 4 个构造点）、
S2-4 的 **2/3**（`RewritePlanReplayService.ReplayAsync`、`DecisionModel.ResolveUnits`）。
**未完成**：S2-3（目录层）、S2-4 的 `ForEachScan`——**两者被同一个结构性约束阻塞**（见下）。
故 `IConcurrencyPool` 与 `CpgBuildAdmissionBudget` **仍然活着**，一行未删。

### ⚠️ S2-2 使 S2-3 与 `ForEachScan` 不再可直接迁移（本次实施的关键修正）

§4.1 已指出嵌套根源在**文件分析内部**（规则 DAG + 8 个 pass）。但计划原先假设
「S2-3 只做一文件一项的最小迁移，文件项内部仍跑完整条流水线」——
**S2-2 落地后该假设失效**：既然规则 DAG 已改走内核，
「文件项内部跑完整条流水线」就**等于**「文件项内部再向同一内核提交」，
必然命中 §10.3 的嵌套守卫。同理，`ParameterShrinkAnalyzer.ForEachScan`
由规则体内部调用，S2-2 之后规则体本身就在工作项里，故也不能迁移。

**实证**（`tests/NLISSN.HostTests/Application/DirectorySchedulerBoundaryTests.cs`）：
1. 目录分析确实从文件项内部产生 `RuleGroup` 提交；
2. 把目录分析整体放进一个工作项 ⇒ **确实抛**含 "flat" 的 `InvalidOperationException`。

**结论**：S2-3 与 `ForEachScan` 的解锁条件同为 **S3-1b（规则内联）+ S3-1c（摊平）**，
即它们应当与扁平 DAG 一起完成，而不是作为 S2 的独立小步。
`AnalysisRuntime` 的生产构造**总是**创建非空 `Scheduler`（`ExecutionRuntime.cs:72`），
故这是生产形态而非测试专有形态。详见执行计划 Task S2-3 / S2-4。

### 计划外的 3 处必改（都已落地，理由见上）

1. `RunWithMetricsAsync` + `WorkExecutionOutcome<TResult>`（§3）。
2. `AnalysisRuntime` 新增可选 `WorkScheduler? scheduler` 注入参数——
   使测试能用 `WorkTelemetryCollector` 观测**内核真实生效的上限**，
   而不是继续依赖即将删除的 `IConcurrencyPool`。
   ⚠️ `InvalidateCaches` / `NextEpoch` 必须传**同一实例**：内核拥有长期 worker，
   每次派生都新建会持续泄漏线程。
3. `RuleGraphExecutor.BuildInputs` 的键从 `RuleNodeId` 改为 `long`
   （内核以 `StableOrder` 为键），内部用 `graph.NodeIndexes` 反查回 `RuleNodeId`，
   **键空间不泄漏给规则实现**。`NodeIndexes` 由 `RuleGraphCompiler.cs:104-108`
   稠密赋值 0..N−1，故 `StableOrder` 到结果下标的映射是保序的。

### 未注入内核时的生命周期

`RuleGraphExecutor` 的 `scheduler` 参数为 null 时（独立调用方与 5 处既有测试），
自建一个 `WorkSchedulerOptions.CreateDefault()` 内核，并在 `finally` 中
`DisposeAsync`。**这是为了避免把 P 个长期 worker 泄漏到进程生命周期之外**——
内核的 worker 一旦启动就常驻，故自建实例必须显式释放。

### 判定被重写而非放宽的测试

`IConcurrencyPool.RunDependencyGraphAsync` 不再是规则图路径，
断言旧层调用次数/力量的测试改为断言内核遥测中的真实上限（新断言更强）：

| 测试 | 旧断言 | 新断言 |
| --- | --- | --- |
| `CompatibilityStageEngines_WhenGroupParallelismIsDisabled_...` | `DependencyGraphMaxDegrees == [1,1,1]` | 4 条 `rule-group` 遥测，每条 `MaxConcurrency == 1`（S2-4 新增冲突域解析提交） |
| `Analyze_WithDefaultPipeline_UsesOneDependencyGraphSubmission` | `DependencyGraphInvocationCount == 1` | 恰好 1 条 `rule-group` 遥测 |
| `RuleDecisionEngine_Decide_SchedulesOnlyConflictResolution` | 池 `InvocationCount == 1`、`ItemCounts == [2]` | 末次 `rule-group` 遥测 `InputCount == 2` |

随之删除失去引用的测试替身 `DependencyGraphCountingPool`。

### S4-2 Step 3 追加修复（2026-09-24 轮次 21）：`SchedulerOptions` 必须来自内核

风险 R4 要求运行日志打印**实际生效**的类别上限。轮次 20 把日志从
「YAML 请求值」改为 `ResolveLimit(类别)`，但当时仍有一处会撒谎：
`AnalysisRuntime.SchedulerOptions` 不是读内核，而是把 `ExecutionOptions` **再映射一遍**。

由于构造签名允许注入 `scheduler`，一旦注入的内核配额与 `ExecutionOptions` 不同，
`SchedulerOptions` 描述的**是一个并未在运行的配额**，日志的
`*Effective` 与 `workerCount` 随之失真——**与 R4 同类**（报告值 ≠ 生效值）。

修复：`WorkScheduler.Options` 暴露其真实 `_options`；
`AnalysisRuntime.SchedulerOptions => Scheduler.Options`；删除私有构造中的重复映射。

先写**红测试**再修：`SchedulerOptions_WhenSchedulerIsInjected_DescribesThatSchedulerNotExecutionOptions`
注入 `WorkerCount = 9` 的内核而 `ExecutionOptions` 为 2，修复前报
`Expected: 9, Actual: 2`。

---

## 13. 不采用的方案

| 方案 | 不采用原因 |
| --- | --- |
| **N1 等待时帮忙（work-stealing）** | 扁平 DAG 后 worker 等待的是同 DAG 的其他节点而非另一个提交，不存在全被占满的稳态（§4.3）；实现可重入领取是纯增复杂度 |
| **N2 目录层扁平但保留文件内嵌套** | 无效：嵌套根源在文件分析内部（§4.1），只在目录层扁平化不消除它 |
| **保留函数级并行（方法批）** | 已决策放弃——⚠️ **仅指函数级*并行***；方法级**批次组装**（`CpgWorkBatchBuilder`）**保留**并放宽到跨文件（§13b.9、需求 D1/D2） |
| **桶升为内核一等概念** | 桶间公平可由成本降序 + 老化解决；预先引入逐桶额度会让内核多两个概念。记为备选 |
| **A1 单全局 P，废除 6 个字段** | 会静默破坏 Schema 2 契约与 `运行时并发配置.md`；A3 保留字段名，概念不增 |
| **A2 每次构图新建内核实例** | 保留 `CpgWorkBatchExecutor.cs:437-450` 的每次 `Task.Run` 起 P worker 行为 |
| **M2 缓冲区池化作为目标** | `CanonicalEdges` 冻结后长期存活，池化须先解决所有权；仅在瞬态缓冲顺带使用 |
| **为 `PendingEdgeBuffer` 设固定容量** | 已被 `nlissn-version4-dop12-diagnostics.md:1200` 否决（容量跨度 64 倍 + 12 并发反噬） |
| **依赖 `GCConserveMemory` / `GCHighMemPercent`** | 已被 `Context/progress.md` 轮次 14 四组 A/B 实测推翻 |

---

## 13b. 批次标准大小：跨文件凑批与函数完整性约束（2026-09-26 讨论记录）

**性质**：需求澄清 + 源码核实。**不含新测量**，不改动任何已完成任务的判定。
本节把两条新需求落到设计上，并**纠正讨论过程中的三处错误归因**：
①「跨文件凑批会降低并行度」（13b.3，**错**）；
②「拼标准大小是为抵消线程切换」（13b.4，**错**，真凶是派发与逐项分配）；
③「单个超大方法无法拆到标准大小」（13b.2，在**「保留完整函数」**这一约束下**这不成立**——
它是该约束的**必然结果**，不是缺陷）。
Gate **G4b** 仍为 `todo`；本节**不**声称其中任何一条已实现。

### 13b.1 需求（2026-09-26 用户给出）

| # | 需求 | 说明 |
| --- | --- | --- |
| **D1** | **跨文件凑标准大小** | 小/中文件的方法要能**跨文件**凑成一个标准大小的投递单元，不被文件边界截断 |
| **D2** | **大对象拆成若干标准组** | 大文件的方法集合要按标准大小分成 K 组投递 |

两条需求共享一条**硬约束**（13b.2）。D1 是 §5.1 表中「若干文件**合并为一个** `WorkItem`」
的实现化；D2 是 §5.1「**拆成 K 个** `WorkItem`」的实现化。二者同属 **G4b** 范围。

### 13b.2 硬约束：原子单位是**完整函数**，允许超出标准大小

**用户明确要求：必须保留完整的函数；函数长度不确定时可以超出一部分。**
⇒ 拆分与合并的原子单位都是**完整方法**，**禁止在函数体内部下刀**。

这条约束是**简化性**的，故写在最前——它**排除**了一整类原本代价极高的方案：

| 被排除的方案 | 为何不必做 |
| --- | --- |
| 方法体内部按基本块拆分 | 会破坏「完整函数」；且需动 `DataFlowPass` 的方法级 worklist 不动点（`DataFlowPass.cs:1020-1083`），属跨切点传播收敛边界 |
| 循环识别 / 回边检测 | 只有「函数体内切」才需要判断切点是否落在循环里 |
| 跨切点边界摘要（summary）契约 | 同上 |

**且**「超标准大小的单项允许超出」在当前实现里是**结构性天然满足**的：
`CpgWorkItemKind` 只有 `{FilePrelude, Declaration, Method}`，最小单位就是一个方法，
**结构上不可能切碎函数**。`Large`/`Oversized` 走单项成批（`CpgWorkBatchBuilder.cs:114-121`），
故「超标准但完整」正是当前行为。

⚠️ **第三处纠正（针对讨论中的错误表述）**：讨论中曾把「单个超大方法（如 `cost = 10,000`）
无法被拆到标准大小」当作一项**缺陷**甚至**不可达**。**在该约束下这不成立**：
既然必须保留完整函数，**超大方法就只能自成一项并超出标准大小**——
这是约束的**必然结果**，而不是能力缺失。用户已明确认可该形态
（「函数长度不确定性可以超出一部分」）。⇒ **D2 的目标是「可拆的大文件按方法分组」，
不是「把任意单个方法切到标准大小」**；后者与 13b.2 的约束**直接矛盾**，已排除。

⇒ **§6.3 的 DOP 等价性风险（R2）不因本节增加**：分片单位仍是方法集合，
`DataFlowPass` 的方法级不动点**完整保留**（其邻接「只含本方法内的邻居」，`DataFlowPass.cs:1040-1041`）。

### 13b.3 纠正：跨文件凑批**不**降低并行度

讨论中曾出现一条**错误推断**：「把 3 个文件合成 1 项 ⇒ 并行度从 3 降到 1」，
并据此认为 D1 与并行目标冲突。**该推断错误。** 源码核实的实际语义：

`TakeReady`（`WorkScheduler.cs:261-348`）每次只返回**一个** ticket，
而**每个 worker 在自己的循环里各调一次**（`WorkerLoopAsync`，`:208-251`）。
⇒ **并行度 = `min(P, 就绪项数)`**，`P = WorkSchedulerOptions.WorkerCount`（`:85-93`）。

| 情形 | 就绪项数 | 并行度 |
| --- | --- | --- |
| 不批化（每方法 1 项） | 方法数量级 | `P` |
| 批化到标准大小 | 约 161（**推算**：64,400 行 ÷ 400） | **仍是 `P`** |

只要**批化后项数仍 > P**，**并行度守恒**。目标规模（**实测**，已排除 12 个
`obj/bin` 生成文件）：`src/` 下 **444 个源文件**、合计 **64,400 行**；
而 `EffectiveCpgMaxDegreeOfParallelism = 12`（§1.1）。
⇒ 即使按最保守的「一文件一批」也有 444 项 > 12 ⇒ **并行度守恒成立**。

**批化在负载均衡上不劣于现状**：就绪序为 `EstimatedCost` 降序（`:300-305`），
而批次 cost **上界**为 `TargetBatchCost + mediumMaxCost − 1 = 599`
（`:123-127` 前置检查保证「加下一项前」不超，`:137-139` 后置检查在达到时立即 flush）。
⚠️ **只谈上界，不谈下界**：`[400, 600)` 这种说法是**错的**——反例：两个 `cost = 350`
的方法会各自成批（`350 + 350 > 400` 在前置检查触发 flush），两批均 `= 350 < 400`。
实际分布由分支决定：后置检查触发 ⇒ 恰为 `400`；前置检查或循环末 flush ⇒ `< 400`；
`Large` 单项批 ∈ `(200, 800]`；`Oversized` 单项批**无上界**。
⇒ 「cost 分布更集中」是**由上界推得**的**部分**结论（上界确实收窄了，
但小于标准大小的批次**依然存在**，且 `Oversized` 项不受约束），**未测量**；
本节**不**声称任何加速量，也**不**声称所有批次都达到标准大小。

### 13b.4 真实开销归因：**不是线程切换**，是派发与逐项分配

另一处归因需要修正：**CPU 密集路径上没有线程切换。**
同步工作经 `Task.FromResult` 包装（§3.2），而 `CreateInvocation` 的
`await ... .ConfigureAwait(false)`（`:775-778`）在 `IsCompleted` 时**同步继续**，
worker 全程停留在同一线程。

⇒ 拼标准大小**确实有必要**，但真实开销是下面三项（**均为分配或算法复杂度，非上下文切换**）：

| # | 开销 | 位置 | 量级 |
| --- | --- | --- | --- |
| 1 | **`TakeReady` 为 O(就绪数)/次派发 ⇒ 全程 O(N²)** | `:283-326` | 线性扫描整个 `Ready` + `List.RemoveAt` 搬移，且**全程持 `_stateGate`** |
| 2 | `Pulse()` **每项**新建一个 `TaskCompletionSource` | `:419-421`（每项完成调一次，`:413`） | 每项 1 次 TCS 分配 |
| 3 | `CreateInvocation` 闭包 + `async` 状态机 | `:753-778` | 每项 2 次分配 |

**第 1 项是唯一随 N 超线性增长的一项**（第 2/3 项均为 O(N) 线性），
且**只在扁平 DAG 下才暴露**：依赖边会让 `Ready` 保持较小，
而无依赖项在 `SeedReady`（`:524-537`）时**一次性全部就绪**，`Ready` 只减不增
⇒ 派发 N 项即 `N + (N−1) + … = O(N²)`。
⚠️ 「随 N 超线性」是**读源码得到的复杂度事实**；三项之间的**相对权重未测量**，
故本节**不**断言第 1 项在实际负载中占主导。

**规模推算**（`src/` 实测 64,400 行 ÷ `TargetBatchCost = 400` ≈ **161 项**）：
批化把 N 从「方法数量级」降到百余。⚠️ 但**这不能据以声称开销已可忽略**——
`O(N²)` 的常数**未测量**（附录 G 已建立"未测量即不得声称"的同一标准）。
⇒ 该常数正是 §13b.10 第 1 步要测的对象，且它决定第 3 步是否**必须同时**给就绪集换优先队列。
**「批次化」与「换优先队列」是两条互补的路，不是替代关系。**

### 13b.5 当前支持状态（源码核实，均为事实非推断）

| 需求 | 状态 | 依据 |
| --- | --- | --- |
| 小/中**同文件内**凑标准大小 | ✅ **已实现**（**有上界，无下界**，见 §13b.3） | `CpgWorkBatchBuilder.AppendMethodBatches`（`:103-148`）以 `TargetBatchCost = 400` 为**触发阈值**（`CpgWorkBatchCostModel.cs:58-63`） |
| 大对象**隔离**（不拆） | ✅ 已实现 | `CpgWorkBatchBuilder.cs:114-121`：`Large`/`Oversized` 单项成批 |
| 超标准单项**允许超出** | ✅ 天然满足 | 原子单位 = 方法，结构上不可切碎（13b.2） |
| **D2** 大对象拆成 K 组 | ❌ **未实现** | `DocumentShardPlanner`/`DocumentShardPlan`/`ShardPlanner` 在 `src` + `tests` **零命中**；Gate **G4b = `todo`** |
| **D1** 跨文件凑标准大小 | ❌ **未实现** | `CpgWorkBatchBuilder.Build` **强制单文件**（`:55-58` 显式 `throw`） |
| 文件级大小分类 | ❌ **不存在** | §1.3 已记录：三个 `LargeFile*` 阈值在 `src/` **零消费者** |

⇒ **两条需求都未实现，且都卡在 G4b 之前的同一层。**

### 13b.6 D1 的四条障碍（可枚举）

| # | 障碍 | 证据 |
| --- | --- | --- |
| 1 | **`StableOrder` 是文件内局部序号** | `DataFlowPass.cs:653`（`partition.Order`）、`CallGraphPass.cs:145`、`ControlDependencePass.cs:77`（`index`）。而内核要求单次提交内**全局唯一**，重复即抛（`WorkScheduler.cs:623`） |
| 2 | **批次被强制单文件** | `CpgWorkBatchBuilder.cs:55-58`；`CpgWorkBatch.SourceFilePath` 为单值（`CpgWorkBatch.cs:14`） |
| 3 | **worker 回调与 reducer 均绑定单文件** | `CallGraphPass.cs:104-113`：`operationWorkByOrder`（本文件视图）+ `PublishCallGraphWorkBatch(result, context.Graph)`（**本文件的图**） |
| 4 | **每文件一张独立图** | `NLCPGBuildContext.cs:87/108` 每文件 `new NLCPGGraph` |

障碍 4 是根本：**归并结果必须回到「它所属文件的那张图」**。跨文件批次要求
reducer **按项路由**到不同图，8 个 pass 的 `Commit*` 步都要改。
障碍 1 与 2 是**前置**且**独立可测**（13b.10 第 2 步）。

### 13b.7 已有的「多文件就绪」基础设施（本节最重要的正面发现）

数据模型**已经**按多文件设计，缺口比预期小：

| 能力 | 位置 |
| --- | --- |
| `StableNodeAnchor` 含 `FilePathId` | `StableNodeAnchor.cs:6` |
| `NodeId` 分配按 `FilePathId` 确定性排序 | `DeterministicNodeIdTable.cs:22-23` |
| 一个图可注册多文件源 | `NLCPGGraph.RegisterSource`（`:561`，内部 `_sourceByPath`） |
| 按文件检索节点 | `NLCPGGraph.GetNodesInFileSpan`（`:668`） |
| 跨文件引用类型 | `CpgBoundaryReferenceKind.External`（`CpgFragmentMetrics.cs:59`） |

⇒ D1 主要缺的是**全局 `StableOrder`** 与**归并按项路由**，不需要重建图模型。

### 13b.8 「把 work 结果合并到主图」解决的是**另一个**问题

讨论中曾提出「把每次的 work 结果直接合并到主图上」以解决 D1。**该机制本身已被本设计采纳，
但它不解决 D1：**

- **它解决的**：D2 的后半段——大文件 K 个分片产出 `LocalCpgFragment` 后
  **还原成该文件的一张图**（§6.1「S1：K 份并行后合并为同一张图」，§4.2 `reduce(F)`）。
  所需数据模型同理已就绪（13b.7）。
- **它不解决的**：D1。跨文件凑批的原子单位是**文件**（§5.1），
  合并进同一 `WorkItem` 的多个文件**内部顺序处理**；
  「合并到主图」不改变「一个 `WorkItem` 里放了几个文件」这一事实。

⚠️ 易混淆点：§4.2 的「同一张图」指 **`F` 的图**（每文件一张），**不是**一张全局主图；
`:324` 只说「单写入约束的粒度是每张图，不是全局」。

### 13b.9 与 §2.2 非目标、§13 的关系（**已就地修订两者**）

§2.2 原写「方法级 `WorkBatch` 组装（`CpgWorkBatchBuilder`）删除」，
§13 原写「保留函数级并行（方法批）」不采用。**D1/D2 使这两条必须精确化：**

> **放弃的是「函数级*并行*」，不是「方法级*批次组装*」。**

理由：`CpgWorkBatchBuilder` 的核心价值是**把方法按 cost 组装成标准大小的批次**，
而 D1/D2 正是该能力**在更大作用域上**的应用。若按原文字面删除该组装器，
D1/D2 将失去实现基础。⇒ **正确表述**：函数级**并行**（每方法一个并行项）放弃；
方法级**批次组装**保留，作用域由「单文件」放宽到「可跨文件的文件组」。
已就地修订 §2.2 与 §13 并回指本节。

### 13b.10 推进顺序（先便宜后贵）与未决问题

| 步 | 内容 | 代价 | 为什么在这个位置 |
| --- | --- | --- | --- |
| **1** | **实测 `O(N²)` 的实际常数**（N = 1k/10k/100k 个无依赖项的单词提交） | 纯测量，**不改生产代码** | 决定第 3 步是否**必须同时**给就绪集换优先队列（13b.4） |
| **2** | 引入**全局严格单调 `StableOrder`**，并放宽 `CpgWorkBatchBuilder` 的单文件断言 | 局部、独立可测 | D1 的前置；与图模型无关（13b.6 障碍 1/2） |
| **3** | reducer **按项路由**到所属文件的图（8 个 pass 的 `Commit*`） | 最大一块 | 障碍 3/4；须在 1/2 之后 |

**未决问题（不得在未决时声称 D1/D2 可交付）：**

1. **第 1 步是判据而非可选项**：若 `O(N²)` 常数很小，则 D1 只需解决语义问题、
   **不必碰 `WorkScheduler`**；反之两处都要改。**目前无任何数据**，故不预判。
2. **`WorkItem` 内部无法并行**：no-nesting 守卫（`:79-91`）对**跨实例**同样生效，
   且注释明确禁止在工作项内新建第二个内核。故「每个 `WorkItem` 内部仍按方法并行」
   **不可实现**。**但这不构成损失**——13b.3 已证并行度由**跨批次**提供（`min(P, N)`）。
3. **本节的「标准大小」仍是 cost 代理量**：执行计划附录 G 已证不存在可用 bytes 上界
   （同一行范围实测跨度 **1349×**），故 D1/D2 的「标准大小」**只能用方法体行数**定义，
   不得改用字节。

---

## 14. 相关入口

- 执行计划：[统一调度内核执行计划](2026-09-24-unified-work-scheduler-execution.md)
- 现状并发页面：[日志与并发](../CodeDesign/目前设计/日志与并发.md)
- 运行时并发配置：[运行时并发配置](../CodeDesign/目前设计/运行时并发配置.md)
- CPG WorkBatch（将被本设计取代）：[CPG WorkBatch 并发](../CodeDesign/目前设计/cpg-workbatch-concurrency.md)
- 内存机理实测：[Version4 DOP-12 诊断报告](../benchmarks/nlissn-version4-dop12-diagnostics.md)
