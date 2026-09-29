# 最小 CPG 架构

> 状态：当前设计。
>
> 读者：需要新增 CPG pass、查询能力或持久化输出的维护者。
>
> 本页只说明 `NLCPG` 怎样组织 Roslyn 事实；删除规则语义见 [删除规则流水线](deletion-pipeline.md)。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-09-24。

## 结论

`NLCPG` 是 Roslyn-native、可冻结、可查询的最小代码属性图。它优先保存源码位置、符号关系、操作结构、方法边界、控制流和保守数据流；不以复刻 joern schema、建通用图数据库或推断动态语义为目标。

图的高密度领域载体 `NLCPGNode` 与 `NLCPGEdge` 当前是 `readonly record struct`。节点不持有 Roslyn 对象，也不保存 `DisplayKind` 或 `Name`、`FullName`、`Signature`、`TypeFullName`、`FilePath` 字符串；这些字段在所属 `NLCPGGraph` 的字符串表中以 `uint` ID 表示，`0` 是空值。构图时的 `NLCPGNodeDraft` 只存在于物化 seam，`NLCPGGraph` 负责 intern；查询、排序、CLI 和展示通过 graph resolver 解析 ID。展示文本仍优先按源码 span 回切原文，再回退到 `FullName`、`Name` 和由 `Kind`/源码语法推导的显示种类。这使载体可以按值复制，但不改变图容器、字符串引用或集合索引本身的分配成本；是否产生实际内存或访问收益仍需在同一输入、同一集合布局和相同 DOP 下测量。

节点的值相等性按稳定锚点优先、`NodeId` 次之、未物化节点字段最后的顺序判断。构图阶段仍由 `NLCPGGraph` 负责锚点去重、身份物化和边提交；调用方不得把复制一个节点值理解为修改图中节点。`NLCPGEdge` 的端点使用 `NodeId`，因此边值可独立放入值集合和冻结索引。

冻结查询索引只保留一份按 `NodeId` 排序的 canonical node 数组。`NodesByKind` 和
`NodesByFilePath` 保存节点 ordinal（后者的 key 是 graph-owned `FilePathId`），由
`OrdinalNodeList` 延迟投影为节点视图；它们不再各自复制完整 `NLCPGNode` 数组。字符串
ID 只在所属 graph 内有意义，不能跨 graph 直接比较；跨 graph 查询必须先通过各自 resolver
规范化到文本或其他稳定身份。

冻结后的边走同一条延迟投影路线。canonical 边不再以 `NLCPGEdge`（72 B/边）常驻，而是由
`CanonicalEdgeStore` 以结构化数组保存 source/target ordinal、`byte` 宽度的 `Kind`，以及
稀疏元数据 id（元数据覆盖率为 0 时侧表整体不分配，实测 9 B/边；有元数据时 13 B/边）。
CSR 表（outgoing/incoming/incomingByKind）只保存 `int[]` ordinal 置换与 offset 表，
`OrdinalEdgeList` 和 `CanonicalEdgeList` 按序数现场投影为 `NLCPGEdge`；因该类型是
`readonly record struct`，投影不产生堆分配。投影出的边字段与冻结前逐字段相同，
`SnapshotVersion` 因此逐字节不变。`NLCPGEdge` 的公开形状、`NLCPGGraph.Edges` 的
返回类型与枚举序均未改变。

冻结后 `NLCPGGraph` 仍保留一份构图期填入的 `HashSet<NLCPGEdge>` 作为边集合的来源，
它的释放（连同去重义务的转移）是尚未执行的后续阶段；因此当前常驻边占用尚未降到
9 B/边，实测整体降幅约 26%，其中 canonical 分量本身已降 87.49%。

## 构图路径

入口项目是 `src/NLCPG/NLCPG.csproj`，主 builder 为 `src/NLCPG/Builder/NLCPGBuilder.cs`。构图从同一份 Roslyn 输入派生事实，再按稳定顺序物化：

