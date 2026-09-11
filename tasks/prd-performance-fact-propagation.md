# PRD: 性能事实传播与运行报告

## 1. Introduction / Overview

NLISSN 的 CPG Builder、规则图执行器和运行时已经产生了多类性能事实，但这些事实目前停留在低层对象、运行时文本日志或单文件结果中，目录、项目和 Workspace 聚合时会丢失。维护者因此只能看到总耗时和少量计数，无法回答以下问题：

- 哪个输入文件或顶层阶段消耗了时间；
- CPG pass、anchor discovery、persistence/cache 与 RuleGraph 阶段分别贡献了多少 wall time；
- 并发累计工作量是否被错误地当成端到端耗时；
- 两次运行是否使用了相同输入、规则、capability、cache 和并发条件，因而可以比较；
- 报告缺失或 instrumentation 失败时，业务分析是否仍然保持原有语义。

本功能建立一个只读、分层、不可变的性能事实传播链：

```text
NLCPG metrics
  -> CpgPerformanceFacts
  -> ApplicationPerformanceFacts
  -> DirectoryPerformanceFacts
  -> RunPerformanceReport
  -> PerformanceSummaryDocument
  -> <runRoot>/Performance/summary.json
```

完成摘要通道服务于 normal 和 benchmark；诊断事件通道只在 diagnostic/profile 模式按需启用。该功能不修改调度、CPG 图身份、规则图语义、决策、改写或写回行为。

## 2. Goals

- 保留每个可分析文件的 CPG、RuleGraph、rewrite 和结果状态性能事实，并按稳定 identity 聚合到目录、项目和 Run。
- 复用现有 `NLCPGBuildMetrics`、anchor/pass/persistence metrics、`RuleGraphNodeTelemetry`、`RuleGraphExecutionMetrics` 和运行时 pool/process 采样，不复制低层 metrics 的所有权。
- 明确区分 `wallElapsedMs` 与并发子操作的 `accumulatedElapsedMs`，避免把累计工作量误报成端到端耗时。
- 通过 run identity、输入指纹、规则 profile、capability、cache、环境、DOP、mode 和结果 snapshot 判断样本是否可比较。
- 在 `artifacts.performance.enabled` 默认关闭的情况下保持现有配置和运行行为；启用后发布稳定的终态 JSON。
- 让性能采集和写盘 fail-open：性能组件、sink 或 writer 失败不能改变业务分析结果。
- 让性能比较 fail-closed：失败、取消、缺少终态或 snapshot 不等价的样本不得进入 median/p95 聚合。
- 使报告可被测试、脚本和后续诊断工具稳定消费，而不把内部 C# facts 直接暴露为 JSON 契约。

## 3. User Stories

### US-001: 查看单文件 CPG 性能事实

**Description:** As a NLISSN maintainer, I want each analyzed source file to retain its CPG build facts so that I can identify slow passes and cache behavior without rerunning the builder manually.

**Acceptance Criteria:**

- [ ] `NLCPGBuildMetrics` 在 builder 返回后被一次性映射为不依赖 NLCPG 类型的 `CpgPerformanceFacts`。
- [ ] facts 包含 build elapsed、node/edge、pass、anchor discovery、persistence/cache 和 data-flow 可用事实。
- [ ] `PrototypeAnalysisResult.Performance` 只携带当前文件的应用级性能事实，不递归携带目录或 Run children。
- [ ] facts 映射不读取或修改 CPG graph、SemanticModel、Compilation、规则事实或 rewrite plan。
- [ ] builder metrics 缺失的字段使用明确的 `null`/状态，而不是用零伪造未知值。
- [ ] focused CPG/Application tests 通过。

### US-002: 保留并聚合目录文件事实

**Description:** As a maintainer analyzing a directory, I want per-file performance facts and a deterministic directory aggregate so that parallel completion order does not change the report.

**Acceptance Criteria:**

- [ ] `DirectoryAnalysisOutcome` 保留按稳定 `Index`/`FilePath` 排序的文件事实。
- [ ] `CombineResults` 和 `BuildResult` 不再丢弃文件级性能事实。
- [ ] 目录 wall time 由目录 owner 独立测量；它不会由并发文件 elapsed 相加推导。
- [ ] `sum`、`max`、样本数量和 top item 的定义在聚合结果中明确，且 children 不依赖完成顺序。
- [ ] 空目录、失败文件、取消和 skipped/unavailable 状态可以区分。
- [ ] directory parallelism enabled/disabled 的业务 snapshot 等价测试通过。

### US-003: 生成 Run 级终态报告

**Description:** As a maintainer, I want one terminal report for a run so that I can inspect the complete stage tree and know whether the result is comparable.

**Acceptance Criteria:**

