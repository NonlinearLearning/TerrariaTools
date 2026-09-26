# NLCPG 高密度图中的 class 与 struct 内存评估

> 状态：研究与架构审查；未修改任何生产类型。
>
> 日期：2026-09-17。
>
> 范围：当前工作区源码形态、仓库归档的性能证据，以及 C#/.NET 一手资料。本报告不是当前 commit 的性能基准结果。

## 决策

**不要**把 `NLCPGNode` 或 `NLCPGEdge` 整体从 `sealed record class` 改为 `struct`/`record struct`。

传闻中的效果在一个窄场景下确实可能出现：大量小型、只含 primitive 数据的对象以 `T[]` 或 `List<T>` 保存时，`T` 从引用类型变为值类型可减少每元素一次独立对象分配，并改善顺序扫描的局部性。但这既不是固定的“内存减半”，也不表示 C# 的 `class` 设计有缺陷。

对 NLCPG 而言，当前节点和边都是引用丰富的图实体，并被多个索引共享。一旦直接改为值类型，字典和多个数组就会复制完整负载；当前 class 设计则让这些容器共享同一对象引用。更可信的优化目标是**表示拓扑**：保留一份 canonical record，让邻接和查询索引保存紧凑 ID 或 ordinal。必须先测量，不能由进程级分配计数反推主导对象类型。

## 审查结论

### P0：历史 23.4 GB 分配量不能归因给节点对象

