# NL 对 C# class 内存优化方案的采用情况审查

> 日期：2026-09-17。范围：当前工作区源码、仓库内历史 runtime log 和既有性能研究。本文不修改生产实现，也没有运行新的性能基准。
>
> 对照方案：[C# class 内存性能优化：方案、证据与高密度图应用](2026-09-17-csharp-class-memory-optimization-solutions.md)。相关模型判断见 [NLCPG 高密度图中的 class 与 struct 内存评估](2026-09-17-nlcpg-class-struct-memory-assessment.md)，历史运行资料见 [分析流程耗时性能研究](2026-09-09-analysis-pipeline-performance-study.md)。

## 结论

当前 NL 工作区已经采用了部分比“把 class 改成 struct”风险更低的措施：持久化分片的整数入边索引、构建时的有界并发和有界发布、若干 `ArrayPool<T>` 缓冲区、少量精确容量预分配，以及稳定锚点范围内的字符串 ID 表。

但这些实现不构成“class 已不是内存问题”的证据。内存图仍保留富字段的 `NLCPGNode`、`NLCPGEdge`，并且冻结索引至少保存一个排序边数组、四个 CSR 邻接数组和一个按 kind 数组，元素仍是 `NLCPGEdge` 引用。当前没有 current-commit 的受控大图基线、按 CPG pass 的分配归因或 retained-heap 类型/根路径证据。因此，历史的 23.4 GB 累计分配不能归因于 class 对象头，更不能推出“改 struct 会减半”。

优先级应是：先用当前代码获取 retained heap 和分配热点，再试验一项内存图的 ordinal 二级索引；只有证明小对象主体和访问间接性才是主因，才原型化紧凑 record 或列式存储。直接把现有节点、边类型整体改为 `struct` 是错误的第一步。

## 判定口径

本文的“已采用”只表示源码存在并被当前路径使用，不表示已经测出收益。“局部采用”表示只覆盖特定边界或少量调用点。“未采用”表示在受版本控制源码中没有找到对应用法；它不等于该技术永远不适用。

## 方案采用情况

| 对照报告的方案 | 当前状态 | 源码证据 | 仍不能推出的结论 |
| --- | --- | --- | --- |
| 主体只存一份，二级索引用 ordinal | 局部采用 | [CpgFrozenShardIncomingEdgeIndex](../../src/NLCPG/Persistence/CpgShardContracts.cs:223) 的持久化入边索引是 `int[] offsets` 和 `int[] edgeIndexes`。 | 这只覆盖 frozen shard 入边索引，不能说明内存图的全部边索引已紧凑化。 |
| 内存图二级索引用 ordinal | 未采用 | [NLCPGGraphIndex](../../src/NLCPG/Model/NLCPGGraphIndex.cs:123) 生成排序边数组、四个 `CsrEdgeTable` 的 `NLCPGEdge[]` 和按 kind 的 `NLCPGEdge[]`。 | 现有 CSR 的 offsets 是整数，并不代表 adjacency 元素已经是 edge ordinal。 |
| 字符串去重或 ID 表 | 局部采用 | [StringInterner](../../src/NLCPG/Model/StringInterner.cs:5) 为 `StableNodeIdentityFactory` 的 fallback anchor 分配文本 ID；[StableNodeAnchor](../../src/NLCPG/Model/StableNodeAnchor.cs:6) 保存 `FilePathId` 和 `ExtraKeyId`。 | 该表不是全图 payload 的统一字典。 [NLCPGNode](../../src/NLCPG/Model/NLCPGNode.cs:6) 仍直接保存 `DisplayKind`、`Name`、`FullName`、`Signature`、`TypeFullName`、`FilePath` 和 `Text` 等字符串。 |
| `String.Intern` 或 CommunityToolkit `StringPool` | 未采用 | 对受版本控制源码搜索 `String.Intern(`、`StringPool` 和相关包引用均无结果。 | 不使用全局 intern 是合理的生命周期选择；是否需要构建范围字符串表必须先由重复字符串和 retained heap 证明。 |
| 限制在途中间数据和并发峰值 | 已采用 | [NLCPGBuilderOptions](../../src/NLCPG/Builder/NLCPGBuilderOptions.cs:13) 有 DOP、重排 allowance 和 `MaxOrderedResultRecordCount`；[PartitionedOperationPass](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:54) 将这些限制传给有序并发池。持久化配置还限制 pending publication、shard export 和 file write。 | 有界窗口降低“可同时保留”的上限，不证明最终图或任何单个 partition 的常驻大小已经下降。 |
| 已知数量时预分配集合 | 局部采用 | [PartitionedOperationPass](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:225) 以 record count 初始化 node descriptor 和 edge candidate 列表；[NLCPGGraphIndex](../../src/NLCPG/Model/NLCPGGraphIndex.cs:227) 按精确数量创建 offsets 和 adjacency 数组。 | `NLCPG` 仍有许多未指定容量的 `List`、`Dictionary`、`HashSet`；没有证据说明所有热点集合都已消除扩容。 |
| `Span` 消除临时文本切片 | 局部采用 | [CpgShardStore](../../src/NLCPG/Persistence/CpgShardStore.cs:462) 和同文件的验证路径使用 `AsSpan` 清理租借缓冲。 | `NLCPG` 仍有三处 `Substring` 后立刻哈希的路径，例如 [CpgShardBuildCoordinator](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs:344)。现有 `AsSpan` 用法并非系统性文本切片优化。 |
| `ArrayPool<T>` 复用高频临时数组 | 已采用 | [CpgShardStore](../../src/NLCPG/Persistence/CpgShardStore.cs:557) 租借验证数组和读取缓冲；[PartitionedOperationPass](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:151) 复用 operation child buffer，并在归还引用数组时 `clearArray: true`。 | 池化降低重复分配，不会缩小同时存活的节点/边，也不保证 working set 降低。 |
| `ObjectPool` 重用工作对象 | 未采用 | 对 `Microsoft.Extensions.ObjectPool`、`ObjectPool<` 和 `new ObjectPool` 的源码搜索均无结果。 | 这不是缺陷。领域节点同时存活时，object pool 不会减少它们的数量；应先测到可完全重置的昂贵临时对象。 |
| 节点/边改为 struct 或 SoA/列式存储 | 未采用为主 | [NLCPGNode](../../src/NLCPG/Model/NLCPGNode.cs:6) 和 [NLCPGEdge](../../src/NLCPG/Model/NLCPGEdge.cs:6) 都是 sealed record class。已有 value type 仅限 [NodeId](../../src/NLCPG/Model/NodeId.cs:3)、[StableNodeAnchor](../../src/NLCPG/Model/StableNodeAnchor.cs:6) 等窄身份/键。 | 已有窄 value type 不表示领域 payload 已连续存储，也不证明 class 是累计分配的主因。 |
| .NET 10 JIT 与 DATAS | 部分采用，配置证据不足 | 生产项目，包括 [NLCPG.csproj](../../src/NLCPG/NLCPG.csproj) 和 [NLISSN.csproj](../../src/NLISSN/NLISSN.csproj)，目标为 `net10.0`。未找到 tracked runtimeconfig template、`GarbageCollectionAdaptationMode`、`ServerGarbageCollection`、`HeapHardLimit` 或 `DOTNET_GC*` 配置。 | target framework 不能证明实际部署 runtime、GC mode 或 DATAS 的实际效果；这些必须以运行环境和对照样本确认。 |

### 缓存和生命周期的补充发现

这不是原报告中独立的方案项，但会直接影响“完成后仍然占了多少内存”的判断。

- [AnalysisRuntime](../../src/NLISSN.Application/ExecutionRuntime.cs:37) 创建的 `WeakTypedCacheRegistry<Compilation>` 使用 `ConditionalWeakTable`，不会单独把 `Compilation` 作为强根长期保留。这是合理的生命周期控制。
- [ByteBudgetLruCache](../../src/NLISSN.Infrastructure/Caching/ByteBudgetLruCache.cs:18) 已实现 LRU 字节预算；[CpgShardQueryResolver](../../src/NLCPG/Analysis/CpgShardQueryResolver.cs:19) 用 shard 的 `ByteLength` 作为权重。
- 但是当前 `src/` 中没有找到生产 composition root 对 `new CpgShardQueryResolver(...)` 的调用，只有 resolver 本身和接收它的查询 API；而 `CpgPersistenceOptions.StreamingReadCacheCapacity` 也没有接到这个按字节参数。即使后续接通，`ByteLength` 是序列化字节数，不是反序列化 `CpgFrozenShard` 对象图的托管堆大小，不能把它当作严格 managed-heap 上限。

## 为什么不能直接把现有边改成 struct

`NLCPGEdge` 不是只有两个整数的小值：它还有 edge kind、结构化标签、context ID 和可选 call-site context。更重要的是，当前 [NLCPGGraphIndex](../../src/NLCPG/Model/NLCPGGraphIndex.cs:68) 让同一条逻辑边在多个索引数组中出现。现在这些数组复制的是引用；若只是把类型改为较大的 struct，则每个数组会复制完整 payload，可能比现状更大，并让排序、传参和集合操作引入更多值复制。

正确的实验设计是保留一个 canonical edge table，只把一种二级 adjacency 从 `NLCPGEdge[]` 改为 `int[]` edge ordinal。它独立回答“多组引用索引是否是热点”，同时保留现有 `NLCPGEdge` 身份、序列化和查询语义。只有此实验和 heap dump 都表明 edge object 自身仍主导时，才考虑 compact edge record 或 SoA。

## 已有内存性能证据

下表来自 2026-08-06 的原始 runtime log。它们适合证明图可达百万节点、数百万边的量级，不是当前 commit 的 benchmark：日志终态没有完整 git SHA、SDK、规则配置或重复样本。

| 历史运行 | DOP | 总耗时 | nodes / edges | 分配证据 | 可以得出的结论 |
| --- | ---: | ---: | ---: | --- | --- |
| [Main.cs main-diff](../../Build/analysis/main-diff-20260806/artifacts/main-diff-20260806/RuntimeLog/runtime.log) | 12 | 100,187 ms | 1,576,714 / 3,601,985 | 最后采样的 run allocation 为 23,394,875,000 bytes；pool operation 累计 9,039.8845 ms。 | 大图和临时 materialization 应优先调查；pool 累计耗时约为 wall time 的 9%，不足以单独说明调度器是主因。 |
| [Main.cs main-basic-diff](../../Build/analysis/main-basic-diff-20260806/artifacts/main-basic-diff-20260806/RuntimeLog/runtime.log) | 12 | 61,098 ms | 1,576,714 / 3,601,985 | 没有可用终态 allocation sample；pool operation 为 1,461.3641 ms。 | 与前一行规则输出和配置不同，不能把 39,089 ms 差异归给一个实现选择。 |
| [NPC.cs npc-rule](../../Build/analysis/npc-rule-20260806/artifacts/npc-rule-20260806/RuntimeLog/runtime.log) | 1 | 113,894 ms | 1,962,560 / 4,646,100 | 没有可用终态 allocation sample；pool operation 合计 1,362.7670 ms。 | 大图规模在另一输入上复现；DOP 1 下并发池不是首要归因对象。 |
| [NPC.cs npc-rule-diff](../../Build/analysis/npc-rule-diff-20260806/artifacts/npc-rule-diff-20260806/RuntimeLog/runtime.log) | 1 | 107,543 ms | 1,962,560 / 4,646,100 | 没有可用终态 allocation sample；pool operation 合计 1,100.8490 ms。 | 同样不能与上一行直接作单因素对比。 |

`main-diff` 的 5 秒采样还显示 heap sample 曾接近 4.45 GB、结束时约 449 MB，而累计 allocation 为 23.4 GB。这再次说明累计分配、GC heap capacity、working set 和 retained object graph 是不同指标；其中任一个都不能单独归因于 `NLCPGNode` 或 `NLCPGEdge` class。

## 当前遥测能力与缺口

当前源码比 2026-09-09 的历史研究描述更完整：

- [RuntimeMeasurementLog](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs:133) 按运行采样 allocation、heap、working set、GC、ThreadPool 和 pool 峰值，并在终态写 node/edge 数。
- [NLCPGBuildMetrics](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs:53) 已有 CPG 总耗时、pass 耗时、anchor discovery、cache counter、node/edge、data-flow method 和 persistence 指标。
- [ApplicationService](../../src/NLISSN.Application/Analysis/ApplicationService.cs:279) 已调用 [CpgPerformanceFactMapper](../../src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs:11)，而 [PerformanceSummaryDocument](../../src/NLISSN/Performance/PerformanceSummaryDocument.cs:168) 会把 CPG pass、anchor、persistence、cache 和图规模写入终态摘要。因此，旧研究中“这些 metrics 尚未进入最终报告”的表述已不完全适用于当前工作区。
- [PerformanceRunMeasurement](../../src/NLISSN.Application/Performance/PerformanceRunMeasurement.cs:8) 和 [PerformanceResourceFacts](../../src/NLISSN.Core/Performance/PerformanceResourceFacts.cs:3) 明确把 allocation、heap 和 working set 标记为 run 级归因，避免把进程计数伪装为单个文件计数。

仍然缺少决定 class/struct 取舍所需的证据：

| 缺口 | 当前情况 | 对本问题的影响 |
| --- | --- | --- |
| current-commit 大图基线 | 本次未运行新 benchmark；历史日志缺运行环境和重复样本。 | 无法判断现在的分配、峰值或速度。 |
| retained heap 按类型和根路径 | 未见当前大图的 `gcdump` / heap dump 证据。 | 无法比较 node、edge、string、索引数组、Roslyn 和缓存的真实存活占比。 |
| 普通 CPG pass 分配 | pass 记录 elapsed，不记录 allocation；partition 诊断也不是 retained-byte attribution。 | 无法判断是 syntax/operation 临时对象、freeze/index 还是领域对象造成 23.4 GB 流量。 |
| persistence 窄路径分配的终态传播 | [CpgShardBuildCoordinator](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs:231) 已测 restore facts 的 elapsed 与 allocation，`NLCPGPersistenceMetrics` 也保存这些字段；但 [CpgPerformanceFactMapper](../../src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs:75) 和 `CpgPersistencePerformanceFacts` 没有映射它们。 | 该狭窄路径的分配目前不能从终态 summary 看见，且它不能代表普通构图路径。 |
| 缓存真实 heap 重量和生产接线 | resolver 的 LRU 用 serialized byte weight，且未找到生产 composition root 实例化。 | 不能用 `CachedBytes` 证明反序列化 shard 的 retained heap 或线上缓存峰值。 |

## 建议的验证顺序

1. 固定一个代表性大输入、当前 SDK/runtime、规则/能力集和 DOP。使用现有 [Run-ConcurrencyPoolPerformance.ps1](../../Miscellaneous/scripts/Run-ConcurrencyPoolPerformance.ps1) 的 1 次 warmup 加 3 次 measurement，保留脚本已经校验的语义 snapshot、allocation peak、heap peak、working-set peak 和 wall time。
2. 在独立 diagnostic run 对构图峰值和构图完成后各采一次 heap dump 或 `dotnet-gcdump`，查看 `NLCPGNode`、`NLCPGEdge`、`string`、边引用数组、Roslyn 对象和缓存的 retained size 与根路径。dump 不进入基准中位数。
3. 若多组边引用数组主导，做一个最小实验：仅将一种 CSR adjacency 改为 `int[]` edge ordinal，保留 canonical edge table。测 build/freeze、顺序遍历、随机查询、retained heap、GC 和语义快照。
4. 若字符串主导，先统计重复实例与字段使用，再比较单次构建范围 ID 表或受限池；不要默认采用 process-wide `String.Intern`。
5. 若短命分配主导，优先处理三个 `Substring` 哈希调用、候选列表容量和已存在池化点附近的临时数组；每次只改一个因素。
6. 只有在以上实验确认小对象主体和间接访问仍主导时，原型化 compact edge record 或列式存储。必须同时覆盖 equality、NodeId/stable anchor、持久化、查询、规则决策、DOP 1/2/16 和 rewrite 结果。

该顺序保留了 class 对共享、身份和可选富元数据的语义优势，同时让“内存减半、访问更快”成为可证伪的项目级假设，而不是由 C# 类型关键字推出的结论。
