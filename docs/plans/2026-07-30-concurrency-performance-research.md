# Concurrency 组件性能调研与执行建议

> 状态：源码调研。日期：2026-07-30。未修改生产代码。GitHub 星标在本轮页面读取时为：`dotnet/runtime` 18.1k、`dotnet/orleans` 10.8k；星标会变化，不作为技术结论。

## 结论

先优化调度器自己的常见路径，不提高默认 DOP，也不把 CPG 两阶段提交直接替换成 BCL Dataflow。当前最值得先做的三项是：

1. 遥测未启用时使用真正的 no-op 快路径，消除每项 `Interlocked`、峰值 CAS 循环和每次操作的 `Stopwatch` 分配。
2. 消除 `CommitTwoStageOrdered` 每轮的 LINQ 枚举、`Task[]` 构造和活动任务二次扫描；同时把 `ActiveWorkItem`、`CompletedWorkItem` 改为值类型候选并以分配基准决定是否保留。
3. 在 DAG 构图时一次性规范化依赖、建立入度和下游表；调度时复用规范化依赖，且在 DOP 为 1 时不再额外 `Task.Run`。

`SelectOrderedAsync` 的双层 `Task.Run` 是一个真实开销，但暂不应直接删除。现有回归测试要求委托在第一次 `await` 前阻塞时，仍能启动整个 worker window。应先把“真正异步委托”和“允许同步阻塞的委托”拆成明确的执行模式，再针对前者去除内层排队。

## 本地热点与边界

| 热点 | 源码证据 | 可能损耗 | 不能破坏的语义 |
| --- | --- | --- | --- |
| 未启用遥测 | `OperationTelemetryTracker` 始终创建 `Stopwatch`，且 `WorkItemStarted`/`WorkItemCompleted` 无条件执行原子操作。 | 小工作项下的固定 CPU 与分配开销。 | 启用 sink 时的峰值、取消、准入和失败排空数据必须不变。 |
| 两阶段有序提交 | 每轮构造 `headSolveTasks`/`activeTasks`，使用 `Where`、`Select`、`Concat`、`ToArray`，随后 `FirstOrDefault` 或 `Single` 找回完成任务。 | DOP 较高、工作很短时产生 O(DOP) 枚举和临时数组；记录包装对象也按每项分配。 | collect/solve 共享窗口、`RetainedRecordCount` 上限、严格输入序 prepare/commit。 |
| 规则 DAG | 初始化及节点启动处重复 `Dependencies.Distinct()`；每个节点构造新字典并 `Task.Run`。 | 重复遍历边、LINQ/字典分配，以及小节点的任务调度成本。 | ready 顺序、依赖结果可见、首错取消、已启动节点排空、环诊断。 |
| 异步有序选择 | worker 本身通过 `Task.Run` 启动，工作项又嵌套一次 `Task.Run`。 | 工作很短时增加 Task、ExecutionContext 和线程池队列开销。 | 委托首次 await 前阻塞时仍填满窗口；首错后不再启动排队项。 |

本地 `ConcurrencyPoolContractTests` 已覆盖乱序完成、窗口有界、首次 await 前阻塞、取消、DAG 环、失败排空和嵌套准入。这些是优化的回归门槛，而不是可删除的“实现细节”。CPG 还要求并发 worker 只读 Roslyn 语义事实，图节点、边和去重仅由稳定调用线程物化。

## 高关注度开源实现对照

