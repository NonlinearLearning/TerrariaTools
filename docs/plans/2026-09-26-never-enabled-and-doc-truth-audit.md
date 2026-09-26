# 未启用代码 与 文档真实性 审计

> 状态：审计报告（只读产出，未修改任何产品或文档文件）。
>
> 审计对象：`HEAD = 1e9486f`（分支 `codex/dataflow-tail-measurement-20260925`），工作树有 327 个未提交改动。
>
> 快照时间：2026-09-26 19:05（工作树在审计期间仍在被其他会话修改，见 §6）。
>
> 判定口径：**只有**从生产入口 `src/NLISSN/Program.cs` → `ConfigurationRunHost` → `CommandHost` → `ApplicationService` / `NLCPGBuilder` 可达才算"已启用"。仅测试、探针或已在死管线中的可达一律记为**未启用**。
>
> 与既有审计的关系：`docs/plans/2026-09-26-unwired-design-audit-report.md`（24 项，4 类）与 `docs/plans/2026-09-26-rejection-audit-report.md` 已存在。本报告**不重复**其条目，只记录经本次独立复核后仍成立、或为其新增/更正的部分。

---

## 0. 结论摘要

两类问题各有一条主线：

**A 类（存在但从未启用）**——最严重的一条是**整个持久化 / 流式 / frozen-shard 子系统在生产上不可达**：`src/NLCPG/Persistence/` 与 `src/NLCPG/Builder/Streaming/` 合计 **21 个文件、5011 行**，全部挂在 `NLCPGBuilderOptions.Persistence` 之下，而该字段在 `src/` 中的唯一赋值是 `= null`（`NLCPGBuilder.cs:1513`）。其次是**两个已完整实现的 pass 永不执行**（`Dominance` / `ControlDependence`），因为它们依赖的能力位不在生产规则请求的集合里。

**B 类（文档不真实）**——最严重的一条是 `设计docs/目前设计/规则目录-编译期生成.md`：它自称"已收口"，却在**同一页内**同时错报了 feature 维度、四个规则族的排除状态、生成目录基线数量，而其中两条已被 Host 测试**反向断言**。这不是"陈旧"，是与代码相反的陈述。

第三类结构性问题是**文档未被版本控制覆盖**：183 个 `.md` 中 **134 个未纳入 git**，其中 `设计docs/目前设计/` 27 页只有 1 页被跟踪——"当前设计"这一权威层在全新 clone 中基本不存在。

---

## 1. A 类：存在但从未启用的代码

### A1. 持久化 / 流式 / frozen-shard 子系统整体不可达 — HIGH

| 项 | 证据 |
| --- | --- |
| 总开关 | `src/NLCPG/Builder/NLCPGBuilderOptions.cs:32` `CpgPersistenceOptions? Persistence = null` |
| `src/` 内唯一赋值 | `src/NLCPG/Builder/NLCPGBuilder.cs:1513` `Persistence = null,`（在 `CreateAnchorDiscoveryOptions` 中显式置空） |
| 生产 builder options | `src/NLISSN.Application/Analysis/ApplicationService.cs:491-501` 只设置 `MaxDegreeOfParallelism` / `RequestedCapabilities` / `CallFlowResolver` / 性能诊断，**不含 `Persistence`** |
| 门控 | `src/NLCPG/Builder/NLCPGBuilder.cs:1161` `if (_options.Persistence is not null)` ⇒ 恒不进入 |
| `new CpgPersistenceOptions` | 全 `src/` 命中 **0**（仅 tests，如 `tests/NLISSN.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs:25`） |
| 被封闭的代码量 | `Persistence/` + `Builder/Streaming/` = **21 文件 / 5011 行**；含 `CpgShardStore.cs`(996)、`CpgShardBuildSession.cs`(644)、`CpgShardBuildCoordinator.cs`(392)、`CpgFrozenShardExporter.cs`(218)、`FragmentOwnershipIndex.cs`(169)、`CpgBuildRoutingIndexWriter.cs`(153)、`CpgBuildRoutingIndexReader.cs`(99) |
| 二级开关 | `NLCPGBuilderOptions.cs:110` `CpgPersistenceOptions.StreamingMode = false`；读取点 `CpgShardBuildCoordinator.cs:71,113,190`、`NLCPGBuilder.cs:1262,1505`，全部在 `Persistence != null` 之内 |
| 配置面 | NLISSN 的 YAML 加载器（`src/NLISSN.Infrastructure/Configuration/`）**没有任何** persistence / store / shard 相关键 ⇒ 非源码调用者也无法开启 |

**为何是"从未启用"而非"暂时关闭"**：开启它需要同时提供 `StoreRoot` 与 `ProfileHash`，而这两个值的唯一来源是配置或源码调用点，两者都不存在。该子系统有充分测试覆盖（`CpgShardBuildCoordinatorTests`、`CpgBuildRoutingIndexTests` 等），因此它是一条**维护成本真实、运行时收益为零**的路径。

### A2. `Dominance` / `ControlDependence` 两个 pass 永不执行 — HIGH

- 门控：`src/NLCPG/Builder/NLCPGBuilder.cs:1417-1418` 由 `buildPlan.RequiresDominance` / `RequiresControlDependence` 决定；二者派生自 capability 位（`NLCPGBuilder.cs:1609-1610`）。
- `src/NLCPG/Contracts/NLCPGCapability.cs`：`Default` **不含** `Dominance` / `ControlDependence`，只有 `All` 含（`All = Default | Dominance | ControlDependence | InterproceduralDataFlow`）。
- 生产请求集来源：`ICpgAnalysisContext.AvailableCapabilities` 默认值为 `NLCPGCapability.All`，但规则声明的是自己的 `RequiredCapabilities`：
  - `RuleDefinitionMark.cs:11` / `RuleDefinitionLift.cs:13` / `RuleDefinitionPropagate.cs:12` / `RuleDefinitionPropose.cs:15` ⇒ `{ NLCPGCapability.Default }`
  - 唯一例外：`src/NLISSN.Rules/Propagate/ExternalSummaryFlowPropagationRule.cs:43` ⇒ `{ NLCPGCapability.InterproceduralDataFlow }`
- 汇总点：`src/NLISSN.Application/Analysis/RulePipeline.cs:58-68` `GetRequiredCapabilities()` 取所有规则的并集 ⇒ `Default | InterproceduralDataFlow`，**不含** Dominance / ControlDependence。
- 生产采用：`ApplicationService.cs:494` `builderOptions.RequestedCapabilities`；另有 `:502` 的回退 `?? new[] { NLCPGCapability.Default }`，同样不含。
- 因此 `DominancePass` / `ControlDependencePass` 的实现、telemetry 阶段 id（`src/NLCPG/Builder/PartitionPerformanceEvent.cs:26-30`）与依赖表条目全部是真实代码，但**在真实运行中恒被跳过**。
- 对照：`InterproceduralDataFlow` **是**可达的（由 `ExternalSummaryFlowPropagationRule` 点亮），说明这不是"能力位机制整体失效"，而是这两个位确实无人请求。

