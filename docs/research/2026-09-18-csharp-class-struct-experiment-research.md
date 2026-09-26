# C# 高密度 class/struct 实验：一手来源研究

> 状态：独立一手来源研究；用于 `experiment/domain-model-struct` 实验设计。
>
> 日期：2026-09-19。
>
> 结论边界：本报告解释语言语义、当前运行时实现和测量方法，不把结构体改造前后的
> 性能收益当作已证实事实。当前仓库没有同一输入、同一 SDK 和同一配置下的
> class 基线与 struct 对照数据，因此不能从本报告推出“内存下降一半”或“访问更快”。

## 结论摘要

高密度、小型、不可变值存放在 `T[]` 或 `List<T>` 中时，`struct` 可能减少每个元素
独立托管对象和引用间接，并改善连续扫描的局部性。这个现象来自值语义和数据布局的
组合，不是 C# 对 `class` 的设计缺陷，也不是语言规定的固定比例收益。

`class` 和 `struct` 首先是语义选择：引用类型提供对象身份、共享引用、`null`、继承
和多态；值类型默认复制值，不能自然表达共享实例。值类型转换到 `object` 或其实现
的接口时会发生装箱，装箱会分配对象并复制值。一个包含字符串、数组、Roslyn 节点
或其他对象引用的 `struct` 只会内联这些引用，不会把引用目标内联。

因此，本仓库实验的可辩护范围是“内部、无外部 API、不可变、短生命周期的载体”，而
不是把公开图实体、索引、缓存、配置和工作流对象机械替换为结构体。实验当前纳入
22 个 `readonly record struct`，并保留公开实体和有生命周期/行为的类型为 class。

## 证据分层

### 语言和标准保证

以下事实来自 C# 参考手册和 C# 标准，而不是某个 CLR 的对象布局假设：

- 引用类型变量保存对象引用，值类型变量直接包含数据；两个引用变量可以指向同一
  对象，而值类型变量各自拥有自己的副本。[S1][S2]
- 值类型赋值、默认按值传参和返回会复制值类型实例。若值类型包含引用字段，复制的
  是引用，引用字段指向的对象仍然共享。[S2]
- 值类型有 `default` 值；非 nullable struct 的默认值由其字段默认值组成，引用字段
  默认为 `null`。把 class 改为 struct 后，不能把“未找到”继续表示为一个普通 `null`
  实例，必须使用 `T?`、`bool + out T` 或明确的状态字段。[S3]
- 从值类型到 `object` 或接口的 boxing conversion 会分配一个对象并把值复制进去；
  从盒中取回值还会检查并复制值。引用类型转为 `object` 只改变引用的静态看法，
  不会改变被引用对象的身份。[S3]

这些规则已经足以否定三种常见推论：

1. `struct` 一定在栈上；实际位置可能是局部、另一个对象字段、数组元素、泛型集合
   元素或 boxed object。
2. `record struct` 自动变成小对象；record 语法提供值相等性和生成成员，但不消除
   字段、复制或装箱成本。
3. 所有 class 都应改为 struct；这会把原有共享/身份语义换成复制语义。

### 当前 .NET 实现观察

以下内容是固定提交的 dotnet/runtime 源码观察，不是 C# 规范合同：

- 当前 `List<T>` 实现以 `T[] _items` 保存元素；构造有容量的 list 会分配 `T[]`，
  扩容会创建新数组并复制元素。[S4] 因此 `List<SmallStruct>` 的元素可以直接位于
  数组元素区域，而 `List<SmallClass>` 的数组元素是对象引用，class 实例位于数组
  之外。这里的“位于元素区域”是该 runtime 对 `T[]` 的实现观察；C# 标准定义了
  数组中的元素变量，但不保证物理地址连续、数组头大小或对象引用的具体表示。数组
  头和 list 对象是两者共有的固定成本，元素数量较大时布局差异才可能占主导。
