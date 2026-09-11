# 开发者指南

## 这篇解决什么问题

本页说明如何定位实现、选择验证和保持当前架构边界。运行配置见 [配置参考](cli-reference.md)，设计推导见 [`设计docs/`](../设计docs/README.md)。

## 工作入口

开始前阅读 `Context/AGENTS.md`、`Context/progress.md` 与 `Context/feature_list.json`，随后运行：

```powershell
pwsh -File .\Miscellaneous\init.ps1
```

`Context/feature_list.json` 定义完成条件；`Context/progress.md` 只记录当前事实与验证边界。

## 修改 NLCPG

入口和主要区域：

- CLI：`src/NLCPG/Program.cs`
- 构建器：`src/NLCPG/Builder/NLCPGBuilder.cs`
- 分片 passes：`src/NLCPG/Builder/Passes/`
- 图模型：`src/NLCPG/Model/`

并行 worker 只能读取 Roslyn semantic facts；稳定调用线程物化图节点、边、去重和顺序。修改分片、持久化或运行时后，要验证不同 DOP 下的图等价性和查询结果。

### Streaming CPG shard store

构建器可在 `NLCPGBuilderOptions.Persistence` 中传入 `CpgPersistenceOptions`，并将 `StreamingMode` 设为 `true`。该模式发布 `file-skeleton`、方法 shard 与 `cross-shard-edges`，不发布 `file-graph`。catalog 保存 node、symbol 与 span 位置；同一 source/profile/schema 的第二次构建会从这些 shard 重建 frozen graph。

store 根目录包含 `catalog.db`、`shards/` 与单 writer 锁文件。打开 store 时会删除遗留 `.tmp` 文件；丢失 catalog 时会从有效 `.cpgbin` header 重建，损坏 shard 会跳过。一个 store 同时只允许一个 writer。调用方需为 profile 的 builder 选项和 schema 变化提供不同的 `ProfileHash` 或版本号。

`CpgShardQueryResolver` 通过 catalog 按 node、symbol 或 span 打开 shard，并以字节上限执行 LRU 缓存。跨 shard slice 查询受 hop、path、call-depth 和访问预算约束；缺少起点或 frontier anchor 时，结果会填充 `UnavailableShards`。跨项目查询尚未提供。

## 修改 NLISSN

入口和主要区域：

- 配置入口：`src/NLISSN/Program.cs`
- 配置加载组件：`src/NLISSN.Infrastructure/Configuration/`
- MSBuild Workspace 输入组件：`src/NLISSN.Infrastructure/Workspace/`
- 配置运行器：`src/NLISSN/Hosting/ConfigurationRunHost.cs`
- 分析宿主：`src/NLISSN/Hosting/CommandHost.cs`
- 目录分析适配：`src/NLISSN/Hosting/DirectoryAnalysisService.cs`
- 工程分析适配：`src/NLISSN/Hosting/WorkspaceAnalysisService.cs`
- 应用编排：`src/NLISSN.Application/Analysis/ApplicationService.cs`
- 运行时：`src/NLISSN.Core/Pipeline/ExecutionRuntime.cs`
- 规则：`src/NLISSN.Rules/`

删除规则遵循“标记 → 传播 → 提升 → 决策 → 改写”。规则图以 `Target.*`、`Flow.*`、`Lift.*` 与 `Relation.*` 端口和语法契约连接；Atomic/Declaration provenance 仅用于证据与声明安全检查，不划分独立运行链。改动此链路前读取对应局部约束和 [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md)。

### Workspace / MSBuild 输入

`NLISSN.Infrastructure.Workspace` 是唯一持有 `MSBuildWorkspace` 的生产项目。它负责打开 `.sln`/`.csproj`、应用 `TargetFramework`、`Configuration`、`Platform` 和条件编译、收集项目引用与真实 metadata references，并把生成源标记为不可写的文档快照。`.csproj` 输入只选择该项目；`.sln` 默认按稳定路径顺序分析所有 C# 项目，`input.project` 可缩小范围。`.cs` 与 `input.project` 组合时，Workspace 打开 owning project，保留完整 `CSharpCompilation`，但 Hosting 只把目标文档交给 `DirectoryAnalysisUseCase.AnalyzeCompiled`。

