# Performance Fact Propagation and Run Report Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在不改变 NLISSN 分析语义、调度和结果身份的前提下，把现有 CPG、RuleGraph、artifact 和运行时性能事实传播为稳定的每文件/终态 JSON，并逐步接入诊断与 benchmark harness。

**Architecture:** 使用两条通道。完成摘要通道通过 `CpgPerformanceFacts -> ApplicationPerformanceFacts -> DirectoryPerformanceFacts -> RunPerformanceReport` 逐层传播 immutable facts；诊断事件通道仅在 diagnostic/profile 模式通过可选 sink 旁路记录高基数事件。Host 负责 Run identity、终态聚合和原子 JSON 发布，worker 不直接写最终 artifact。

**Tech Stack:** C#/.NET（以仓库 `global.json` 为准）、现有 NLCPG/NLISSN Application/Host/Infrastructure projects、xUnit、YamlDotNet、System.Text.Json、现有 `tests/NLISSN.*Tests` 与 `tests/NLISSN.PerformanceTests` harness。

> **当前执行状态（2026-09-21）：** P1-P4 的生产接线已完成，Workspace 额外保留 project/TFM 层级；P5 的外部工具 attachment policy 和 opt-in integration wrapper 已完成。普通生产 CLI 不自动启动 `dotnet-trace`、`dotnet-counters` 或 `dotnet-gcdump`，这是当前设计边界。生产 NLISSN build、性能相关 focused tests 和 Workspace schema contract 已通过；常规 PerformanceTests 在未设置 `NLISSN_RUN_TERRARIA_EXTERNAL_TESTS=1` 时为 65 通过、5 跳过、0 失败，外部 Terraria 样本仍需显式启用并提供数据集。

---

## 执行规则

- 本计划只描述实现顺序和验证方式；每一步都应先写 focused failing test，再实现最小行为，再运行该测试。
- P1–P3 不修改 CPG 并发、RuleGraph 调度、admission、NodeId、edge ordinal、pass 顺序或分析语义。
- 任何已有用户改动都必须保留。提交时只加入当前任务涉及的文件，不要把 `.learnings/ERRORS.md`、覆盖率专题或其他无关变更带入提交。
- 每个任务结束后创建一个小提交；提交消息只是建议，实际提交前检查 `git diff --check` 和测试输出。
- 计划中的路径以仓库根 `D:\ProjectItem\SourceCode\Net\NL` 为基准；命令使用 PowerShell 语法。

## 现状基线

实现前先确认以下事实，不要假设现有 API 已经完成传播：

- `src/NLCPG/Builder/NLCPGBuildMetrics.cs` 已有 pass、anchor、persistence、cache、data-flow、node/edge metrics。
- `src/NLISSN.Application/Analysis/ApplicationService.cs` 当前只把图规模放进 `PrototypeAnalysisResult`，需要在 builder 返回点映射完整 CPG facts。
- `src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs` 已有 RuleGraph telemetry、RuleGraph metrics 与 GraphMetrics，但尚无性能伴随字段。
- `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs` 保留 `FileResults`，但 `BuildResult`/`CombineResults` 当前只聚合业务结果。
- `src/NLISSN/Hosting/DirectoryAnalysisService.cs`、`WorkspaceAnalysisService.cs` 和 `CommandHost.cs` 当前最终返回 `PrototypeAnalysisResult`。
- `src/NLISSN/Telemetry/RuntimeMeasurementLog.cs` 已能输出总耗时、业务计数、图规模、规则峰值、pool 和进程采样，但不应成为所有领域 metrics 的 owner。
- `src/NLISSN.Infrastructure/Configuration/ArtifactSettings.cs`、`YamlConfigurationLoader.cs` 和 `Miscellaneous/schemas/nlissn.schema.2.json` 尚无 `artifacts.performance`。

基线命令：

```powershell
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
```

预期：基线项目可以编译，相关既有测试通过；若基线已经失败，先记录失败测试和原因，不能把它归因于本功能。

## P1: 完成摘要和事实传播

### Task 1: 建立性能域的中立类型和状态语义

**Files:**
- Create: `src/NLISSN.Core/Performance/PerformanceStatus.cs`
- Create: `src/NLISSN.Core/Performance/CpgPerformanceFacts.cs`
- Create: `src/NLISSN.Core/Performance/ApplicationPerformanceFacts.cs`
- Create: `src/NLISSN.Core/Performance/DirectoryPerformanceFacts.cs`
- Create: `src/NLISSN.Core/Performance/RunPerformanceReport.cs`
- Test: `tests/NLISSN.UnitTests/Performance/PerformanceFactsTests.cs`

**Step 1: Write the failing test**

