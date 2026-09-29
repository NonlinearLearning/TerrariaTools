# 性能事实传播与运行报告设计决策

> 状态：已确认设计，尚未实现。
>
> 日期：2026-09-10。
>
> 当前设计入口：[性能事实传播与运行报告](../CodeDesign/目前设计/性能分析组件.md)。

## 1. 问题

NLCPG 已经产生了比 Host 能看到的更多性能事实。`NLCPGBuilder.LastBuildMetrics` 包含 pass、anchor discovery、persistence、cache、data-flow、node 和 edge 信息，但当前 Application 只投影 node/edge，Directory 聚合时又丢弃逐文件性能事实，Host 最终只能看到总耗时和少量计数。

规则图是相对完整的对照：`RuleGraphNodeTelemetry` 已经保存每个规则节点的输入数、输出数、耗时和状态，并可进入 `PrototypeAnalysisResult`。本设计将同样的“低层生产不可变事实、上层窄投影和聚合”模式扩展到 CPG 与 Host 报告。

本设计不解决某个具体 pass 的性能优化，而是先建立可信的事实传播和比较边界。

## 2. 设计目标

- 让单文件、目录、项目、Workspace 和 Run 的性能事实都能被定位和聚合。
- 保留 CPG pass、anchor、persistence、cache、规则节点和后置 artifact 的阶段边界。
- 让报告可以回答“测了什么、用什么测、结果是否可比较”。
- 让 normal/benchmark 使用低基数完成摘要，diagnostic/profile 使用独立的高基数诊断通道。
- 保持 graph、rule、decision、evidence、rewrite、diff、write-back 和并发语义不变。
- 让性能报告失败不会把成功的业务分析变成失败。

## 3. 被考虑的方案

### 方案 A：Host 重新测量和反向读取

Host 在分析结束后通过类型转换、反射或重新遍历结果猜测 CPG pass、规则和 artifact 耗时。

拒绝原因：Host 会依赖低层实现；Builder 的可变 `LastBuildMetrics` 可能已经失效；无法正确分辨并发 scope；反向扫描可能重复工作并改变观测成本。

### 方案 B：把共享 profiler/collector 传过所有层

创建一个通用 `PerformanceMeasurementSession` 或全局 collector，让 NLCPG、Application、Directory 和 Host 都直接发布事件。

拒绝原因：正常分析会承担事件分发成本；共享可变状态增加并发、顺序和生命周期问题；领域组件会被 profiler API 反向耦合；高基数诊断和低基数终态摘要的失败语义不同。

### 方案 C：分层 immutable facts + 可选诊断事件（采用）

低层只发布自己拥有的不可变摘要，上层按 item identity 做一次窄映射或聚合；diagnostic/profile 才启用可选事件 sink。

选择理由：这保留了显式数据流、确定性 fan-in 和清晰的 owner，同时吸收 LLVM PassInstrumentation、TimeProfiler、LNT 和 llvm-mca 的旁路 instrumentation 启发。

## 4. 采用的架构

```text
NLCPG Builder
  -> CpgPerformanceFacts
  -> ApplicationPerformanceFacts
  -> DirectoryPerformanceFacts
  -> RunPerformanceReport
  -> PerformanceSummaryDocument
  -> <runRoot>/Performance/summary.json
```

### 4.1 两条通道

完成摘要通道是 normal 和 benchmark 的必需通道：

```text
NLCPG metrics
  -> immutable CpgPerformanceFacts
  -> single-item result
  -> deterministic directory/project aggregate
  -> terminal report
```

诊断事件通道只在 diagnostic/profile 模式使用：

```text
CPG/Rule/Workspace stage
  -> optional PerformanceEventSink
  -> diagnostic event attachment
```

两条通道不共享一个可变全局 collector。完成摘要不能因为诊断事件丢失而缺失；诊断 sink 失败只使诊断数据不完整。

### 4.2 层级 owner

