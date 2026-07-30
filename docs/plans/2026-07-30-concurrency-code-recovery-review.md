# Concurrency 组件代码回收审查

> 状态：审查提案。日期：2026-07-30。未修改生产代码。

## 结论

可以继续从 `NL.Concurrency` 回收代码，但目标应是缩小共享组件，不是以另一个通用并发框架替换它。当前 `IConcurrencyPool` 同时承载了三种不同层次的责任：有序批处理、CPG 的有界确定性物化、删除规则 DAG 调度；后两项各只有一个生产调用方，已经不构成共享基础设施。

建议按下面顺序处理：

1. 把 `RunDependencyGraphAsync`、`DependencyWorkItem` 和 `DependencyExecutionResult` 回收至 `src/NLISSN.Rule/` 的 `RuleGraphScheduler`。它的唯一生产调用是 `RuleGraphExecutor`，节点排序、依赖输入和失败语义都属于规则图。
2. 把 `CommitOrdered`、`CommitTwoStageOrdered` 与 `ConcurrencyWindowOptions` 回收至 `src/NLCPG/Builder/` 的 CPG 分区提交器。它们的唯一生产调用分别是 `PartitionedOperationPass` 和 `DataFlowPass`，并且 `retainedRecordCount`、重排窗口和单线程图物化是 CPG 的确定性契约。
3. 把仅供 `PartitionedSyntaxPass` 使用的 `SelectCpuBoundOrdered` 收回该 pass 的私有帮助器，或在该 pass 内以一个小型适配器封装 BCL 并行循环。不要为一个调用点维持接口方法、遥测枚举项和所有测试替身。
4. `ForEachAsync` 只有 `CpgShardBuildCoordinator` 与 `ParameterShrinkAnalyzer` 两个生产调用方。先为两者补齐“首个失败后不再启动新项、等待已启动项结束、保留索引”契约测试，再分别采用局部帮助器或 BCL `Parallel.ForEachAsync`。未证实语义等价前，不应直接替换。

保留 `SelectOrderedAsync` 作为唯一的共享执行原语，并暂时保留准入控制和遥测。前者有目录分析、规则决策和重写回放三个生产场景；后者虽然当前只由 `AnalysisRuntime` 构造，但仍负责跨这些场景的运行时资源上限、租约嵌套和观测。下一轮可再评估是否把“LatencySensitive/Throughput、两秒老化阈值”移至 Application 的运行时策略对象。

## 当前证据

公共接口位于 `src/NLISSN.Infrastructure/Concurrency/IConcurrencyPool.cs`，实现位于 `BoundedConcurrencyPool.cs`。生产调用按方法归类如下；测试替身也必须随接口缩减而删除或改为更小的局部替身。

| API | 生产调用 | 判断 |
| --- | --- | --- |
| `SelectOrderedAsync` | `DirectoryAnalysisUseCase`（两个路径）、`DecisionModel`、`RewritePlanReplayService` | 保留共享。返回输入序结果的契约跨删除分析、决策和回放复用。 |
| `SelectCpuBoundOrdered` | `PartitionedSyntaxPass` | 回收至 CPG。它只是同步版本的有序选择。 |
| `CommitOrdered` | `PartitionedOperationPass` | 回收至 CPG。提交动作物化图节点、边和分片。 |
| `CommitTwoStageOrdered` | `DataFlowPass` | 回收至 CPG。采集、准备、求解和图提交形成一个 CPG 专用执行计划。 |
| `ForEachAsync` | `CpgShardBuildCoordinator`、`ParameterShrinkAnalyzer` | 拆分或局部替换，不能先删除失败取消契约。 |
| `RunDependencyGraphAsync` | `RuleGraphExecutor` | 回收至规则模块。唯一节点类型是 `RuleNodeId`，调用方立即将结果投影为规则节点遥测。 |