覆盖 immutable facts 的构造、状态值、`wallElapsedMs`/`accumulatedElapsedMs` 区分、未知值 null、stable identity 和没有 children 的单项 `ApplicationPerformanceFacts`。

**Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceFactsTests"
```

Expected: FAIL because the performance types do not exist.

**Step 3: Write minimal implementation**

定义只包含中立标量、只读集合和嵌套 facts 的 record。禁止引用 `NLCPGBuildMetrics`、`SemanticModel`、`Compilation`、CPG graph、Builder、Stopwatch、logger 或 serializer。明确 `completed/failed/cancelled/skipped/unavailable/unknown`，并让 aggregate population 与 comparison eligibility 可表达。

**Step 4: Run test to verify it passes**

Run the same focused test. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Performance tests/NLISSN.UnitTests/Performance/PerformanceFactsTests.cs
git commit -m "feat: define performance fact contracts"
```

### Task 2: 将 NLCPG metrics 映射为单文件 CPG facts

**Files:**
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs`
- Create: `src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGPerformanceFactMappingTests.cs`
- Test: `tests/NLISSN.HostTests/Application/ApplicationServicePerformanceTests.cs`

**Step 1: Write the failing test**

用现有 CPG fixture 构造单文件分析，断言 `NLCPGBuildMetrics` 的 build elapsed、pass、anchor、persistence、cache、data-flow、node/edge facts 可在 `result.Performance.Cpg` 读取；同时断言 mapper 不改变 decisions、edits、graph snapshot。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~NLCPGPerformanceFactMappingTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ApplicationServicePerformanceTests"
```

Expected: FAIL because `PrototypeAnalysisResult.Performance` and the mapper do not exist.

**Step 3: Write minimal implementation**

在 `BuildAnalysisContextCore` 中让 builder 返回后的 `LastBuildMetrics` 立即进入 mapper；由 Application 生成当前 item 的 `ApplicationPerformanceFacts`，并将其作为可选 `Performance` 伴随字段放进 `PrototypeAnalysisResult`。不要把 builder 实例留给 Host，也不要在 mapper 中重新测量。

**Step 4: Run test to verify it passes**

运行上面两个 focused commands。Expected: PASS，并且现有 CPG contract tests 不回归。

**Step 5: Commit**

```powershell
git add src/NLISSN.Application/Analysis/ApplicationService.cs src/NLISSN.Application/Performance src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs tests/NLISSN.ContractTests/Cpg/NLCPGPerformanceFactMappingTests.cs tests/NLISSN.HostTests/Application/ApplicationServicePerformanceTests.cs
git commit -m "feat: propagate per-file cpg performance facts"
```

### Task 3: 把 RuleGraph telemetry 纳入 Application facts

**Files:**
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- Modify: `src/NLISSN.Core/Performance/ApplicationPerformanceFacts.cs`
- Test: `tests/NLISSN.HostTests/Application/RuleGraphPerformanceFactTests.cs`

**Step 1: Write the failing test**

对已有 RuleGraph fixture 断言每个 `RuleGraphNodeTelemetry` 按稳定 `NodeId` 映射到 facts，保留 input/output/elapsed/status，并保留 ready/concurrent peaks；无 RuleGraph 的兼容路径返回明确 unavailable/empty，而不是伪造零耗时节点。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RuleGraphPerformanceFactTests"
```

Expected: FAIL because rule telemetry is still only exposed through the old result fields.

**Step 3: Write minimal implementation**

用 `RuleGraphNodeTelemetry` 作为唯一低层来源映射 rule stage facts。保留原字段以兼容现有 callers，但不复制或重跑 RuleGraph；排序只在报告投影层按稳定 identity 执行。

**Step 4: Run test to verify it passes**

Run the focused Host test and the existing RuleGraph test filter. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Application/Analysis src/NLISSN.Core/Performance tests/NLISSN.HostTests/Application/RuleGraphPerformanceFactTests.cs
git commit -m "feat: include rule graph telemetry in performance facts"
```

### Task 4: 保留逐文件结果并确定性聚合目录 facts

**Files:**
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs` (`DirectoryAnalysisOutcome`/`DirectoryFileAnalysisResult` contracts)
- Test: `tests/NLISSN.UnitTests/Analysis/DirectoryPerformanceAggregationTests.cs`
- Test: `tests/NLISSN.HostTests/Application/DirectoryAnalysisPerformanceTests.cs`

**Step 1: Write the failing test**

构造两个或多个带不同 elapsed 的 file results，以不同完成顺序输入聚合器，断言 children 按 Index/FilePath 稳定排序，aggregate 记录 count/sum/max/top item，且目录 wall time 不等于 children sum。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DirectoryPerformanceAggregationTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DirectoryAnalysisPerformanceTests"
```

