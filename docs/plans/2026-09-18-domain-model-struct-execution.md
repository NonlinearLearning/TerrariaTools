# 内部领域数据载体值类型实验执行计划

关联设计：[内部领域数据载体值类型实验设计](2026-09-18-domain-model-struct-design.md)

分支：`experiment/domain-model-struct`

## 交付边界

本分支从当前工作树创建，保留创建前已有的未提交改动。实验只修改设计明确的
内部 CPG/NLISSN carrier、相关测试和本计划列出的文档；不清理、不还原其他用户改动。

## 步骤

### 1. 建立基线

- 核对 `git status --short --branch`，记录既有工作树边界。
- 运行 `pwsh -File .\Miscellaneous\init.ps1`。
- 构建 `src/NLCPG/NLCPG.csproj` 和 `src/NLISSN.Core/NLISSN.Core.csproj`。
- 运行 CPG 领域、持久化和 streaming 相关的 Contract/Unit 测试，保存实际结果。
- 记录基线类型反射结果、图快照和可用性能计数；不把未执行的 tier 写成通过。

### 2. 转换内部 carrier

按设计清单逐个改为 `readonly record struct`，优先使用小批次补丁：

- `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs`
- `src/NLCPG/Builder/Streaming/CpgEdgeCandidate.cs`
- `src/NLCPG/Builder/Streaming/CrossShardSummary.cs`
- `src/NLCPG/Builder/Streaming/FragmentOwnershipIndex.cs`
- `src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs`
- `src/NLCPG/Model/NLCPGGraph.cs`
- `src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs`
- `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs`
- `src/NLCPG/Builder/NLCPGBuilder.cs` (`DefinitionFact`)
- `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` (`CpgRestoreMetrics`,
  `CpgBaseRestoreResult`, `CpgShardExportRequest`)
- `src/NLCPG/Analysis/CpgRelationQueryService.cs` (`QueryKey`)
- `src/NLCPG/Analysis/NLCPGSliceQuery.cs` (`QueryKey`)
- `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs` (`RoutingIndexCacheKey`)
- `src/NLISSN.Core/Propagation/PropagationFactKey.cs`
- `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs`
- `src/NLISSN.Core/Decision/AnalysisEvidence.cs`

编译错误按语义修复，而不是用 `as`、隐式装箱或复制集合绕过值类型；重点检查
nullable owner、结构体默认值、字典 key、`PriorityQueue` 优先级、`foreach` 复制、
反射构造和 `IReadOnlyList<T>` 泛型边界。结构体查询失败优先使用 `bool + out`，不
使用 null 哨兵或装箱。

### 3. 增加结构契约测试

在 `tests/NLISSN.ContractTests` 增加针对性断言：

- 纳入类型通过反射是 `IsValueType == true`，且不是公开类型；
- descriptor/candidate 属性仍只包含稳定 CPG 数据；
- propagation key/source/priority 和 evidence pending node/edge 通过反射是非公开值类型；
- 跨过程 plan、data-flow definition fact、查询缓存键、routing index key、restore result
  和 shard export request 通过反射是非公开值类型；
- 泛型 array/list/dictionary/hashset 路径不改变结果；
- default/nullable owner、传播来源解析、evidence 相等性和重复候选行为保持不变。

复用已有 `CpgShardContractTests`、`NLCPGNodeIdContractTests` 的 export/commit 测试，
避免为了反射而复制生产构造路径。

### 4. 运行语义回归

至少执行：

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
```

然后运行适用的 `Run-TestTiers.ps1 -Fast`、`-Host` 和 `-Performance`，只记录实际
完成的命令和结果。重点核对 graph/rule/decision/rewrite snapshot、shard persistence
和 DOP 1/2/16 等价边界，并单独核对 propagation/evidence 结果。

### 5. 运行性能对照

- 使用研究报告和仓库现有 performance fixture，固定输入、SDK、配置和 DOP。
- 基线组来自转换前提交点或等价工作树快照；实验组来自本分支转换后代码。
- 每组至少预热并重复运行，记录分配、GC、working set/retained heap、阶段耗时和
  泛型集合访问吞吐；若没有转换前同输入基线，只报告实验组健康检查，不报告收益。
- 将原始输出保存到 `docs/benchmarks/` 或现有 benchmark 约定的位置；报告不把
  JIT 首次运行或单次偶然样本当作结论。

### 6. 文档和验收收口

- 回读设计、执行计划及所有 Markdown 链接。
- 运行 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1`。
- 运行 `git diff --check`，检查只包含本任务的新增/修改内容和用户既有工作树。
- 更新 `Context/progress.md` 只记录当前事实、已验证边界和未验证边界；不把实验
  假设写成 feature 完成证据。
- 若性能没有改善，保留基线和失败边界，并在计划中记录回退决定。

## 当前验证证据（2026-09-19）

以下结果来自当前分支、SDK `10.0.400`，按串行顺序执行；运行输出保存在
`Build/TestResults/` 对应 run 目录：

