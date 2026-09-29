# NLISSN/NLCPG 分析流程耗时性能研究报告

> 状态：研究完成，已补充 GitHub 源码级对照和初版设计，未修改生产代码。
>
> 日期：2026-09-09。
>
> 研究问题：当前代码从输入加载到 CPG 构建、规则图执行和制品发布的耗时，应该按哪些方向测量，哪些方向最可能是瓶颈，以及如何用可比较的实验确认结论？

## 结论摘要

当前最值得优先分析的不是 `BoundedConcurrencyPool` 本身，而是 Roslyn 语义查询驱动的 CPG 构建和它产生的大量对象、节点与边。

代码和历史 runtime log 给出的证据支持以下排序：

1. **CPG pass、Roslyn semantic query 和图物化**：`Syntax`、`Operation`、`DataFlow`、`InterproceduralDataFlow` 等 pass 已有内部计时，但还没有进入端到端 runtime log。大文件会生成百万级节点和数百万条边，必须按 pass 拆分。
2. **分配量、GC 和图规模**：历史 `Main.cs` 日志在约 100 秒运行中累计分配约 23.4 GB（十进制），这更像是需要优先验证的内存/分配压力，而不是普通的调度开销。
3. **Anchor discovery 的双遍成本**：启用预分配节点 ID 或 streaming persistence 时，`BuildFromSemanticModel` 会先执行一次 anchor discovery，再执行正式 build；两次 pass 的耗时必须单独报告。
4. **Workspace/MSBuild 加载**：`.sln`/`.csproj` 路径会先经过 `MSBuildWorkspace`、restore、项目筛选、编译和文档快照生成。现有总耗时包含这些阶段，但没有阶段字段，不能把 Workspace 变慢误判为 CPG 变慢。
5. **规则图执行与结果物化**：规则节点级 `RuleGraphNodeTelemetry` 已存在；需要聚合 Mark、Propagate、Lift、Propose 四阶段的总耗时、最大耗时、输入/输出量，而不是只看最终总时间。
6. **目录 DOP 与单文件 CPG DOP 的交互**：两者由不同配置控制，并且 `CpgBuildAdmissionBudget` 会限制实际重叠。应使用矩阵实验，不要只改变一个总 DOP 后推断并发收益。
7. **Rewrite、diff、evidence 和 write-back**：这些是在分析结果之后发生的后置成本，应在 `WriteDiff`、`WriteEvidence`、`WriteBack` 和 `SkipRewrite` 组合下隔离测量。
8. **性能分析组件应拆成“管线事件 + 可选视图 + 样本聚合”**：LLVM `llvm-mca` 将模拟管线、事件监听和多个报告 View 解耦；LLVM `llvm-exegesis` 将执行器、重复测量、验证计数器和聚合器解耦。NLISSN 应沿用这种职责分离，避免把诊断字段直接塞进规则或 CPG 语义对象。

目前不能给出“当前 commit 的正式性能排名”：已有数值来自 2026-08-06 的历史日志，未绑定到当前 commit、当前 SDK 和一组新鲜的重复样本。本文给出的是热点假设、可重复实验方案和指标缺口。

## 研究范围与方法

研究范围覆盖以下端到端路径：

```text
ConfigurationRunHost / CLI
    -> CommandHost
    -> directory / file / MSBuild workspace input
    -> DirectoryAnalysisService / WorkspaceAnalysisService
    -> DirectoryAnalysisUseCase
    -> ApplicationService
    -> Roslyn SemanticModel + NLCPGBuilder
    -> CPG passes and optional persistence
    -> RuleGraphAnalysisExecutor
    -> Mark -> Propagate -> Lift -> Propose / Decision
    -> rewrite, diff, evidence and runtime log
```

检索采用官方一手材料：Microsoft .NET/Roslyn 文档和源码、LLVM LNT、LLVM Test Suite、LLVM `llvm-mca`/`llvm-exegesis` 源码、GitHub CodeQL 源码、Joern 官方仓库和 BenchmarkDotNet 官方文档。外部项目只用于确定可比较的阶段边界和测量方法，不把它们的实现细节当作 NLISSN 的现状。

本次没有启动子代理，直接由当前会话完成代码阅读、网络检索和报告编写。

## 代码流程与测量边界

### 输入和宿主层

`CommandHost.AnalyzeCoreAsync` 在选择输入分支前创建 `RuntimeMeasurementLog`，因此日志的 `elapsedMs` 会覆盖输入读取、Workspace 加载、分析、制品写入和 evidence 写入，但没有这些阶段的独立计时。

- [CommandHost.cs](../../src/NLISSN/Hosting/CommandHost.cs:38)：创建 runtime、runtime log 和规则选择；随后分流 directory、workspace、single-file/replay。
- [DirectoryAnalysisService.cs](../../src/NLISSN/Hosting/DirectoryAnalysisService.cs:21)：枚举 `.cs` 文件、顺序读取源码、调用目录用例，最后顺序物化 diff/write-back。
- [WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs:21)：调用 Workspace loader，再按项目使用完整 `CSharpCompilation` 分析文档。

目录路径中的实际顺序是：

```text
EnumerateFiles + sort
    -> ReadAllTextAsync for every file
    -> DirectoryAnalysisUseCase.Analyze
    -> file-level analysis, optionally concurrent
    -> ordered result combine
    -> diff / write-back materialization
```

Workspace 路径中的实际顺序是：

```text
optional restore
    -> MSBuildWorkspace.Create
    -> OpenSolutionAsync / OpenProjectAsync
    -> project selection and snapshot loading
    -> CSharpCompilation + SemanticModel per document
    -> existing directory analysis use case
```

因此 Workspace 输入至少要记录两个总量：

- `workspaceLoadElapsedMs`：从 restore 或 loader 开始到 snapshot 完成；
- `analysisElapsedMs`：snapshot 已准备好后，从文档分析到结果合并。

### 单文件与 CPG 构建

单文件入口在 [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs:151) 创建 `CSharpSyntaxTree`、root、compilation 和 `SemanticModel`。Workspace/目录编译入口把已有 `SemanticModel` 和 syntax root 传入同一分析服务，避免应用层重新构造最小 compilation。

`BuildAnalysisContextCore` 在 [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs:206) 中创建 `NLCPGBuilder`，将规则管道需要的 capability 传给 builder，然后调用 `BuildFromSemanticModel`。这意味着 CPG 是规则图执行前的共同前置成本，任何规则耗时分析都必须先把 CPG 成本独立出来。

`NLCPGBuilder.Build` 已经用 `MeasureStage` 记录以下阶段：