Expected: FAIL because `BuildResult`/`CombineResults` discard performance facts.

**Step 3: Write minimal implementation**

扩展 `DirectoryFileAnalysisResult`/`DirectoryAnalysisOutcome` 以保留 item facts，令 `BuildResult` 和 `CombineResults` 通过 stable index/identity 做一次聚合。保持所有已有业务字段、rewrite plan、evidence 和 result ordering 不变。空目录必须返回空但合法的 performance outcome。

**Step 4: Run test to verify it passes**

运行两个 focused tests，再运行 DirectoryAnalysisUseCase 现有测试。Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs tests/NLISSN.UnitTests/Analysis/DirectoryPerformanceAggregationTests.cs tests/NLISSN.HostTests/Application/DirectoryAnalysisPerformanceTests.cs
git commit -m "feat: retain deterministic directory performance facts"
```

### Task 5: P1 终态 report 投影和最小 JSON writer

**Files:**
- Create: `src/NLISSN/Performance/PerformanceSummaryDocument.cs`
- Create: `src/NLISSN/Performance/PerformanceSummaryWriter.cs`
- Create: `src/NLISSN/Performance/PerformanceSummaryPublisher.cs`
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Modify: `src/NLISSN/Telemetry/RuntimeMeasurementLog.cs`
- Test: `tests/NLISSN.HostTests/Performance/PerformanceSummaryWriterTests.cs`
- Test: `tests/NLISSN.HostTests/Performance/PerformanceSummaryPublicationTests.cs`

**Step 1: Write the failing test**

给定固定 Run report，断言 wire DTO 有独立 schemaVersion、terminal status、稳定排序和 null/zero 区分；模拟 writer 异常，断言业务结果仍返回；模拟重复发布，断言只发布一次终态。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceSummaryWriterTests|FullyQualifiedName~PerformanceSummaryPublicationTests"
```

Expected: FAIL because the DTO, publisher and terminal performance report do not exist.

**Step 3: Write minimal implementation**

实现 `facts -> RunPerformanceReport -> PerformanceSummaryDocument` 的显式映射。P1 只接通 CPG/RuleGraph/terminal facts；未接通 Workspace/Artifact 父阶段写 `unavailable`。writer 先写同目录临时文件，flush/close 后用现有平台可用的原子替换策略发布 `Performance/summary.json`；异常被捕获并记录为 report publication failure，不抛回业务分析。

**Step 4: Run test to verify it passes**

运行 focused Host tests和已有 RuntimeMeasurementLog tests。Expected: PASS；通过 JSON parse 验证输出可读。

**Step 5: Commit**

```powershell
git add src/NLISSN/Performance src/NLISSN/Hosting/CommandHost.cs src/NLISSN/Telemetry/RuntimeMeasurementLog.cs tests/NLISSN.HostTests/Performance
git commit -m "feat: publish terminal performance summary"
```

### Task 6: P1 语义不变和失败隔离验证

**Files:**
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`（仅在现有合适 fixture 增加断言）
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- Create: `tests/NLISSN.HostTests/Performance/PerformanceFailureIsolationTests.cs`

**Step 1: Write the failing test**

覆盖性能 disabled、sink 抛异常、summary writer I/O 失败三种路径，比较开启/关闭性能时 graph/rule/decision/evidence/rewrite/diff snapshot；对 DOP 1、2、16（受环境限制可记录实际值）比较非时间业务 snapshot。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceFailureIsolationTests|FullyQualifiedName~PipelineComponentTests"
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceOptimizationRegressionTests"
```

Expected: FAIL until the new report path and fault injection seams exist.

**Step 3: Write minimal implementation**

增加只影响测试的 writer/sink injection seam；不要在生产路径中吞掉业务异常。验证性能组件只能丢弃诊断数据，不能修改业务 result 或调度。

**Step 4: Run test to verify it passes**

Run the focused commands, then the full Unit/Contract/Host suites. Expected: PASS。

**Step 5: Commit**

```powershell
git add tests/NLISSN.HostTests/Application/PipelineComponentTests.cs tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs tests/NLISSN.HostTests/Performance/PerformanceFailureIsolationTests.cs
git commit -m "test: prove performance instrumentation is fail-open"
```

## P2: Host 阶段、资源语义和配置接入

### Task 7: 引入独立 AnalysisRunOutcome 并贯通 Directory/Workspace