- 当前 runtime 的 `Dictionary<TKey, TValue>` 使用 `int[]` bucket 和包含 `TKey`、
  `TValue` 字段的 `Entry[]`；`HashSet<T>` 使用包含 `T` 的 `Entry[]`；
  `PriorityQueue<TElement, TPriority>` 使用 `(TElement Element, TPriority Priority)[]`
  节点数组。[S4] 在这个实现中，值类型会成为条目元素的一部分，而引用类型条目通常
  保存引用；这仍然不是集合 API 的跨 runtime 布局承诺。这个观察同时说明了风险：
  字典条目、哈希、比较、扩容或多份索引可能复制完整 struct；它不能由 `List<T>` 的
  直觉推广成所有集合都会更省内存。
- CoreCLR 的 `object.h` 和 `syncblk.h` 定义了当前运行时对象头、方法表/对象大小
  和对齐相关的内部表示。[S5] 这些源码可以解释独立对象为什么有额外成本，但不能
  作为跨平台、跨位数或未来 runtime 的固定字节数保证；对象大小必须在目标环境测量。
- 当同一值被放入多个字典、排序副本或索引数组时，struct 可能复制完整值；class
  容器通常只复制引用。含有多个字符串或集合引用的大 struct 可能节省一个外层对象，
  却增加复制/索引成本。

### 测量工具能证明什么

- BenchmarkDotNet 的 `MemoryDiagnoser` 不是默认开启的诊断器；官方文档列出它报告的
  `Gen 0`、`Gen 1`、`Gen 2` 和 `Allocated` 列，并说明启用诊断器时会执行独立运行以
  避免影响主要结果。[S6] 这些列仍主要回答分配流量和 GC 计数，不等于 retained heap。
- `dotnet-counters` 是用于健康检查和一级性能调查的工具，可以观察 `System.Runtime`
  的 GC 次数、累计分配和 GC heap 相关指标；它不能替代按类型的 retained-heap 归因。[S7]
- `working set`、累计 allocated bytes 和完整 GC 后的 retained heap 是不同指标。任何
  一个指标单独下降，都不能证明用户观察到的“内存占用下降一半”。需要固定输入、重复
  样本和对象图/类型归因。

## 数据布局推导

假设一个元素只有两个 `int` 字段，并且有 `N` 个元素：

```text
PairClass[]  ~= 数组头 + N 个引用槽位 + N 个独立对象及其对象头/对齐
PairStruct[] ~= 数组头 + N 个内联元素
```

这是一个用于列出成本项的模型，不是 C# 规范规定的对象大小公式。`PairStruct[]` 的
“内联元素”和 `PairClass[]` 的“引用槽位 + 独立对象”来自当前 .NET 实现及官方设计
指南的描述；目标 runtime、架构和数组布局仍须单独确认。这个模型说明为什么小型值数组
可能显著节省空间，也说明为什么模型不适用于当前所有领域对象：

- 值类型包含引用字段时，只内联引用槽位；字符串、字典、数组和 Roslyn 对象仍然
  独立存在。
- struct 较大时，按值返回、排序、扩容和多个索引的复制会变得昂贵。
- `Dictionary<TKey, TValue>`、`HashSet<T>` 和 `PriorityQueue<TElement, TPriority>`
  仍需分别测量条目布局、hash、比较和出入队复制；不能用 `T[]` 的直觉替代这些结论。
- 通过 `object`、非泛型集合、接口、反射或格式化边界时，boxing 可能重新引入托管
  对象分配。

## 对原始说法的判定

| 说法 | 判定 | 可成立的边界 |
| --- | --- | --- |
| 高密度小 class 改为 struct 可能降低内存 | 有条件成立 | 元素主要存放在 `T[]`/`List<T>`，值小、不可变、引用字段少，且没有重复 value-copy。 |
| 顺序访问可能更快 | 有条件成立 | 内联布局减少一次间接访问且工作集适合缓存；需要固定输入的重复基准。 |
| 内存一定下降一半 | 不成立 | 字段、对齐、容量、引用图、GC 状态、索引复制和 runtime 都会改变比例。 |
| struct 一定在栈上 | 不成立 | 值类型可以在托管数组、对象字段或 boxed object 中。 |
| class 设计有问题 | 不成立 | class 的额外引用/对象成本换取了身份、共享、可空性和多态。 |
| record struct 没有堆分配 | 不成立 | boxing、闭包、接口/object 边界和引用字段目标仍可能分配或存活。 |

## 对 NL 仓库实验的约束