### A3. `UseSynchronousLocalWorkBatchExecution` 恒为 false — HIGH

- `src/NLCPG/Builder/NLCPGBuilderOptions.cs:47` `public bool UseSynchronousLocalWorkBatchExecution { get; init; }`，默认 `false`。
- 读取点仅 `src/NLCPG/Builder/NLCPGBuilder.cs:755`（转发给执行器）。
- **全仓赋值点为 0**（`src/`、`tests/`、`tools/` 均无 `UseSynchronousLocalWorkBatchExecution = true`）。
- 配套常量 `NLCPGBuilderOptions.cs:45` `public const int ProjectWorkerLocalDegreeOfParallelism = 1;` —— 全仓引用数 **0**。
- 后果：`src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs:529` 的同步执行分支永不可达；
  而 `设计docs/目前设计/cpg-workbatch-concurrency.md:3` 明确写"project worker 的 local DOP1 synchronous adapter **已接入**"。

### A4. 四个调度阈值声明后无人读取 — MEDIUM

`src/NLCPG/Builder/NLCPGBuilderOptions.cs` 中的这四项，在 `src/` 内的全部出现就是自身的声明与默认值赋值：

| 字段 | 声明 | 默认值 | `src/` 读取点 |
| --- | --- | --- | --- |
| `LargeFileLineThreshold` | `:20` | `:94` = 800 | 0 |
| `LargeFileMethodThreshold` | `:21` | `:95` = 8 | 0 |
| `LargeMethodLineSpanThreshold` | `:22` | `:96` = 80 | 0 |
| `SyntaxLargeFileLineThreshold` | `:26` | `:100` = 800 | 0 |

测试中有 26–32 处引用，但全部是**位置参数填充**（构造函数实参），没有一处断言读取效果。真正的分类阈值住在另一个类型里：`src/NLCPG/Builder/Concurrency/CpgWorkBatchCostModel.cs:63-66` 的 `CpgWorkBatchCostOptions.Default/Packing`（`smallMaxCost: 40, mediumMaxCost: 200, largeMaxCost: 800`），经 `NLCPGBuilderOptions.cs:71-72` 的 `EffectiveWorkBatchCostOptions` 进入。

因此 `设计docs/目前设计/cpg-workbatch-concurrency.md:85` 的"分类阈值必须是 `NLCPGBuilderOptions` 的调度配置"既不符合现状，而这批字段本身也构成一个**改值不改变行为**的假配置面。

### A5. `OrderedResultReorderAllowance` / `MaxOrderedResultRecordCount` 及派生属性全无消费者 — MEDIUM

- `NLCPGBuilderOptions.cs:34-35` 两个 init 字段；`:51-54` 两个 `Effective*` 只读属性。
- 在 `src/` + `tests/` + `tools/` 中**引用数均为 0**（穷举确认：仅声明与赋值本身）。
- 历史消费者曾存在：`Build/MemoryOptimization/M5/ab/{pre,diff,post}/src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:70-71`。
- 当前 `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs` 中已**完全没有** `Reorder` / `OrderedResult` / `ConcurrencyWindowOptions` 的任何引用。
- ⇒ 这是一批在重构中被摘掉消费者的**孤儿配置**，而非"预留"（无 TODO、无注释说明）。

### A6. `NLCPGBuilderMode` / `NLCPGSyntaxPassMode` 是无法选择的单成员枚举 — MEDIUM

- `NLCPGBuilderOptions.cs:7-10` `NLCPGBuilderMode` 只有 `Partitioned`；`:12-15` `NLCPGSyntaxPassMode` 同样只有一个成员。
- `BuildMode` 从未被**读取**：赋值点 `NLCPGBuilderOptions.cs:92`、`PartitionedOperationPass.cs:316,46`、`NLCPGBuilder.cs:1179`。
- `SyntaxPassMode` 唯一读取点是拼日志字符串（`NLCPGBuilder.cs:2130`）。
- ⇒ 文档与注释中"legacy / partitioned 两条管线可通过 mode 选择"的语义**在枚举层已不可表达**；两者已固化为单一路径。
- 注意与 A8 区分：`LegacyPipeline` 字段仍然存在，但它是 `NLCPGBuilder.cs:24` 的一个**静态字段**，与 `BuildMode` 无任何连接。

### A7. 分片查询组件在生产中从不构造 — MEDIUM

- `new CpgShardQueryResolver`：`src/` 命中 **0**；仅 tests（`CpgShardQueryResolverTests.cs:16,38,62`，`NLCPGSliceQueryTests.cs` 多处）。
- `new CpgShardRelationQueryService`：`src/` 命中 **0**；仅 `tests/NLISSN.ContractTests/Cpg/CpgRelationQueryTests.cs:147,192,216,238`。
- `NLCPGSliceQuery` 的 `CpgShardQueryResolver` 重载（`NLCPGSliceQuery.cs:21`）同样只被测试使用；生产走 `new NLCPGSliceQuery(graph)`（`NLCPGSliceQuery.cs:15` 路径）。
- 生产的关系服务是 `CpgRelationQueryService`（`src/NLISSN.Core/Analysis/MarkAnalysisSnapshot.cs:37`、`View/NLCPGStructureViewBuilder.cs:114`），工作在**内存图**上。
- 叠加 A1（持久化从不开启）⇒ 分片查询不仅无调用者，连被查询的 store 都不存在。

### A8. `LegacyPipeline` / `OperationPass` 属死管线 — MEDIUM

- `NLCPGBuilder.cs:24` `LegacyPipeline`、`:34` `PartitionedPreOperationPipeline`、`:39` `PartitionedPostOperationPipeline` 三个静态列表；执行器 `:3523` `RunPipeline(...)`。
- **全仓对 `RunPipeline` 的调用点为 0**。
- `src/NLCPG/Builder/Passes/OperationPass.cs:23,32` 是转发壳，其唯一引用在 `LegacyPipeline` 内。
- 真实执行路径是 `NLCPGBuilder.cs:1279` `RunPartitionedOperationPass(...)`。
- ⇒ 文档把 `OperationPass.cs` 列为当前 operation 能力锚点是误导性的（见 B6）。

### A9. WorkBatch 遥测：出口在生产中无人实现 — MEDIUM