**Files:**
- Create: `src/NLISSN.Application/Analysis/AnalysisRunOutcome.cs`
- Modify: `src/NLISSN/Hosting/DirectoryAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Modify: `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`
- Test: `tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisPerformanceTests.cs`
- Test: `tests/NLISSN.HostTests/Application/AnalysisRunOutcomeTests.cs`

**Step 1: Write the failing test**

从 directory、project 和 workspace 入口执行代表性 fixture，断言业务 `Result` 与 `Performance` 同时返回；Workspace project/TFM 层级保留，且 Host 不需要反向访问 Builder。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~WorkspaceAnalysisPerformanceTests|FullyQualifiedName~AnalysisRunOutcomeTests"
```

Expected: FAIL because service methods return only `PrototypeAnalysisResult`。

**Step 3: Write minimal implementation**

新增独立 outcome，逐步改变内部 service 返回类型；在 CLI/legacy public boundary 需要兼容时，只在边界投影 `Result`，不要丢掉内部 report。Workspace loader/open/project/TFM facts 由 Workspace owner 产生，不能混入 CPG facts。

**Step 4: Run test to verify it passes**

Run focused Host tests and existing Workspace host tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Application/Analysis/AnalysisRunOutcome.cs src/NLISSN/Hosting/DirectoryAnalysisService.cs src/NLISSN/Hosting/WorkspaceAnalysisService.cs src/NLISSN/Hosting/CommandHost.cs src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisPerformanceTests.cs tests/NLISSN.HostTests/Application/AnalysisRunOutcomeTests.cs
git commit -m "refactor: carry run performance outcome separately"
```

### Task 8: 加入 Workspace、Directory、Artifact 父阶段 scope

**Files:**
- Create: `src/NLISSN.Core/Performance/PerformanceStageId.cs`
- Create: `src/NLISSN.Core/Performance/PerformanceStageSample.cs`
- Create: `src/NLISSN.Application/Performance/PerformanceStageScope.cs`
- Modify: `src/NLISSN/Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs`
- Modify: `src/NLISSN/Hosting/DirectoryAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Test: `tests/NLISSN.HostTests/Performance/PerformanceStageTreeTests.cs`

**Step 1: Write the failing test**

断言阶段树包含 `Workspace.Load`、`Directory.Read`、`CPG.Build`、`Rule.Mark`、`Rule.Propagate`、`Rule.Lift`、`Rule.Propose`、`Artifact.Rewrite`、`Artifact.Diff`、`Artifact.Evidence`、`Artifact.WriteBack` 等稳定协议名；父 wall time 独立存在，children sum 可大于父 wall time。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceStageTreeTests"
```

Expected: FAIL because current runtime log has no stage tree.

**Step 3: Write minimal implementation**

定义稳定 stage protocol 和 scope。scope 只记录 start/end、wall elapsed、status 和可选 counters；父阶段不由 children 相加推导。P2 未接通的阶段显式为 `unavailable`，配置跳过为 `skipped`。

**Step 4: Run test to verify it passes**

Run focused stage-tree tests plus Directory/Workspace host tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Performance src/NLISSN.Application/Performance src/NLISSN.Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs src/NLISSN/Hosting tests/NLISSN.HostTests/Performance/PerformanceStageTreeTests.cs
git commit -m "feat: record stable host performance stages"
```

### Task 9: 配置 schema、loader 和独立 Performance artifact

**Files:**
- Modify: `Miscellaneous/schemas/nlissn.schema.2.json`
- Modify: `src/NLISSN.Infrastructure/Configuration/ArtifactSettings.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/ResolvedConfigurationArtifact.cs`
- Modify: `src/NLISSN.Infrastructure/Configuration/AnalysisConfiguration.cs`
- Test: `tests/NLISSN.ContractTests/Configuration/NlissnSchemaContractTests.cs`
- Modify: `tests/NLISSN.HostTests/Configuration/YamlConfigurationLoaderTests.cs`

**Step 1: Write the failing test**

增加配置 fixture：默认不生成性能报告；显式 `artifacts.performance.enabled: true` 映射到 `<runRoot>/Performance/summary.json`；runtimeLog 与 performance 可以独立启用；resolved configuration fingerprint 包含 enable/mode。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~NlissnSchemaContractTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~YamlConfigurationLoaderTests"
```

Expected: FAIL because schema, YAML model and `ArtifactSettings` do not contain performance configuration.

**Step 3: Write minimal implementation**

增加 `YamlPerformance`/resolved settings 和 `PerformanceSummaryPath`，默认 false；沿用现有 artifact root/run id 解析。不要复用 `analysisLog`，因为它当前明确没有 writer 且启用会被拒绝。

**Step 4: Run test to verify it passes**

Run both focused configuration suites and existing schema tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add Miscellaneous/schemas/nlissn.schema.2.json src/NLISSN.Infrastructure/Configuration/ArtifactSettings.cs src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs src/NLISSN.Infrastructure/Configuration/ResolvedConfigurationArtifact.cs src/NLISSN.Infrastructure/Configuration/AnalysisConfiguration.cs tests/NLISSN.ContractTests/Configuration/NlissnSchemaContractTests.cs tests/NLISSN.HostTests/Configuration/YamlConfigurationLoaderTests.cs
git commit -m "feat: configure opt-in performance artifacts"
```

