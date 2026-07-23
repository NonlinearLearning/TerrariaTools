# 开发者指南

## 这篇解决什么问题

本页说明如何定位实现、选择验证和保持当前架构边界。命令参数见 [CLI 参考](cli-reference.md)，设计推导见 [`设计docs/`](../设计docs/README.md)。

## 工作入口

开始前阅读根目录 `AGENTS.md`、`progress.md` 与 `feature_list.json`，随后运行：

```powershell
pwsh -File .\init.ps1
```

`feature_list.json` 定义完成条件；`progress.md` 只记录当前事实与验证边界。

## 修改 MinimalRoslynCpg

入口和主要区域：

- CLI：`src/MinimalRoslynCpg/Program.cs`
- 构建器：`src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- 分片 passes：`src/MinimalRoslynCpg/Builder/Passes/`
- 图模型：`src/MinimalRoslynCpg/Model/`

并行 worker 只能读取 Roslyn semantic facts；稳定调用线程物化图节点、边、去重和顺序。修改分片、持久化或运行时后，要验证不同 DOP 下的图等价性和查询结果。

### Streaming CPG shard store

构建器可在 `RoslynCpgBuilderOptions.Persistence` 中传入 `CpgPersistenceOptions`，并将 `StreamingMode` 设为 `true`。该模式发布 `file-skeleton`、方法 shard 与 `cross-shard-edges`，不发布 `file-graph`。catalog 保存 node、symbol 与 span 位置；同一 source/profile/schema 的第二次构建会从这些 shard 重建 frozen graph。

store 根目录包含 `catalog.db`、`shards/` 与单 writer 锁文件。打开 store 时会删除遗留 `.tmp` 文件；丢失 catalog 时会从有效 `.cpgbin` header 重建，损坏 shard 会跳过。一个 store 同时只允许一个 writer。调用方需为 profile 的 builder 选项和 schema 变化提供不同的 `ProfileHash` 或版本号。

`CpgShardQueryResolver` 通过 catalog 按 node、symbol 或 span 打开 shard，并以字节上限执行 LRU 缓存。跨 shard slice 查询受 hop、path、call-depth 和访问预算约束；缺少起点或 frontier anchor 时，结果会填充 `UnavailableShards`。跨项目查询尚未提供。

## 修改 RoslynPrototype

入口和主要区域：

- CLI：`src/RoslynPrototype/Program.cs`
- 宿主：`src/Host/DeletionCommandHost.cs`
- 目录分析：`src/Host/DeletionDirectoryAnalysisService.cs`
- 应用编排：`src/Application/DeletionApplicationService.cs`
- 运行时：`src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`
- 规则：`src/Rules/`

删除规则遵循“标记 → 传播 → 提升 → 决策 → 改写”。改动此链路前读取对应局部约束和 [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md)。

## 测试与验证

测试工程位于 `tests/` 下的 Unit、Contract、Host 和 Performance 项目。先选择拥有该行为的最小项目，再扩大到分层执行：

```powershell
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore --filter "FullyQualifiedName~<TestName>" -p:UseSharedCompilation=false
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\Run-TestTiers.ps1 -Host
```

真实源码性能测量独立于 `dotnet test`。它要求显式输入目录，默认对 DOP
8、12、14、16 各执行一次预热和三次测量，写入每次的 runtime log 以及中位数报告：

```powershell
pwsh -File .\scripts\Run-PerformanceSuite.ps1 `
  -SourceRoot "D:\path\to\source" `
  -TargetName PlayerInput
```

报告根目录默认在 `Build\PerformanceResults\`，包含 `summary.json`、
`summary.csv`、`summary.md` 和按 DOP/阶段隔离的日志。该命令仅用于有意的
性能决策；常规回归继续使用分层测试。

CPG 微基准与 mutation 检查也保持独立：

```powershell
dotnet run --project .\tools\CpgMicrobenchmarks\CpgMicrobenchmarks.csproj -c Release -- --filter "*"
pwsh -File .\scripts\Run-MutationTests.ps1
```

前者分别测量固定仓库 fixture 的分片导出、分片序列化与写入、四分片目录发布、
以及内存图 slice 查询；它不调用目录 CLI。后者的存活 mutant 用于补充语义回归，
不构成覆盖率门槛。mutation 脚本默认以单并发和 Basic 级别运行
`DefaultDeleteProposalRule.cs`，并仅运行其 Unit 项目；需要扩大范围时显式传入 `-MutatePattern`、
`-MutationLevel` 和 `-Concurrency`。

并发调度测试保留现有的受控 checkpoint、故障日程和取消测试。已评估的
Microsoft Coyote PoC 未保留：在当前 `net10.0` 与 xUnit 组合中，它需要额外的 IL
rewrite 流程，且没有找到现有持久化、写入锁和取消覆盖之外的可复现调度。后续只有在
出现无法用这些受控测试表达的交错缺陷时，才重新评估该隔离 PoC。

运行前按根目录约束设置 `DOTNET_CLI_HOME`；`init.ps1` 会完成该设置。改动 CLI、文档或 harness 时，额外运行：

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
```

## 文档与状态同步

- 用户入口与命令：维护 `README.md`、`docs/quick-start.md`、`docs/cli-reference.md`。
- 实现流程：维护 `docs/developer-guide.md`、`docs/contributing.md`。
- 当前 feature：只在 `feature_list.json` 更新状态与完成条件。
- 当前交接：仅在必要时精简更新 `progress.md`。

## 下一步

交付前检查 [贡献指南](contributing.md) 和 [Harness 验证矩阵](harness-verification-matrix.md)。