- `NLCPGBuilderOptions.cs:42` `ICpgWorkBatchPerformanceEventSink? WorkBatchPerformanceEventSink = null`。
- `src/` 内**没有任何实现类**，也没有任何赋值点（仅 `NLCPGBuilderOptions.cs:42` 声明、`PartitionPerformanceEvent.cs:53/58/61` 接口与扩展方法）。
- 唯一赋值在测试：`tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLocalPassTests.cs:113`、`DocumentShardingEquivalenceTests.cs:78`、`DominanceRaceReachabilityProbe.cs:100`、`tests/NLISSN.PerformanceTests/Cpg/CpgWorkBatchPerformanceTests.cs:50`。
- 结果：`NLCPGBuilder.cs:3249` 的 `TryRecord` 在生产恒为 no-op（fail-open 扩展，`PartitionPerformanceEvent.cs:58-63`）。
- 内存列表（`NLCPGBuilder.cs:98,1143,3248,1491`）仍被填充并随 `NLCPGBuildMetrics.WorkBatchPerformanceEvents` 返回，但 `src/` 内**无人读取**该列表（只有测试读）。
- 对照：`IPartitionPerformanceEventSink` **是**可达的（`CommandHost.cs:70` 接 `diagnosticsCollector`），说明这是 work-batch 一条支路特有的接线缺口。

### A10. 枚举成员无生产者 — LOW

| 类型 | 成员 | 实际生产者 |
| --- | --- | --- |
| `CpgWorkBatchKind`（`Concurrency/CpgWorkBatch.cs:3-8`） | `Mixed` | 无。实际只产出 `Prelude`（`CpgWorkBatchBuilder.cs:119`）与 `Methods`（`:140,143,153,165,171`，`ControlDependencePass.cs:105`） |
| `CpgWorkItemKind`（`Concurrency/CpgWorkItem.cs:3-8`） | `FilePrelude` | 无。实际只产出 `Method`（`DataFlowPass.cs:748` 等）与 `Declaration`（`CallGraphPass.cs:180`）——`FilePrelude` 全仓只在枚举定义处出现 |

### A11. `StageQuotaScope.Dedicated` 不可达 — LOW（设计上如此，但文档措辞需注意）

- `src/NLCPG/Builder/StageQuotaAllocation.cs:19-20` 注释明确：独占额度"**启用前必须先有实测标定**（见 `StageQuotaCalibration`），否则 `StageQuotaPolicy.Resolve` 会抛出"。
- `StageQuotaPolicy.Resolve` **已接线**（`NLCPGBuilder.cs:1400-1405`），走的是 `Shared` 路径。
- 全仓无 `StageQuotaCalibration` 的构造点 ⇒ `Dedicated` 分支不可达，且这是**有意的失败关闭设计**，不是缺陷。
- 但 `StageQuotaAllocation.cs:15-16,22` 的注释与相关设计页把它描述为"这是默认值，也是当前生产的真实行为"，措辞容易被读成"两条路径都活跃"。

### A12. 仅测试可达的 telemetry / 内部属性 — LOW

| 成员 | 声明 | 生产读取者 |
| --- | --- | --- |
| `LastMethodDecorationTelemetry` | `Passes/MethodDecorationPass.cs:74` (internal) | **0**（仅测试） |
| `LastFlowSummaryMetrics` | `Builder/NLCPGBuilder.cs:705` (public) | **0**（仅测试） |
| `LastSyntaxPassTelemetry` | `Passes/SyntaxPass.cs` (internal) | 0（仅测试/工具） |
| `LastInterproceduralPlanCapacity` | `Builder/NLCPGBuilder.cs:709` (internal) | 0 |
| `LastRestoreMetrics` | `Builder/CpgShardBuildCoordinator.cs:33` (internal) | 0（且在 A1 死路径内） |

### A13. `MinimalRoslynCpg` 已成孤儿 — LOW

- 磁盘上 `src/MinimalRoslynCpg/` 只剩 **1 个文件**：`src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`(30469 B)。
- 四个 `.csproj` 均已在工作树中删除（`git status --porcelain` 显示 ` D src/MinimalRoslynCpg.Core/...`、` D src/MinimalRoslynCpg.Persistence/...`、` D src/MinimalRoslynCpg.Query/...`、` D src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`）。
- **没有任何 `.csproj` 引用它**（`get-childitem src,tests -Filter *.csproj | Select-String MinimalRoslynCpg` 为空）⇒ `RoslynCpgGraph.cs` **不参与任何编译**。
- 契约测试**主动要求**该 csproj 不存在：`tests/NLISSN.ContractTests/TestProjectBoundaryTests.cs:93-103` `HistoricalMinimalRoslynCpgProject_IsNotPresent()`。
- 遗留文件本身却仍 `using MinimalRoslynCpg.Contracts;`（`RoslynCpgGraph.cs:1`），而 `MinimalRoslynCpg.Contracts` 项目已删除 ⇒ 即使重新纳入编译也会失败。
- 大量**历史**计划文档仍按现役项目描述它（`docs/plans/2026-07-25-cpg-persisted-base-runtime-analysis-execution-proposal.md` 等），属历史层，严重度低。

### A14. `CpgQueryPurpose.Evidence` / 分类结果无消费者 — LOW

- `src/NLCPG/Analysis/CpgRelationQuery.cs:24-29` 定义 `RuleAnalysis` / `StructureView` / `Evidence`；实际只传 `RuleAnalysis`（`:105` 默认）与 `StructureView`（`NLCPGStructureViewBuilder.cs:126`）。
- `CpgWorkBatchCostModel.Classify`（`:148-171`）的唯一生产调用来自 `Estimate` 内部（`:144`）；`CpgWorkBatchSizeClass` 与 `CpgWorkBatchCostFallbackReason.UnknownSpanCost` 在 `src/` 内无读取者 ⇒ 文档要求的"并在诊断中标记 `UnknownSpanCost`"没有下游。
- `CpgRelationProfile` 5 个成员中生产只产生 `StructuralContainment`（`StageRuleContexts.cs:92,119`、`NLCPGStructureViewBuilder.cs:27,54`）；`SemanticBinding` / `LocalDataFlow` / `ControlDependence` / `BackwardSlice` 仅出现在自身 switch（`CpgRelationQuery.cs:152-155,166-171`）与 `:178` 的能力判定中。

---

## 2. B 类：文档不真实

### B1. `规则目录-编译期生成.md` 与代码相反（含被测试反向断言的两条） — HIGH

`设计docs/目前设计/规则目录-编译期生成.md` 头部自称"状态：**已收口**（2026-09-09）"。逐条比对：