归档的 `Main.cs` 运行记录了 1,576,714 个节点、3,601,985 条边、100,187 ms 墙钟时间和 23,394,875,000 bytes 累计分配；另一个历史输入达到 1,962,560 个节点和 4,646,100 条边。同一报告明确说明这些是 2026-08-06 的日志，并非当前 commit 的基准，也把“CPG allocation 与 node/edge 的对应关系”列为 P0 缺口。见[历史日志证据](2026-09-09-analysis-pipeline-performance-study.md#历史日志证据)和[遥测缺口表](2026-09-09-analysis-pipeline-performance-study.md#已有本地遥测能力与缺口)。

`RuntimeMeasurementLog` 采样进程累计分配、managed heap、working set 和 GC 次数，但不识别分配类型，也没有逐个 CPG pass 的一般分配增量（[RuntimeMeasurementLog.cs](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs:133)）。`NLCPGBuildMetrics` 输出 pass 耗时和图规模，没有普通构图路径的 allocation delta（[NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs:82)）；跨层 mapper 保留了这一边界（[CpgPerformanceFactMapper.cs](../../src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs:53)）。

因此，23.4 GB 只能证明这个工作负载值得优先调查，不能证明 `NLCPGNode`、`NLCPGEdge`、Roslyn semantic object、字符串、字典、临时 LINQ 数组、shard export 或索引构造中的任何一种是主因。

### P0：直接把 `NLCPGNode` 改为 struct 会同时改变语义和存储经济性

`NLCPGNode` 含有七个字符串字段、多个 nullable 枚举/值字段以及一个 `StableNodeAnchor`，不是小型 primitive-like value（[NLCPGNode.cs](../../src/NLCPG/Model/NLCPGNode.cs:6)）。构图阶段它按 stable anchor 去重、合并，身份和合并路径使用 `with`（[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:91)、[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:500)、[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:537)）。对值类型而言，每次这些操作都会复制完整负载，而不是复制一个引用。

Builder 还刻意在若干缓存中使用节点实例身份：syntax-to-node、operation-to-node 和 node-keyed map 使用 `ReferenceEqualityComparer`；CFG 和 data-flow 结构以 `NLCPGNode` 为键和值（[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:45)、[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:53)、[DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs:68)）。值类型副本没有稳定对象身份。这不是替换一个关键字：这些 map 必须显式改为以 `NodeId` 或 `StableNodeAnchor` 为键，并重新验证所有下游图语义。

冻结后，同一节点被 node-ID dictionary、ordered node array、kind array 和 file-span array 共同引用（[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:237)、[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs:120)）。class 下，二级数组保存的是指向 canonical object 的引用；struct 下，每个数组都会再保存一份完整节点值，包括全部字符串引用和 nullable 字段。这与“一个连续小值数组”的传闻前提不同。

### P0：直接把 `NLCPGEdge` 改为 struct 很可能增大冻结索引

`NLCPGEdge` 含两个 ID 和可选 label、context、call-site context；后者自身包含字符串（[NLCPGEdge.cs](../../src/NLCPG/Model/NLCPGEdge.cs:6)、[NLCPGCallSiteContext.cs](../../src/NLCPG/Model/NLCPGCallSiteContext.cs:4)）。它是引用丰富的 record，而不是紧凑的整数对。

冻结索引保留一个 ordered edge array、四个 CSR adjacency array 和一个 by-kind array。当前它们都保存指向相同 edge object 的引用（[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs:123)、[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs:139)、[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs:243)、[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs:257)）。如果 edge 改成 struct，这些 buffer 都会复制完整边值，外加 `HashSet` 的值条目（[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:10)）。这会省掉部分对象头，却把六个引用数组变成六个值数组，是可信的内存回归风险。

持久化模型已经给出更有希望的形状：frozen shard 保存 canonical `CpgFrozenEdge`，入边邻接则使用 `int[]` edge index（[CpgShardContracts.cs](../../src/NLCPG/Persistence/CpgShardContracts.cs:223)）；exporter 只构建一次这些整数索引（[CpgFrozenShardExporter.cs](../../src/NLCPG/Persistence/CpgFrozenShardExporter.cs:74)）。在考虑 edge value-type 转换前，应先评估内存索引使用 ordinal buffer 的方案。

### P1：现有代码已经在紧凑 key 边界使用了 value type

`NodeId`、`StableNodeAnchor`、`NLCPGContextId` 和 `NLCPGCallSiteContext` 都是 `readonly record struct`（[NodeId.cs](../../src/NLCPG/Model/NodeId.cs:3)、[StableNodeAnchor.cs](../../src/NLCPG/Model/StableNodeAnchor.cs:6)、[NLCPGContextId.cs](../../src/NLCPG/Model/NLCPGContextId.cs:5)）。pending-edge buffer 也把去重 key 和 buffered representation 定义为 record struct，直到真正需要时才 materialize 引用类型 `PendingEdge`（[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs:658)）。

这正是目标模式：在高密度、不可变、紧凑的 key/buffer 位置使用 value data；在需要共享、丰富 metadata 或对象身份的位置使用 reference object。它反驳了“C# 迫使所有图数据都使用昂贵 class”的说法。

### P1：当前 streaming descriptor 也不是可自动转为 struct 的安全候选

`CpgNodeDescriptor` 包含 stable anchor、多个字符串引用、nullable span/dispatch 字段，随后会被 materialize 回 node（[CpgNodeDescriptor.cs](../../src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs:7)）。`CpgEdgeCandidate` 包含两个完整 anchor 以及可选 label/context/call-site payload（[CpgEdgeCandidate.cs](../../src/NLCPG/Builder/Streaming/CpgEdgeCandidate.cs:7)）。它们的生命周期较短，fragment commit 后会释放（[OperationFragmentFacts.cs](../../src/NLCPG/Builder/Streaming/OperationFragmentFacts.cs:49)），但两者都不是小值。

把它们改为 struct 可能避免 `List<T>` 中每项目标的对象分配，但也会让 `List<T>.ToArray`、排序、hashing 和 candidate copy 搬运完整 descriptor value。operation 路径把 candidate 收集到 list 后又复制进 array（[PartitionedOperationPass.cs](../../src/NLCPG/Builder/Passes/PartitionedOperationPass.cs:223)）。这只能作为基准假设。若测量显示此阶段为热点，更好的候选可能是 NodeId 分配后只携带 node ID/ordinal 的后期表示，而不是先把当前 anchor-heavy descriptor 改为 struct。

## 语言与运行时资料实际说明了什么

| 说法 | 一手来源结论 | 边界 |
| --- | --- | --- |
| 值变量包含值，引用变量包含对象引用。 | [C# 标准](https://github.com/dotnet/csharpstandard/blob/df649b5d1ed2b68126128ab6af4c2b4f58b4c0ca/standard/types.md#81-general)定义了这一区别以及多个引用变量共享同一对象的可能性。 | 标准不承诺对象头大小、CPU cache 行为或任何百分比收益。 |
| `List<T>` 使元素表示影响实际布局。 | 固定版本的 [CoreCLR `List<T>` 源码](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs) 使用 `T[] _items`。 | `List<SmallStruct>` 可以比 `List<SmallClass>` 更紧凑；这不适用于一个 struct 在多个图索引中被反复复制的情况。 |
| 对象头是实际运行时成本。 | 固定版本的 [CoreCLR object model 源码](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/coreclr/vm/object.h) 定义了 `OBJHEADER_SIZE`、`OBJECT_SIZE` 和对齐规则。 | 这是当前实现细节，不是 C# 规范契约，也不能替代目标运行时实测。 |
| value-type array 可能有更好 locality，但 copy 和 boxing 有成本。 | Microsoft 的 [class 与 struct 指南](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct)说明了 inline value array、locality、值复制和 boxing。 | 页面明确标记为 not-current；其中 16 bytes 是历史设计启发式，不是 JIT 硬阈值。 |
| boxing 会重新引入堆分配。 | [Microsoft boxing 文档](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing)说明 boxing value type 会创建 object。 | 泛型可避免许多情形，但每个实际的 `object`/interface 边界仍需 profile。 |

正确结论是：高密度小值可能受益于 value representation，但语言有意用复制交换共享和身份。`record struct` 与 `struct` 有同样的值语义；`record` 拼写不会把大型、引用丰富的负载变得紧凑。

## 归档证据与当前可观测性

| 证据 | 它支持什么 | 它不能支持什么 |
| --- | --- | --- |
| 约 1.58M node / 3.60M edge，约 23.4 GB 累计分配的历史运行 | 大图和临时 materialization 值得优先调查。 | 节点/边对象头占据某个比例的结论。 |
| 约 1.96M node / 4.65M edge 的历史运行 | 图可达到每个重复表示都值得计量的规模。 | 当前性能基线，因为归档日志没有当前 commit、SDK 和受控重复样本。 |
| 当前 stage metrics 与 partition diagnostics | 可分开 pass 耗时、图规模、cache fact 和 collection/materialization 工作（[PartitionPerformanceEvent.cs](../../src/NLCPG/Builder/PartitionPerformanceEvent.cs:20)）。 | 按类型的 allocation attribution；partition event 不含 allocation 或 retained-byte 字段。 |
| 已有 persistence restore metrics | restore-fact 与 graph-import allocation 已在该窄路径上测量（[CpgShardBuildCoordinator.cs](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs:231)）。 | 普通 syntax、operation、freeze、index 和 query 路径的 allocation。 |

仓库保留的优化规则与本结论一致：先拆分遥测，再改变图模型或并发默认值（[2026-07-19至29-实施与性能归档.md](../../设计docs/优化历史/2026-07-19至29-实施与性能归档.md#早期优化的保留结论)）。

## 推荐顺序

1. **建立受控 baseline。** 固定一个代表性大源文件、SDK、commit、capability/configuration；采用 warmup 加重复测量，并以 DOP 1 作为语义 baseline。仓库的 [Run-ConcurrencyPoolPerformance.ps1](../../Miscellaneous/scripts/Run-ConcurrencyPoolPerformance.ps1:1) 已会记录墙钟时间、allocation/heap/working-set peak、图规模，并拒绝输出快照发生变化的样本。
2. **在改表示前补 attribution。** 对 `Syntax`、`Operation` collection、`Operation` materialization、`FreezeQueryIndex` 和 persistence boundary 分别记录 allocation delta，并与 node/edge delta 同时保存。单个 `GC.GetTotalAllocatedBytes` 增量无法区分 node object、Roslyn query result 或 temporary array。
3. **做一次受控 retained-heap snapshot。** 每个受控 diagnostic run 使用一次 `dotnet-gcdump` 或等价 heap viewer，按 retained bytes 和 instance count 排序 `NLCPGNode`、`NLCPGEdge`、frozen shard record、string、dictionary、edge array、Roslyn type 和 temporary buffer。不要把 dump run 放入普通时间中位数；仓库文档已将其定义为仅诊断用途（[developer-guide.md](../developer-guide.md#外部-profile-关联)）。
4. **若 frozen edge 占主导，试验 ordinal adjacency。** 保留 canonical edge store，一次只把一种 `NLCPGEdge[]` index 改为 edge-ordinal array；同时测 retained heap、freeze time、query time 和完整图行为。shard incoming-index 是本地先例，不是内存图一定获益的证明。
5. **若 construction candidate 占主导，试验 compact post-allocation candidate。** 不要先把现有 anchor-heavy descriptor 改为 struct；比较只保留 NodeId/ordinal 和分配后确实需要 metadata 的 value record，并单独测量 copy/boxing。
6. **最后才考虑窄范围 value-type 实验。** 候选必须 immutable、在目标运行时足够小、不依赖 reference identity、不在多个 value array 中重复、且没有 hot boxing path；同时保持 graph snapshot、DOP 1/2/16、query、decision 和 rewrite 等价。

## 已否决的捷径

- **“所有 graph record 都改 struct。”** 否决：忽略共享、reference identity、多个 index 和 copy cost。
- **“历史 allocation counter 证明可以省 50%。”** 否决：它衡量 allocation flow，而不是按类型的 ownership 或 retained heap。
- **“改用 `ref struct`。”** 否决：图数据需被 heap collection、dictionary、async/persistence path 长期保留，`ref struct` 的生命周期限制与该模型不兼容。
- **“只把 `NLCPGEdge` 改 struct。”** 在测量 ordinal-index 替代方案前否决：当前边的重复索引使它成为风险最高的直接转换。

## 未来变更的验收证据

表示变更只有在同一输入和环境中同时获得以下证据时才能接受：

1. 控制 full collection 或 heap snapshot 后的 retained heap/type count；
2. 按 CPG stage 区分的每次 build、每节点和每边 allocation bytes；
3. build、freeze 和代表性 query 的 median 与 tail wall time；
4. GC count 与 working-set peak；
5. graph signature、query result、rule decision、rewrite 和 DOP 等价性；
6. persistence/reload 行为与 shard compatibility 无回归。

在此之前，证据支持的是 index-layout 调查，而不是全量 class-to-struct 迁移。