### 纳入的内部载体

当前实验分支把以下 22 个类型改为 `readonly record struct`，保持类型名和成员名不变，
并保留泛型集合路径：

| 类型 | 主要承载位置 | 研究判断 |
| --- | --- | --- |
| `CpgNodeDescriptor` | `List<CpgNodeDescriptor>`、descriptor materialization | 外层对象可内联，但含多个字符串和 nullable 字段，收益必须实测。 |
| `CpgEdgeCandidate` | candidate list、去重和 export | 含 label/context 引用；不能按 primitive-only record 估计。 |
| `CpgFragmentOwnership` | `Dictionary` key、nullable owner | 值相等性和 `CpgFragmentOwnership?` 的空 owner 语义必须保持。 |
| `CrossShardSummary` | 跨分片摘要 | 短生命周期，但字符串字段仍是引用。 |
| `PendingCandidateBuckets`、`BoundaryBucket` | 字典值/键和边界发布 | 外层载体内联，内部字典/list 仍为引用对象。 |
| `NLCPGGraph.PendingEdge`、`MutableGraphFacts` | pending edge list、冻结前快照 | 仍引用 canonical node 或集合，不能宣称完整图内联。 |
| `CpgFrozenShardGraphFacts` | 恢复路径返回值 | 只是低基数的内部结果载体。 |
| `PropagationFactKey` | `Dictionary` key、固定点去重 | 字符串字段较多，hash 和复制成本可能抵消分配收益。 |
| `PropagationSourceFact` | source dictionary、`PriorityQueue` element | `MarkRecord` 仍为引用；队列出入会复制外层值。 |
| `PropagationWorkItemPriority` | `PriorityQueue` priority | 多字符串字段，比较和复制是明确风险。 |
| `AnalysisEvidenceCollector.PendingNode` | evidence pending-node list、排序/分组 | nullable 字段和字符串引用仍保留，需验证排序和快照。 |
| `AnalysisEvidenceCollector.PendingEdge` | evidence pending-edge list、去重排序 | 重点是 record 值相等性和边集合结果不变。 |
| `InterproceduralDataFlowPlan` | interprocedural plan list、批量边提交 | 四个 `NLCPGNode` 引用仍共享 canonical node，值类型只移除外层载体对象。 |
| `NLCPGBuilder.DefinitionFact` | reaching-definition 字典、事实数组 | 高基数字符串引用事实；`DefinitionFact?` 使用 `Nullable<T>`，必须显式解包。 |
| `CpgRestoreMetrics`、`CpgBaseRestoreResult` | persistence restore 返回值和上次指标 | 低基数内部结果载体，转换不代表 retained graph 已内联。 |
| `CpgShardExportRequest` | shard export request list | 异步泛型路径保持值传递；`NodeIds` 仍是集合引用。 |
| `CpgRelationQueryService.QueryKey`、`NLCPGSliceQuery.QueryKey` | relation/slice result cache key | 只改变 key 的外层表示，必须保持值相等性和缓存命中。 |
| `SqliteCpgShardCatalog.RoutingIndexCacheKey` | routing-index task cache key | 并发字典中的紧凑不可变 key，索引对象本身仍为引用对象。 |

这些类型的共同条件是：没有项目外公开或 protected API、没有资源所有权和可变生命周期，
实例主要表示已经收集的分析/图事实。`readonly` 只约束外层字段重新赋值，不会让字段
引用到的对象自动变为值，也不会阻止被引用集合自身可变。

### 明确保留为 class 的类型

- `NLCPGNode`、`NLCPGEdge`、`NLCPGLocalView` 是公开图契约，被多个索引、规则和查询
  消费；它们包含丰富引用字段，部分代码依赖对象身份和共享 canonical instance。
- `NLCPGGraph`、`NLCPGGraphIndex`、CSR 表和各种 cache/service 拥有状态、索引行为、
  冻结生命周期或资源管理职责。
- `OperationFragmentFacts`、builder/persistence publication/checkpoint、配置对象和
  YAML 反序列化对象具有可变状态、setter、释放或工作流语义。
- `MethodLinkageGraph`、`CpgFrozenGraphProjection` 等聚合/投影对象主要持有多个集合
  引用，改外层 class 不能消除实际 retained graph。