- [ ] Application/Host 通过独立 `AnalysisRunOutcome` 携带业务结果和 `RunPerformanceReport`。
- [ ] 报告包含 run identity、输入 identity、环境、规则/capability/cache fingerprint、分层 DOP、mode/sample metadata 和 snapshot identity。
- [ ] 阶段协议名稳定，不依赖实现类名；未接通阶段为 `unavailable`，配置跳过阶段为 `skipped`，无法可靠取得的值为 `unknown`。
- [ ] terminal summary 同时保留端到端 wall time、阶段汇总、业务计数、图/规则/制品规模和资源事实。
- [ ] 报告完整性和 snapshot 等价校验结果明确表示 `comparisonEligible`。
- [ ] 终态 JSON 只在所有可用事实聚合完成后发布。

### US-004: 可选启用性能 artifact

**Description:** As a user running NLISSN, I want performance reporting to be opt-in and independent from the runtime text log so that ordinary runs do not pay for or create extra artifacts.

**Acceptance Criteria:**

- [ ] schema 2 支持 `artifacts.performance.enabled`，默认值为 `false`。
- [ ] `artifacts.performance` 与 `artifacts.runtimeLog` 独立配置；启用其中一个不会隐式启用另一个。
- [ ] 启用后终态报告写入 `<runRoot>/Performance/summary.json`。
- [ ] worker 不直接写最终 JSON；writer 采用临时文件加原子发布。
- [ ] writer 失败不会使已成功的业务分析失败，并留下可观察的诊断状态。
- [ ] 配置 schema、loader、resolved configuration 和 host tests 通过。

### US-005: 隔离诊断事件与正式统计

**Description:** As a performance investigator, I want optional high-cardinality diagnostic events without contaminating normal benchmark statistics or changing analysis scheduling.

**Acceptance Criteria:**

- [ ] `normal`、`diagnostic`、`profile`、`benchmark` 是显式 mode，且报告记录 mode 和 sample metadata。
- [ ] `PerformanceEventSink` 是可选旁路；完成摘要不依赖 sink，sink 丢弃或抛异常只影响诊断数据完整性。
- [ ] diagnostic/profile 事件不进入 normal/benchmark median 或 p95。
- [ ] P1/P2 不实现通用 profiler、partition 级诊断或调度修改；partition 诊断明确留到 P3。
- [ ] diagnostic 失败隔离、模式混合拒绝和等价性测试通过。

### US-006: 比较运行结果而不误报回归

**Description:** As a benchmark maintainer, I want incomplete or non-equivalent runs excluded from comparison so that the report is evidence rather than an unsafe regression verdict.

**Acceptance Criteria:**

- [ ] 不同输入、rule profile、capability、cache、DOP、环境、诊断开关或 graph/rule/artifact snapshot 的样本保留原始 facts，但 `comparisonEligible=false`。
- [ ] 失败、取消、缺少成功 terminal summary 或 snapshot 不等价的样本不进入统计集合。
- [ ] 少于三次 measurement 时报告原始值并标记低置信度；达到样本要求时提供 median 和 p95。
- [ ] 性能组件只报告事实、完整性和等价性，不自动宣布性能回归或改变分析决策。
- [ ] 现有 performance tests 能验证 DOP 矩阵的业务 snapshot 等价。

## 4. Functional Requirements

### 4.1 Fact ownership and propagation

- **FR-1:** `NLCPGBuilder` 必须继续拥有 `NLCPGBuildMetrics`；它不得依赖 Host、Application artifact writer 或 JSON serializer。
- **FR-2:** Application 在 `BuildFromSemanticModel` 返回后必须将 `NLCPGBuildMetrics` 映射为 immutable `CpgPerformanceFacts`，并在同一文件结果中与规则事实绑定。
- **FR-3:** `ApplicationPerformanceFacts` 必须表示当前 analysis item 的 CPG、RuleGraph、rewrite 和 status facts；它不得包含目录 children、Run wall time 或 mutable builder/session 引用。
- **FR-4:** Directory/Application 必须保留逐文件 facts，并按稳定 item identity 聚合为 `DirectoryPerformanceFacts`；并发完成顺序不得影响 children 或 aggregate ordering。
- **FR-5:** Host 必须通过独立 `AnalysisRunOutcome` 获取业务 `PrototypeAnalysisResult` 和 `RunPerformanceReport`，不得反向访问 Builder 的 mutable state。
- **FR-6:** 内部 facts、Run report 和 JSON wire DTO 必须是分开的映射层；内部 record 增字段不得自动改变 JSON schema。

### 4.2 Stage and resource semantics

