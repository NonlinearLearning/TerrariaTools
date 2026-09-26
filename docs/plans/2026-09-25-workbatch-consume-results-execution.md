# M3 WorkBatch 消费即释放 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 对只需要 reducer 副作用的调用方，取消已提交结果的累计强引用，保留旧收集入口的返回契约。

**Architecture:** 增加显式的 ExecuteAndConsumeAsync 入口；同步、异步仍共用现有处理和有序 reducer。内部以一次执行固定的 collectResults 标志决定是否建立结果列表，消费入口要求非空回调。先只迁移已确认忽略返回值的 DataFlow。

**Tech Stack:** C# / .NET Task、现有有界 Channel、CpgWorkBatchExecutor、xUnit。

---

日期：2026-09-25。feature：`workbatch-consume-results`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置、命令和门槛见 [总索引](2026-09-25-memory-optimization-execution-index.md)。本轮仅交付执行文档。

## 1. 取舍与成本

| 方案 | 决定 | 理由 |
| --- | --- | --- |
| 显式消费入口，共享执行内核 | 采用 | 消除长期列表根引用，不复制调度逻辑 |
| 自动根据回调存在与否取消收集 | 不采用 | 现有调用可能同时要回调和返回列表，属于契约变化 |
| 给所有结果加 IDisposable / 清空调用方对象 | 不采用 | 生命周期与业务所有权不是一回事 |
| 调大/调小 channel、统一调度器迁移 | 不采用 | 不解决 reduced 列表根引用，扩大成本 |

粗估 1–2 人日。只改 Executor、DataFlow 接线和对应测试。其他 pass 后续逐个审计，不顺手全量迁移。

## 2. 根因与边界

[CpgWorkBatchExecutor.cs](../../src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs) 的 `ReduceFragmentsAsync`
在回调后 `reduced.Add(nextResult.Result)`；`ExecuteSynchronously` 同样 `results.Add(result)`。
[DataFlowPass.RunDataFlowPipeline](../../src/NLCPG/Builder/Passes/DataFlowPass.cs) 不使用结果列表，
但结果对象的 Candidates 仍被列表持有，`MethodDataFlowPlan.Release` 没有释放它们。

[ControlFlowPass](../../src/NLCPG/Builder/Passes/ControlFlowPass.cs) 和
[ControlDependencePass](../../src/NLCPG/Builder/Passes/ControlDependencePass.cs) 取得完整 fragments 后统一发布，必须继续收集。
[G5](../../Build/g5-报告-N5-N6-N1.md) 的 channel 实验不否定本项；本项测存活对象，不把调度长尾作为收益目标。

消费后“不再累计保留”不等于所有结果立刻可回收：worker 当前值、reducer 当前值、channel 和乱序 pending 仍可能持有少量或长尾积压结果。
本项不宣称限制 CompletedNotReduced，也不把有界 channel 写成整个存活堆的上限。

## 3. 接口和内部变化

拟新增公开入口，参数顺序与旧 ExecuteAsync 对齐，回调必须提供：

```csharp
public Task ExecuteAndConsumeAsync<TResult>(
    IReadOnlyList<CpgWorkBatch> batches,
    Func<CpgWorkBatch, int, CancellationToken, TResult> processBatch,
    Action<TResult> consumeResult,
    CancellationToken cancellationToken = default,
    string? stageId = null,
    Func<TResult, CpgWorkBatchResultMetrics>? resultMetrics = null);
```

私有核心仍只保留一份，以 `collectResults` 贯穿同步、异步和 reducer。循环的核心差异：

```csharp
List<TResult>? collected = collectResults ? new(batchCount) : null;
// 保持原有 stable-order 排空规则。
consumeResult?.Invoke(result);
collected?.Add(result);
// telemetry 的调用位置、异常优先级与原实现一致。
```

旧入口固定 true，返回完整有序列表；新入口固定 false，只 await 共享核心的完成，内部返回共享空列表即可。
false 路径不得偷偷创建一个与批次数等长的空引用数组，也不创建“保存结果再清空”的过渡列表。
必须保留 _concurrencyPool admission、固定 worker 数、channel 容量、空输入和取消/失败清理。