| 文档声明 | 代码事实 | 证据 |
| --- | --- | --- |
| `:3` "当前生成目录**无 feature 维度**" | 生成目录**携带** feature 维度 | `RuleCatalogGenerator.cs:451,474,493-495` `includeFeatures` 参数与 `global::NLISSN.Core.Pipeline.RuleFeature.{...}` 输出；`:188-210` 按 feature 分组并做 `FeatureMissingStage` 校验 |
| `:83` "`RuleFeature` **不存在**，`DefaultEnabled` 也不存在" | `RuleFeature` **存在**且为注册属性构造参数 | `src/NLISSN.Rule/RuleRegistration.cs:6` `public enum RuleFeature`、`:26` `RuleRegistrationAttribute(RuleFeature feature)`、`:31` `public RuleFeature Feature { get; }` |
| `:63,83,247,260` "四个延期规则族仍由 `RuleCatalogIgnore` **排除**，不生成 descriptor、factory 或规则图节点" | 四族**全部注册**，且 `RuleCatalogIgnore` **从未被应用** | 四族 `RuleRegistration` 计数各为 **4**；`[RuleCatalogIgnore` 在 `src/` 全仓命中 **0** |
| `:259` "生成目录基线固定为 `19/12/5/32`，共 `68` 个 factory" | 实际 `RuleRegistration` 应用数为 **23 / 17 / 9 / 36 = 85** | `src/NLISSN.Rules/{Mark,Propagate,Lift,Propose}` 逐目录统计 |
| `:177` "不保留 `CreateDefaultRules`，也不保留四个延期规则 `bool` 重载" | 四个 `bool` 重载**存在并生效** | `src/NLISSN/Composition/RuleSelectionAdapter.cs:19-24` 四个 `bool` 形参；`:27-45` 映射为四个 `RuleFeature` |

**最严重的一条**：Host 测试的**断言方向与文档相反**——

`tests/NLISSN.HostTests/Application/MethodDeletionFeatureScopeTests.cs:11` `GeneratedCatalogContainsFeatureRulesButDefaultPipelineExcludesThem()`：
- `:21-26` 断言目录**包含** `mark.unreachable-method` 与 `propose.unreferenced-method`（即"延期族"确实在目录里）；
- `:27-30` 断言它们的 `Feature != RuleFeature.Core`（即 feature 维度确实存在）；
- `:32-41` 断言**默认** `new RuleSelection()` 组合后的 pipeline **不含**它们。

也就是说，真实语义是"**四族已注册、由 `RuleFeature` 在组合期按选择过滤**"，而文档写的是"**在生成期由 `RuleCatalogIgnore` 排除**"。这不仅是陈旧：它把过滤发生的**阶段**和**机制**都说反了。`:44` 另有 `ExplicitMethodDeletionFeaturesAddAllFourStages()` 断言显式开启时四阶段齐备。

叠加事实：四个开关**在生产上真实可用**——`Miscellaneous/nlissn.yml:11-14` 有 `deleteUnreachableMethods` / `deleteUnreferencedMethods` / `clearUnusedInterfaceImplementations` / `privatizeInternalOnlyPublicMethods`，经 `YamlConfigurationLoader.cs:207,220,909` → `RulePolicySettings.cs:8` → `CommandHost.cs:277-295` → `RuleSelectionAdapter.cs` 完整生效。

> 该页 `:255` 标注"验收记录（2026-09-09）"，`:257-259` 记录当时的测试与数量。数字与机制都随代码演进而未回填，但页头仍写"已收口"、`更新时间：2026-09-09`——**在 `设计docs/目前设计/` 这一"当前设计"层中，这是一页反向陈述**。

### B2. 两页"当前设计"指向不存在的 `RuleRegistry.cs` — MEDIUM

- `设计docs/目前设计/项目概览.md:65`："默认规则由 `src/NLISSN/Composition/RuleRegistry.cs` 创建后注入。"
- `设计docs/目前设计/delete-class-components.md:13`："默认规则集由 `src/NLISSN/Composition/RuleRegistry.cs` 组装。"
- **该文件不存在**；`src/NLISSN/Composition/` 只有 `RulePipelineComposer.cs`、`RuleSelection.cs`、`RuleSelectionAdapter.cs`。
- 真实组合点：`RulePipelineComposer.cs:38-41`（消费 `GeneratedRuleCatalog.Mark`…），由 `CommandHost.cs:82-84` 调用。
- **契约测试反向断言其不存在**：`tests/NLISSN.ContractTests/RuleCatalog/RuleCatalogProductionBoundaryTests.cs:8-29` `ProductionCompositionHasNoManualRuleRegistry()` 断言 `RuleRegistry.cs` 不存在，且 `src/` 内无 `"RuleRegistry.CreateDefaultRules"`。
- ⇒ 两页"当前设计"描述了一个被契约测试**明令禁止**的设计。

### B3. `cpg-architecture.md` 的"冻结后仍保留 `HashSet<NLCPGEdge>`"已被移除 — HIGH

- 文档 `设计docs/目前设计/cpg-architecture.md:36-38`：冻结后 `NLCPGGraph` 仍保留一份构图期填入的 `HashSet<NLCPGEdge>` 作为边集合来源……因此当前常驻边占用尚未降到 9 B/边。
- 代码事实：`src/NLCPG/Model/NLCPGGraph.cs:170-171`
  `public IReadOnlyCollection<NLCPGEdge> Edges => _queryIndex is null ? Array.Empty<NLCPGEdge>() : _queryIndex.InsertionOrderedEdges;`
- `_edges` 在该文件中**只剩历史注释**（`:169,240,1027`）；`:1027` 明写"`_edges` 已在冻结时不再常驻（边改由 `NLCPGGraphIndex` 的投影视图提供）"；`CurrentEdgeCount`（`:173-174`）走 `_pendingEdges` / `_queryIndex.EdgeStore.Count`。
- 边的真实承载是结构化数组：`src/NLCPG/Model/CanonicalEdgeStore.cs:5-6,38-45`（`int[]` 序数 + `byte[]` kind + 稀疏元数据池）。
- ⇒ 文档描述的字段已不存在，且**据此推出的"9 B/边尚未达成"结论失去前提**。
- 说明：`cpg-architecture.md:27-34` 对 `CanonicalEdgeStore` 的描述与代码**一致**，错的只有 `:36-38` 这一句。

### B4. `cpg-capabilities.md` 的"正向/反向遍历"只有反向 — HIGH

- 文档 `:28`："有界切片 | 稳定索引上的**正向/反向**遍历，显式报告截断 | `src/NLCPG/Analysis/NLCPGSliceQuery.cs`"
- 代码：`NLCPGSliceQuery.cs` 的公开入口只有 `QueryBackward`（`:27`）与 `QueryBackwardAsync`（`:198`）；无任何 `QueryForward` / `Forward` 成员。
- `NLCPGSliceQueryOptions.cs:8` 自述"定义**反向** CPG 切片使用的有界边集合和遍历限制"。
- ⇒ 承诺的正向遍历不存在。