- `CpgRelationQueryService.QueryState` 与 `NLCPGSliceQuery.SliceState` 使用 `Parent`
  递归表示查询路径；结构体不能直接包含自身，改造需要同时改为 path ordinal/index
  模型，因此不属于本次 carrier 表示实验。
- `CpgShardPublication`、`CpgShardPublicationResult`、`CpgReusableCloneRequest`、
  `CpgCatalogPublication` 和 `CpgShardBuildCheckpoint` 是 Channel、排序缓冲、clone
  列表或 catalog writer 的发布工作流消息，不是稳定领域事实；其中 checkpoint 通过
  `Action<object>` 观察器传递，改为 struct 会引入装箱。
- `NLCPGSourceSemanticInput`、`OperationInventoryEntry`、各类 `Operation*`/
  `Syntax*` partition record、`MarkRegionFacts` 和 `AtomicCandidateFacts` 持有
  Roslyn semantic/operation/syntax 工作态，复制和生命周期由分析 pass 管理，不能按
  小型值载体的数组模型推断收益。
- 携带 `SyntaxNode`、`SemanticModel`、`IOperation` 或其他 Roslyn 临时状态的分析 record
  是工作态，不是稳定高密度原子载体；转换只会增加复制和生命周期风险。

### 必须保持的语义不变量

1. CPG 节点/边/边界边、NodeId、shard export/restore 和查询结果完全一致。
2. `CpgFragmentOwnership` 的选择顺序、字典去重和空 owner 行为一致。
3. propagation fixed point 的来源解析、priority 顺序和 admitted facts 一致。
4. evidence 的稳定 key、排序、去重和预算截断一致。
5. 公开程序集 API、JSON/SQLite/shard schema 不变。
6. 所有生产热路径继续使用泛型集合，不通过 `object` 或非泛型集合传递这些值。

## 推荐的对照实验

### 微基准

对每种代表性 carrier 分别测量：

1. 构造 `N` 个元素的时间和每操作 allocated bytes；
2. 预构造集合的顺序扫描时间，排除构造成本；
3. dictionary/hashset 查找、priority queue 入队/出队；
4. 经过 `object`、接口和泛型 API 的路径，识别 boxing；
5. 完整 GC 后仍存活的对象数量和 retained bytes。

`MemoryDiagnoser` 的 `Allocated`/GC 列只能回答分配流量的一部分；长时间运行还应配合
`dotnet-counters`，而 retained heap 应用独立的 heap dump 或等价分析验证，不能混入正常
吞吐样本。

### 端到端对照

基线和实验组必须固定：源码输入、元素数量、集合容量、规则配置、SDK/runtime、x64/
Arm64、GC 模式、Release 配置、DOP、预热次数和测量次数。每组至少执行一次 warmup 和
三次 measurement，记录中位数、离散程度以及：

- CPG/分析阶段 elapsed；
- `GC.GetTotalAllocatedBytes` 或等价 allocated 计数；
- Gen 0/1/2 次数；
- managed heap、working set 和峰值进程内存；
- retained 类型/根路径；
- 图、传播、证据、持久化和 rewrite 的等价快照。

若没有“转换前 class + 转换后 struct”的同输入样本，只能报告健康检查和语义等价，
不能报告收益百分比。若收益只出现在构造阶段而扫描阶段变慢，应分别呈现，不能合并为
“访问速度提升”。

## 当前实验状态与未验证项

当前分支已经有 22 个候选的结构体表示和反射契约测试；这证明表示和可见性目标，不能
证明其内存比例或速度变化。2026-09-21 的独立小批量测量已记录在
[NLCPG 字符串表与序号索引小批量测量](../benchmarks/2026-09-20-nlcpg-string-table-index-compaction.md)：
同一 `.NET 10.0.11` runtime、SDK `10.0.400`、Debug、DOP=1 下，carrier 每组 2 次预热、
5 次采样、50,000 个元素；相对 class 中位数，`struct-string` 分配量低约 33.8% 但耗时
高约 4.8%，`struct-id` 分配量低约 66.8% 但耗时高约 9.4%。同一测试还记录了固定源码
NLCPG 的构图、查询、export/restore、GC heap 和峰值 working-set 样本；恢复后的图和
文本投影断言保持一致，但没有旧 class NLCPG pipeline 基线，因此不能从它推出 graph
端到端内存下降或访问提速。这不是 BenchmarkDotNet，也不是 retained-heap 证据；
对象级 retained heap、Release class 基线和跨 runtime 结论仍是待测假设。