`RunDependencyGraphAsync` 已含有无法从规则语义中抽离的行为：用调用方传入的比较器决定 ready 队列顺序，向节点委托传递前序结果，首个失败时取消同级任务、等待排空并记录 `FailureDrainElapsed`。见 [BoundedConcurrencyPool.cs](../../src/NLISSN.Infrastructure/Concurrency/BoundedConcurrencyPool.cs#L710) 和 [RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs#L240)。这不是通用列表并行化。

CPG 两个提交 API 也不是普通 map：`CommitOrdered` 以 `ReorderAllowance` 和 `MaxCompletedRecordCount` 限制完成缓冲，再严格按源序调用 `commit`；两阶段版本还在 collect/solve 之间共享同一个并发与保留窗口。见 [BoundedConcurrencyPool.cs](../../src/NLISSN.Infrastructure/Concurrency/BoundedConcurrencyPool.cs#L299)、[PartitionedOperationPass.cs](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs#L45) 与 [DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs#L331)。仓库约束要求并行工作只读 Roslyn 语义事实，图物化保持在稳定调用线程，因此这里的提交器应由 CPG 拥有。

## GitHub 一手源码对照

| 项目 | 固定源码与 API | 支持的结论 | 对本仓库的限制 |
| --- | --- | --- | --- |
| `dotnet/runtime` | [TransformBlock](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Tasks.Dataflow/src/Blocks/TransformBlock.cs)；[DataflowBlockOptions](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Tasks.Dataflow/src/Base/DataflowBlockOptions.cs)；[Parallel.ForEachAsync](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/Parallel.ForEachAsync.cs)；[ConcurrencyLimiter](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/ConcurrencyLimiter.cs)；[ConcurrencyLimiterOptions](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.RateLimiting/src/System/Threading/RateLimiting/ConcurrencyLimiterOptions.cs) | `TransformBlock<TInput,TOutput>` 在并行且 `EnsureOrdered` 时创建专用 `ReorderingBuffer<TOutput>`；`ExecutionDataflowBlockOptions` 把 `MaxDegreeOfParallelism`、`BoundedCapacity`、`CancellationToken` 和 `EnsureOrdered` 作为明确、独立的选项。`Parallel.ForEachAsync` 的 `ForEachAsyncState.RecordException` 在记录异常后取消内部 token，待所有 worker 结束后才完成任务。`ConcurrencyLimiter` 把 permit、队列顺序和队列上限单独配置，并以 lease 释放许可。这证实有界执行、失败取消和准入可以是小而独立的原语。 | Dataflow 的容量是消息容量，不等价于当前按 `retainedRecordCount` 和预估字节数限制的 CPG 缓冲；它也没有单线程 CPG commit 回调或两阶段共享窗口。`Parallel.ForEachAsync` 的公开 body 不提供输入索引，不能直接替换当前 `ForEachAsync`；`ConcurrencyLimiter` 也不表达当前的项目数/字节双预算、工作类别权重或老化提升。它们只能作为局部实现候选，不能直接取代两个 CPG 提交 API 或当前准入策略。 |
| `microsoft/BuildXL` | [DispatcherQueue](https://github.com/microsoft/BuildXL/blob/8799f48bd2e80a63b92489385a78a7633c9f5853/Public/Src/Engine/Scheduler/WorkDispatcher/DispatcherQueue.cs)；[Scheduler](https://github.com/microsoft/BuildXL/blob/8799f48bd2e80a63b92489385a78a7633c9f5853/Public/Src/Engine/Scheduler/Scheduler.cs) | `DispatcherQueue` 以领域 `RunnablePip`、优先级、权重和 `MaxParallelDegree` 调度；`Scheduler` 持有 pip 图、关键路径和失败/资源策略。它把依赖图调度留在构建领域，而不是提供一个跨产品的 `RunDependencyGraphAsync<TNode,TResult>`。 | BuildXL 的 pip、缓存和分布式执行远超规则图，不能复用其实现。可借鉴的是归属边界：规则图调度应回到 `NLISSN.Rule`，而非移植 BuildXL。 |
| `microsoft/vs-threading` | [AsyncSemaphore](https://github.com/microsoft/vs-threading/blob/4c53e3408b7cdf29a5a63544c48d9ccfd43d033f/src/Microsoft.VisualStudio.Threading/AsyncSemaphore.cs) | `AsyncSemaphore` 只负责 FIFO 准入、取消注册和可释放租约；它不兼任有序结果、DAG 或业务提交。这个最小边界支持保留或独立 `ConcurrencyAdmissionController`，而不是让执行池继续扩张。 | 当前控制器还包含双工作类别、资源预留和老化提升，语义比信号量多。不能直接替换；应先把这些策略的所有权从执行算法中分离出来。 |

所有链接固定到本轮读取的提交：`dotnet/runtime` `7a6987d6e1ca353163a8684054b71693c9db8fc6`、BuildXL `8799f48bd2e80a63b92489385a78a7633c9f5853`、vs-threading `4c53e3408b7cdf29a5a63544c48d9ccfd43d033f`。

## 迁移门槛

先缩接口，再移动实现；不要在一次提交中同时替换调度算法和调用方。

1. 为规则图迁移保留现有测试：依赖结果可见、ready 队列排序、首个失败取消同级、未满足依赖节点不启动、失败排空遥测。迁移后 `IConcurrencyPool` 不再暴露图类型。
2. 为 CPG 提交器保留 DOP 图等价测试：乱序完成但源序 commit、头部阻塞时的重排窗口上限、`ReorderAllowance == 0`、两阶段 collect/solve 缓冲共享、取消和异常时不发生越序物化。
3. 对 `ForEachAsync` 的每个调用方建立独立失败测试后才使用 BCL；BCL 只替代工作者循环，不替代本仓库对失败排空、索引或遥测的行为。
4. 最后删除 `IConcurrencyPool` 上已回收的方法、对应 `ConcurrencyOperationKind` 项和测试中的整接口转发替身，改为按领域创建窄替身。

完成标准是：接口只保留真正跨模块的有序选择和最小运行时资源契约；规则 DAG 与 CPG 稳定提交各由其领域拥有；所有既有 DOP、顺序、取消、失败排空和遥测测试在新边界上通过。