| 层 | 负责产生/处理 | 明确禁止 |
| --- | --- | --- |
| NLCPG | pass、anchor、persistence、cache、data-flow、graph facts | 依赖 Host、写最终 JSON、改 CPG 语义 |
| Application | 把 CPG facts 与规则/rewrite facts 绑定到当前 item | 重新测量或持有 Builder 给 Host |
| Directory | 保留 ordered children，聚合文件/目录/project facts | 丢弃逐文件明细或把并发 sum 当 wall |
| Workspace | restore、open、project、compilation/document 阶段 facts | 把 loader facts 混入 CPG facts |
| Host | Run identity、根阶段、artifact、终态报告 | 反向访问 Builder 或重算 pass 时间 |
| Harness | warmup、measurement、DOP matrix、snapshot equality、统计 | 放宽比较条件或改变分析策略 |

## 5. 数据契约

### 5.1 中立 facts

跨层类型放在 Core 的 performance namespace 中，但只包含中立、不可变数据：

```text
CpgPerformanceFacts
  itemId
  sourceIdentity
  buildElapsedMs
  passSamples[]
  anchorDiscovery
  persistence
  cacheCounters
  dataFlowMethodSamples
  nodeCount / edgeCount

ApplicationPerformanceFacts
  itemId
  cpg
  ruleGraph
  rewrite
  status

DirectoryPerformanceFacts
  itemId
  children[]
  stageSummary
  graph/rule/artifact snapshot

RunPerformanceReport
  runId
  input/environment identity
  root stage
  children
  terminal summary
  diagnostic attachment references
```

这些类型不能包含 `NLCPGBuildMetrics`、`SemanticModel`、`Compilation`、CPG graph、规则事实、Builder、Stopwatch、logger 或 serializer。

### 5.2 业务结果与运行结果

`PrototypeAnalysisResult.Performance` 是可选的当前 item 伴随字段，只指向 `ApplicationPerformanceFacts`，不递归包含目录或 Run children。

Host/Application 边界增加独立的：

```text
AnalysisRunOutcome
  Result: PrototypeAnalysisResult
  Performance: RunPerformanceReport
```

这样现有业务结果仍表达分析语义，而完整性能树由 Run outcome 传递。

### 5.3 内部模型与外部 JSON

内部 facts、Run report 和 JSON wire DTO 分离：

```text
facts -> RunPerformanceReport -> PerformanceSummaryDocument -> JSON
```

JSON schema 具有独立 `schemaVersion`，不会因为内部类型重命名或增加 diagnostic 字段而自动变化。writer 负责显式映射、稳定排序和状态编码。

## 6. 时间和资源模型

### 6.1 阶段身份

`stageId` 使用稳定协议名，不使用类名或方法名：

```text
Workspace.Load
Directory.Read
CPG.Build
CPG.Syntax
CPG.Operation
CPG.DataFlow
Rule.Mark
Rule.Propagate
Rule.Lift
Rule.Propose
Artifact.Rewrite
Artifact.Diff
Artifact.Evidence
Artifact.WriteBack
```

每个阶段包含 `parentStageId`、`itemId`、`status` 和 `errorKind`。完整阶段树先定义，尚未实现的阶段写 `unavailable`。

### 6.2 wall 与累计工作量

- `wallElapsedMs` 是当前 scope 的独立墙钟时间，是比较的主要时间指标。
- `accumulatedElapsedMs` 是并发子操作累计工作量，允许大于父阶段 wall time。
- 父阶段 wall time 由 owner 独立测量，不能由 children 相加推导。
- anchor discovery 和 persistence restore 必须单列，不能重复计入正式 CPG pass。
- `sum`、`max`、`p50`、`p95` 必须记录聚合层和样本集合。

### 6.3 资源归因

GC allocation、GC count、heap 和 working set 默认是 process/run/顶层阶段事实。并发文件或规则 scope 不得用全局计数器差值伪装成 item 级资源消耗。

只有独占 scope 或明确的诊断运行才允许提供较强的 item 归因；否则使用 `null` 并保留归因级别。

## 7. 身份、比较和状态

报告记录：

```text
schemaVersion
runId
gitCommit
inputKind / normalized input identity
source or fixture manifest hash
ruleProfileHash
capabilityFingerprint
cacheMode
sdk / runtime / os / cpu
directoryDop / cpgDop / ruleDop
mode / sampleNumber / isWarmup
```