Metrics 在结果被消费前按既有时机计算，不能把 DataFlow.Release 后的空字段当输出量。
诊断模式下 DataFlow 另存候选序列快照是既有明确需求；主收益测量关闭诊断，诊断开启的保留单列。

## 4. Task 1：增加可判定的生命周期护栏

**Files:**
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLifecycleTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchConsumptionTests.cs`。
- Test: `tests/NLISSN.PerformanceTests/Concurrency/CpgWorkBatchExecutorContractTests.cs`。
- Fixtures: 共享载荷/输入放 `tests/NLISSN.Testing/` 对应支持目录。

1. 新入口未实现时，编译或 API 测试应失败；现有收集入口的返回列表、stable order 测试须先通过。
2. 用可控事件而非 sleep 制造乱序完成，断言消费顺序与原入口一致；覆盖 DOP1 同步适配和 DOP2 异步。
3. 生命周期测试：多个带独立大 byte[] 的结果已消费，最后一批仍由事件阻塞；回调仅记录 ID，外部只保留 WeakReference。
4. 隔离生产函数/JIT 局部变量生存期，在已消费若干额外结果后，检查最早结果可回收；不要求 reducer 最近一个局部值立刻回收。
5. GC.Collect 只允许在隔离测试里使用。旧收集入口作为负对照，应仍持有全部结果；新入口不能累计持有早期结果。
6. 覆盖空批次、null 消费回调、入队前取消、分析中取消、worker 抛错、reducer 抛错；均不允许返回部分成功。

测试成本/稳定性要求：屏障超时必须明确失败并释放事件，防止挂住测试进程；不得捕获载荷到断言委托形成假保留。

## 5. Task 2：实现入口并迁移 DataFlow

**Files:** Modify `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs`、`src/NLCPG/Builder/Passes/DataFlowPass.cs`。

1. 抽出/扩展现有私有入口以传递 collectResults，不复制 workers、channel 或异常处理循环。
2. 两条 reducer 路径都条件收集；旧入口固定开启，新入口固定关闭。
3. 仅替换 RunDataFlowPipeline 的调用名，保留提交回调、resultMetrics 和 Release/finally。
4. 对照最后一个未完成 batch 的中间态存活对象数；“等整个阶段结束再测”不能证明生命周期改善。
5. 跑完整有序 DataFlow 图/候选序 oracle，并保留旧 API 返回值测试。

## 6. 验证命令与通过条件

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgWorkBatchLifecycleTests|FullyQualifiedName~CpgWorkBatchConsumptionTests|FullyQualifiedName~CpgWorkBatchDataFlowTests|FullyQualifiedName~DataFlowDiagnosticsTests'
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgWorkBatchExecutorContractTests'
```

先执行总索引构建。验收：

- 新入口的累计结果收集容器分配为零；旧入口列表数量、顺序和内容完全不变。
- 在阶段仍运行时，早期已消费结果不再被执行器累计保留；负对照明确失败，确认测试有区分力。
- 首个异常、取消传播、telemetry 数量/时机和 worker 并发护栏不变。
- DataFlow 完整有序输出相等，诊断开启时结果仍齐全；主测量不混入诊断快照保留。
- 阶段耗时满足共同门槛；主要收益报告为存活/晋升压力的结构改善，不伪造总分配显著下降。

若弱引用测试只有取消全部任务后才通过，或必须改调度机制才能成立，先排查隐藏根引用；不将本项升级为调度器重构。

---

## 7. 实施记录（2026-09-25）