Application 只接收 `CSharpCompilation`、`SemanticModel`、`SyntaxTree` 和项目源文件，不重新创建最小 compilation，也不引用 `MSBuildWorkspace`。项目引用保留为 Roslyn compilation reference，不能将引用项目源码拼进目标项目的源集合。缺失 reference、编译 error、未选择多 TFM 和 Workspace failure 必须 fail closed；单文档不属于指定项目时返回 `NLISSNWS025`。单文档结果和 rewrite plan 只能包含目标文档，manifest 的 `sourceFileCount` 为 `1`。生成源可以参与语义分析，但禁止物理写回和 rewrite plan capture。

`input.generators: enabled` 通过 `Project.GetSourceGeneratedDocumentsAsync` 执行 Workspace 的 source-generator pipeline。MSBuild analyzer references、`AdditionalFiles` 和 analyzer config 会进入 generator；返回的 generated documents 会显式加入真实 `CSharpCompilation`，并标记为不可写。对 `ProjectReference OutputItemType="Analyzer"`，Infrastructure 还用所选 `Configuration`、`Platform` 和 `TargetFramework` 读取 evaluated `TargetPath`；输出 DLL 不存在时返回 `NLISSNWS024` 并 fail closed，加载过程不会隐式 build analyzer 项目。`input.generators: disabled` 不执行 generator；只有位于项目外部且不在 NuGet 缓存或 .NET SDK 路径中的 generator reference 才产生提示。`generatedSources: exclude` 只从可写文档快照中排除生成源，不移除 compilation 中的语法树。

新增或修改这条链路时，至少验证真实 fixture 的 `.sln`、`.csproj`、`.cs + input.project`、项目引用 symbol、目标文档筛选、Debug/Release 预处理符号、generated `.g.cs`、framework reference 和 no-writeback；只看 CLI 退出码不足以证明语义正确。

## 测试与验证

测试工程位于 `tests/` 下的 Unit、Contract、Host 和 Performance 项目。先选择拥有该行为的最小项目，再扩大到分层执行：

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter "FullyQualifiedName~<TestName>" -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
```

真实源码性能测量独立于 `dotnet test`。`Run-ConcurrencyPoolPerformance.ps1` 要求显式输入
`.cs` 文件，默认对 DOP
1、8、12、16 各执行一次预热和三次测量，写入每次的 runtime log 以及中位数报告：

当需要区分目录 DOP 与每文件 CPG DOP 时，使用 `--cpg-max-degree-of-parallelism` 覆盖 builder 值，并比较 `(12,1)`、`(1,12)`、`(12,12)`。每组保持输入、规则、日志配置和 SDK 相同；阶段累计是逐文件 elapsed 总和，端到端裁决使用墙钟中位数。该诊断不会改变默认 DOP。

```powershell
pwsh -File .\Miscellaneous\scripts\Run-ConcurrencyPoolPerformance.ps1 `
  -SourceFile "D:\path\to\source\Example.cs" `
  -TargetName Example `
  -Dop "1,8,12,16"
```