跨 Run 聚合要求输入、规则、capability、SDK、cache、DOP、mode、诊断开关和 graph/rule/artifact snapshot 等价。否则仍保留原始报告，但 `comparisonEligible` 为 `false`。

状态值必须区分：

```text
completed / failed / cancelled / skipped / unavailable / unknown
```

失败、取消、缺失终态或不完整报告不能进入 benchmark 聚合；不能用零值猜测缺失事实。

## 8. 模式、配置和发布

新增配置：

```yaml
artifacts:
  performance:
    enabled: false
    mode: normal
```

它与 `artifacts.runtimeLog.enabled` 独立。启用后写入：

```text
<runRoot>/Performance/summary.json
```

`summary.json` 在所有可用 facts 聚合并完成状态校验后，以临时文件加原子替换方式发布。worker 不直接写最终 JSON。

模式规则：

| 模式 | 内容 | 聚合规则 |
| --- | --- | --- |
| `normal` | 低开销摘要和运行时采样 | 只与 normal 聚合 |
| `diagnostic` | per-stage/per-item 和未来高基数事件 | 不进入 normal/benchmark 中位数 |
| `profile` | 外部 trace/counters/gcdump 关联 | 独立保存，不进入普通统计 |
| `benchmark` | 固定输入、预热、测量和等价校验 | 只纳入可比较样本 |

性能采集对分析 fail-open；性能比较对不完整或不等价样本 fail-closed。

## 9. 分阶段交付

| 阶段 | 交付 | 验收重点 |
| --- | --- | --- |
| P0 | identity、stage vocabulary、状态和 JSON schema | 文档、schema example、字段语义审阅 |
| P1 | CPG facts、anchor/pass/persistence、RuleGraph telemetry 的单项与终态传播 | mapping、deterministic aggregation、stable JSON、故障隔离 |
| P2 | Workspace/Directory/Artifact 父阶段和资源 scope | 阶段树、父子 wall、unknown/unavailable、Host outcome |
| P3 | diagnostic-only partition collection/materialization 与事件 sink | 事件丢弃、DOP 1/2/16、结果等价 |
| P4 | 现有 harness 的多样本、top item、DOP matrix 和比较资格 | warmup、median/p95、snapshot equality |
| P5 | trace、counters 和独立 gcdump attachment | runId/stageId 关联，普通样本无诊断开销 |

P1 到 P3 不修改 CPG 并发、规则 DAG、admission、NodeId、edge ordinal 或分析语义。

## 10. 验收标准

### 10.1 语义不变

- 关闭性能报告、sink 抛异常或 writer 写失败时，业务分析按原契约完成或失败。
- DOP 1、2、16 的 graph、rule stage、decision、evidence、rewrite 和 diff snapshot 等价。
- 性能代码不改变 capability、pass 顺序、规则输入输出或结果排序。

### 10.2 报告稳定

- 内部 facts 不直接序列化。
- 文件、项目、阶段和规则节点按 stable identity 排序。
- wall、accumulated、resource attribution 和 aggregate population 语义不混淆。
- zero、unknown、unavailable、skipped、failed、cancelled 可区分。
- 缺少成功终态或等价性失败的样本不进入性能聚合。

### 10.3 测试层级

- Unit：facts mapping、聚合、状态、零值、并发累计值和失败事件。
- Contract：JSON schema、稳定排序、内部/外部 DTO、配置默认值。
- Host：Run outcome、Workspace/CPG/RuleGraph/artifact 阶段、原子发布和 sink 隔离。
- Performance：固定 fixture、warmup 加三次测量、(1,1)/(1,12)/(12,1)/(12,12) 和输出快照。
- Diagnostic：事件丢弃、外部 attachment 关联和不进入正式聚合。

## 11. 未纳入本次实现的内容

- 不重写 CPG 或规则调度器。
- 不把通用 profiler 框架作为 P1 前置条件。
- 不在 P1 开启 per-partition allocation、object graph dump 或 CPU profile。
- 不把性能报告变成自动回归判定器。
- 不复制 LLVM 的 native pipeline、cycle simulation 或调度语义。