### Task 10: 资源 attribution、pool facts 和终态完整性

**Files:**
- Modify: `src/NLISSN/Telemetry/RuntimeMeasurementLog.cs`
- Modify: `src/NLISSN.Application/Performance/PerformanceStageScope.cs`
- Modify: `src/NLISSN.Core/Performance/RunPerformanceReport.cs`
- Create: `tests/NLISSN.HostTests/Performance/PerformanceResourceAttributionTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs`

**Step 1: Write the failing test**

覆盖 process/run 级 allocation、GC、heap、working set、pool queue wait、peak active/buffer；并发文件 scope 的 resource attribution 必须为 null，独占 scope 才可以填值；缺少 terminal facts 时 completeness false。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceResourceAttributionTests"
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ConcurrencyPoolContractTests"
```

Expected: FAIL until attribution level and completeness are represented.

**Step 3: Write minimal implementation**

把 RuntimeMeasurementLog 的已有轻量字段映射为 run/顶层 facts；不要把全局 GC/allocation 差值分摊给并发文件。pool telemetry 保留 queue wait 与 peaks，但不把 pool elapsed 当作 parent wall time。

**Step 4: Run test to verify it passes**

Run focused Host/Performance tests and the full related suites. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Telemetry/RuntimeMeasurementLog.cs src/NLISSN.Application/Performance src/NLISSN.Core/Performance tests/NLISSN.HostTests/Performance/PerformanceResourceAttributionTests.cs tests/NLISSN.PerformanceTests/Concurrency/ConcurrencyPoolContractTests.cs
git commit -m "feat: preserve run-level resource attribution"
```

### Task 11: P2 端到端 artifact 与失败隔离

**Files:**
- Modify: `src/NLISSN/Hosting/CommandHost.cs`
- Modify: `src/NLISSN/Hosting/DirectoryAnalysisService.cs`
- Modify: `src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- Modify: `tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs`
- Create: `tests/NLISSN.HostTests/Performance/PerformanceArtifactEndToEndTests.cs`

**Step 1: Write the failing test**

执行 file/directory/workspace 的 opt-in 配置，断言只在成功终态生成 `Performance/summary.json`，JSON 可解析且 children 稳定排序；关闭配置时不存在该文件；artifact writer 失败仍返回业务结果。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceArtifactEndToEndTests|FullyQualifiedName~WorkspaceAnalysisHostTests"
```

Expected: FAIL until CommandHost creates and publishes the report for all supported entry points.

**Step 3: Write minimal implementation**

在 Host 完成 evidence/rewrite/diff/write-back 状态收集后构造 terminal report；性能 artifact 作为独立可选步骤执行。保持 replay、write-back、evidence 和 existing runtime log 行为不变。

**Step 4: Run test to verify it passes**

Run focused end-to-end tests, then Unit/Contract/Host full suites. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Hosting/CommandHost.cs src/NLISSN/Hosting/DirectoryAnalysisService.cs src/NLISSN/Hosting/WorkspaceAnalysisService.cs tests/NLISSN.HostTests/Workspace/WorkspaceAnalysisHostTests.cs tests/NLISSN.HostTests/Performance/PerformanceArtifactEndToEndTests.cs
git commit -m "test: verify opt-in performance artifact end to end"
```

## P3: 诊断事件和 partition 细节（diagnostic-only）

### Task 12: 定义可选 PerformanceEventSink 和事件故障语义

**Files:**
- Create: `src/NLISSN.Core/Performance/PerformanceEvent.cs`
- Create: `src/NLISSN.Core/Performance/IPerformanceEventSink.cs`
- Create: `src/NLISSN.Application/Performance/NullPerformanceEventSink.cs`
- Modify: `src/NLISSN.Application/Analysis/ApplicationService.cs`
- Modify: `src/NLISSN.Rule/RuleGraphExecutor.cs`
- Test: `tests/NLISSN.UnitTests/Performance/PerformanceEventSinkTests.cs`
- Test: `tests/NLISSN.HostTests/Performance/PerformanceEventFailureIsolationTests.cs`

**Step 1: Write the failing test**

验证 sink disabled 时无事件开销路径；sink 接收事件时按 stable identity 保留；sink 抛异常或丢弃事件不影响 graph/rule/result；diagnostic events 不进入 terminal measurement population。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceEventSinkTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceEventFailureIsolationTests"
```

