# 内部领域数据载体值类型实验设计

状态：实验分支设计，适用于 `experiment/domain-model-struct`，不改变默认分支的
当前架构结论。

日期：2026-09-18

## 目标

验证 C# 中高密度、短生命周期的 CPG 和 NLISSN 分析数据载体从引用类型改为值类型
后，是否能够降低对象分配和保留堆，并改善连续访问。实验必须同时证明图语义、
传播/证据语义、持久化结果和并行度确定性没有变化。

“内存下降一半”和“访问速度提升”是待验证假设，不是预设结果。`class` 的对象头
和数组中的对象引用可能是主要成本，但 `struct` 也可能因为复制、对齐、装箱或
重复嵌入而使成本上升；字符串、集合和 Roslyn 对象仍然是独立的引用对象。

研究依据：

- [C# class/struct 密度研究](../research/2026-09-17-csharp-class-struct-density-research.md)
- [C# class 内存优化方案研究](../research/2026-09-17-csharp-class-memory-optimization-solutions.md)
- [NLCPG class/struct 内存评估](../research/2026-09-17-nlcpg-class-struct-memory-assessment.md)
- [NLCPG 现状采用审计](../research/2026-09-17-nl-class-memory-optimization-adoption-audit.md)

## 范围判定

“无任何 API”按以下可审查规则解释：类型不向项目外暴露 `public` 或 `protected`
API；类型不承担生命周期、资源所有权、可变状态、索引维护或校验工厂职责；实例
字段只表示已经收集的 CPG 或 NLISSN 分析事实。内部的 materialization 辅助方法不改变这个判定，
因为它们不是外部契约且不维护对象状态。

类型必须属于 CPG model、streaming builder、frozen graph facts 或 NLISSN 内部分析
载体边界。仅仅是 `record class` 不足以成为转换理由；低频的协调器参数、Roslyn
临时状态和持久化公共契约分别按职责排除。

## 纳入清单

| 类型 | 位置 | 纳入理由 | 关键风险 |
| --- | --- | --- | --- |
| `CpgNodeDescriptor` | `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs` | 每个流式节点的稳定字段，存放在高基数列表中 | 值复制、结构体尺寸、可选字段布局 |
| `CpgEdgeCandidate` | `src/NLCPG/Builder/Streaming/CpgEdgeCandidate.cs` | 每条候选边的稳定字段，存放在高基数列表和去重集合中 | 字典/hashset 行为、label 引用仍然保留 |
| `CpgFragmentOwnership` | `src/NLCPG/Builder/Streaming/FragmentOwnershipIndex.cs` | 片段范围键，作为字典键并在 owner 查找中大量传递 | `CpgFragmentOwnership?` 变为 `Nullable<T>` |
| `CrossShardSummary` | `src/NLCPG/Builder/Streaming/CrossShardSummary.cs` | 跨分片调用的稳定摘要 | 结构体复制 |
| `SkeletonShardPublisher.PendingCandidateBuckets` | `src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs` | 一次候选分桶的不可变聚合 | 字典和列表引用不变，只有外层载体内联 |
| `SkeletonShardPublisher.BoundaryBucket` | `src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs` | 边界分片队列的不可变键 | 作为字典键时的默认值和 hash 行为 |
| `NLCPGGraph.PendingEdge` | `src/NLCPG/Model/NLCPGGraph.cs` | 延迟物化的内部边事实 | 每项仍引用两个 `NLCPGNode` |
| `NLCPGGraph.MutableGraphFacts` | `src/NLCPG/Model/NLCPGGraph.cs` | 图冻结前的内部事实聚合 | 低基数，主要用于契约完整性 |
| `CpgFrozenShardGraphFacts` | `src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs` | 恢复路径返回的内部冻结图事实 | 低基数，集合引用不变 |
| `InterproceduralDataFlowPlan` | `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs` | 已排序的跨过程边连接候选，暂存于 plan list 后批量提交 | 四个图节点引用仍共享 canonical node，`Distinct`/排序必须一致 |
| `NLCPGBuilder.DefinitionFact` | `src/NLCPG/Builder/NLCPGBuilder.cs` | reaching-definition 索引中的高基数稳定事实 | `DefinitionFact?` 改为 `Nullable<T>` 后必须显式解包 |
| `CpgRestoreMetrics`、`CpgBaseRestoreResult` | `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` | 内部恢复结果与指标的不可变返回载体 | 低基数；nullable restore result 的访问语义必须保持 |
| `CpgShardExportRequest` | `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` | 分片导出队列中的不可变请求元素 | `TextSpan`、节点集合仍是字段引用，异步泛型队列不得装箱 |

### CPG 查询和持久化内部键

| 类型 | 位置 | 纳入理由 | 关键风险 |
| --- | --- | --- | --- |
| `CpgRelationQueryService.QueryKey` | `src/NLCPG/Analysis/CpgRelationQueryService.cs` | 查询结果字典的不可变缓存键 | 必须保持 record 的字段值相等性和 hash 行为 |
| `NLCPGSliceQuery.QueryKey` | `src/NLCPG/Analysis/NLCPGSliceQuery.cs` | 切片结果字典的不可变缓存键 | `AllowedEdgeKinds` 排序结果和缓存命中必须一致 |
| `SqliteCpgShardCatalog.RoutingIndexCacheKey` | `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs` | routing index task cache 的不可变键 | 并发字典访问和失败移除逻辑不能改变 |

### NLISSN 内部分析载体

| 类型 | 位置 | 纳入理由 | 关键风险 |
| --- | --- | --- | --- |
| `PropagationFactKey` | `src/NLISSN.Core/Propagation/PropagationFactKey.cs` | 固定点传播的字典键，按事实身份去重并在排序/查询中重复访问 | 字段较多，字典 hash 和复制成本可能抵消分配收益 |
| `PropagationFixedPointExecutor.PropagationSourceFact` | `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs` | 传播工作队列和来源事实字典中的内部值载体 | `MarkRecord` 仍是引用，队列出入会复制外层值 |
| `PropagationFixedPointExecutor.PropagationWorkItemPriority` | `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs` | `PriorityQueue` 的泛型优先级，生命周期短且只表达排序字段 | 多字符串字段造成较大值复制和比较成本 |
| `AnalysisEvidenceCollector.PendingNode` | `src/NLISSN.Core/Decision/AnalysisEvidence.cs` | 证据收集阶段的高基数列表元素，会被快照、分组和排序 | 可空字段与字符串引用仍占用字段空间，列表复制语义需保持 |
| `AnalysisEvidenceCollector.PendingEdge` | `src/NLISSN.Core/Decision/AnalysisEvidence.cs` | 证据边的短生命周期列表元素和去重排序输入 | 需要保持 record 的值相等性和边去重结果 |

以上 22 个类型统一改为 `readonly record struct`。保留现有类型名、成员名和内部
辅助方法，以降低调用方改动；不增加隐式转换，不把它们放入非泛型 API，不通过
`object` 或非泛型集合传递。NLISSN 载体的转换不代表其引用字段（例如
`MarkRecord`、字符串和 Roslyn 对象）也被内联。

## 明确排除

- `NLCPGNode`、`NLCPGEdge`、`NLCPGLocalView`：目前是公开图领域契约，被规则、分析
  上下文、查询和测试广泛消费；改值类型会改变 API、nullable 和复制语义。
- `NLCPGGraph`、`NLCPGGraphIndex`、`CsrEdgeTable`：拥有状态、冻结生命周期、查询
  或索引行为，不是数据载体。
- `CpgRelationQueryService.QueryState`、`NLCPGSliceQuery.SliceState`：通过 `Parent`
  字段递归形成查询路径；改为结构体会形成无限大小的自引用布局，除非同时改成
  ordinal/path-index 模型，这超出本实验范围。
- `NLCPGEdgeLabel`：有工厂、稳定键生成和转换行为。
- `CpgFrozenShard`、`CpgFrozenNode`、`CpgFrozenEdge`、`CpgReusableFragmentKey` 等
  持久化/query contracts：是公开边界，且可能受 JSON、数据库和跨程序集调用影响。
- `OperationFragmentFacts`：具有 `Release()` 生命周期和可变内部集合。
- `NLCPGSourceSemanticInput`、`OperationInventoryEntry`、`OperationRootPlan`、
  `OperationFragmentRecord`、`SyntaxPartitionResult`、`SyntaxSemanticFacts` 和
  `DataFlowMethodPartition`：字段直接持有 `SemanticModel`、`SyntaxNode`、
  `IOperation` 或 Roslyn symbol，属于按 pass 生命周期管理的工作态，不是稳定领域事实。
- `MethodLinkageGraph`：持有多个 symbol 集合和字典的图聚合，实例基数低且集合
  引用占主要成本；改外层载体不能消除实际工作集。
- `CpgFrozenGraphProjection`：是面向查询的多集合投影，职责是投影/查询协调，不是
  高密度原子事实。
- YAML 配置对象和带 setter 的配置 record：需要反序列化写入、默认值合并或配置
  生命周期，结构体会把其可变配置语义变成复制语义。
- 结构分析器中携带 Roslyn 节点、语义模型或临时上下文的 record：属于分析工作态，
  不是稳定值载体；其中引用对象仍是主要成本，转换会增加复制和生命周期风险。
- NLISSN 公开规则、分析、决策和 rewrite 模型：大部分是跨阶段契约，或包含 Roslyn
  对象、集合所有权和行为；不满足本实验的内部无外部 API 条件。
- `CpgShardPublication`、`CpgShardPublicationResult`、`CpgReusableCloneRequest`、
  `CpgShardBuildCheckpoint` 和 `CpgCatalogPublication`：分别进入 Channel、排序缓冲、
  reusable clone 列表或 catalog batch writer，承担发布/资源协调职责；它们不是本实验
  的稳定领域事实。尤其 `CpgShardBuildCheckpoint` 通过 `Action<object>` 诊断观察器
  传递，改为值类型会在观察边界引入 boxing。
- `CpgRoutingIndexCandidate`：异步迭代器每个已完成 build 只产生一个候选，基数低且
  包含已经物化的 `CpgBuildRoutingIndex` 引用；没有把低频 persistence iterator DTO
  作为本实验的密集 carrier。

### 剩余内部 record/class 审计

对源码中仍为 `private` 或 `internal` 的 record/class 做了第二轮职责审计。以下类型
没有纳入转换；它们要么是低基数的协调器计划，要么保存 Roslyn 工作态、聚合索引、
发布消息或诊断观察值。将它们机械改为 struct 不会覆盖本实验的高密度载体假设，且
会引入复制、生命周期或装箱风险。

| 类型 | 源码证据 | 保留为 class 的理由 |
| --- | --- | --- |
| `CapabilityBuildPlan` | [`NLCPGBuilder.cs:98`](../../src/NLCPG/Builder/NLCPGBuilder.cs:98) | 每次构建只创建一个能力解析计划，随后作为 builder 协调参数传递；它不存放在高基数集合中，11 个标志字段的按值复制可能抵消移除一次对象分配的收益。 |
| `LoopControlTargets`、`DataFlowOperationIndex` | [`NLCPGBuilder.cs:111`](../../src/NLCPG/Builder/NLCPGBuilder.cs:111) | 字段直接持有 `IOperation`、`IReadOnlyDictionary<IOperation, NLCPGNode>`，属于 Roslyn/data-flow 工作态；不满足稳定领域事实边界。 |
| `UsedFactRecord`、`UsedFactPartition`、`DataFlowMethodPartition`、`CfgSensitivePartition`、`CfgSensitiveWorkResult` | [`DataFlowPass.cs:32`](../../src/NLCPG/Builder/Passes/DataFlowPass.cs:32)、[`DataFlowPass.cs:247`](../../src/NLCPG/Builder/Passes/DataFlowPass.cs:247) | 分区记录携带 `IOperation`、`IMethodSymbol`、可变字典、递归子记录或 pass 指标，服务于一次数据流执行并由 pass 管理生命周期。 |
| `OperationRootPlan`、`OperationFragmentRecord`、`OperationPartitionResult`、`OperationPartitionPerformanceSample`、`OperationBuildStrategy` | [`PartitionedOperationPass.cs:13`](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:13) | 这些记录直接保存 `SyntaxNode`、`IOperation`、`IMethodSymbol` 或分区性能样本，是分区收集/物化工作态，不是稳定 carrier。 |
| `SyntaxPartitionResult`、`SyntaxSemanticFacts` | [`PartitionedSyntaxPass.cs:7`](../../src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs:7) | 字段包含 Roslyn symbol 和语义解析结果，仅在语法分区阶段短暂存活；转换会扩大工作态复制面。 |
| `MarkRegionFacts`、`AtomicCandidateFacts`、`SymbolUsageProfile.Index`、`MethodLinkageGraph` | [`MarkAnalysisSnapshot.cs:254`](../../src/NLISSN.Core/Analysis/MarkAnalysisSnapshot.cs:254)、[`SymbolUsageProfile.cs:244`](../../src/NLISSN.Core/Analysis/SymbolUsageProfile.cs:244)、[`MethodLinkageGraphBuilder.cs:182`](../../src/NLISSN.Core/Analysis/MethodLinkage/MethodLinkageGraphBuilder.cs:182) | 分别持有 `SyntaxNode`/`ExpressionSyntax`、Roslyn symbol 字典集合或方法链接聚合图；主要 retained 成本来自其引用图和索引，而不是外层 record 对象。 |
| 各结构分析器的 `*Structure` record | [`BinaryExpressionAnalyzer.cs:13`](../../src/NLISSN.Core/Analysis/StructureAnalyzers/BinaryExpressionAnalyzer.cs:13) | 保存语法节点数组和分析过程中的结构视图，属于 analyzer 的临时工作态，不是跨阶段稳定值。 |
| `CpgShardPublication`、`CpgShardPublicationResult`、`CpgReusableCloneRequest`、`CpgShardBuildCheckpoint` | [`CpgShardBuildSession.cs:679`](../../src/NLCPG/Builder/CpgShardBuildSession.cs:679) | 分别进入 Channel、顺序重排缓冲、可复用 clone 列表和 checkpoint observer；这些类型承载发布/资源协调职责。checkpoint 还通过 `Action<object>` 传递，改成 struct 会在观察边界装箱。 |
| `CpgCatalogPublication` | [`CpgCatalogBatchWriter.cs:193`](../../src/NLCPG/Persistence/Sqlite/CpgCatalogBatchWriter.cs:193) | 进入有界 catalog publication Channel，包含 lease、冻结 shard 和可复用键；这是持久化发布消息，不是稳定领域事实。 |
| `CpgShardExportCheckpoint` | [`CpgShardBuildCoordinator.cs:421`](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs:421) | 通过 [`ExportCheckpointObserver`](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs:42) 的 `Action<object>` 诊断边界传递，struct 会引入明确 boxing。 |
| `CpgRoutingIndexCandidate` | [`SqliteCpgShardCatalog.cs:1244`](../../src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs:1244) | 异步迭代器每次完成构建至多产生一个候选，且携带已物化的 `CpgBuildRoutingIndex`；基数低，转换不属于密集集合实验。 |
| `FrameworkParameterShape`、`FrameworkFlowSummaryDeclaration` | [`NLCPGDefaultFlowSummaries.cs:53`](../../src/NLCPG/Analysis/FlowSummaries/NLCPGDefaultFlowSummaries.cs:53) | 是静态框架摘要目录的声明辅助类型，数量固定且很小；声明类型还包含 `TryCreate` 行为，不是纯数据载体。 |
| `CliOptions`、`LocalViewOptions`、`AnchorSelector` | [`NLCPGCli.cs:415`](../../src/NLCPG/Cli/NLCPGCli.cs:415) | 属于 CLI 解析边界，不属于 CPG/NLISSN 领域数据模型；其中 selector 还承载参数解释后的选择行为。 |
| `QueryState`、`SliceState`、`GrantedRequest` | [`CpgRelationQueryService.cs:324`](../../src/NLCPG/Analysis/CpgRelationQueryService.cs:324)、[`NLCPGSliceQuery.cs:331`](../../src/NLCPG/Analysis/NLCPGSliceQuery.cs:331)、[`CpgBuildAdmissionBudget.cs:230`](../../src/NLCPG/Builder/CpgBuildAdmissionBudget.cs:230) | 前两者是递归查询路径状态，后者属于 admission lease 协调状态；它们不是无状态高密度事实，且 `SliceState.Parent` 不能直接形成值类型自引用。 |

该审计使“22 个纳入类型”成为职责边界，而不是对所有 `record class` 的语法扫描结果。
后续若要扩大范围，必须先单独设计 Roslyn 工作态、发布消息或 ordinal/path 表示，不能
把扩大范围混入本次 carrier 表示实验。

## 值类型语义约束

### 不可变性

所有纳入类型使用 `readonly record struct`。引用字段仍然指向不可变或只读契约；本
实验不把 `List<T>` 改成可变暴露，也不改变集合所有权。

### nullable 与 default

`CpgFragmentOwnership?`、可选上下文和可选引用继续表达“没有值”。NLISSN 内部
查询失败使用现有 `Try`/`bool + out` 契约；值类型的
`default` 不代表构造器已执行，因此内部代码不能把 `default(T)` 当作有效片段；需要
失败返回时优先保留现有 nullable 或 `Try` 模式。测试必须覆盖空 owner、空集合、
传播来源未找到和默认值不会被误当成有效 key 的场景。

### equality、字典和装箱

record struct 保留记录的值相等性，`CpgFragmentOwnership`、`CpgEdgeCandidate`、
`PropagationFactKey` 和证据边的字典/list 去重行为必须与基线一致。所有热路径保持泛型集合；反射测试中的
`Array.CreateInstance` 只用于验证，不作为生产路径。不得将值类型隐式转换到
`object`、非泛型 `IEnumerable` 或非泛型字典。

### 集合布局

实验重点是 `T[]`、`List<T>`、`Dictionary<TKey,TValue>` 和 `HashSet<T>` 的泛型路径。
对于数组/list，值类型元素直接内联，理论上可移除每个 carrier 对象的对象头和引用；
对于字典/hashset/priority queue，需要测量 entry 复制、hash、比较和队列出入成本，
不能只依据类型尺寸推断收益。

### 持久化兼容

纳入类型不直接参与公开 JSON、SQLite 或 shard schema。冻结 shard 的节点、边和
索引格式必须保持不变；只有内部 materialization 的输入表示变化。

## 行为不变量

实验前后必须保持：

1. 相同源码输入产生相同 NodeId、节点快照、边快照和边界边。
2. `NLCPGBuilder` 在 DOP 1、2、8、12、14、16 下结果相同。
3. shard export、restore、incoming-edge index、symbol/span lookup 结果相同。
4. `CpgFragmentOwnership` 的选择顺序、字典键去重和 owner 为空行为相同。
5. streaming descriptor/candidate 不保留 Roslyn 或 graph service 引用。
6. propagation fixed point 的来源解析、优先级顺序和 admitted fact 集合不变。
7. evidence node/edge 的稳定键、排序、去重和预算截断结果不变。
8. 公开程序集 API 和持久化 schema 不发生变化。

## 性能证据

基线和实验组使用同一构建配置、同一 fixture 和同一运行参数，至少记录：

- 总分配字节、GC 次数和代表性阶段分配；
- retained heap、private bytes/working set 和峰值进程内存；
- CPG 构建、streaming materialization、shard export、propagation 和 evidence materialization 的耗时；
- 泛型数组/list 遍历以及 dictionary/hashset/priority queue 查找或出入的吞吐；
- DOP 1/2/16 的语义等价和耗时变化。

结果同时报告绝对值和相对变化，并标出测量误差。没有重复运行和固定输入，不得
声称“减少一半”或“访问速度提升”。

## 失败回退

若值类型导致语义回归、装箱、复制或工作集上升，保留研究报告和基线证据，撤回
对应类型的转换；不得为了通过测试把 public contract 或状态类强行转换。必要时
后续可单独设计 struct payload/adapter，但不在本实验中混入。