| 项目与固定源码 | 源码模式 | 可借鉴之处 | 不应照搬之处 |
| --- | --- | --- | --- |
| [`dotnet/runtime` `Parallel.ForEachAsync`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/Parallel.ForEachAsync.cs) | 一个 worker 领取下一个元素后，才负责补充一个 worker；默认调度器直接用 `ThreadPool.UnsafeQueueUserWorkItem`，避开额外 `Task.StartNew`。首个异常记录后取消内部 token，最后一个 worker 才完成整个操作。 | 对“纯异步、无同步阻塞”的选择路径，采用固定 worker 状态对象和一次入队，而非每项双层 Task。失败处理应继续等已启动工作收敛。 | 它没有输入索引公开契约，也不支持本仓库的同步阻塞容错、提交窗口或准入租约，不能直接替换。 |
| [`dotnet/runtime` `ReorderingBuffer`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Tasks.Dataflow/src/Internal/ReorderingBuffer.cs) | 完成项以递增 ID 写入一个字典；若刚好是下一个 ID，直接输出，并连续释放已缓存的后继项。 | `CommitTwoStageOrdered` 应同样维护“下一个需要提交的 order”，完成即入按 order 索引的缓冲，连续释放；不要为寻找 head 反复构造任务集合。 | Dataflow 的重排缓冲不表达当前 collect/prepare/solve 的共享容量与记录预算，不能整体替换。 |
| [`dotnet/runtime` `BoundedChannel`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs) | 用固定容量队列和显式阻塞写入者链表实施背压；读取腾出容量后再唤醒写入者。 | 本组件的窗口也应把“容量、保留记录、可启动项”作为显式状态推进，不通过反复扫描推断。 | Channel 容量是消息数，不等价于 CPG 的记录数和字节预算；不要把它当作完成缓冲的直接替换。 |
| [`dotnet/orleans` `WorkItemGroup`](https://github.com/dotnet/orleans/blob/35e53810f1e8cc70c6996d5933504acc052544c3/src/Orleans.Runtime/Scheduler/WorkItemGroup.cs) | 每个 activation 有自己的队列和状态机；仅从 Waiting 转为 Runnable 时排入线程池，执行端批量 drain 队列。 | 调度状态应由单一拥有者维护，避免每一轮从活动任务集合重新计算状态。 | Orleans 的单 activation 串行语义与 CPG 并行计算不同，不适合作为执行器替换。 |

## 分阶段执行方案

### P0：先测量，建立等价门

为四条路径各建立小工作项和真实工作项两组基准，并拆分 wall time、分配字节、GC、线程池队列长度和已有并发遥测：

1. `SelectOrderedAsync`：立即完成、一次真实异步等待、首次 await 前阻塞。
2. `CommitTwoStageOrdered`：微型 collect/solve 与真实 `DataFlowPass` 小样本；记录每项分配和 head 阻塞时的吞吐。
3. `RunDependencyGraphAsync`：宽 DAG、链式 DAG、DOP=1、DOP>1；记录节点数和边数。
4. 关闭与开启遥测的对照；功能输出、提交顺序、峰值缓冲和失败排空必须等价。

不要将机器相关耗时阈值写成 pass/fail；将其作为诊断数据。任何真实 CPG 比较必须同时检查 DOP 图等价。

### P1：低风险、局部优化

1. 将 `OperationTelemetryTracker` 设计为静态 no-op 实例或独立无遥测路径。禁用时 `Report` 也不得创建 `ConcurrencyOperationTelemetry`；启用 sink 的路径保持原字段和原子更新。验收：无 sink 的分配下降，开启 sink 的遥测契约测试不变。
2. 将两阶段完成记录和活动记录改为 `readonly record struct` 的试验分支，并以 allocation benchmark 决定保留。随后以显式 `for`/`foreach` 替代热循环 LINQ，复用固定 DOP 容量的任务槽，而非轮次式 `Concat(...).ToArray()`。验收：所有窗口、乱序和异常测试通过，短任务分配下降。
3. DAG 初始化时构造 `IReadOnlyList<TNode>` 去重依赖、入度和下游表；调度时从该表构造依赖结果。DOP=1 使用捕获同步异常的直接 async helper，不包装 `Task.Run`。验收：同一图的结果、ready 顺序、循环错误、失败排空遥测一致；边遍历和 Task 分配下降。

### P2：按委托性质拆分异步选择

新增带明确定义的模式或窄 API，而不是悄悄改变现有 `SelectOrderedAsync`：

1. `AsyncOnly`：委托不得在返回 Task 前阻塞。worker 直接调用委托，借鉴 `Parallel.ForEachAsync` 的固定 worker 补充方式。
2. `BlockingTolerant`：保留内层线程池隔离，继续满足现有“首次 await 前阻塞也填满窗口”的回归测试。
3. 调用方仅在已证明为真正异步的 I/O 路径切到 `AsyncOnly`；CPU 路径继续使用 `SelectCpuBoundOrdered` 或 CPG 局部执行器。

这样才能把双层 Task 的开销从常见异步路径移除，同时不把线程池饥饿转化为功能回归。不要全局调用 `ThreadPool.SetMinThreads`，也不要仅靠提高 DOP 掩盖同步阻塞。

### P3：边界收敛后再优化

现有代码审查已确认 DAG 与 CPG 两阶段提交各只有领域调用方。将它们分别迁回规则图和 CPG builder 后，再按实际数据优化其内部状态机；共享组件仅保留真正跨模块的有序异步选择、准入和最小遥测。此步骤降低接口转发和跨领域抽象成本，但不是 P1 的前置条件。

## 预计收益与风险

| 优先级 | 改动 | 预期受益工作负载 | 风险 |
| --- | --- | --- | --- |
| P1 | 禁用遥测快路径 | 大量短任务、默认无 runtime telemetry 的运行 | 低；需覆盖启用 sink。 |
| P1 | DAG 依赖规范化及 DOP=1 直调 | 小规则图、边多的规则图、串行调试/小输入 | 低到中；同步异常和取消必须仍被捕获。 |
| P1 | 两阶段去 LINQ 与值记录 | 短 collect/solve、DOP 较高、频繁小分片 | 中；窗口条件容易被改坏。 |
| P2 | AsyncOnly worker 模式 | 真异步 I/O、每项工作较短 | 中到高；必须保留阻塞容错路径。 |
| P3 | 领域内收敛 | 长期维护、领域特化调度 | 中；属于重构，不能与算法替换同提交进行。 |

默认 DOP 调优排在最后。若真实 CPG 仍然主要耗在 Roslyn 语义查询或稳定图物化，调度器优化只能减少开销，不能跨越该串行边界。

## 验收清单

1. 串行运行 `dotnet build` 和相关并发、CPG contract、规则图 host 测试，避免共享输出目录锁冲突。
2. 保持输入序结果和提交；`ReorderAllowance`、记录上限和窗口跨度均不扩张。
3. 保持首错后不启动新项、取消已启动项并完成排空；DAG 环继续抛出同类异常。
4. 在 DOP 1 与目标 DOP 上比较 CPG 输出图；不得只以吞吐提高宣称成功。
5. 报告 allocations、GC 和吞吐的重复样本，区分微基准与真实源码运行。