Expected: FAIL because no event sink contract exists.

**Step 3: Write minimal implementation**

事件只含 stage/item identity、elapsed/counters/status 和 run association；不能携带可变领域对象。调用采用 try/catch 隔离，禁止共享全局 mutable collector。normal/benchmark 使用 Null sink 或不调用 sink。

**Step 4: Run test to verify it passes**

Run focused tests and P1/P2 report tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN.Core/Performance src/NLISSN.Application/Performance src/NLISSN.Application/Analysis/ApplicationService.cs src/NLISSN.Rule/RuleGraphExecutor.cs tests/NLISSN.UnitTests/Performance/PerformanceEventSinkTests.cs tests/NLISSN.HostTests/Performance/PerformanceEventFailureIsolationTests.cs
git commit -m "feat: add optional diagnostic performance events"
```

### Task 13: diagnostic-only partition collection/materialization facts

**Files:**
- Modify: `src/NLCPG/Builder/PartitionedSyntaxPass.cs`
- Modify: `src/NLCPG/Builder/PartitionedOperationPass.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Create: `src/NLCPG/Builder/PartitionPerformanceEvent.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/PartitionPerformanceDiagnosticsTests.cs`
- Test: `tests/NLISSN.PerformanceTests/Performance/CpgPartitionDiagnosticTests.cs`

**Step 1: Write the failing test**

在 diagnostic 模式下断言 partition collection/materialization event 包含 partition identity、input/output count、wall/accumulated elapsed、queue/admission facts；normal 模式不输出这些高基数事件。DOP 1/2/16 下 graph/node/edge snapshot 必须等价。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PartitionPerformanceDiagnosticsTests"
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgPartitionDiagnosticTests"
```

Expected: FAIL because partition facts are not exposed through diagnostic-only events.

**Step 3: Write minimal implementation**

在既有 collection/materialization 边界添加可选事件调用；不修改 partition 分配、worker 写入、排序、admission 或 merge 算法。partition resource attribution 仍为 null，除非 scope 明确独占。

**Step 4: Run test to verify it passes**

Run focused Contract/Performance tests and existing partition builder contract tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLCPG/Builder/PartitionedSyntaxPass.cs src/NLCPG/Builder/PartitionedOperationPass.cs src/NLCPG/Builder/NLCPGBuilder.cs src/NLCPG/Builder/PartitionPerformanceEvent.cs tests/NLISSN.ContractTests/Cpg/PartitionPerformanceDiagnosticsTests.cs tests/NLISSN.PerformanceTests/Performance/CpgPartitionDiagnosticTests.cs
git commit -m "feat: add diagnostic-only partition performance facts"
```

### Task 14: P3 diagnostic attachment manifest和等价性门禁

**Files:**
- Create: `src/NLISSN/Performance/PerformanceDiagnosticAttachment.cs`
- Modify: `src/NLISSN/Performance/PerformanceSummaryDocument.cs`
- Modify: `src/NLISSN/Performance/PerformanceSummaryWriter.cs`
- Create: `tests/NLISSN.HostTests/Performance/DiagnosticAttachmentContractTests.cs`

**Step 1: Write the failing test**

断言 attachment 只记录相对路径、kind、runId/stageId、mode、完整性和可用性；丢失 attachment 不会使 summary 伪造成功；diagnostic report 明确不参与 normal/benchmark aggregate。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiagnosticAttachmentContractTests"
```

Expected: FAIL because no attachment manifest contract exists.

**Step 3: Write minimal implementation**

把事件和未来外部 profile 作为 attachment reference，不把二进制内容嵌入 summary。保持 artifact path 在 run root 内，稳定排序，缺失/不可用显式编码。

**Step 4: Run test to verify it passes**

Run focused attachment and summary contract tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Performance tests/NLISSN.HostTests/Performance/DiagnosticAttachmentContractTests.cs
git commit -m "feat: describe diagnostic performance attachments"
```

## P4: Benchmark harness、样本聚合和比较资格

### Task 15: 设计并实现 Run identity 和 snapshot equivalence

**Files:**
- Create: `src/NLISSN/Performance/PerformanceComparisonIdentity.cs`
- Create: `src/NLISSN/Performance/PerformanceEquivalenceChecker.cs`
- Modify: `src/NLISSN/Performance/RunPerformanceReport.cs`
- Create: `tests/NLISSN.UnitTests/Performance/PerformanceEquivalenceCheckerTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`

**Step 1: Write the failing test**