- **FR-7:** 报告必须使用稳定的 `stageId` 协议名，并提供 parent/child 关系；至少覆盖 Workspace、Directory、CPG、RuleGraph 和 Artifact 顶层阶段。
- **FR-8:** 每个有可靠 scope 的阶段必须区分 `wallElapsedMs` 和 `accumulatedElapsedMs`；父阶段 wall time 必须独立测量。
- **FR-9:** allocation、GC count、heap 和 working set 默认只声明 process/run/顶层阶段归因；并发文件级资源归因不可可靠时必须为 `null`。
- **FR-10:** 报告必须区分 `completed`、`failed`、`cancelled`、`skipped`、`unavailable` 和 `unknown`，真实测得零值才可编码为 `0`。
- **FR-11:** anchor discovery、persistence restore/persist、cache hit/miss、pass elapsed 和 RuleGraph node telemetry 必须保留其原始语义，不重复计入或重新推导。

### 4.3 Identity, comparison and aggregation

- **FR-12:** Run report 必须记录 schemaVersion、runId、git/input identity、rule profile hash、capability fingerprint、cache mode、SDK/runtime/OS/CPU、分层 DOP、mode、sample number 和 warmup 标记。
- **FR-13:** report 必须记录 graph/rule/decision/rewrite/diff/artifact snapshot identity 或等价校验结果。
- **FR-14:** 只有输入、配置、环境、mode、diagnostic profile 和结果 snapshot 满足比较条件时，样本才可进入统计。
- **FR-15:** 阶段聚合至少保留 count、sum、max、median/p95（样本足够时）和 top item；并发累计值不得替代 wall time。
- **FR-16:** 归一化指标（例如 bytes/node、ms/source KiB、ms/output）必须保留原始分子和分母，零分母返回 null 或显式 zero-output 状态。

### 4.4 Configuration and publication

- **FR-17:** schema 2 必须增加 `artifacts.performance`，其 `enabled` 默认为 `false`，并允许显式 mode 配置或使用稳定默认值。
- **FR-18:** resolved configuration fingerprint 必须包含 performance enable/mode，使不同采集配置不会静默合并。
- **FR-19:** performance writer 必须将 summary 发布到 `<runRoot>/Performance/summary.json`，并在发布前完成终态 completeness validation。
- **FR-20:** 原子发布失败、sink 异常或 artifact I/O 失败不得改变业务分析成功/失败状态；性能报告自身必须记录缺失或失败原因。
- **FR-21:** worker、CPG Builder 和 RuleGraph executor 不得直接写最终 summary JSON。

### 4.5 Modes and future diagnostics

- **FR-22:** `normal` 必须只启用低开销完成摘要和已有轻量运行时采样；它不能隐式打开高基数事件。
- **FR-23:** `diagnostic` 可以保存 per-stage/per-item 事件，`profile` 可以关联外部 trace/counters/gcdump，`benchmark` 必须使用固定输入、warmup 和等价性校验。
- **FR-24:** diagnostic/profile attachment 必须关联同一 `runId` 和 `stageId`，且独立于正式统计集合。
- **FR-25:** P1–P2 不得引入通用 profiler、修改 scheduler/admission、修改 NodeId/edge ordinal 或改变分析语义；P3 才允许 diagnostic-only partition detail。

## 5. Non-Goals (Out of Scope)

- 不实现通用 profiler、CPU sampling profiler 或替代 `dotnet-trace`、`dotnet-counters`、`dotnet-gcdump`。
- 不修改 CPG pass 顺序、capability build plan、NodeId、edge ordinal、规则 DAG、admission、并发策略或结果排序。
- 不做 P1 的 partition 级 allocation、object graph dump 或 worker 调度诊断。
- 不把 `NLCPGBuildMetrics`、`SemanticModel`、`Compilation`、CPG graph、规则事实、Builder、Stopwatch 或 logger 放进跨层性能契约。
- 不将 `RuntimeMeasurementLog` 变成所有领域 metrics 的唯一 owner，也不强迫 normal run 依赖 event sink。
- 不自动判断性能回归、选择优化方案或改变业务 decision、evidence、rewrite 和 write-back。
- 不把 profile/gcdump 样本混入 normal/benchmark median、p95 或默认报告的正式比较集合。
- 不在本期提供 dashboard、数据库存储、远程上传或跨机器基准服务。

## 6. Design Considerations

### 6.1 User-facing artifact

性能报告是面向维护者和 harness 的 machine-readable artifact，不是交互 UI。JSON 必须稳定排序、字段语义明确、未知值可区分，且同一聚合结果未来可以驱动文本或 CSV 输出。

### 6.2 Failure semantics

采集与业务分析解耦：性能数据缺失不应改变分析语义；但比较工具不能把缺失当作零，也不能从中间日志猜测成功终态。这个“分析 fail-open、比较 fail-closed”的边界必须贯穿所有 writer、聚合和测试。

### 6.3 Existing facts to reuse