feature `workbatch-consume-results` 已实施。改动落在两个产品文件和三个测试文件：

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs` | 新增公开入口 `ExecuteAndConsumeAsync<TResult>`；私有内核透传 `collectResults`；同步与异步两条路径均条件收集 |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | `RunDataFlowPipeline` 只改调用名（`ExecuteAsync` → `ExecuteAndConsumeAsync`），提交回调、`resultMetrics` 与 `Release/finally` 原样保留 |
| `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchConsumptionTests.cs` | 新增；13 条用例 |
| `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLifecycleTests.cs` | 新增 2 条旧入口返回契约用例 |
| `tests/NLISSN.PerformanceTests/Concurrency/CpgWorkBatchExecutorContractTests.cs` | 新增 3 条消费入口的 worker/channel/取消护栏用例 |

### 7.1 实现要点（对齐 §3）

- 旧入口 `ExecuteAsync` 固定 `collectResults: true`，返回完整有序列表；新入口固定 `false`。
- `false` 路径返回共享的 `Array.Empty<TResult>()`，**不**创建与批次数等长的空引用数组，也**不**创建“先保存再清空”的过渡列表。
- `_concurrencyPool` admission、固定 worker 数、channel 容量、空输入短路和取消/失败清理均未改动。
- `resultMetrics` 在消费回调之前按原时机求值，故度量不依赖 `Release` 之后的字段。

### 7.2 定向测试证据

两条命令来自 §6（`-p:` 需加引号，否则 PowerShell 把 `-p` 视为 `-ProgressAction` 的歧义前缀）：

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore '-p:UseSharedCompilation=false' --filter 'FullyQualifiedName~CpgWorkBatchLifecycleTests|FullyQualifiedName~CpgWorkBatchConsumptionTests|FullyQualifiedName~CpgWorkBatchDataFlowTests|FullyQualifiedName~DataFlowDiagnosticsTests'
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj --no-restore '-p:UseSharedCompilation=false' --filter 'FullyQualifiedName~CpgWorkBatchExecutorContractTests'
```

| 运行 | 结果 | 日志 |
| --- | --- | --- |
| 实施前基线（不含新入口） | Contract 20/20；Performance 5/5 | `Build/M3-workbatch-consume/baseline/` |
| 实施后 Contract 定向 | **35 通过 / 0 失败 / 0 跳过** | `Build/M3-workbatch-consume/after/contract-targeted.final.log` |
| 实施后 Performance 定向 | **8 通过 / 0 失败 / 0 跳过** | `Build/M3-workbatch-consume/after/perf-executor-contract.final.log` |
| 全部 `CpgWorkBatch` Contract 用例 | **69 通过 / 0 失败** | `Build/M3-workbatch-consume/after/contract-all-workbatch.log` |

`NLCPG.csproj` 与 `RoslynDeletionPrototype.ContractTests.csproj` 构建 0 警告 0 错误。

### 7.3 区分力（变异检查）

生命周期用例若对实现不敏感就没有价值，故做了变异检查：把消费入口的
`collectResults: false` 改回 `true`（即让新入口偷偷重新累计结果），只跑
`CpgWorkBatchConsumptionTests`：

- 结果：**12 通过 / 1 失败**。
- 失败者正是 `ExecuteAndConsumeAsync_WhileLastBatchIsBlocked_EarlierConsumedResultsAreCollectable`，
  断言 `Expected: 0 / Actual: 6`——即执行器仍保留前 6 个已消费结果。
- 负对照 `ExecuteAsync_WhileLastBatchIsBlocked_StillRetainsConsumedResults` 在两次运行中都通过。

补丁已用 SHA256 备份还原，还原后哈希为 `0460B07E…416B9C`，定向 Contract 复跑仍为 34/34。
日志：`Build/M3-workbatch-consume/mutation-collect-true.log`。

### 7.4 结构判据（容器分配）

`ExecuteAndConsumeAsync_DoesNotAllocateResultCollectionContainers` 用同步路径
（`useSynchronousExecution: true`，全部工作都在调用线程上）做确定性差分：两条路径返回
**同一个**预构造载荷实例，使逐 batch 的载荷分配恒等，差值只剩收集入口独有的结果列表后备数组。
断言收集入口比消费入口至少多分配 `batchCount * 8` 字节（x64 引用宽度），且消费入口确实低于收集入口。
绝对字节数不作为判据——telemetry 等共享机械的分配在两条路径上都存在。

### 7.5 边界与未覆盖

- 本项只报告**结构改善**：执行器不再长期累计已消费结果。**未**测量进程峰值内存、晋升率或端到端分配总量，
  也不声称任何显著下降。
- 消费后“不再累计保留”**不等于**结果立刻可回收：worker 当前值、reducer 最近局部值、channel
  和乱序 pending 仍会临时持有少量或长尾结果。本项不限制 `CompletedNotReduced`，也不把有界 channel
  写成整个存活堆的上限。
- 只迁移了已确认忽略返回值的 `DataFlowPass.RunDataFlowPipeline`。
  `ControlFlowPass` / `ControlDependencePass` 取得完整 fragments 后统一发布，必须继续收集，本轮未动。