覆盖相同输入/config/cache/DOP/snapshot 可比较；任一 identity 不同、terminal 缺失、失败/取消或业务 snapshot 不等价时 `comparisonEligible=false` 且给出稳定 reason code。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceEquivalenceCheckerTests"
```

Expected: FAIL because no comparison checker exists.

**Step 3: Write minimal implementation**

比较器只读取报告中的 immutable identity 和 snapshot，不访问 semantic model、CPG graph 或重新执行分析。失败样本保留在 raw sample list，但拒绝进入 aggregate population。

**Step 4: Run test to verify it passes**

Run focused Unit test and existing DOP snapshot tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Performance tests/NLISSN.UnitTests/Performance/PerformanceEquivalenceCheckerTests.cs tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs
git commit -m "feat: gate performance samples by equivalence"
```

### Task 16: 实现样本 warmup、median/p95 和 top-item 聚合

**Files:**
- Create: `src/NLISSN/Performance/PerformanceSampleAggregator.cs`
- Modify: `src/NLISSN/Performance/PerformanceSummaryDocument.cs`
- Test: `tests/NLISSN.UnitTests/Performance/PerformanceSampleAggregatorTests.cs`
- Test: `tests/NLISSN.ContractTests/Performance/PerformanceSummarySchemaTests.cs`

**Step 1: Write the failing test**

给定 warmup、三次 measurement、失败样本、低样本集合，断言 warmup 不进入统计，少于三次只输出 raw/low-confidence，三次及以上输出 median/p95/count/sum/max/top item；并发累计值单独统计。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceSampleAggregatorTests"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceSummarySchemaTests"
```

Expected: FAIL because aggregate and schema contract are not implemented.

**Step 3: Write minimal implementation**

聚合器输入必须先经过 equivalence checker；明确 percentile 算法、样本数和小样本状态，且不把 diagnostic/profile samples 混入 normal/benchmark。JSON schema 或 example 在此任务同步固定字段语义。

**Step 4: Run test to verify it passes**

Run both focused tests and the performance project. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Performance tests/NLISSN.UnitTests/Performance/PerformanceSampleAggregatorTests.cs tests/NLISSN.ContractTests/Performance/PerformanceSummarySchemaTests.cs
git commit -m "feat: aggregate comparable performance samples"
```

### Task 17: 接入现有 PerformanceTests 的固定 fixture 和 DOP matrix

**Files:**
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/TestSupport/CommandHostTestConfigurationExtensions.cs`
- Create: `tests/NLISSN.PerformanceTests/Performance/PerformanceSummaryHarnessTests.cs`
- Modify: `docs/harness-verification-matrix.md`（只记录实际可运行命令和环境约束）

**Step 1: Write the failing test**

建立固定源码/fixture、一次 warmup 加三次 measurement，以及 `(1,1)/(1,12)/(12,1)/(12,12)` directory/cpg DOP 组合；断言可比较样本的业务 snapshot 等价并生成 summary。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceSummaryHarnessTests"
```

Expected: FAIL until the harness consumes the new report and sample metadata。

**Step 3: Write minimal implementation**

复用现有 `PerformanceOptimizationRegressionTests` 的 fixture/snapshot helper；harness 不改变调度参数之外的分析设置，不将测量结果写入 repository tracked files。环境不支持某 DOP 时记录 skipped/unsupported，不把它当作性能零值。

**Step 4: Run test to verify it passes**

Run focused harness tests and the full PerformanceTests project. Expected: PASS；输出每次样本的 runId、mode、warmup、eligibility 和 summary path。

**Step 5: Commit**

```powershell
git add tests/NLISSN.PerformanceTests docs/harness-verification-matrix.md
git commit -m "test: consume performance summaries in harness"
```

## P5: 外部诊断工具关联

### Task 18: 设计 trace/counters/gcdump attachment 生命周期

**Files:**
- Create: `src/NLISSN/Performance/ExternalDiagnosticAttachment.cs`
- Create: `src/NLISSN/Performance/ExternalDiagnosticToolPolicy.cs`
- Modify: `src/NLISSN/Performance/PerformanceDiagnosticAttachment.cs`
- Test: `tests/NLISSN.UnitTests/Performance/ExternalDiagnosticAttachmentTests.cs`
- Test: `tests/NLISSN.HostTests/Performance/ExternalDiagnosticToolPolicyTests.cs`

**Step 1: Write the failing test**