环境还应记录实际 SDK。仓库初始化检查显示请求的 preview SDK 与当前实际使用的 SDK
可能不同；不同 SDK/runtime 的结果不能进入同一组中位数。

## 来源

- **[S1]** Microsoft Learn，Reference types：
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types>
  （固定 Markdown：<https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/csharp/language-reference/keywords/reference-types.md>）。
- **[S2]** Microsoft Learn，Value types：
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/value-types>
  （固定 Markdown：<https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/csharp/language-reference/builtin-types/value-types.md>）。
- **[S3]** C# language specification，types 与 conversions，提交
  `df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca`：
  [types §8.1](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68128ab6af4c2b4f58b4c0ca/standard/types.md#81-general)
  与 [§8.3 value types](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68128ab6af4c2b4f58b4c0ca/standard/types.md#83-value-types)，以及
  [conversions §10.2.9 boxing](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/conversions.md#1029-boxing-conversions)。
- **[S4]** dotnet/runtime `List<T>`，提交 `44210ed3`：
  [`_items`](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs#L25-L25)
  和[扩容复制](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs#L111-L116)；
  同一提交的 `Dictionary<TKey,TValue>`、`HashSet<T>` 和 `PriorityQueue<TElement,TPriority>`：
  [`Dictionary.Entry`](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs#L1871-L1881)、
  [`HashSet.Entry`](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/HashSet.cs#L1829-L1838)、
  [`PriorityQueue._nodes`](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs#L26-L26)。
- **[S5]** dotnet/runtime CoreCLR object model，提交 `44210ed3`：
  <https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/coreclr/vm/object.h>
  和
  <https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/coreclr/vm/syncblk.h>。
- **[S6]** BenchmarkDotNet Diagnosers，提交 `e2eefbcf`：
  [diagnosers 文档的 Usage](https://github.com/dotnet/BenchmarkDotNet/blob/e2eefbcf76d189d9f23cc96e458f298e446b9b2b/docs/articles/configs/diagnosers.md#usage)
  与 [Restrictions](https://github.com/dotnet/BenchmarkDotNet/blob/e2eefbcf76d189d9f23cc96e458f298e446b9b2b/docs/articles/configs/diagnosers.md#restrictions)；
  `MemoryDiagnoser` 实现：
  <https://github.com/dotnet/BenchmarkDotNet/blob/e2eefbcf76d189d9f23cc96e458f298e446b9b2b/src/BenchmarkDotNet/Diagnosers/MemoryDiagnoser.cs>。
- **[S7]** Microsoft Learn，dotnet-counters：
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters>
  （固定 Markdown：<https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/core/diagnostics/dotnet-counters.md>）。
- **[S8]** Microsoft Learn，Choosing Between Class and Struct：
  <https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct>
  （固定 Markdown：<https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/standard/design-guidelines/choosing-between-class-and-struct.md>）。该页面包含历史性设计启发式，文中的 16-byte 建议不应当作当前 JIT 的硬阈值。
- **[S9]** Microsoft Learn，Record types（C# reference）：
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record>。
  `record struct` 的值语义不能消除其字段、复制或装箱成本；该来源用于核对 record
  类型的语言级生成成员和语义，而不是用于推导对象布局。
- **[L1]** 本仓库既有 [class/struct 密度研究](2026-09-17-csharp-class-struct-density-research.md)、
  [class 内存优化方案研究](2026-09-17-csharp-class-memory-performance-web-research-sources.md)
  和 [NLCPG class/struct 评估](2026-09-17-nlcpg-class-struct-memory-assessment.md)。这些是本报告的
  本地背景资料，不替代上面的语言/运行时一手来源。

网络资料核验日期：2026-09-19。固定提交用于减少来源漂移；CoreCLR、集合和
BenchmarkDotNet 的具体实现仍应在目标 SDK/runtime 上重新验证。