### B5. `docs/quick-start.md` 把配置文件名拼错 — MEDIUM

- `docs/quick-start.md:33`："将该内容保存为 `Build\nlcpg-smoke\lissn.yml`"。
- 加载器要求 `nlissn.yml`：`src/NLCPG.Configuration/UnifiedYamlDocument.cs:32`、`YamlConfigurationLoader.cs:12`。
- ⇒ 按文档操作会直接失败。同文件 `:136` 与 `Miscellaneous/schemas/README.md:4` 还引用了 `nlissn.schema.3.json`——该文件在磁盘上存在但**未被 git 跟踪**（仅 `nlissn.schema.2.json` 被跟踪）。

### B6. 文档把死管线的 pass 列为当前锚点 — MEDIUM

- `设计docs/目前设计/cpg-architecture.md:64` 与 `cpg-capabilities.md:22`：`| Operation | OperationPass.cs、PartitionedOperationPass.cs | …`。
- `OperationPass.cs` 属 A8 的死 `LegacyPipeline`。维护者按此表去改一个不会执行的 pass。

### B7. 文档承诺的标识符在代码中不存在 — MEDIUM

| 文档声明 | 全仓（`*.cs` / `*.md`）命中 |
| --- | --- |
| `cpg-workbatch-concurrency.md:271` "记录 `UnsplittableLargeMethod`" | **仅该文档这一行**；源码与测试为 0 |
| `cpg-workbatch-concurrency.md:60,284-296` 的 `EvidenceCatalog` 层 | `src/` 与 `tests/` 命中 **0** |
| `cpg-capabilities.md:29-30` 的 routing sidecar 作为"当前可依赖能力" | 类型存在（`CpgBuildRoutingIndexWriter/Reader`），但整体在 A1 死路径内 |

### B8. WorkBatch §14 遥测清单 9 项字段未实现 — MEDIUM

文档 `cpg-workbatch-concurrency.md:302-306` 要求"每个 batch **至少**记录"5 类；实际 `CpgWorkBatchPerformanceEvent`（`src/NLCPG/Builder/PartitionPerformanceEvent.cs:33-50`）字段为：

`StageId, BatchId, StableOrder, InputCount, EstimatedCost, EstimatedBytes, OutputNodeCount, OutputEdgeCount, FragmentBytes, QueueWaitMilliseconds, ProcessingElapsedMilliseconds, ReducerWaitElapsedMilliseconds, WorkerIndex, PeakActiveWorkerCount, QueueHighWaterMark, CompletedNotReducedHighWaterMark, RunId`

**未实现**：`collection` / `local solve` / `commit` / `total elapsed` 的四段切分；`method count`；`largest item cost`；`backpressure count`；`cancelled count`；`failure kind`；`data-flow truncation`。
⇒ 与文档自身 `:322-324` 的"实际字段覆盖"段落**互相矛盾**：后者只列已实现字段，前者把未实现项写成验收要求。

### B9. 缓存 / 基础设施页的局部陈旧 — LOW

- `设计docs/目前设计/运行时基础设施.md:31`：`src/NLISSN.Infrastructure/Configuration` 职责写作 "**Schema 2 YAML**"。
- 代码：`YamlConfigurationLoader.cs:13-14` `LegacySchemaVersion = 2` 与 `UnifiedSchemaVersion = 3`；`:127` 明确接受二者。⇒ 漏报 Schema 3。
- 正向核验：同页 `:32-35` 的五个模块目录全部存在；`缓存边界.md` **完全准确**——`WeakTypedCacheRegistry.cs:26` 的 `GetOrAdd` 单飞、`ByteBudgetLruCache<TKey,TValue>` 的 `byteWeight` 契约、以及 `:41` 声称 `CpgShardQueryResolver` 用通用 LRU 做 retention，均在 `CpgShardQueryResolver.cs:15,23` 得到证实（`private readonly ByteBudgetLruCache<string, CpgFrozenShard> _cache`）。

### B10. `docs/developer-guide.md` 把不可达能力写成可用 — MEDIUM

- `:28-34` 的 "Streaming CPG shard store" 小节把 `NLCPGBuilderOptions.Persistence` / `StreamingMode` 与 `CpgShardQueryResolver` 呈现为可用能力；三者均在 A1 / A7 中确认生产不可达。
- `docs/quick-start.md:43` 又把读者引向该小节。
- `:141` 引用的 mutation 目标 `DefaultDeleteProposalRule.cs` 实际名为 `src/NLISSN.Rules/Propose/DefaultRemovalProposalRule.cs`。
- 另：`Miscellaneous/scripts/Run-MutationTests.ps1:3` 的默认值 `$MutatePattern = "**/DefaultDeleteProposalRule.cs"` 同样是重命名前的旧名（该脚本本身被 gitignore，且依赖未安装的 `tools\dotnet-tools\dotnet-stryker.exe`）。

### B11. `src/NLISSN/AGENTS.md` 引用不存在的 CLI 参数 — LOW

- `src/NLISSN/AGENTS.md:5`："rewrite 改动必须保留 `--no-diff`、`--skip-rewrite` 等无写入路径"。
- 全仓 `"--no-diff"` / `"--skip-rewrite"` 命中 **0**；`Program.cs` 对任何非空参数直接拒绝（`args.Length != 0`，见 `check-harness-consistency.ps1:52` 强制断言的 `args.Length != 0`）。
- ⇒ 这两个 flag 在零参数 CLI 下不存在，指令无法执行。无写入路径本身**是**存在的（`ConfigurationRunHost.cs:20` 导出默认关闭），只是不通过 CLI flag 暴露。

### B12. `Context/progress.md` 的 D1/D2 障碍描述已过期 — MEDIUM

`Context/progress.md:75-77` 仍写"D1 四条障碍 ②`CpgWorkBatchBuilder.Build` **强制单文件**（`:55-58`）"等。代码事实（改动时间 18:13–18:38，**晚于**该文件描述）：

- `CpgWorkBatchBuilder.cs:52-58` 现为："跨文件装箱（D1）：允许多个文件的 work item 进入同一次 build"。
- 跨文件装箱入口已接线：`PartitionedOperationPass.cs:344` `AssembleWorkBatchesAcrossDocuments(NLCPGBuildContext context)`，调用点 `ControlFlowPass.cs:77`、`DominancePass.cs:391`、`MemberAccessPass.cs:83`；`CallGraphPass.cs:112` 注释"// D1：跨文件聚合装箱"。
- 障碍 ③④（reducer per-item routing）已由 `NLCPGBuildContext.ResolveGraph/ResolveDocument` + 上述入口实现；`BuildManyDocuments` 在 `ApplicationService.cs:187` 被调用。
- D2（大文件切分）已实现并接线：`DocumentShardPlanner.cs:88,248-253` `PackLargeFile` 用 `LargeFileShardTargetLines` 切分；`DocumentShard.cs` 记录 `StableOrder`（**全局单调、跨文件唯一**，`:368-370` `ComputeStableOrder(fileOrdinal, shardIndex)`）；`DocumentShardPlanAdapter.cs` 在 `ApplicationService.cs:185` 设置 `WorkShardPlanner`；消费点 `NLCPGBuilder.cs:1103,631-632,671`。