```text
PersistenceRestore / PersistenceGraphImport
Syntax
MethodModel
Operation
StreamingBasePublish
CallGraph
MemberAccess
ControlFlow
DataFlow
InterproceduralDataFlow
Dominance
ControlDependence
FreezeQueryIndex
PersistenceWrite
BuildInventoryAudit
```

具体实现见 [NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:206) 和 [NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs:75)。`NLCPGBuildMetrics` 已包含 `ElapsedMilliseconds`、`PassElapsedMilliseconds`、cache hit/miss、节点/边数量、data-flow method metrics 和 persistence metrics，这是当前最应该复用的测量数据，而不是先引入一个泛化 profiler。

### Anchor discovery 双遍

当 `UsePreallocatedNodeIds` 或 streaming persistence 使 `RequiresPreallocatedNodeIds()` 返回 true 时，[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:160) 的 `BuildFromSemanticModel` 会：

1. 创建 anchor-discovery builder；
2. 用相同的 semantic model 和 syntax root 跑一次受限构图；
3. 收集 stable anchors 并创建 allocation；
4. 再跑正式 `Build`。

`AnchorDiscoveryElapsedMilliseconds` 与 `AnchorDiscoveryPassElapsedMilliseconds` 已保存到 `LastBuildMetrics`。正式实验必须把它们加入端到端阶段表，否则优化正式 pass 可能只是把时间转移到 preflight。

### CPG 并行边界

[PartitionedSyntaxPass.cs](../../src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs:14) 将分区内的 `GetSymbolInfo`、声明符号和类型事实收集放到 worker，再回填缓存；[PartitionedOperationPass.cs](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:37) 对 operation root 并行读取 `SemanticModel.GetOperation`，随后在有序提交阶段物化节点、边和 shard。

这个设计产生两个不同的性能对象：

- **worker 采集**：Roslyn semantic query、操作树遍历、临时 `List`/`Dictionary`/record 分配；
- **ordered commit**：节点/边创建、去重、索引、排序、持久化和顺序约束。

只看一个 `Operation` 总时间无法判断瓶颈在语义查询还是主线程物化。建议为每个 partition 进一步记录 `collectElapsedMs`、`materializeElapsedMs`、record count、node candidate count、edge candidate count 和 retained bytes。

### 规则图、后处理和制品

[RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs:199) 对每个规则节点包裹 `Stopwatch`，产生 `RuleGraphNodeTelemetry`，同时通过共享并发池运行依赖图。`ApplicationService.RunAnalysis` 在 [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs:93) 取得这些 telemetry，再执行：

```text
FilterNestedDeleteDecisions
    -> FilterUnsafeLocalDeclarationDeletes
    -> PrototypeRewriter.Rewrite
    -> PrototypeAnalysisResult
```

当前 telemetry 被放入结果对象，但 `RuntimeMeasurementLog` 只写入最终 decisions、edits、nodes、edges 和 rule graph 峰值，没有逐节点或按阶段聚合耗时。因此规则图可能是热点，也可能只是被 CPG 前置成本掩盖，必须补充聚合数据后再判断。

目录结果随后在 [DirectoryAnalysisService.cs](../../src/NLISSN/Hosting/DirectoryAnalysisService.cs:37) 顺序写 diff 和文件；`CommandHost.CompleteRuntimeLogAsync` 还可能写 evidence 后才关闭 runtime log。制品阶段应单独测量，避免把磁盘写入或 evidence 序列化归因给规则执行。

## 现有本地遥测能力与缺口

### 已有能力

`RuntimeMeasurementLog` 在 [RuntimeMeasurementLog.cs](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs:112) 每 5 秒记录：

- 端到端 elapsed；
- `GC.GetTotalAllocatedBytes`、heap、working set；
- Gen 0/1/2 collection count；
- ThreadPool 线程、pending/completed work item；
- 并发池操作数量、queue wait、active/ready/buffer/retained record 峰值；
- graph node/edge count；
- rule graph ready/concurrent peak。

`NLCPGBuildMetrics` 还提供 CPG pass、持久化、operation cache 和 data-flow method 级信息，但这些数据没有随着 `PrototypeAnalysisResult` 或 runtime log 进入最终报告。

### 缺口

当前日志没有以下字段：

| 缺口 | 影响 | 优先级 |
|---|---|---:|
| Workspace/restore/project load elapsed | 无法分离 MSBuild 前置成本 | P1 |
| Directory enumeration/read/parse/compilation elapsed | 无法比较输入 I/O 与分析成本 | P1 |
| CPG `PassElapsedMilliseconds` | 无法定位具体 pass | P0 |
| Anchor discovery 总耗时和正式 build 耗时 | 无法量化双遍代价 | P0 |
| CPG allocation 与 node/edge 对应关系 | 无法判断 GC/分配是否主因 | P0 |
| Mark/Propagate/Lift/Propose 聚合耗时 | 无法判断规则阶段热点 | P1 |
| Rule node telemetry 的 sum/max/p95 和输入输出量 | 无法定位异常规则 | P1 |
| Rewrite/diff/evidence/write-back elapsed | 后置制品成本混入总耗时 | P1 |
| SemanticModel/operation cache hit/miss 的统一摘要 | 无法验证查询复用收益 | P1 |
| CPU sample 与阶段标记关联 | 只能看到总进程健康状态 | P1 |

`RuntimeMeasurementLog` 的 5 秒采样适合观察长期 heap 和 ThreadPool 趋势，不适合定位短于采样周期的 pass。阶段 Stopwatch 和外部 CPU trace 需要同时使用。

## 历史日志证据

以下数据来自仓库内 `Build/analysis` 下的 runtime log，运行日期为 2026-08-06。它们用于确定量级和实验方向，不是当前 `HEAD` 的正式 benchmark；日志也没有把 git SHA、SDK 版本和完整配置写入终态行。

| 输入/运行 | DOP | 总耗时 | nodes | edges | 运行中累计分配 | pool operation elapsed 合计 | 备注 |
|---|---:|---:|---:|---:|---:|---:|---|
| [Main.cs main-diff](../../Build/analysis/main-diff-20260806/artifacts/main-diff-20260806/RuntimeLog/runtime.log) | 12 | 100,187 ms | 1,576,714 | 3,601,985 | 23,394,875,000 bytes，约 23.4 GB | 9,039.8845 ms | 约占总耗时 9.0%；合计不等于 wall time |
| [Main.cs main-basic-diff](../../Build/analysis/main-basic-diff-20260806/artifacts/main-basic-diff-20260806/RuntimeLog/runtime.log) | 12 | 61,098 ms | 1,576,714 | 3,601,985 | 未记录可用终态样本 | 1,461.3641 ms | 图规模相同，配置/规则输出不同，不能据此归因 |
| [NPC.cs npc-rule](../../Build/analysis/npc-rule-20260806/artifacts/npc-rule-20260806/RuntimeLog/runtime.log) | 1 | 113,894 ms | 1,962,560 | 4,646,100 | 未记录可用终态样本 | 1,362.7670 ms | DOP 1，约占总耗时 1.2% |
| [NPC.cs npc-rule-diff](../../Build/analysis/npc-rule-diff-20260806/artifacts/npc-rule-diff-20260806/RuntimeLog/runtime.log) | 1 | 107,543 ms | 1,962,560 | 4,646,100 | 未记录可用终态样本 | 1,100.8490 ms | DOP 1，约占总耗时 1.0% |