覆盖 tool unavailable、command failure、成功 attachment、临时文件清理和 run/stage association；normal 模式不启动外部工具，profile 模式失败也不能改变业务结果。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ExternalDiagnosticAttachmentTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ExternalDiagnosticToolPolicyTests"
```

Expected: FAIL because external diagnostic policy and attachment lifecycle are not defined.

**Step 3: Write minimal implementation**

只保存命令、版本、开始/结束、exit code、相对 artifact path、runId/stageId 和 availability；默认不自动调用工具。将工具执行置于 profile mode 的显式 policy 中，禁止把 profile 数据写入 normal aggregate。

**Step 4: Run test to verify it passes**

Run focused Unit/Host tests. Expected: PASS。

**Step 5: Commit**

```powershell
git add src/NLISSN/Performance tests/NLISSN.UnitTests/Performance/ExternalDiagnosticAttachmentTests.cs tests/NLISSN.HostTests/Performance/ExternalDiagnosticToolPolicyTests.cs
git commit -m "feat: define external diagnostic attachments"
```

### Task 19: 在代表性样本上验证外部 profile 关联

**Files:**
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceSummaryHarnessTests.cs`
- Create: `tests/NLISSN.PerformanceTests/Performance/ExternalDiagnosticIntegrationTests.cs`
- Modify: `docs/developer-guide.md`（补充显式工具前置条件和命令）
- Modify: `设计docs/目前设计/性能分析组件.md`（记录已验证工具能力，不改变 P1 边界）

**Step 1: Write the failing test**

在工具存在时验证代表性 fixture 的 trace/counters/gcdump manifest 能关联到同一 runId/stageId；工具不存在时 test 标记 skipped，不伪造成功报告。

**Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ExternalDiagnosticIntegrationTests"
```

Expected: FAIL when integration harness and attachment association have not been implemented; on machines without a tool the test must produce an explicit skipped result rather than a false pass.

**Step 3: Write minimal implementation**

实现受控开发机上的 integration wrapper，限定输入、超时、输出目录和 cleanup；读取 attachment manifest 验证 association，不对外部 trace 内部内容作业务判断。

**Step 4: Run test to verify it passes**

Run the integration test with explicit tool prerequisites, then the normal PerformanceTests suite with profile integration disabled. Expected: profile run succeeds when tools exist; ordinary suite remains green and has no diagnostic attachment dependency。

**Step 5: Commit**

```powershell
git add tests/NLISSN.PerformanceTests docs/developer-guide.md 设计docs/目前设计/性能分析组件.md
git commit -m "test: verify external performance profile association"
```

## 最终验证和交付检查

完成 P1–P5 后，在干净的临时 artifact root 上按以下顺序执行：

```powershell
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false
git diff --check
```

逐项核验：

- 默认配置没有 `Performance/summary.json`；显式 enable 才发布。
- summary JSON 可解析，schemaVersion、runId、mode、terminal status、comparison eligibility 和稳定排序字段存在。
- 单文件、目录、Workspace 的 business result 与性能开启/关闭等价。
- `wallElapsedMs` 与 `accumulatedElapsedMs` 语义可区分，未知/不可用/跳过/零值不混淆。
- 失败、取消、缺少终态和 snapshot 不等价的样本保留 raw facts 但不进入统计。
- normal/benchmark 不依赖 event sink、profile tool 或 partition detail。
- diagnostic/profile attachment 不进入正式 median/p95。
- 相关设计入口、PRD、计划、schema example 和 harness 文档链接有效。
- `git status --short` 只包含本功能实际修改和用户已有变更；无临时 JSON、profile、构建产物或测试输出被误加入版本控制。

完成后，应按仓库流程提交最终变更，并把未实现的后续优化（通用 profiler、调度修改、自动回归判定）继续留在 non-goals，而不是隐式扩展本计划。

## 当前验收证据和未闭合边界

2026-09-21 的隔离 worktree 验证结果：

- `dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false`：0 warning、0 error。
- 性能相关 Unit / Contract / Host 定向测试、Workspace schema contract 和阶段失败语义测试均通过。
- PerformanceTests 中 summary harness、DOP 矩阵、等价性和 external diagnostic wrapper 定向测试：21 通过。
- Workspace project/TFM 层级 summary Host 回归：1 通过。
- 完整 PerformanceTests：65 通过、5 跳过、0 失败；5 个跳过项属于显式 Terraria 外部测试，只有设置 `NLISSN_RUN_TERRARIA_EXTERNAL_TESTS=1` 才会运行。

因此，“性能事实传播和报告实现”已经有可验证的生产接线，常规 PerformanceTests 也已在外部样本门禁下通过。若要验收 Terraria 外部样本，仍需在具备 `D:\lodes\TR\Backup\New1.27\1.45 2\TR` 的环境中显式启用该测试集合；不应复制用户原工作区内容、放宽断言或伪造 profile 结果。