其他 `progress.md` 陈旧引用：
- `:76` 称 `NLCPGBuilder.cs:2812` 的 `CanDeclareSymbol` 有 **24** 项 —— 实际已在 `:3752-3777`，共 **23** 项。
- `:75` 引用 `WorkScheduler.cs:811` 的重复 `StableOrder` 抛错 —— **正确**（实际 `:810-812` `"Duplicate StableOrder {…} in one submission."`），保留为准确项。

### B13. 状态文件自身的契约被违反 — MEDIUM

- `Context/progress.md:3` 自称"a concise handoff, not a change log"；根 `AGENTS.md` 要求"需要交接时**替换过期内容，而非追加日志**"。
- 实际：**5286 行 / 522,056 字节**（`Get-Content` 数组长度 5286；`Measure-Object -Line` 4113），79 个 `## ` 段落，含大量"轮次 25…31"式日期化日志。
- `Context/feature_list.json` `lastUpdated: "2026-09-25"`；status 词汇不一致：`done` 45、`completed` 3、`complete` 2（同一概念三种拼法），另有 `in_progress` 6、`blocked` 1、`pending` 1、`superseded` 2、`todo` 2。

### B14. 文档与一致性脚本的覆盖缺口 — MEDIUM

`Miscellaneous/scripts/check-harness-consistency.ps1`（131 行）是仓库的强制一致性检查，但它**只做 8 处字符串包含断言**（`:52-60`），**不校验任何 Markdown 链接或路径引用**。

具体缺口：
- `:59` `Require-Contains $runtimeGuideText '.omx/state/' 'docs/harness-runtime.md'` —— 断言 `docs/harness-runtime.md` 必须提到 `.omx/state/`，而 `.omx` 在磁盘上**不存在**（`Test-Path .omx` = False）。该断言保护的是一个指向空无的路径。
- `:62` 用正则 `^Current feature: \`(?<id>...)\`` 抽取当前 feature 并跨表校验 `feature_list.json`。**该分支确实会执行**（`Context/progress.md:5260` 存在匹配行），因此 `progress.md` 的任何重排都可能静默使这段校验失效——它依赖一个位于 5286 行文件第 5260 行的魔法标记，且**没有任何检查保证该标记存在**（`if ($currentFeature.Success)` 无 else）。
- 全文无 `Test-Path` 遍历、无链接解析、无 `设计docs` 覆盖 ⇒ B1–B11 这类"引用了不存在的文件/符号"**在结构上不可能被该脚本发现**。

### B15. 引用不存在目标的文档链接 — MEDIUM

对 `设计docs/目前设计/` 全部 27 页做路径引用解析，**12 处**指向不存在的目标：

| 文档 | 不存在的目标 |
| --- | --- |
| `项目概览.md:65` | `src/NLISSN/Composition/RuleRegistry.cs`（见 B2） |
| `项目概览.md:13,67` | `docs/research/2026-08-04-application-layer-review.md` |
| `delete-class-components.md:13` | `src/NLISSN/Composition/RuleRegistry.cs`（见 B2） |
| `运行时基础设施.md` | `docs/research/2026-08-04-application-layer-review.md` |
| `测试组件.md`（末行） | `docs/research/2026-08-04-high-star-github-documentation-style.md` |
| `fast-path-boundaries.md:52` | `tests/NLISSN.UnitTests/Application/DirectoryAnalysisUseCaseTests.cs`、`tests/NLISSN.HostTests/Application/ApplicationServiceFlowTests.cs`（二者均不存在；邻近的 `PipelineComponentTests.cs` / `RuleGraphCompilerTests.cs` **是**存在的） |
| `README.md` | 上述 4 个 `docs/research/2026-08-04-*` 另加 `2026-08-04-design-doc-length-audit.md`、`2026-08-04-high-star-github-docs-live-audit.md`、`2026-08-04-high-star-github-docs-study.md`、`2026-08-04-high-star-github-documentation-style.md` |

另：`项目概览.md:9,13`、`cpg-capabilities.md:11`、`cpg-roadmap.md:11`、`testing-strategy.md:11`、`测试组件.md` 均声明 `更新时间：2026-08-04`，而 `目前设计` 中最新的页面 mtime 为 2026-09-25（`性能分析组件.md`）——**更新时间戳已不代表内容时效**。

---

## 3. C 类：文档未被版本控制覆盖（结构性问题）

> 计数口径：只统计**仓库自撰文档**，排除 agent/工具产物目录（`.agents`、`.nuget`、`.VSCodeCounter`、`.research`、`.agent-workplace`、`.perf-research`、`.learnings`、`Build/`、`BenchmarkDotNet.Artifacts/`、`tasks/`）。**若把这些目录也算进来，磁盘 `.md` 为 330 个**——引用该总数会显著夸大问题，故不采用。

| 目录 | 仓库自撰 `.md` | 已跟踪 | 说明 |
| --- | --- | --- | --- |
| 合计 | 176 | **45** | 131 个未跟踪（其中 `设计docs` 属**有意 gitignore**，其余多为从未 `git add`） |
| `设计docs/` | 47 | **1** | 仅 `设计docs/目前设计/性能分析组件.md` 被跟踪；其余经 `.gitignore` 的 `设计docs` 行使 `!` 白名单例外 |
| `设计docs/目前设计/` | 27 | **1** | ⇒ "当前设计"层在全新 clone 中几乎不存在 |
| `设计docs/历史设计/` | 17 | **0** | 整层缺失 |
| `docs/` 合计 | 110 | — | 门户 + plans + research + benchmarks |
| `docs/plans/` | 65 | 19 | 46 个只是从未 `git add`（**未被 ignore**） |
| `docs/research/` | 23 | 6 | — |
| `docs/benchmarks/` | 10 | **0** | 全部未跟踪 |
| `src/` `.md` | 5 | — | 含各项目 `AGENTS.md` |
| `Context/` | 2 | 0 | `progress.md`、`问题.md` 均 gitignore（`feature_list.json` 非 `.md`，同样 gitignore） |
| `约束/` | 3 | 0 | gitignore |