- `src/NLCPG/Builder/NLCPGBuildMetrics.cs`：pass、anchor、persistence、cache、data-flow、node/edge facts。
- `src/NLISSN.Rule/RuleGraphExecutor.cs`：`RuleGraphNodeTelemetry` 与 `RuleGraphExecutionMetrics`。
- `src/NLISSN/Telemetry/RuntimeMeasurementLog.cs`：process、GC、working set、pool 和 terminal counters。
- `src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs`：逐文件结果与稳定 `Index` 发布顺序。
- `src/NLISSN/Hosting/DirectoryAnalysisService.cs` 与 `WorkspaceAnalysisService.cs`：artifact materialization 和 Workspace/project 边界。

这些对象仍由原 owner 维护；新性能域只做窄映射和聚合。

### 6.4 Compatibility and rollout

默认配置不生成性能 artifact，因此旧配置与普通运行行为保持兼容。实现应先落地 schema/facts/contract tests，再逐步接通 Host 和 benchmark harness。每个阶段都必须有 focused tests 与 snapshot equivalence evidence。

## 7. Technical Considerations

- 目标框架、编译器和测试入口以当前仓库的 `global.json`、各 `.csproj` 和既有 `dotnet test --no-restore -p:UseSharedCompilation=false` 约定为准。
- Core 类型不能引用 NLCPG implementation；`CpgPerformanceFacts` 必须使用中立字段，映射在 Application/NLCPG 边界完成。
- `AnalysisRunOutcome` 需要逐步改造 Directory、Workspace 和 CommandHost 的返回链；不能只在 Host 侧另开一个旁路并重新测量。
- 原子发布要兼容 run artifact 的“目录不存在即新运行、目录已存在即拒绝复用”约束，并处理临时文件清理。
- 资源快照在并发场景下默认是 process/run 级；若没有独占 scope，不能声称是每文件分配量。
- 聚合器必须以 stable identity 排序，禁止使用 task completion order、字典枚举偶然顺序或线程交错顺序作为报告顺序。
- 性能报告 schema 独立于 NLISSN configuration schema 和 CPG schema；JSON DTO 版本升级需要显式决定。
- P4 的统计 harness 应复用现有 `tests/NLISSN.PerformanceTests`，固定 fixture、warmup 和测量次数，避免把单次 noisy run 当作结论。
- P5 的外部工具 attachment 只建立关联和生命周期约束；工具不可用时，normal/benchmark 不应失败或伪造 profile 数据。

## 8. Success Metrics

- 对启用性能 artifact 的每次成功 run，`Performance/summary.json` 存在、可解析、具有稳定 schemaVersion、run identity 和 terminal status。
- 单文件、目录、Workspace 三种入口均能保留逐 item facts；同一输入在不同并发完成顺序下生成相同的非时间字段和排序。
- 报告能分别展示 CPG pass/anchor/persistence、RuleGraph node telemetry、artifact 阶段和 process/run 资源事实，且不重复计算已存在的 elapsed。
- 关闭性能报告、sink 抛异常或 writer 失败时，业务 graph/rule/decision/evidence/rewrite/diff snapshot 与原路径等价。
- 不等价、不完整、失败或取消的样本不会进入正式 median/p95；至少三次有效 measurement 时可生成 median/p95 与样本计数。
- normal run 不创建性能 artifact；开启 performance 不会自动开启 runtime log，反之亦然。
- P1–P3 的 DOP 矩阵 `(1,1)`、`(1,12)`、`(12,1)`、`(12,12)`（以及仓库可用的近似值）保留业务输出等价证据，不引入调度语义变化。
- P5 attachment 能通过 `runId`/`stageId` 定位 profile/counters/gcdump，但这些附件不污染正式 benchmark 统计。

## 9. Open Questions

以下问题不阻塞本 PRD 或 P1 的执行，但必须在对应阶段开始前由实现者根据真实仓库约束落定并记录在设计决策中：

- `RunPerformanceReport` 和 JSON DTO 最终放置在哪个现有 project/namespace，才能避免 Core 对 Infrastructure 的依赖，同时不引入新的跨层循环引用？
- `gitCommit`、rule profile hash 和 capability fingerprint 在当前 CLI/Workspace 入口中的唯一可信来源是什么？若来源不可用，报告应如何编码 unknown？
- `mode` 是仅由 `artifacts.performance` 配置指定，还是允许复用 `logging.profile`；两者如何避免一个配置隐式改变另一个？
- 现有 artifact root 的原子替换策略在 Windows 文件锁场景下采用何种实现和重试上限？
- Workspace 的 project/TFM identity 与目录 `itemId` 的稳定规范是否需要单独写入 `设计docs/目前设计/README.md`？
- P4 benchmark harness 的固定 fixture、SDK、缓存预热和硬件隔离条件由哪个测试 owner 维护？
- P5 是否在 CI 只验证 attachment manifest，还是在受控开发机上实际运行外部诊断工具？