```text
SyntaxTree + SemanticModel + IOperation
  -> method discovery / cost estimate / WorkBatch
  -> fixed workers -> immutable local facts/fragments
  -> stable reducer -> syntax / symbol facts
  -> operation facts
  -> method decoration
  -> CFG / data-flow / call / member facts
  -> deduplicated graph
  -> frozen graph or optional shard store
```

worker 只读取并返回 Roslyn semantic facts。NodeId、edge ordinal、去重、跨 shard 连接、catalog 和发布顺序必须在稳定调用线程处理。这样 worker 完成时机不会改变图身份或下游规则输入。

## Pass 与职责

| Pass | 代码锚点 | 输出边界 |
| --- | --- | --- |
| Syntax | `src/NLCPG/Builder/Passes/SyntaxPass.cs` | 节点、token、声明、引用和源码 span |
| Method decoration | `MethodDecorationPass.cs` | 方法、参数、返回值、entry/exit 和所属关系 |
| Operation | `OperationPass.cs`、`PartitionedOperationPass.cs` | `IOperation` 结构和语法关联 |
| Control flow | `ControlFlowPass.cs` | 方法内 `CfgNext`、`CfgTrue`、`CfgFalse` 等边 |
| Data flow | `DataFlowPass.cs` | 保守的局部与参数 reaching-definition 事实 |
| Interprocedural bridge | `CallGraphPass.cs`、`InterproceduralDataFlowPass.cs` | 直接调用目标和受限的跨方法关联 |
| Member access | `MemberAccessPass.cs` | receiver、成员和部分参数/访问关联 |

新增 pass 前先确认现有 NodeKind、EdgeKind 或 capability 无法表达目标事实。新增枚举需要同时写稳定身份、去重、查询、DOP 和负例契约，不能仅凭“多一个节点更方便”扩展图。

## 冻结与分片

内存冻结图是兼容基线。启用持久化流式构建时，session 在语法和方法边界建立后，按源码顺序发布文件骨架、方法边界和 operation fragment。一个 operation NodeId 只属于一个 fragment；跨 shard 连接在提交线程补齐。

未完成构建对读取方不可见。缺少 frontier、无法解析的跨 shard anchor 或损坏的 shard 都产出 `unavailable` 事实，而不是伪造一条局部边。完成构建可以额外发布路由 sidecar：`CpgBuildRoutingIndexWriter` 写入节点、span、边界和符号路由，`CpgBuildRoutingIndexReader` 校验魔数、版本、长度和 SHA-256 后恢复索引；旧构建继续保留 catalog 回退。

## 并行不变量

- DOP 只切分独立的 syntax/operation 工作，不改变最终提交顺序。
- 物化前不允许 worker 直接修改共享图容器。
- WorkBatch worker 只返回不可变事实或 `LocalCpgFragment`；NodeId 分配、边去重和图发布由 reducer 负责。
- batch queue、estimated cost/bytes 和 completed-not-reduced result sink 都有界；取消或首个失败不会发布部分成功图。
- CallGraph 和 interprocedural bridge 在局部方法结果及稳定 call-site 顺序发布后才运行。
- DOP 1、2、16 必须比较图签名、查询结果、规则决策、改写源码和 diff，而不只比较节点数量。
- 路由 sidecar、shard store 和查询预算属于可选构建输出，不替代内存图。

## 非目标和验证边界

当前不承诺完整 interprocedural data flow、虚派发、dynamic dispatch、外部库 summary、完整支配关系、预处理器语义、trivia 或通用查询 DSL。已具备 routing sidecar 不等于已经证明真实工程上的 catalog 时间、磁盘占用或查询延迟收益；这些数字必须由同一输入的 warmed 样本给出。

验证入口包括 `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`、
`CpgWorkBatchDeterminismTests.cs`、`CpgWorkBatchLifecycleTests.cs`、
`CpgBuildRoutingIndexTests.cs`、`tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs`
和 `tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs`。WorkBatch 遥测契约位于
`tests/NLISSN.PerformanceTests/Cpg/CpgWorkBatchPerformanceTests.cs`；详见
[CPG 能力边界](cpg-capabilities.md) 与 [CPG 演进优先级](cpg-roadmap.md)。