被版本控制**跟踪但位于上述排除目录**的还有 4 个，故 `git ls-files '*.md'` 总数为 **49**（45 + 4）：`tasks/prd-rule-catalog-source-generator.md`、`tasks/prd-performance-fact-propagation.md`、`.learnings/ERRORS.md`、`.learnings/LEARNINGS.md`。
| `Context/` | — | 0 | `progress.md`、`feature_list.json`、`问题.md` 全部 gitignore |
| `约束/` | 3 | 0 | gitignore |
| `Miscellaneous/scripts/` | — | 2 | 仅 `New-CpgDopSmallFixture.ps1`、`Run-ConcurrencyPoolPerformance.ps1`；`check-harness-consistency.ps1`、`Run-TestTiers.ps1`、`harness-*.ps1`、`Run-MutationTests.ps1`、`README.md`、`tests/*.tests.ps1` 全部 gitignore |

**后果**：**49 条来自已跟踪文档的链接指向未跟踪/被 ignore 的目标**，在全新 clone 中全部断裂。包括：
- `docs/README.md:19` → 两份审计报告（即 §0 提到的 `unwired-design-audit-report.md`）——门户把未跟踪文件当作正式入口；
- `README.md:31` → `设计docs/README.md`；
- `docs/AGENTS.md` / `docs/contributing.md` / `docs/developer-guide.md` / `docs/harness-runtime.md` → 根 `AGENTS.md`（`harness-audit.ps1` 自报 `RootGuideTracked : False`）；
- `docs/contributing.md` / `README.md` → `Context/progress.md`。

而**文档仍在指示读者运行被 ignore 的脚本**：`docs/harness-runtime.md:12-14`、`docs/AGENTS.md:19`、`docs/contributing.md:47`、`docs/developer-guide.md:149`、`docs/quick-start.md:145`；`Miscellaneous/init.ps1` 被 `README.md:15`、`docs/quick-start.md:15`、`docs/contributing.md:19`、`docs/developer-guide.md:12`、`设计docs/目前设计/项目概览.md:82`、`testing-strategy.md:53` 引用；`tools/DataFlowTailMeasurement/{Program.cs,DataFlowTailMeasurement.csproj,summarize.py}` 全部 0 跟踪，而 `docs/harness-runtime.md:22` 要求运行 `python tools/DataFlowTailMeasurement/summarize.py`。

`.githooks/pre-commit` 存在但被 `.gitignore:13` ignore；`docs/harness-runtime.md:37-39` 指示 `git config core.hooksPath .githooks`——在全新 clone 中该目录不存在。当前 `core.hooksPath` 未设置（`harness-audit.ps1` 报 `HooksPath : not-configured`）。

---

## 4. 经核验为**准确**的文档（避免过度报告）

以下声明经独立复核与代码一致，**不应**被列入待修列表：

- **`设计docs/目前设计/缓存边界.md` 全文准确**：`WeakTypedCacheRegistry.cs:19` 的 `ConditionalWeakTable` + `Lazy<object>` 单飞（`:26` `GetOrAdd`）、`ByteBudgetLruCache.cs` 的 `byteWeight`/预算/`CacheStatistics`、以及 `:41` 关于 `CpgShardQueryResolver` 复用通用 LRU 的说法（`CpgShardQueryResolver.cs:15,23`）。四个被引用的测试文件全部存在。
- **`设计docs/目前设计/workspace-input.md` 准确**：`MsBuildWorkspaceInputLoader`、`WorkspaceSolutionSnapshot`、`WorkspaceProjectSnapshot`、`WorkspaceAnalysisService`、`WorkspaceDocumentSnapshot`、`CompiledDirectorySourceFile`、`AnalyzeCompiled` 全部存在于 `src/`；诊断码 `NLISSNWS020`–`NLISSNWS025` 真实存在（`MsBuildWorkspaceInputLoader.cs:88,164,275,372,403,742,759,791,1033,1168`）；`tests/NLISSN.HostTests/Workspace/` 存在。
- **`设计docs/目前设计/运行时并发配置.md` 准确**：`RoslynPrototypeExecutionOptions` 存在（`src/NLISSN.Application/ExecutionRuntime.cs:10`，用于 `CommandHost.cs:52`、`AnalysisRuntimeFactory.cs:8`）。
- **`设计docs/目前设计/规则事实端口-枚举化.md` 准确**：`RuleFactKind` 有 264 处引用跨 45 个文件；`RuleFactPorts` 在 `src/` 为 0。
- **`docs/harness-verification-matrix.md` 准确**：`PerformanceSummaryHarnessTests` 位于 `tests/NLISSN.PerformanceTests/Performance/PerformanceSummaryHarnessTests.cs:12`；`PerformanceEquivalenceChecker` / `PerformanceSampleAggregator` / `PerformanceSummaryDocument` 均存在。
- **`docs/contributing.md:42-44` 准确**：三条 build/test 命令的项目路径真实存在。
- **`cpg-workbatch-concurrency.md` 的阈值数字准确**：`:79-83` 的 1-40 / 41-200 / 201-800 / >800 与 `CpgWorkBatchCostModel.cs:63-66` 的 `smallMaxCost: 40, mediumMaxCost: 200, largeMaxCost: 800` 一致（错的是"这些阈值住在 `NLCPGBuilderOptions` 上"这句，见 A4）。
- **`cpg-workbatch-concurrency.md` §6/§7 准确**：固定 P 个长期 worker、有界 `Channel<CpgWorkBatch>`、`EffectiveQueueCapacity = max(2P,8)`（`CpgWorkBatchExecutor.cs:89-91,583-643`）、`LocalCpgFragment` 字段与文档代码块逐项一致。
- **`cpg-capabilities.md:30` sidecar 校验准确**：`CpgBuildRoutingIndexReader.cs:30-53` 校验魔数 `CPGI`、格式版本、`schemaVersion`、payload 长度与 SHA-256。
- **`cpg-architecture.md:17-19,27-34` 准确**：`NLCPGNode` / `NLCPGEdge` 为 `readonly record struct`、端点用 `NodeId`；`CanonicalEdgeStore` 的 `int[]` 序数 + `byte` kind + 稀疏元数据侧表与代码一致。
- **零参数入口链准确**：`Program.cs`（非空参数即抛）→ `ConfigurationRunHost`（`ConfigurationRunHost.cs:8-15`）；导出默认关闭（`:20`）；`项目概览.md:26-38` 描述正确。
- **`项目概览.md:67` 的物理布局例外准确**：`src/NLISSN.Application/ExecutionRuntime.cs` 命名空间为 `NLISSN.Core.Pipeline`（`:8`）并被 Core 链接编译。
- **`fast-path-boundaries.md:17-24` 的入口链与四开关门控准确**：`CommandHost.cs:82-84,274-296` → `RuleSelectionAdapter.cs:13-45` → `DirectoryAnalysisUseCase.cs:149`。
- **`cpg-project-workbatch-pool.md` 准确**：页头自标"已废弃"，其 §19 描述的当前 `NLCPG.ProjectJson` 组件与 `ProjectJsonExporter.cs:191,277,337,372,421` 的 manifest 字段和 `NLCPGEXP001`–`NLCPGEXP005` 一致（另有文档未列的 `NLCPGEXP006`，`:146`）。
- **`运行时基础设施.md:32-35` 模块地图准确**：`Configuration` / `Logging` / `Concurrency` / `Caching` / `Testing` 五个目录均存在且职责描述相符（仅 `:31` 的 "Schema 2" 漏报 Schema 3，见 B9）。
- **`日志与并发.md:41` 准确**：`IConcurrencyPool` 的 `SelectOrderedAsync` ×2、`SelectCpuBoundOrdered`、`CommitOrdered`、`CommitTwoStageOrdered`、`ForEachAsync`、`RunDependencyGraphAsync`、`ExecuteWithAdmissionAsync` 全部存在。
- **`cpg-capabilities.md:24` 准确**：`ControlFlowPass.cs:530-610` 的 `AddTryEdges` 确实产生 try/catch/finally 相关 CFG 边。
- **`cpg-workbatch-concurrency.md` 引用的验证入口测试文件全部存在**：`NLCPGPartitionedBuilderTests.cs`、`CpgExecutionMatrixTests.cs`、`CpgBuildRoutingIndexTests.cs`、`CpgRelationQueryUnitTests.cs`、`StructureViewBuilderTests.cs`、`CpgWorkBatchDeterminismTests.cs`、`CpgWorkBatchLifecycleTests.cs`、`CpgWorkBatchPerformanceTests.cs`、`GraphAnalyzerTests.cs`、`docs/benchmarks/cpg-workbatch-baseline.md`。
- **`Context/progress.md:75` 对 `WorkScheduler.cs:811` 的引用准确**（实际 `:810-812`）。
- **commit `1e9486f` 与 `progress.md` 描述一致**：6 文件、+1696/−101。