- `dotnet build src/NLCPG/NLCPG.csproj --no-restore -p:UseSharedCompilation=false`：通过，0 warning / 0 error。
- `dotnet build src/NLISSN.Core/NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false`：通过，0 warning / 0 error。
- `dotnet test tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false`：`338/338` 通过。
- `dotnet test tests/NLISSN.UnitTests/RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false`：`117/117` 通过。
- `Run-TestTiers.ps1 -Fast`：Unit `117/117`、Contract `338/338` 通过。
- `Run-TestTiers.ps1 -Performance`：工具目录临时加入 `PATH` 后 `67/67` 通过，结果目录为
  `Build/TestResults/20260919-022435-ac6133d6`。第一次未加入该目录的运行因
  `dotnet-trace`、`dotnet-gcdump` 和 `dotnet-counters` 不在 `PATH` 而失败，不作为性能结果。
- `Run-TestTiers.ps1 -Host`：`647/655` 通过，8 项失败。失败集中在既有 rule structure 白名单、workspace repository-root fixture、PlayerInput/delegate rule fixture 和三个 test-code-set coverage fixture；没有失败堆栈落在本实验 carrier 转换或新契约测试中，因此记录为当前工作树的未通过边界，而不是宣称 Host tier 通过。

Performance tier 只证明当前实现可以运行现有性能/诊断 harness；本分支没有同一输入、同一
SDK/runtime 和同一配置下的 class 基线，因此没有报告内存下降比例或访问速度提升。

### Host 失败的隔离基线证据

为避免把 Host 失败未经验证地归因于 struct 实验，在 `D:\Temp\NL-domain-struct-baseline`
建立了不包含当前工作树未提交 carrier 改动的 detached HEAD `b20388a` 工作树，并串行重跑
完整 Host 项目。当前工作树和基线均为 `647/655`，且解析两份 `.trx` 后的 8 个失败测试
名称集合完全相同：

- `RuleStructureContractTests.StructuralKind_ContainsOnlyApprovedStructureConclusions`
- `WorkspaceConfigurationTests.Load_LegacySourceInputDoesNotCreateWorkspaceOptions`
- `PropagationRuleExpansionTests.Analyze_LogicalAndChainRightTarget_PropagatesTargetFactToLeftLogicalSubtree`
- `PipelineComponentTests.AnalyzeFromArgs_ForDirectoryDeclaration_RewritesLargeAssetProjectAndKeepsCompilationValid`
- `PipelineComponentTests.AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksDelegateMethodGroupBindingsAndInvocations`
- `TestCodeSetCoverageTests.Analyze_AllTestCodeSetSources_BuildsGraphAndRunsApplicationPipeline` × 3

基线和实验还产生了相同的关键断言结果（例如 `MethodDeletion` 白名单差异、
`PlayerInput`/delegate fixture 断言和三个 coverage fixture 的零 decision/seed mark）。因此
这 8 项记录为当前 HEAD/fixture 的 pre-existing Host 边界，而不是本次 carrier 转换回归；
它们没有被修改或假定为已解决。隔离工作树只用于诊断，验证完成后移除，不属于本分支交付物。

### 最新验证目录与版本指纹边界

- `Run-TestTiers.ps1 -Fast` 的最新结果目录为
  `Build/TestResults/20260919-042652-5ef2f4bf`；其中 Unit `117/117`、Contract
  `338/338` 通过。
- `Run-TestTiers.ps1 -Host` 的最新结果目录为
  `Build/TestResults/20260919-042955-931b3089`；其中 Host `647/655` 通过，8 项失败。
- `pwsh -File .\Miscellaneous\init.ps1` 在 SDK `10.0.400` 下通过；
  `check-harness-consistency.ps1 -RepoRoot (Get-Location).Path` 通过。
- 独立反射核验编译后的 `NLCPG.dll` 与 `NLISSN.Core.dll`：22 个清单类型全部找到、
  `IsValueType=true` 且非公开；scoped production diff 的 externally visible type
  declaration 检查通过。
- 上述 harness `run.json` 的 `gitSha` 是当前 `HEAD` `b20388a92f0954ad202476f415d09a2bbf2cde8c`。
  本实验改动仍是未提交工作树，因此该 SHA 只是仓库基线锚点，不是包含 22 个
  struct 改动的完整内容指纹；源码、测试、设计文档和实际命令输出共同构成当前
  实验的验证证据。

## 完成条件

- 设计中的 22 个纳入类型全部转换，或每个未转换项有源码证据和明确回退理由；
  当前排除的递归查询状态、Roslyn 工作态、发布 Channel 消息、诊断 checkpoint、低基数
  capability/CLI 计划和 routing iterator candidate 已在设计文档的“剩余内部 record/class
  审计”中绑定源码证据和职责理由。
- 公开图模型、持久化 schema 和现有对外 API 未改变。
- owning project build、定向 Contract/Unit 回归和适用 harness 检查通过。
- 图快照、持久化恢复、边界边、DOP 等价均有实际证据。
- 性能结果包含绝对值、相对变化、重复运行说明和未验证边界。
- 设计文档、执行计划、测试和实现保持一致。

## 提交拆分建议

若需要提交，按可审查的小提交拆分：

1. 设计文档和执行计划。
2. 内部 carrier 表示转换。
3. 结构和值语义 Contract 测试。
4. 性能测量和验证证据。

提交前不提交 `.agent-workplace/`，也不夹带当前工作树中与本实验无关的改动。