- 诊断模式（`DataFlowDiagnostics != null`）下 DataFlow 另存候选序列快照属既有明确需求，保留单列；
  本项未改其保留语义。
- 弱引用测试在阶段**仍在运行**的中间态判定（最后一批由事件阻塞），不是“等整个阶段结束再测”。

### 7.6 环境诚实边界

实施期间工作树上有**其他并发工作流**在改同一批文件（`NLCPGBuilder.cs`、`DataFlowPass.cs`、
`tests/NLISSN.ContractTests/Cpg/*`）。它们多次产生与本项无关的编译错误，验证因此需要轮询重试直到
其编辑落定。上述通过数字取自**这些并发编辑已恢复一致**的构建；同一断言在多个不同时间点各复跑一次均为绿。

`Run-TestTiers.ps1 -Fast` 全量结果：**696 通过 / 4 失败 / 0 跳过（共 700）**，耗时 4 m 50 s
（`Build/M3-workbatch-consume/after/tier-fast.log`，trx
`Build/TestResults/20260925-070955-2f81b91e/contract.trx`）。
其中 **`CpgWorkBatch` 相关 69 条全部通过**。4 条失败**全部落在本项未触碰的文件**，均属其他工作流：

| 失败用例 | 归属（我未触碰） |
| --- | --- |
| `InterproceduralPlanCompactionTests.ReclaimIdleSortBufferCapacity_ActuallyReplacesOverBudgetSlots` | `??` 新文件，其他工作流 |
| `CpgFragmentByteCalibrationTests.EstimatedBytesAndFragmentBytes_MeasureDifferentQuantities_SoTelemetryIsNotACalibration` | `??` 新文件，其他工作流 |
| `NLCPGPartitionedBuilderTests.BuildFromSource_PartitionedSyntaxPass_PreservesGraphsAcrossDegreesOfParallelism` | `M`，其他工作流；失败原因是并发 `testhost` 锁住 `RoslynDeletionPrototype.ContractTests.dll`（`IOException`），非断言失败 |
| `NLISSN.Tests.Architecture.LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph` | 由其他工作流改 `src/NLCPG/NLCPG.csproj`、`src/NLISSN/NLISSN.csproj` 引入的既有项目引用期望不符 |

同理，`Run-TestTiers.ps1 -Fast` 首轮曾以 `MSB3027`/`MSB3021` 结束：其他工作流的并发 `testhost`
（PID 31184）锁住了输出 DLL，属**文件锁竞争**而非构建或测试缺陷；未杀该进程，等它自行退出后重跑。

`check-harness-consistency.ps1` 全量通过（`[check-harness-consistency] OK`，exit 0）。
注意：该脚本的 CLI 冒烟在 `finally` 里删除 `%TEMP%` 下的临时目录；本机 `C:\WINDOWS\TEMP`
对该删除操作返回“拒绝访问”，故直接运行会以 exit 1 结束——这是**环境**问题而非一致性失败。
把 `TEMP`/`TMP` 指向可写目录后完整通过。

### 7.7 变更文件哈希（SHA256）

| 文件 | SHA256 |
| --- | --- |
| `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs` | `0460B07E3A90F8E17711673B13E97F8F07C319FC605E67CC5B29C9EA9F416B9C` |
| `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchConsumptionTests.cs` | `268C7AE412EB9E917C843E70234889233AE252D85A77A0171880515A7C34364B` |
| `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLifecycleTests.cs` | `424B49F1A5240707FF441C01C6D7A7C870348A9A8E6BAB092BCC81253F204F04` |
| `tests/NLISSN.PerformanceTests/Concurrency/CpgWorkBatchExecutorContractTests.cs` | `1B307250BA796B69569C37B9CCCA50E5CF6DC3E5A0CFA73452A4022248A7104E` |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | `D69448111D9C7B194C5742593BA261E7BECB8D9AA4429A1CFE5CD9F188C85120`（**含并发写入者的改动**，本项只占 :493 一处调用名） |

`CpgWorkBatchExecutor.cs` 在工作树中是**未跟踪**文件（`??`），故不能用 `git diff HEAD` 当基线，
本节哈希即为此项的改动证据。