---

## 5. 无法仅凭阅读判定的项（需实跑 / 需作者意图）

- `cpg-architecture.md:38` 的量化结论（整体降幅约 26%、canonical 分量降 87.49%）是否为当前数字 —— 读代码只能证明其**前提句已失效**（B3）；要给出新数字需同输入、同布局、同 DOP 的实测。
- `cpg-workbatch-concurrency.md:310-314` / §16 的 DOP 1/2/16 等价与尾延迟验收是否真实通过 —— 断言存在，但需在 `Miscellaneous/init.ps1` 之后实跑。
- A4 / A5 中零读取的配置字段是否有**仓库外**消费者（NLCPG 作为库被外部引用）—— 仓库内为 0，否定需要构建产物引用分析。
- `ProjectJsonExporter.cs:220,247` 的文档循环严格串行，而配置项名为 `projectWorkerCount`（默认 12）且被 `ProjectExportOptions.cs:72,84` 投影为 `EffectiveMaxDegreeOfParallelism` 并写入 builder（`:300`）—— 这是"仅用于单文档局部 DOP"的有意设计还是遗留，需作者意图或提交历史。
- B2 的 `RuleRegistry.cs` 是"从未存在"还是"已改名删除" —— 需 git 历史。
- A2 / A12 / A14 的死面是否被规划为未来规则使用 —— `cpg-roadmap.md:26-31` 的优先级表未列支配/控制依赖，也未明确排除。
- `设计docs/目前设计/规则目录-编译期生成.md` 的 `:255` 验收记录是 2026-09-09 的快照；其中的测试计数（Unit 80/80、Contract 307/307、Host 616/616）**当时可能为真**，本次未复跑，故不作为"虚假验收"指控——只指出其**数量与机制描述已与当前代码不符且未回填**。

---

## 6. 审计有效性边界

- 本报告为**纯静态阅读 + grep**，**未构建、未运行任何测试**。仓库共享 `Build` 输出目录，约定要求串行构建/测试并设 `-p:UseSharedCompilation=false` 与 `Miscellaneous/init.ps1` 提供的 `DOTNET_CLI_HOME`。
- **工作树在审计期间持续变化**：`src/NLCPG/Builder/NLCPGBuilder.cs` mtime 18:38:25、`CpgWorkBatchBuilder.cs` 18:33:01、`PartitionedOperationPass.cs` 18:15:50、`ApplicationService.cs` 18:15:23、`DocumentShardPlanAdapter.cs` 18:14:31、`NLCPGBuilderOptions.cs` 18:13:00 —— 均**晚于**既有审计报告 `docs/plans/2026-09-26-unwired-design-audit-report.md`（mtime 17:45:19）。因此既有报告 §5 的 `ShardOrder` 缺口与 §0 的 D1 描述需按 B12 更新。
- 行号均为上述时点的实际值；鉴于工作树活跃，引用时应以符号名为主、行号为辅。
- 本次审计**只读**：除本报告外未创建、修改或删除任何文件。

---

## 7. 建议的处置顺序（按影响 × 成本）

1. **B1**（`规则目录-编译期生成.md`）—— 一页之内 5 处反向陈述、2 处被测试反向断言、基线数量错 17。这是"当前设计"层中最严重的一页，且修正成本低（改一段状态说明 + 回填数量）。
2. **A1 + B10 + B7** —— 持久化/流式子系统：要么在 `设计docs/目前设计/` 与 `docs/developer-guide.md` 明确标注"代码存在但生产未接线"，要么为该子系统补一条真实的生产配置入口。5011 行不可达代码是最大的维护负担。
3. **B2** —— 两页"当前设计"指向被契约测试禁止的 `RuleRegistry.cs`；改正成本极低（指向 `RulePipelineComposer.cs`）。
4. **A2** —— `Dominance` / `ControlDependence`：决定是"补生产 capability 请求"还是"在文档中标注当前不参与执行"。
5. **C 类（版本控制覆盖）** —— 决定 `设计docs/目前设计/`、`docs/benchmarks/`、harness 脚本是否应纳入 git。只要它们不纳入，任何"当前设计"的权威性都无法在 clone 间传递。
6. **B14** —— 把 `check-harness-consistency.ps1` 从 8 条字符串断言扩展为链接/路径解析校验，使 B3–B11 这类问题在 CI 中被自动发现，而不是靠人工审计。
7. **B12 / B13** —— 按 `progress.md` 自身契约做一次**替换式重写**（非追加），并把 D1/D2 已完成的事实回填；统一 `feature_list.json` 的 status 词汇。