由此只能得到三个谨慎结论：

- 这两个大文件的图规模已经达到百万级节点和数百万级边，CPG 图物化和临时对象值得优先采样；
- 历史日志中 pool operation 合计显著小于端到端耗时，暂时没有证据把调度器当作第一瓶颈；
- 不同运行的规则、diff、诊断数量和配置不完全相同，不能用 100,187 ms 对 61,098 ms 直接宣称某个功能造成了 39,089 ms 差异。

仓库历史还显示过以下优化方向：data-flow/call construction、shared shard cache、弱 compilation cache、目录 DOP 与 CPG DOP 拆分、bounded concurrency pool 和 Mark 结果物化优化。它们说明这些方向已有本地工程背景，但没有替代当前版本的受控分阶段测量。

## 网络检索和项目对照

### Roslyn compiler/workspace

| 一手来源 | 读取到的事实 | 对本仓库的含义 |
|---|---|---|
| [SemanticModel.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) | Roslyn 的 semantic model 会缓存 local symbols 和语义信息；同一个 model 上的重复查询可以复用已有结果；长时间持有 model 会延长相关内存生命周期 | 每个 compilation/tree 复用一个 `SemanticModel`；分别测量 `GetSymbolInfo`、`GetDeclaredSymbol`、`GetTypeInfo`、`GetOperation` 的调用量和 cache 行为；不要无限期跨运行持有 model |
| [Compilation.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/Compilation.cs) | `Compilation` 是 immutable、按需计算并缓存必要数据；`GetSemanticModel` 通过 provider 或创建 model，`CreateSemanticModel` 会创建新 model | 不要在 CPG pass 中反复创建同一 tree 的 SemanticModel；Workspace 分支应继续复用真实 compilation |
| [SemanticModelProvider.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModelProvider.cs) | SemanticModel 的创建和提供由独立 provider 抽象承载 | 将 model 获取耗时和后续语义查询耗时分开，不要把二者混为一个“Roslyn 慢”结论 |
| [Roslyn Workspaces MSBuild source](https://github.com/dotnet/roslyn/tree/main/src/Workspaces/MSBuild) 与 [NuGet package](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild) | Workspaces MSBuild 组件用于分析 MSBuild 项目和解决方案 | `.sln`/`.csproj` 输入必须单独计量 load、project selection、compilation 和 document snapshot；不能只用单文件 benchmark 推断 Workspace 性能 |

### CPG 分析项目

| 项目 | 官方材料 | 可借鉴的测量边界 | 不应过度推断的内容 |
|---|---|---|---|
| Joern | [Code Property Graph 文档](https://docs.joern.io/code-property-graph/)、[官方仓库 README](https://raw.githubusercontent.com/joernio/joern/master/README.md) | CPG 是带属性和边标签的有向 multigraph，组合 AST、控制流、数据流等表示；因此构图、图索引、查询和规则执行应分别记录 | 文档没有为 NLISSN 证明某一种并行策略或某个 pass 一定更快；不能把 Joern 的架构描述当成本仓库 benchmark 结果 |
| GitHub CodeQL | [Code scanning with CodeQL](https://docs.github.com/en/code-security/concepts/code-scanning/codeql/codeql-code-scanning)、[官方 CodeQL 仓库](https://github.com/github/codeql) | 官方流程明确拆成生成 CodeQL database 和在 database 上运行 query；部分 query pack 还包含 compilation cache。这个边界适合对照 NLCPG 的 build/query/rule stages | CodeQL 数据库格式、query engine 和 NLCPG 不同；不能直接比较秒数、内存或线程数 |

对照项目的共同启发是：代码表示构建、表示持久化/索引、查询/规则执行和结果发布是不同成本中心。NLISSN 当前把其中若干成本汇总在一次 `Analyze` 和一个 runtime log 中，下一步应该先恢复这些边界的可观测性。

### .NET 运行时诊断

| 官方材料 | 用法和限制 |
|---|---|
| [`dotnet-trace` 文档](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) | 基于 .NET EventPipe 采集运行中进程的 trace，不要求 native profiler；可使用线程时间采样 profile，Windows 上可用 Visual Studio 或 PerfView 分析 `.nettrace`。适合定位 CPU 热路径和阶段内的调用栈。 |
| [`dotnet-counters` 文档](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) | 适合第一层运行时健康检查，可观察 CPU、GC、heap allocation、working set 等 counter，并输出 CSV/JSON；应和阶段 elapsed 一起采集。 |
| [`dotnet-gcdump` 文档](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump) | 通过 EventPipe 触发 Gen 2/full GC 重建对象图；大 heap 会长时间暂停并增加内存压力，不应在性能敏感运行中频繁采集，只应对代表性样本做一次诊断。 |

### 基准测试方法

[BenchmarkDotNet 官方 overview](https://benchmarkdotnet.org/articles/overview.html)、[RunStrategy 文档](https://benchmarkdotnet.org/articles/guides/choosing-run-strategy.html) 和 [Diagnosers 文档](https://benchmarkdotnet.org/articles/configs/diagnosers.html) 可作为独立 benchmark harness 的方法参考：隔离进程、预热、测量迭代和 diagnoser 应明确区分。当前仓库已有 PowerShell 性能 harness，因此不建议为了本研究直接替换现有 harness；应把 BenchmarkDotNet 的可重复性原则映射到现有脚本。

## 推荐的测量模型

### 记录层次

每一次 run 至少记录以下固定元数据：

```text
runId
gitSha
dotnetSdk
runtime
os / processor / logicalCpuCount
inputKind
inputPath or fixtureId
inputManifestHash
sourceFileCount / sourceBytes
projectCount / targetFramework
ruleSelectionHash
skipRewrite / writeDiff / writeEvidence / writeBack
directoryDop / cpgDop / groupDop / helperDop
```

阶段记录建议使用如下结构：

```text
runId
phase
scope             # process, workspace, project, file, cpg, rule-stage, artifact
itemId            # project path, file path, pass name or RuleNodeId
wallElapsedMs
accumulatedElapsedMs
allocatedBytes
inputCount
outputCount
peakWorkingSetBytes
gen0/gen1/gen2 delta
status
```

必须区分两个时间概念：

- `wallElapsedMs`：端到端裁决指标；
- `accumulatedElapsedMs`：并行文件或并行 rule node 的 elapsed 总和，用于定位 CPU 工作量，不能与 wall time 相加。

对 CPG 额外记录：

```text
buildElapsedMs
anchorDiscoveryElapsedMs
anchorDiscoveryPassElapsedMs[pass]
passElapsedMs[pass]
nodeCount / edgeCount / operationInventoryCount
operationNodeCacheHit/Miss
operationRootCacheHit/Miss
dataFlow method metrics
persistence restore/write/reuse metrics
```

对规则图额外记录：

```text
stage                  # Mark, Propagate, Lift, Propose
ruleNodeId
status
inputCount / outputCount
elapsedMs
dependencyCount
```

最终至少输出每个阶段的 `sum`、`max`、`p50`、`p95`，以及按输入文件和规则节点排序的 top 10。单次 run 只保留逐节点明细也可以，但汇总必须稳定可比较。

### 控制变量

每组实验固定：

- git commit；
- SDK、runtime、配置和规则选择；
- 输入 manifest、文件字节数和 SHA-256；
- 编译选项和 `TargetFramework`；
- 输出开关；
- CPU/电源模式和后台负载尽量稳定。

仓库当前 `init.ps1` 实际使用 SDK 为 `10.0.400`，而仓库文档要求 `10.0.200-preview.0.26103.119`。这个差异必须写入每次 benchmark 元数据，正式比较前应优先在仓库要求的 SDK 下重跑；不能把不同 SDK 的结果放入同一中位数。

### 预热和样本

针对每个配置：

1. 启动一个独立进程完成一次预热；
2. 再执行 3-5 次测量；
3. 报告 wall-clock median、p95、最小值和离散程度；
4. 分开报告 cold process、warm process/compilation cache、CPG persistence hit 和 persistence miss；
5. 只有 graph、evidence、decision、rewrite 和 diff 输出通过等价检查时，才接受速度比较。

进程级总时间不能只取一次；尤其是 Workspace、Roslyn cache 和大 heap GC 都可能让首次运行与后续运行明显不同。

## 优先实验矩阵

### 第一组：确定 CPG pass 热点

用一个大文件和一个小型确定性 fixture，规则集、输入和 SDK 固定。先关闭 diff/evidence/write-back，只保留分析结果，记录：

```text
(1,1), (1,12), (12,1), (12,12)
```

这里第一个数是目录/文件级 DOP，第二个数是 CPG DOP。单文件输入时目录 DOP 影响较小，但仍应保留以确认宿主路径；目录 fixture 用于观察两个维度的准入等待。

每组比较：

- CPG pass elapsed；
- anchor discovery 占比；
- `collect` 与 ordered commit 的比例；
- allocated bytes、GC、working set；
- graph node/edge 等价性。

### 第二组：识别规则图阶段

在相同 CPG 配置下分别运行：

- 核心规则集；
- 只启用 Mark；
- Mark + Propagate；
- Mark + Propagate + Lift；
- 完整 Mark + Propagate + Lift + Propose。

每次都保留 CPG pass 明细，并对规则节点 telemetry 做聚合。这样可以区分“规则变多导致 CPG capability 变多”和“规则执行本身变慢”。如果改变规则选择会改变 `RequestedCapabilities`，必须把 capability fingerprint 作为配置维度记录。

### 第三组：后置制品成本

固定相同分析结果，比较以下开关组合：

| 分析 | Rewrite | Diff | Evidence | Write-back | 目的 |
|---|---|---|---|---|---|
| A | skip | off | off | off | CPG + rule graph 基线 |
| B | on | off | off | off | rewrite CPU/分配 |
| C | on | on | off | off | diff 序列化和磁盘 |
| D | on | on | on | off | evidence 构建/写入 |
| E | on | on | on | on | 真实端到端 |

目录输入还要把并行分析和顺序 materialization 分开：当前文件分析可以并发，但 diff/write-back 在 `MaterializeOutcome` 中按稳定顺序处理。

### 第四组：Workspace 与单文件边界

同一目标文档分别用：

- direct `.cs`；
- `.cs` + `input.project`；
- `.csproj`；
- `.sln` + project selector；
- `.sln` 全项目。

记录 `restore`、Workspace open、project selection、compilation/reference、source-generated document 和目标文档分析的独立时间。Workspace 的完整 compilation 语义不能为了 benchmark 方便替换成最小 compilation，否则结果不代表生产流程。

## 现有 harness 的落地方式

开发者指南已经给出了真实源码并发测量入口：

```powershell
pwsh -File .\Miscellaneous\scripts\Run-ConcurrencyPoolPerformance.ps1 `
  -SourceFile "D:\path\to\source\Example.cs" `
  -TargetName Example `
  -Dop "1,8,12,16"
```

脚本按文档默认做预热和三次测量，并校验成功终态和 DOP 1 的图/规则快照。对本研究建议扩展报告字段，而不是改变默认 DOP 或放宽等价校验。目录 DOP 与 CPG DOP 的拆分应按开发者指南中的 `(12,1)`、`(1,12)`、`(12,12)` 组合执行。

外部运行时诊断可按代表性样本附加：

```text
dotnet-counters collect -> CPU/GC/heap/working-set CSV
dotnet-trace collect    -> EventPipe CPU/thread-time trace
dotnet-gcdump collect   -> one-off object graph on a controlled run
```

具体 profile 和命令参数应以当前安装版本的 `--help` 及官方文档为准。`dotnet-gcdump` 不应加入每次重复测量；它会触发 full/Gen 2 GC，结果本身会改变被观察运行。

## GitHub 分析器与 LLVM 项目的性能分析实践

本节补充与当前代码流程边界最接近的 GitHub 一手项目。它们不提供 NLISSN 可直接复用的秒数；可迁移的是测量模型、样本处理方式和诊断入口。源码固定点见本节末尾，正式 benchmark 仍应把外部项目和本仓库一起固定到 commit。

### LLVM LNT：把编译、执行和诊断拆开

[LLVM LNT](https://github.com/llvm/llvm-lnt) 是 LLVM 测试套件的结果采集和性能回归工具。其 [test_suite.py](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py) 的执行模型有几个直接价值：

- `compile`、`exec`、`score` 和 size 等结果维度分开记录，而不是把构建和运行合成一个时间；
- `exec_multisample`、`compile_multisample` 分开控制样本数；普通运行按需要执行编译样本和执行样本，并在最后合并报告（[多样本循环](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py#L430-L455)）；
- `--drop-exec` 要求存在多个执行样本，并在合并前丢弃前若干执行样本（[参数校验和过滤](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py#L642-L666)）。这把预热样本和正式样本的语义写进工具，而不是由读者手工猜测；
- 配置、编译、安装和 lit 执行有独立路径；`--build` 可以只完成构建，`--exec` 使用已配置的构建目录（[运行前状态处理](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py#L606-L630)）；
- perf profile 只在一个样本采集，并在 profile 路径限制并行编译/执行，避免把并行调度噪声混进 profile（[profile 样本和线程处理](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py#L888-L915)）；
- `--diagnose` 只针对 `--only-test` 的单个测试生成诊断报告，报告路径会保留构建日志、临时产物和编译器诊断信息（[诊断报告流程](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py#L1141-L1256)）。

对 NLISSN 的对应关系是：

| LNT 做法 | NLISSN 的落点 |
|---|---|
| compile / exec / report 分离 | `WorkspaceLoad`、`CPGBuild`、`RuleGraph`、`Artifact` 分阶段记录；终态仍保留端到端 wall time |
| 多样本和丢弃预热 | 当前 harness 的 warmup + 3 次测量继续保留；另报 cold process、warm process、persistence hit/miss，不能混成一个中位数 |
| profile 只采一份 | 对代表性运行单独采 `dotnet-trace`/CPU profile；常规样本不携带 profile 开销 |
| single-test diagnose | 选择一个大文件和一个小 fixture 做深诊断，输出 pass、分配和调用栈；其余运行只收集轻量指标 |
| build directory / exec mode | 把 CPG persistence hit 与 miss 作为独立配置，避免把恢复时间与全量构图时间混为一类 |
| interleaved builds / regression bisect | 后续性能回归时固定输入和输出快照，按 commit 做交错运行或二分；不以一次运行宣布优化有效 |

LNT 的重要启发不是照搬其 native toolchain，而是让“测量对象”有稳定身份：编译时间、执行时间、代码尺寸和诊断采样各有字段。NLISSN 的 `pool operation elapsed` 也应保持为累计工作量字段，不能替代端到端 wall time。

### LLVM Test Suite：同时观察时间、资源和产物

[LLVM Test Suite](https://github.com/llvm/llvm-test-suite) 的 [顶层 CMakeLists.txt](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt) 把性能分析所需的边界暴露在构建配置中：

- 通过选项采集 binary code size，并支持 IR PGO、instrumentation profile 和 sample profile（[code-size/PGO 配置](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt#L138-L213)）；
- 通过 `TEST_SUITE_DIAGNOSE_FLAGS` 注入 `-ftime-report` 或 `-mllvm -stats`，把“需要详细诊断的运行”与普通 benchmark 分开（[诊断 flags](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt#L215-L230)）；
- 提供禁用 PIE/ASLR 的确定性配置，用于减少地址随机化对内存和 cache 类 benchmark 的干扰（[确定性配置](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt#L311-L321)）；
- 使用 `timeit` 统计 compile/link time，并可记录 compile max RSS；还可以切换 benchmarking-only 和 LLVM statistics（[compile time、RSS 和 statistics](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt#L356-L396)）。

这对 NLISSN 有三个具体映射：

1. CPG 不只记录 pass elapsed，还应记录每个 pass 的 `allocatedBytes`、node/edge delta、峰值 working set，以及每节点/每边归一化值；这相当于把 LLVM Test Suite 的时间、RSS、代码尺寸三类证据映射为 CPG 的时间、资源、图规模。
2. 将 `NLCPGBuildMetrics` 的详细 pass 诊断视为类似 `TEST_SUITE_DIAGNOSE_FLAGS` 的低频诊断模式。它可以在一个代表性样本中打开，不应成为所有重复测量的默认开销。
3. 图节点数、边数、decision/edit 数和 artifact 大小都属于结果产物指标；任何性能结论必须先检查这些指标与 baseline 等价，不能只看 elapsed。

LLVM Test Suite 的 PGO、PIE/ASLR 和 native code size 不能直接移植到 Roslyn C# CPG。能迁移的是“资源指标要和时间同级记录”以及“诊断配置必须显式化”。

### Roslyn Benchmarks：隔离 setup，参数化输入和分配

[dotnet/roslyn](https://github.com/dotnet/roslyn) 的 [src/Tools/Benchmarks](https://github.com/dotnet/roslyn/tree/main/src/Tools/Benchmarks) 和 [src/Tools/IdeBenchmarks](https://github.com/dotnet/roslyn/tree/main/src/Tools/IdeBenchmarks) 展示了适合 Roslyn 语义操作的微基准写法：

- [Benchmarks.csproj](https://github.com/dotnet/roslyn/blob/main/src/Tools/Benchmarks/Benchmarks.csproj) 使用 BenchmarkDotNet；
- [DecisionDagBenchmarks.cs](https://github.com/dotnet/roslyn/blob/main/src/Tools/Benchmarks/DecisionDagBenchmarks.cs) 和 [IsPatternBenchmarks.cs](https://github.com/dotnet/roslyn/blob/main/src/Tools/Benchmarks/IsPatternBenchmarks.cs) 将输入生成和初始化放进 `GlobalSetup`，把 hit/miss、first/last 等输入形态用 Params 展开，并对照 baseline 实现；
- 这些 benchmark 使用 `[MemoryDiagnoser]`、`OperationsPerInvoke`、`[EvaluateOverhead(false)]`、`NoInlining` 或 setup/cleanup 生命周期控制测量误差；Windows 专属场景使用 OS filter；
- [MemoryDiagnoserConfig.cs](https://github.com/dotnet/roslyn/blob/main/src/Tools/IdeBenchmarks/MemoryDiagnoserConfig.cs) 和 [SQLitePersistentStorageBenchmark.cs](https://github.com/dotnet/roslyn/blob/main/src/Tools/IdeBenchmarks/SQLitePersistentStorageBenchmark.cs) 说明 IDE benchmark 还会把内存和持久化存储行为作为可单独诊断的维度，而不是只比较一个吞吐数字。

对 NLISSN 的建议是建立小型的、受控的微基准层，但不要用它替代端到端 harness：

| Roslyn benchmark 做法 | NLCPG 微基准候选 |
|---|---|
| `GlobalSetup` 之外不计输入准备 | 预先构造 syntax tree、compilation、semantic model；测量方法只执行目标 `GetSymbolInfo` / `GetOperation` 或一个 pass |
| Params 覆盖输入形态 | 覆盖小/大 syntax tree、cache hit/miss、operation root 数、跨方法 data-flow、锚点是否启用 |
| baseline 实现 | 比较旧的 materialization 路径、缓存路径和新路径；输出 node/edge/semantic snapshot 必须等价 |
| `MemoryDiagnoser` | 记录 allocated bytes、Gen 0/1/2、retained graph objects；端到端运行再由 runtime log 观察 heap/working set |
| OperationsPerInvoke | 对大量同构节点或 partition 做批量归一化，避免单次操作太短而被计时器误差主导 |
| setup/cleanup 和 OS filter | 每轮清理或重建可变缓存；Windows-only 的 workspace/file operation 不与跨平台 CPG 结果混报 |

这里需要保持一个边界：BenchmarkDotNet 适合回答“某个语义查询、缓存或物化函数在固定输入下如何变化”，不适合直接回答 Workspace restore、规则图依赖调度和制品写回的端到端耗时。后者仍需当前的进程级 harness、阶段 Stopwatch 和运行时 trace。

### LLVM `llvm-mca`：用事件管线和可插拔 View 组织分析

[LLVM `llvm-mca`](https://github.com/llvm/llvm-project/tree/main/llvm/tools/llvm-mca) 是静态机器码性能分析器。它不是 NLISSN 可以直接嵌入的 profiler，但其组件边界对“如何编写分析组件”最有参考价值：

- [View.h](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/include/llvm/MCA/View.h#L10-L35) 把 View 定义为 `HWEventListener`，要求实现文本 `printView`、稳定名称和可选 `toJSON`；因此分析过程只发布事件，报告格式由 View 决定。
- [Pipeline.h](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/include/llvm/MCA/Pipeline.h#L40-L79) 将 stages 保存在有序列表中，`Pipeline::run` 以 cycle 为边界执行；[Pipeline.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/lib/MCA/Pipeline.cpp#L37-L83) 明确区分 cycle begin、逆序更新、首 stage dispatch 和正序 cycle end。
- 主程序在每个分析 region 上先构造 pipeline，再按选项挂接 `SummaryView`、`DispatchStatistics`、`ResourcePressureView`、`TimelineView` 和 target custom View，最后统一执行和输出（[llvm-mca.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-mca/llvm-mca.cpp#L752-L849)）。
- View 的输出同时保留人读文本和结构化 JSON；例如 `SummaryView` 将 iterations、cycles、uOps、IPC、吞吐写入相同的数据对象（[SummaryView.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-mca/Views/SummaryView.cpp#L64-L108)），`DispatchStatistics` 将 stall 分类和百分比保留为独立统计（[DispatchStatistics.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-mca/Views/DispatchStatistics.cpp#L20-L92)）。

对 NLISSN 的具体启发：

| `llvm-mca` 做法 | NLISSN 初版映射 |
|---|---|
| pipeline 只推进状态并发布事件 | `NLCPGBuilder`、Workspace 和规则执行器继续拥有语义；性能组件只接收阶段完成事件 |
| View 负责一个报告切面 | 分别实现 stage summary、resource summary、rule hotspot、diagnostic attachment，不让一个日志类承担所有聚合 |
| 文本和 JSON 由同一统计对象生成 | runtime log 保留稳定文本，另写 schema 化 summary；两者不得各自重新计算关键数字 |
| target custom View 可扩展 | future 的 CPG pass/规则节点可以注册扩展字段，但不能改变核心阶段 identity 或结果语义 |

`llvm-mca` 的限制也要保留：它模拟的是稳定的机器管线，不处理 Roslyn Workspace、GC 或文件写回；这里迁移的是事件/View 边界，不是其具体模拟模型。

### LLVM `llvm-exegesis`：把测量、隔离、重复和聚合拆开

[LLVM `llvm-exegesis`](https://github.com/llvm/llvm-project/tree/main/llvm/tools/llvm-exegesis) 研究真实 CPU 上的指令延迟和吞吐。它展示了性能测量组件需要主动处理的误差来源：

- `BenchmarkRunner` 把 benchmark mode、execution mode、validation counters 和 `runMeasurements` 抽象分开；[FunctionExecutor](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-exegesis/lib/BenchmarkRunner.h#L34-L140) 统一计数器采样接口。
- 执行器既支持进程内运行，也支持 Linux 子进程隔离；进程外路径处理 CPU affinity、崩溃、信号和计数器读取，避免被测 snippet 的故障污染宿主（[BenchmarkRunner.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-exegesis/lib/BenchmarkRunner.cpp#L183-L220)）。
- `LatencyBenchmarkRunner` 重复测量，累积 validation counter，并在多计数器结果下保留方差最低的稳定读数；最后按 min、max、mean 或 min-variance 输出（[LatencyBenchmarkRunner.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-exegesis/lib/LatencyBenchmarkRunner.cpp#L71-L160)）。
- 跨配置合并通过 `ResultAggregator` 完成，并显式区分 duplicate、min 和 middle-half 等策略；它要求测量键和测量维度对称（[ResultAggregator.cpp](https://github.com/llvm/llvm-project/blob/10d4bdf93a82fb290b8c55da6ed19ff54da67296/llvm/tools/llvm-exegesis/lib/ResultAggregator.cpp#L58-L93)）。

对 NLISSN 的具体启发是：`normal`、`diagnostic`、`profile` 和 `benchmark` 必须是显式运行模式；同一个文件的 wall time、pool accumulated time、allocated bytes 和外部 trace 不能被一个“总耗时”字段吞掉。正式比较至少需要重复样本、结果键一致性、输入/配置 identity 和异常样本处理。

### Joern：按文件报告、有限并发和 CPG overlay

[Joern](https://github.com/joernio/joern) 是 CPG 分析平台。它提供的是分析流水线和报告组织的对照，不是 NLISSN 的性能基线：

- `X2Cpg` 将默认 overlay creator 集中在一个列表中，按 `Base`、`ControlFlow`、`TypeRelations`、`CallGraph` 顺序运行（[X2Cpg.scala](https://github.com/joernio/joern/blob/f117701adfd1e2e8c41d93c0760d115931df9ed6/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/X2Cpg.scala#L372-L385)）。这说明“基础 CPG”和“后置分析层”可以拥有清晰阶段 identity。
- `TimeUtils.time` 使用单一高分辨率计时器封装 block，并统一格式化输出（[TimeUtils.scala](https://github.com/joernio/joern/blob/f117701adfd1e2e8c41d93c0760d115931df9ed6/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/TimeUtils.scala#L7-L28)）。
- `Report` 用并发 map 收集每个文件的 LOC、解析状态、CPG 状态和 duration，输出前按文件名排序并给出总计（[Report.scala](https://github.com/joernio/joern/blob/f117701adfd1e2e8c41d93c0760d115931df9ed6/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/Report.scala#L11-L21) 与 [Report.scala](https://github.com/joernio/joern/blob/f117701adfd1e2e8c41d93c0760d115931df9ed6/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/Report.scala#L56-L96)）。
- `ConcurrentTaskUtil` 同时提供固定线程池和 parallel stream 两种路径，并明确说明固定池适合高内存任务、parallel stream 适合大量短任务（[ConcurrentTaskUtil.scala](https://github.com/joernio/joern/blob/f117701adfd1e2e8c41d93c0760d115931df9ed6/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/ConcurrentTaskUtil.scala#L10-L63)）。

迁移到 NLISSN 时只采用“按 item 记录、最后稳定排序、并发策略显式化”三点；Joern 的 report 仍是粗粒度 duration，不能替代 NLCPG 已有的 pass、persistence 和规则节点 telemetry。

### CodeQL：把查询性能问题做成可执行的分析规则

[GitHub CodeQL](https://github.com/github/codeql) 提供另一种有价值的分析类实践：性能问题本身也可以进入分析组件和回归测试，而不只是靠人工 profile。

- 官方文档将 `--threads` 定义为查询执行的显式并发参数，默认值为 `1`，`0` 表示使用逻辑处理器数量（[threads-query-execution.rst](https://github.com/github/codeql/blob/61bdd3cfdebd4a8f0e00a47dd45ed2cdf3dc6354/docs/codeql/reusables/threads-query-execution.rst#L1-L3)）。
- `LargeJoinOrder.ql` 从 structured logs 中计算 pipeline 最大 tuple 数、结果规模、依赖规模、重复率和 badness，并按 badness 降序输出（[LargeJoinOrder.ql](https://github.com/github/codeql/blob/61bdd3cfdebd4a8f0e00a47dd45ed2cdf3dc6354/ql/ql/src/queries/performance/LargeJoinOrder.ql#L12-L25) 与 [输出部分](https://github.com/github/codeql/blob/61bdd3cfdebd4a8f0e00a47dd45ed2cdf3dc6354/ql/ql/src/queries/performance/LargeJoinOrder.ql#L265-L281)）。
- `MissingNoinline.ql` 将性能约束编码为带 `performance` 标签的高精度问题规则，检查被拆出的 join-order predicate 是否缺少 `noinline`/`nomagic`（[MissingNoinline.ql](https://github.com/github/codeql/blob/61bdd3cfdebd4a8f0e00a47dd45ed2cdf3dc6354/ql/ql/src/queries/performance/MissingNoinline.ql#L1-L26)）。

对 NLISSN 的启发是增加“性能结果自校验”：当某个 pass 的 candidate/retained 数量、规则节点输入输出或 graph expansion 超过阈值时，报告应给出可定位的诊断记录。这个机制可以先是报告告警，不应直接改变删除语义；CodeQL 的 query-performance rule 也不能证明 NLISSN 的 C# CPG 性能。

### 外部项目的共同测量模型

从上述项目可以抽象出适合 NLISSN 的统一模型：

| 维度 | 证据来源 | NLISSN 应记录的字段 |
|---|---|---|
| 时间边界 | LNT compile/exec，LLVM compile/link/benchmark，Roslyn benchmark iteration | `wallElapsedMs`、`accumulatedElapsedMs`、phase/item identity |
| 样本质量 | LNT multisample、drop-exec、single-profile | warmup count、measurement count、cold/warm/persistence mode、median/p95 |
| 资源压力 | LLVM max RSS/code size，Roslyn MemoryDiagnoser | allocated bytes、GC delta、working set peak、graph node/edge、artifact bytes |
| 诊断深度 | LNT diagnose，LLVM diagnose flags，Roslyn diagnosers | normal run 的轻量日志 + 单独诊断 run 的 trace/pass/object detail |
| 输入维度 | Roslyn Params 和 LNT only-test | fixture id、source bytes、project/TFM、capability fingerprint、cache state |
| 结果可信度 | 外部项目都把结果输出作为测试报告的一部分 | graph/rule/artifact snapshot 等价后才比较速度 |

因此现有报告中的优先顺序保持不变，但实验记录应增加以下三条项目化约束：

- **LNT 风格的阶段和样本约束**：先固定输入、规则、SDK 和输出，预热后执行 3-5 次；profile、gcdump 和深诊断单独运行。
- **LLVM 风格的资源约束**：每次性能结果同时带 node/edge、allocation、GC、working set 和 artifact size；阶段诊断只在代表性样本开启。
- **Roslyn 风格的微基准约束**：将 semantic query、operation traversal、ordered materialization、cache hit/miss 各自做可参数化 benchmark，setup 不计入目标操作时间。

这些项目都不能替 NLISSN 提供当前 commit 的正式性能结论。它们只能帮助确定“测什么、如何分样本、如何保留诊断证据”；正式结论仍以本仓库固定 commit 的重复运行和语义等价校验为准。

## 性能假设与验收标准

### H1: CPG pass 是第一热点

验证：`PassElapsedMilliseconds` 中一个或多个 pass 占 `buildElapsedMs` 的主要比例，并且 CPU trace 的 top stacks 与该 pass 的 semantic query/graph materialization 对齐。

验收：能够给出每个 pass 的 wall/accumulated 时间、分配量和节点/边产出，而不是只说“CPG 较慢”。

### H2: 大量分配和 GC 限制吞吐

验证：在相同输入和输出下，allocation per node/edge、GC pause/collection 和 working-set 峰值与 wall-time 变化相关；低 DOP 或减少临时 materialization 后耗时和 GC 同时下降。

验收：至少有 `allocatedBytes / nodeCount`、`allocatedBytes / edgeCount`、GC delta 和 CPU trace；不能只凭 heap sample 归因。

### H3: Anchor discovery 双遍带来可观测固定成本

验证：开启/关闭 preallocated IDs 或 streaming persistence，在 graph 输出等价时比较 `anchorDiscoveryElapsedMs`、正式 build 和总耗时。

验收：双遍成本以独立阶段显示，并且没有把 anchor discovery 的 pass 时间重复计入正式 pass 汇总。

### H4: 调度器不是主瓶颈，但 DOP 组合会改变峰值内存

验证：pool elapsed/queue wait 与总时间、CPU utilization、GC 和 working set 同时观察；比较 `(12,1)`、`(1,12)`、`(12,12)`。

验收：报告同时给 wall median、pool accumulated time、queue wait、actual peak active 和 memory peak；不以 pool elapsed 合计替代 wall time。

### H5: 规则阶段可能被 CPG 固定成本掩盖

验证：用相同 CPG capability 和输入逐步启用四阶段规则，聚合 node telemetry。

验收：能列出 Mark/Propagate/Lift/Propose 的总耗时和 top rule node，并说明输出数量变化。

### H6: Workspace 性能与单文件性能不是同一问题

验证：同一目标文档在 direct `.cs`、`.csproj` 和 `.sln` 入口下记录 loader 与 analysis 两段时间。

验收：报告中 Workspace load 不再隐藏在一个 `Analysis completed elapsedMs` 中。

## 优先级和下一步

| 优先级 | 动作 | 原因 |
|---|---|---|
| P0 | 将 `NLCPGBuildMetrics`、anchor discovery 和 pass timings 汇总到每文件结果/运行报告 | 代码已有数据，收益最快，直接决定后续优化方向 |
| P0 | 在 runtime log 增加阶段 begin/end、allocated delta、node/edge 和 input identity | 现有 5 秒采样无法定位短阶段 |
| P1 | 对 `PartitionedSyntaxPass`/`PartitionedOperationPass` 分开统计 semantic collection 与 ordered materialization | 判断并行优化是否被主线程提交阶段抵消 |
| P1 | 执行 `(1,1)`、`(1,12)`、`(12,1)`、`(12,12)` 矩阵并做输出等价校验 | 区分目录并发、CPG 并发和 admission wait |
| P1 | 用 `dotnet-trace` + `dotnet-counters` 做一个大文件代表性样本 | 连接阶段 Stopwatch 与真实 CPU/GC 热路径 |
| P1 | 分离 Workspace load 与 analysis，并比较 cold/warm | 防止把 MSBuild/Roslyn 初始化误归因于规则 |
| P2 | 汇总 RuleGraphNodeTelemetry 并建立 top-node 报告 | 在确认 CPG 后定位规则层异常 |
| P2 | 单独比较 rewrite/diff/evidence/write-back | 只在分析阶段确认后再优化制品阶段 |
| P2 | 对 shard persistence 做 hit/miss/reuse 的 wall-time 和分配对比 | 评估缓存是否把构图成本转成 I/O/恢复成本 |

## 限制与未验证项

- 本报告没有运行当前 commit 的完整真实性能 benchmark，也没有运行 `dotnet-trace`、`dotnet-counters` 或 `dotnet-gcdump`；这些是下一轮实验建议。
- 历史 log 来自 2026-08-06，输入路径指向仓库外的大型 Terraria 源码；没有完整记录 git SHA、SDK、CPU 状态和所有配置，因此只能作为量级证据。
- 当前工作区 SDK 实际为 `10.0.400`，仓库要求的是 `10.0.200-preview.0.26103.119`；SDK 差异会影响 Roslyn、MSBuild 和 runtime 结果。
- runtime log 的 pool operation elapsed 是并发操作的累计/个别耗时，多个操作可能重叠；不能把它们相加后当成端到端 wall time。
- 规则选择会影响 required capabilities，进而影响 CPG pass 是否执行；比较不同规则集时必须记录 capability fingerprint。
- `dotnet-gcdump` 会触发 full/Gen 2 GC，诊断结果不应混入常规 benchmark 的中位数。
- Joern 和 CodeQL 只提供架构和分析工作流的外部对照，不提供与 NLCPG 可直接比较的时间、内存或并行度数据。
- 本报告未修改生产代码；工作区中其他用户改动保持原样。

## 来源

### 仓库内一手材料

- [developer-guide.md](../developer-guide.md)：现有性能 harness、DOP 矩阵和 Workspace 约束。
- [CommandHost.cs](../../src/NLISSN/Hosting/CommandHost.cs)
- [DirectoryAnalysisService.cs](../../src/NLISSN/Hosting/DirectoryAnalysisService.cs)
- [WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs)
- [MsBuildWorkspaceInputLoader.cs](../../src/NLISSN.Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs)
- [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs)
- [RuleGraphAnalysisExecutor.cs](../../src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs)
- [RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs)
- [NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs)
- [NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs)
- [PartitionedSyntaxPass.cs](../../src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs)
- [PartitionedOperationPass.cs](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs)
- [RuntimeMeasurementLog.cs](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs)

### GitHub 源码固定点

本轮源码级核对使用以下 shallow clone 的 `HEAD`；链接中的 commit 是证据固定点，不代表这些项目的性能数字可以直接迁移到 NLISSN。

| 项目 | commit | 重点源码 |
|---|---|---|
| LLVM `llvm-project` | `10d4bdf93a82fb290b8c55da6ed19ff54da67296` | `llvm/MCA`、`llvm-mca`、`llvm-exegesis` |
| LLVM LNT | `0955c116eb879f2061aba792605de7d435ccbe1e` | `lnt/tests/test_suite.py` |
| LLVM Test Suite | `2b51e7a7aa2653960774eb0251fc896d79e8cc2d` | `CMakeLists.txt`、`tools/timeit.c` |
| Roslyn | `38a51ec2d3a23600078f62be5b581464954c6e9f` | `src/Tools/Benchmarks`、`src/Tools/IdeBenchmarks` |
| CodeQL | `61bdd3cfdebd4a8f0e00a47dd45ed2cdf3dc6354` | `ql/ql/src/queries/performance` |
| Joern | `f117701adfd1e2e8c41d93c0760d115931df9ed6` | `frontends/x2cpg`、`console/cpgcreation` |

对应的初版设计见 [性能分析组件](../CodeDesign/目前设计/性能分析组件.md)。

### 网络一手材料

- Microsoft Roslyn [SemanticModel source](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModel.cs)
- Microsoft Roslyn [Compilation source](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/Compilation.cs)
- Microsoft Roslyn [SemanticModelProvider source](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModelProvider.cs)
- Microsoft Roslyn [MSBuild workspace source tree](https://github.com/dotnet/roslyn/tree/main/src/Workspaces/MSBuild)
- NuGet official [Microsoft.CodeAnalysis.Workspaces.MSBuild](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild)
- Microsoft [.NET `dotnet-trace`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- Microsoft [.NET `dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- Microsoft [.NET `dotnet-gcdump`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
- Joern [Code Property Graph](https://docs.joern.io/code-property-graph/)
- Joern [official repository README](https://raw.githubusercontent.com/joernio/joern/master/README.md)
- GitHub [Code scanning with CodeQL](https://docs.github.com/en/code-security/concepts/code-scanning/codeql/codeql-code-scanning)
- GitHub [CodeQL repository](https://github.com/github/codeql)
- BenchmarkDotNet [overview](https://benchmarkdotnet.org/articles/overview.html)
- BenchmarkDotNet [choosing a run strategy](https://benchmarkdotnet.org/articles/guides/choosing-run-strategy.html)
- BenchmarkDotNet [diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
