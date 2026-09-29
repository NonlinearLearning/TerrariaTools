# NLCPG 项目级 WorkBatch 池设计

> **状态：已废弃 —— 项目级池源码已删除（2026-09-24）。**
>
> 本文描述的项目级 WorkBatch 池（project planner、bounded producer/worker/result sink、
> 稳定 reducer、`ProjectCpgWorkBatchExecutor`、`ProjectWorkBatchBudget`、
> `ProjectCpgWorkBatchTelemetry` 等）已从仓库中移除：`NLCPG.ProjectExport` 不再有并行池，
> 逐文档导出是唯一的生产导出路径。保留本文仅作决策历史与设计依据记录，**不要据此
> 实现对源码的期望**；当前有效设计见 [NLCPG WorkBatch 并发设计](cpg-workbatch-concurrency.md)。
>
> 导出实现的当前形态见 [NLCPG.ProjectJson 组件](#nlcpgprojectjson-组件2026-09-24)。
>
> 读者：维护 `NLCPG` 构图、CPG 导出、Roslyn project compilation、CPG 持久化和性能诊断的开发者。
>
> 关联：[CPG WorkBatch 并发设计](cpg-workbatch-concurrency.md)、[CPG WorkBatch 项目级执行计划](../../plans/2026-09-21-cpg-project-workbatch-pool-execution.md)、[Version4 基线](../../benchmarks/cpg-workbatch-version4.md)。
>
> 更新时间：2026-09-21（2026-09-24 标注废弃）。

## NLCPG.ProjectJson 组件（2026-09-24）

导出功能已抽为可复用组件，与 CLI 分离：

| 位置 | 角色 |
| --- | --- |
| `src/NLISSN.Infrastructure/ProjectJson/`（项目 `NLCPG.ProjectJson`，命名空间 `NLCPG.ProjectJson`） | 库组件：`ProjectJsonExporter`（逐文档导出 + manifest）、`ProjectExportOptions`、`ProjectExportResult`、`ProjectExportConfigurationLoader` |
| `src/NLCPG.ProjectExport/`（项目 `NLCPG.ProjectExport`，Exe） | 薄 CLI：只解析 `nlissn.yml` 并调用组件 |
| `artifacts.projectJson`（NLISSN 配置） | NLISSN 运行内联导出：分析完成后对同一 `input` 执行导出，默认关闭 |

### 导出范围与输出布局（2026-09-24）

- **覆盖全部项目**：`.sln` 输入导出其全部 C# 项目；`.csproj` 输入导出该项目。CPG 的公开
  构图边界仍是**单个文档**（`BuildFromSource` / `BuildFromSemanticModel`），因此"完整导出"
  指每个文档都有 payload 加一份全局 manifest，而不是一个跨文档的统一图。
- **布局按输入类型确定**：`.sln` → `<output>/<ProjectName>/<项目内相对路径>.json`；
  `.csproj` → `<output>/<项目内相对路径>.json`。项目子目录名需要去重（`-2`、`-3`）。
- **manifest 是完整性契约**：始终写入 `<output>/manifest.json`，含 `status`
  （`complete`/`incomplete`）、`totalDocuments`、`writtenFiles`、`failedFiles`、`projects[]`
  （每项目的 `outputDirectory`/`status`/计数/`files[]`），以及顶层跨项目并集的 `nodes`/`edges`。
  顶层 `files`/`nodes`/`edges` 与 `project` 字段保持 schemaVersion 1 的既有形态，
  使单项目消费方无需改动。
- **失败隔离**：单文档失败记 `NLCPGEXP002` 并继续；整项目失败记 `NLCPGEXP003`，其余项目继续；
  无项目记 `NLCPGEXP001`；导出级致命错误记 `NLCPGEXP004`；复用了不可读的旧 payload 时记
  `NLCPGEXP005` 并重建该文件。
- **`ProjectExportResult.Succeeded`** 只表示"未因致命错误中断（manifest 已写出）"；
  个别文件/项目失败属于可继续的降级，由 `Status` 表达，避免调用方把降级误判为崩溃。

## 1. 决策摘要

> 以下内容描述**已删除**的池实现，仅作历史记录。


当前源码已包含 project planner、bounded project producer/worker/result
sink、稳定 reducer、DOP1 synchronous local adapter、resume 分流和 catalog/
manifest 边界。本文中的 DOP 等价、Version4 内存和性能条件仍需运行对应
Contract/Performance/acceptance 命令后才能标记为通过。

当前 `NLCPGBuilder.BuildFromSemanticModel` 的公开构图边界仍是完整文档，
因此 exporter 先用 local planner 发现并估算 file-prelude/method batches，
再把同一文档的局部 batches 聚合为一个完整 document envelope。这样 project
pool 不会对同一文档重复构图或重复写 payload；真正的局部 fragment 分批构图
仍需后续扩展 builder adapter 后才能单独放入 project sink。

## 1. 决策摘要

`NLCPG.ProjectExport` 采用一个全项目级 WorkBatch 池。项目加载完成后，先发现全部文档的 file-prelude 和方法工作项，再把所有文档的局部 `CpgWorkBatch` 放入同一个有界队列，由固定的 12 个长期 worker 消费。每个 worker 内的 NLCPG 局部构图 DOP 固定为 1，不再为当前文档创建第二层 12-worker executor。

```text
Roslyn project compilation
  -> 全部文档发现、resume 扫描、稳定排序和成本估算
  -> project-level WorkBatch producer
  -> 一个有界 project WorkBatch channel
  -> 固定 12 个长期 worker，worker 内 NLCPG DOP = 1
  -> 有界 LocalCpgFragment / FileExportProjection sink
  -> 稳定顺序 reducer
  -> 单写入 catalog 和 payload
  -> manifest 原子替换
```

这里的“全项目级”指调度和准入边界跨越整个 project，不要求一个 batch 同时包含多个文档。现有按文档生成的 `CpgWorkBatch` 仍是局部不可变工作单元，由项目级 envelope 携带文档身份和全局稳定顺序。这样可以复用已完成的成本模型、fragment 合同和 reducer 语义，同时消除 `foreach document -> new builder -> DOP12` 的串行外层瓶颈。

## 2. 当前问题

当前导出路径的主要结构如下：

```text
WorkspaceProjectSnapshot
  -> ResumeExportAsync
  -> foreach document
       -> new NLCPGBuilder(DOP = 12)
       -> BuildFromSemanticModel(document)
       -> 写一个 payload
  -> 汇总 catalog
  -> 写 manifest
```

这种结构有三个问题：

1. 顶层一次只允许一个文档进入构图。小文档、无方法文档和收尾阶段不能为 12 个 worker 提供足够的 CPU 工作。
2. DOP12 只属于当前文档内部的 builder，并不代表 12 个文档同时运行；如果单个文档方法少，worker 数量会迅速降到很低。
3. 文档循环持有 project compilation、workspace snapshot、catalog 和中间集合的生命周期。完成最后一个文档时，manifest 查询、SQLite 读取、GC 和 Roslyn 对象释放可能与构图峰值重叠，放大内存异常。

Version4 的 DOP12 运行曾经在接近完成时出现 `Out of memory`，但 D 盘仍有足够空间；这证明问题不能简单归因于磁盘空间。具体基线和未完成边界记录在 [Version4 基线](../../benchmarks/cpg-workbatch-version4.md)。项目级池必须同时处理 CPU 供给和结果、catalog、Roslyn 对象的有界生命周期。

## 3. 目标与非目标

### 3.1 目标

- 对一次 project export 创建一个固定的 12-worker pool，worker 生命周期覆盖整个计算阶段。
- 把所有尚未由有效 resume payload 覆盖的文档放入同一个 project-level producer/worker/sink 管线。
- worker 内只执行 DOP1 的局部 CPG 工作，不嵌套创建线程、task-per-method 或第二个 WorkBatch pool。
- 让有足够可运行 CPU batch 时 `ActiveWorkerCount` 达到 12，并用 telemetry 解释无法达到时是队列、reducer、catalog、Roslyn barrier、GC 还是输入不足。
- 限制待运行 batch、已完成未归并 fragment、待写 payload 和 catalog 投影的数量及估计字节数。
- 保持 graph、query、rule、rewrite、diff、payload、catalog 和 manifest 的确定性。
- 失败、取消或内存水位超限时不发布半成品 manifest，已完成的 payload 仍可由 resume 流程识别。

### 3.2 非目标

- 不通过忙等、空循环或人为 `Task.Run` 制造 100% CPU。SQLite、文件 I/O、GC、Roslyn barrier 和 manifest 收尾期间 CPU 降低是可接受状态，但必须可诊断。
- 不把全项目所有节点和边重新积累成一个超大内存对象。project-level pool 不是取消流式持久化的理由。
- 不在 worker 中直接写共享 `NLCPGGraph`、共享 `NodeId` 分配器、SQLite connection 或 manifest 文件。
- 不在本阶段把跨文档调用图改成无 barrier 的并行写入。跨文档语义仍由已有 call graph 和 interprocedural barrier 约束。
- 不保证任意小项目在任何时间都有 12 个可运行 batch；当可运行 batch 少于 12 个时，物理上不存在足够的计算工作。

## 4. 调度对象与稳定身份

### 4.1 局部 WorkBatch 保持不变

已有 `CpgWorkBatch` 仍表示一个文档内的不可变工作集合，字段包括 `BatchId`、`SourceFilePath`、`StableOrder`、`Items`、成本、估计节点数、估计字节数和 batch kind。它的 `StableOrder` 只在文档范围内有意义。

项目级池增加一个 envelope，而不是让 `CpgWorkBatch` 偷偷变成可变的多文档对象：

```csharp
public sealed record ProjectCpgWorkBatch(
  long ProjectBatchId,
  int DocumentStableOrder,
  string SourceFilePath,
  CpgWorkBatch LocalBatch,
  int GlobalStableOrder,
  int EstimatedCost,
  int EstimatedBytes,
  ProjectCpgWorkKind Kind);
```

实现时如果沿用现有类型命名，必须保留这些语义：

- `ProjectBatchId`、文档顺序和 local batch 顺序来自稳定输入，不得来自 worker 完成时间、线程编号或 channel dequeue 顺序。
- `GlobalStableOrder` 对一次 project run 唯一。它用于 reducer 和 telemetry 关联，不要求 worker 按此顺序执行。
- `SourceFilePath` 使用 project-relative normalized path 作为持久化和排序身份；绝对路径只保留在运行时 workspace 解析边界。
- file-prelude、method batch 和 resume import 是不同的 kind。resume import 不应伪装成 CPU 构图 batch。
- project planner 只调度 outermost method。local function、lambda 和嵌套 operation 属于 owning method 的工作范围，不能重复排队。

### 4.2 全项目规划顺序

producer 使用以下稳定顺序发现工作：

1. project-relative path，Ordinal 比较；
2. 文档的 Roslyn document id 或稳定 source identity；
3. file-prelude；
4. 方法的 source span 起点、终点、symbol key 和 work item kind；
5. 现有 `CpgWorkBatchBuilder` 的 target/max cost、方法数和估计字节限制。

文档完成顺序不参与身份。即使 worker 先完成后面的文档，reducer 仍按 `GlobalStableOrder` 消费；catalog 的 SQL 主键和 manifest 的节点、边顺序仍按既有稳定 key 输出。

## 5. 全项目数据流和 barrier

```text
B0  WorkspaceProjectSnapshot + Roslyn Compilation 准备
    |
B1  读取已有 payload、确定待计算文档、发现全项目 work item
    |
B2  全项目 WorkBatch producer -> bounded channel
    |
B3  12 worker 处理文档局部 batch，返回 fragment/projection
    |
B4  project reducer 建立稳定投影、节点和跨文档边的去重状态
    |
B5  catalog/payload 单写入阶段完成，SQLite 查询游标关闭
    |
B6  manifest 读取最终 catalog，写临时文件并原子替换
```

CPG 内部若仍需要已有的 Syntax、Operation、CFG、Dominance、DataFlow、CallGraph 和 interprocedural barrier，则 B3 内部继续遵守 [CPG WorkBatch 并发设计](cpg-workbatch-concurrency.md) 的阶段顺序。项目级池只改变“哪些文档的局部 batch 共享 worker”，不跳过语义 barrier。

### 5.1 worker 阶段

worker 可以读取同一个 project `Compilation`、只读 `SyntaxTree`、只读 `SemanticModel` 和已经发布的只读 symbol index。worker 产生 `LocalCpgFragment` 或 document-level `FileExportProjection`，释放临时 Roslyn 集合后再把结果交给有界 sink。

worker 不得：

- 修改 `Workspace`、`Project`、`Document` 或 compilation；
- 调用 `Task.Run` 处理单个 method 或 operation；
- 把完整 `SemanticModel`、`SyntaxNode`、`IOperation` 放入 fragment；
- 写 SQLite、payload、manifest 或共享 graph；
- 在 project pool 内调用一个新的 DOP12 `NLCPGBuilder`。

### 5.2 跨文档语义

project compilation 仍是跨文档符号解析的来源。若某个 CPG 构建阶段需要全局 method index、call target 或 method summary，则先完成对应局部 fragment，再进入已有的 global resolve barrier。未解析的 endpoint 必须保持 `unknown` 或 unavailable，不能因为另一个文档尚未完成而猜测边。

第一版可以按文档返回 projection，并由 catalog 以稳定 edge key 去重；只有在语义要求完整 project graph 时，才在 B4 建立跨文档 summary index。不能为了填满 worker 而复制完整 project graph 到每个 worker。

## 6. 固定 12-worker 池

### 6.1 核心不变量

- 一次 project run 最多创建 12 个长期 worker task；生产配置的有效值为 12。
- 每个 worker 只拥有一个执行循环，处理完一个 envelope 后继续从同一 project channel 领取工作。
- worker 调用的 NLCPG builder 或 batch adapter 使用 `MaxDegreeOfParallelism = 1`。
- project-level admission 是唯一 CPU worker 准入边界。单文档 builder 不得再次向全局 `IConcurrencyPool` 申请 12 个 slot。
- queue、fragment sink 和 catalog writer 均有界。队列满时 producer 异步等待，不创建额外补偿 task。
- 不以 worker 数量等同 CPU 使用率。`ActiveWorkerCount=12` 是可运行 CPU batch 时的调度合同；当 worker 在等待有界 sink 或 barrier 时，`IdleReason` 必须说明原因。

### 6.2 固定 worker 的启动和停止

启动顺序为：创建 linked cancellation、创建 bounded channels、创建 project telemetry tracker、启动 12 个 worker、启动单一 reducer/catalog writer，最后启动 producer。停止顺序为：producer 完成 channel、worker 完成 result channel、reducer flush、catalog close、manifest commit。

任一 worker 或 reducer 首次失败都必须记录原始异常并取消 linked token。其他 worker 在安全边界停止领取新 batch；不能让 producer 继续积压新工作，也不能把部分成功误报为完整 export。

## 7. 背压、内存和持久化边界

项目级准入同时限制以下量：

```text
queued batch estimated bytes
  + active worker reservation
  + completed-but-not-reduced fragment bytes
  + pending payload/catalog projection bytes
  + Roslyn/project baseline reservation
  <= project memory budget
```

估计值是调度上限，不是 CLR 内存承诺；实际 fragment bytes、working set 和 GC pause 仍需 telemetry。初始配置应至少有：

- project queue capacity：不小于 `2 * 12`，并受 estimated cost/bytes 双重限制；
- fragment sink capacity：不小于 `2 * 12`，超出后 worker 在写 sink 时等待；
- 单个 oversized method：保持单 batch，不与另一个大型方法合并；
- catalog writer：单写入者、逐 projection 事务提交，禁止多线程同时写 SQLite；
- manifest：catalog 完成且 connection 关闭或查询生命周期明确结束后，写 `manifest.json.tmp`，成功后原子替换。

当前 exporter 的 resume 设计必须保持以下边界：

- 已验证的旧 payload 只进入 resume import/catalog 阶段，不占用 CPG 计算 worker；
- 缺失或无效 payload 才进入 project WorkBatch 计算队列；
- payload 可以按文档逐个落盘，不能把 930 个 projection 保留到最后；
- catalog 负责全局节点和 edge key 去重以及 manifest 的流式投影；
- manifest 写入失败时不发布新 manifest，临时文件和临时 catalog 按失败清理策略处理，已完成 payload 保留给下一次 resume。

## 8. 取消、失败和恢复

### 8.1 取消

取消发生后，producer 停止生成新 envelope 并完成输入 channel；worker 在当前 Roslyn 安全边界退出；result sink 停止接受新 fragment；catalog 不再提交新的完整发布状态。已写出的 payload 可以保留，但没有完整 manifest 的目录不能被标记为成功导出。

清理必须幂等：重复取消、worker 已退出后 reducer 再失败、catalog close 抛异常，都不能二次删除或覆盖用户已有的有效 payload。孤立的 `.nlcpg-resume-catalog-*.db`、`-wal` 和 `-shm` 文件由受控清理策略处理；正在运行或用户用于诊断的文件不得强行删除。

### 8.2 worker 或 reducer 失败

失败结果至少包含：project run id、global batch id、source path、document stable order、worker index、stage id、原始异常类型和是否已经写入 payload。失败时：

1. 只保留第一个失败作为主失败，其他失败作为附加诊断；
2. 取消所有 worker 和 producer；
3. 等待固定 worker 结束，避免残留写入；
4. 关闭 catalog 和临时 manifest writer；
5. 不生成成功 manifest；
6. 下一次 resume 重新验证 payload，不信任未完成 run 的内存状态。

## 9. Telemetry 和 CPU 空闲诊断

项目级事件至少记录：

| 指标 | 语义 |
| --- | --- |
| `WorkerCount` | 本次 project pool 配置的固定 worker 数，生产验收为 12 |
| `ActiveWorkerCount` | 正在执行 CPU batch 的 worker 数 |
| `RunnableBatchCount` | 已进入准入且可立即执行的 batch 数 |
| `WorkerActiveRatio` | 计算阶段 active worker 时间 / worker 总时间 |
| `IdleReason` | `NoRunnableBatch`、`WaitingForFragmentSink`、`WaitingForCatalog`、`RoslynBarrier`、`GcOrRuntime`、`Cancelled` 或 `Shutdown` |
| `QueuedBatchHighWater` | channel 中 batch 数和 estimated cost/bytes 峰值 |
| `CompletedNotReducedHighWater` | 已完成但尚未归并的 fragment 数和 bytes 峰值 |
| `ReducerWaitMilliseconds` | fragment 完成后等待稳定归并的时间 |
| `CatalogWriteMilliseconds` | projection 事务和 SQLite 写入时间 |
| `ManifestFinalizeMilliseconds` | catalog 查询、临时 manifest 写入和原子替换时间 |
| `WorkerTaskCount` | 一次 run 创建的 worker task 数，不能随 batch 或 method 增长 |
| `RuntimePauseMilliseconds` | worker 在有界等待期间观察到的 `GC.GetTotalPauseDuration()` 增量；只作为 `GcOrRuntime` 诊断证据 |

CPU 采样只作为外部观察值，不能作为唯一正确性断言。验收首先断言：在至少 12 个可运行 CPU batch 且没有背压/barrier 的窗口内 `ActiveWorkerCount=12`；然后解释该窗口之外的空闲。这样可以区分“没有工作”“结果端背压”和“worker 实际没有被调度”三类问题。

## 10. 确定性与验收合同

以 DOP1 或现有 ordered path 作为语义 oracle。项目级实现至少比较以下结果：

- 每个 source path 的节点、边、stable anchor、span、label 和诊断；
- 全局 node id、edge key、cross-file endpoint 和 edge file list；
- catalog 查询结果和 manifest 的节点、边、文件顺序；
- resume 已有 payload、缺失 payload、无效 payload 混合场景；
- 取消、worker failure、reducer failure 后没有成功 manifest；
- 规则查询、rewrite plan、per-file diff 与 DOP1 相同。

正式生产 profile 使用 12 worker。测试还应覆盖 DOP1 作为 oracle、DOP2 作为低并发回归、DOP12 作为目标验收；如允许 DOP16 压力测试，必须证明它仍受 project pool 上限约束，不能出现 16 个 project worker 或每个 worker 再开一组内部 worker。

### Version4 通过条件

- 930 个文档均有完成状态，或每个失败文档都有明确诊断；
- 成功运行生成完整 manifest，失败/取消运行不生成半成品 manifest；
- DOP12 不发生此前的 OOM，峰值 working set 和 fragment/catalog 水位在预算内；
- `WorkerTaskCount` 为固定 12，不随文档数或方法数增长；
- 有可运行计算 batch 时 active worker 达到 12，所有空闲时间都有 `IdleReason`；
- DOP1、DOP2、DOP12 的 graph/payload/catalog/manifest 语义等价；
- 性能报告分别记录 CPU、I/O、GC、catalog 和 manifest 收尾时间，不以“CPU 看起来接近 100%”替代语义和内存验收。

## 11. 不采用的方案

### 每文档一个 DOP12 builder

不采用。它保留顶层文档串行，无法填补小文档和尾部空闲，而且会让 project export 的并发预算与 builder 内部并发预算重复计算。

### 930 个文档全部 `Task.Run`

不采用。它没有稳定的准入、结果背压或失败传播，容易同时保留大量 `Compilation`、fragment 和 projection，正是收尾阶段内存异常的放大器。

### worker 直接写共享 graph 或 SQLite

不采用。共享写锁会重新串行化热路径，SQLite 多写入者会引入锁等待和提交顺序差异；graph identity 和 catalog edge 去重也会依赖完成顺序。

### 用忙等保证 CPU 100%

不采用。忙等只增加 CPU 消耗，不增加 CPG 计算吞吐，并会在 OOM 或 catalog 背压时进一步恶化系统状态。正确目标是可运行计算阶段的 12-worker 利用率和可解释的非计算空闲。

## 12. 实施入口

按 [项目级 WorkBatch 池执行计划](../../plans/2026-09-21-cpg-project-workbatch-pool-execution.md) 执行。该计划以合同测试开始，随后完成 project planner、固定 worker、NLCPG DOP1 adapter、ProjectJsonExporter 接入、背压和 telemetry，最后运行 DOP1/2/12/16 及 Version4 acceptance。

在该计划完成前，本页的“12 worker”和“Version4 通过条件”都是设计目标，不是已验证事实；不得把现有单文档 WorkBatch 的 `ActiveWorkerCount` 证据误写成全项目级池已完成。