报告根目录默认在 `Build\PerformanceResults\`，包含 `summary.json`、
`summary.csv` 和按 DOP/阶段隔离的日志。脚本会拒绝缺少成功终态日志或与 DOP 1
图/规则快照不一致的样本。该命令仅用于有意的性能决策；常规回归继续使用分层测试。

### 外部 profile 关联

`dotnet-trace`、`dotnet-counters` 和 `dotnet-gcdump` 是独立的开发机诊断工具，
不会由 NLISSN 自动安装，也不会在 `normal` 或普通 `benchmark` 测试中启动。需要
验证代表性 Roslyn fixture 的 profile attachment 时，先在开发机安装并确认三个命令
都位于 `PATH`：

```powershell
dotnet tool install --global dotnet-trace
dotnet tool install --global dotnet-counters
dotnet tool install --global dotnet-gcdump
dotnet-trace --version
dotnet-counters --version
dotnet-gcdump --version
```

然后显式启用外部诊断测试：

```powershell
$env:NLISSN_RUN_EXTERNAL_DIAGNOSTIC_TESTS = "1"
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~ExternalDiagnosticIntegrationTests"
```

该测试对当前测试进程运行受控的短时 fixture workload，并将每个工具的命令、版本、
开始/结束时间、退出码和相对 artifact 路径写入临时 manifest；manifest 只用同一个
`runId`/`stageId` 关联 attachment，不解析外部二进制内容。测试结束会清理临时目录。
工具未安装、环境变量未设置或工具命令失败都不会伪造成功 profile；未满足前置条件时
测试显式标记为 skipped。profile attachment 也不进入普通性能样本的 median/p95。

针对目录 DOP 与 CPG DOP 的拆分诊断，可先生成固定的 103 文件小型输入集：不超过
512 KiB 的 3 个最大 C# 文件加 100 个最小 C# 文件。选择按字节数排序，并以规范化相对路径作为并列排序键；
脚本会复制文件并写出包含字节数和 SHA-256 的 `manifest.json`，因此每次运行都能核对
输入没有漂移：

```powershell
pwsh -File .\Miscellaneous\scripts\New-CpgDopSmallFixture.ps1 `
  -SourceRoot "D:\lodes\TR\Backup\New1.27\1.45 2\TR" `
  -OutputRoot .\Build\cpg-dop-small-fixture-20260724 `
  -CleanOutput
```

生成的 fixture 以输出目录中的 `manifest.json` 为唯一输入记录；比较前确认其文件清单、字节数和 SHA-256 与待比较运行一致。该诊断用于定位 DOP 边界，不替代完整工程验收。

Mutation 检查保持独立。脚本默认以单并发和 Basic 级别运行
`DefaultDeleteProposalRule.cs`，并仅运行其 Unit 项目；需要扩大范围时显式传入 `-MutatePattern`、
`-MutationLevel` 和 `-Concurrency`。

并发调度测试保留现有的受控 checkpoint、故障日程和取消测试。已评估的
Microsoft Coyote PoC 未保留：在当前 `net10.0` 与 xUnit 组合中，它需要额外的 IL
rewrite 流程，且没有找到现有持久化、写入锁和取消覆盖之外的可复现调度。后续只有在
出现无法用这些受控测试表达的交错缺陷时，才重新评估该隔离 PoC。

运行前按根目录约束设置 `DOTNET_CLI_HOME`；`Miscellaneous/init.ps1` 会完成该设置。CLI、文档或 harness 改动应运行对应的 `dotnet build`、`dotnet test` 和 CLI smoke；随后使用 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1` 核对当前入口、文档与状态文件。

绑定校验只在 `nlissn.yml` 显式设置 `analysis.validateBindings: true` 时运行。它在规则图输出归并和 rewrite 前检查 CPG、规则和决策关系；Error 会保留诊断报告并跳过写入，默认分析路径不承担全图校验成本。

Schema 2 配置按 `parse -> diagnostics -> resolve -> map -> execute` 处理。语法、重复键和未知属性错误终止解析；对可解析文档，字段值、路径和制品约束会聚合为稳定排序的诊断。`resolved-configuration.json` 与 evidence 配置投影记录有效值、`explicit`/`schema-default` 来源和稳定指纹，指纹排除每次运行的制品路径。

## 文档与状态同步

- 用户入口与命令：维护 `README.md`、`docs/quick-start.md`、`docs/cli-reference.md`。
- 实现流程：维护 `docs/developer-guide.md`、`docs/contributing.md`。
- 当前 feature：只在 `Context/feature_list.json` 更新状态与完成条件。
- 当前交接：仅在必要时精简更新 `Context/progress.md`。

## 下一步

交付前检查 [贡献指南](contributing.md) 和 [Harness 验证矩阵](harness-verification-matrix.md)。
