# NLCPG 架构与文件说明

## 1. 项目定位

`NLCPG` 是一个以 Roslyn 语法、语义和 `IOperation` 为事实来源的最小代码属性图（CPG）可执行项目。它把单个 C# 源文件构造成统一的节点和边图，再按需增加控制流、数据流、支配关系和有限的跨过程数据流能力。

项目不试图复刻完整的 Joern schema，也不把未能由 Roslyn 确定的外部、动态或多目标调用伪装成确定事实。默认构图完成后会冻结查询索引；持久化启用时，图还可拆成分片文件，并用 SQLite 和路由索引定位分片。

入口是 `Program.cs`，其唯一职责是调用 `Cli/NLCPGCli.cs`。库消费者通常从 `Builder/NLCPGBuilder.cs` 调用 `BuildFromSource` 或 `BuildFromSemanticModel`。

## 2. 整体框架

```text
C# source / SemanticModel
        |
        v
NLCPGBuilder + NLCPGBuildContext
        |
        +-- SyntaxPass / MethodDecorationPass
        +-- OperationPass 或 PartitionedOperationPass
        +-- CallGraphPass / MemberAccessPass
        +-- ControlFlowPass / DataFlowPass
        +-- 可选: InterproceduralDataFlowPass / DominancePass / ControlDependencePass
        v
NLCPGGraph (节点、边、稳定 NodeId、冻结查询索引)
        |
        +-- NLCPGSliceQuery: 内存图或分片图的反向切片
        +-- NLCPGCli: 图统计与局部视图输出
        +-- CpgShardBuildCoordinator: 分片、SQLite 目录和 routing.cpgidx
```

### 构图阶段

1. `NLCPGBuildContext` 持有 `SemanticModel`、语法根、源码文本、文件路径和待构建图。
2. `NLCPGBuilder` 根据选项选择顺序管线或分区管线。分区工作线程只读取 Roslyn 事实；节点、边、去重和最终顺序仍在稳定提交路径中物化。
3. 基础阶段建立语法、声明、符号、类型和操作事实。后续阶段补调用、成员访问、方法内 CFG 和到达定义式 DataFlow 边。
4. 选项要求时，构建器在已有基础事实之上增加支配、控制依赖及仅针对确定内部唯一目标的跨过程数据流桥。
5. `NLCPGGraph.FreezeQueryIndex()` 将可变收集态变成可查询的稳定快照，释放仅用于构建的临时 Roslyn 映射。

### 身份与查询

- `NodeId` 是运行期紧凑标识；`StableNodeAnchor` 和 `StableNodeIdentityFactory` 保留可重建的稳定身份。
- `NLCPGGraphIndex` 按节点、方向和边类型冻结邻接表，支撑图查询、局部视图和切片。
- `NLCPGSliceQuery` 默认进行受预算约束的反向遍历；遇到可用的分片解析器时，可按节点加载前沿分片。

### 持久化

- `CpgFrozenShardExporter` 将冻结图切成骨架、方法边界、操作片段和跨片邻接分片。
- `CpgShardStore` 负责二进制分片字节、哈希和结构校验；`SqliteCpgShardCatalog` 保存构建、分片、复用和定位元数据。
- `CpgBuildRoutingIndexWriter` 在 `builds/<buildId>/routing.cpgidx` 写入节点、符号、跨度与边界路由。SQLite 完成构建前会校验该文件，查询先利用它缩小需要打开的分片集合。

#### 冻结分片

冻结分片是 `CpgFrozenShard`：从已执行 `FreezeQueryIndex()` 的图导出的不可变数据包。它代表一个文件骨架、方法边界或操作片段，而不是构建期间仍可追加节点和边的缓存。

- **片内数据**：`Nodes` 使用连续的 `LocalIndex`，`Edges` 的两个端点也使用该局部编号，减少磁盘和内存占用。
- **跨片连接**：`BoundaryEdges` 的端点改用全局 `NodeId`，因此读取一个分片时仍能知道它与其他分片的连接关系。
- **快速反向查询**：`IncomingEdgeOffsets` 和 `IncomingEdgeIndexes` 是片内入边索引，避免每次反向遍历都扫描全部边。
- **发布条件**：分片写入 `.cpgbin` 后必须通过内容哈希和二进制结构校验，并由目录标记为 `Complete`；未完成或无效分片不能作为已发布构建查询结果。
- **用途**：`NLCPGSliceQuery` 可按需读取相关分片；源码和 NodeId 指纹匹配时，后续构建还可复用未变更的操作片段。

## 3. 目录职责

| 目录 | 职责 |
| --- | --- |
| 根目录 | 项目配置和极薄的可执行入口。 |
| `Cli/` | 命令行参数、默认图统计和局部视图 JSON 输出。 |
| `Configuration/` | `nlissn.yml` 的加载与 YAML 节点访问辅助。 |
| `Contracts/` | 跨层共享的节点、边、能力、调用分派与视图枚举。 |
| `Model/` | 内存图、稳定身份、不可变节点/边和冻结查询索引。 |
| `Builder/` | Roslyn 上下文、构图调度、分区、流式发布与持久化编排。 |
| `Builder/Passes/` | 每个构图或分析阶段的实现。 |
| `Builder/Preallocation/` | 在分区/流式构建前收集稳定锚点。 |
| `Builder/Streaming/` | 操作片段、节点归属、跨片边和骨架分片的稳定提交。 |
| `Analysis/` | 切片查询和分片按需读取。 |
| `Analysis/FlowSummaries/` | 有限跨过程数据流使用的方法摘要。 |
| `Persistence/` | 冻结分片协议、文件存储、路由索引和持久化接口。 |
| `Persistence/Sqlite/` | SQLite 目录模式、批写入器和查询实现。 |
| `docs/` | 阅读入口和节点/边目录。 |

## 4. 逐文件说明

### 根目录、CLI 和 Contracts

| 文件 | 职责 |
| --- | --- |
| `NLCPG.csproj` | .NET 10 可执行项目；引用 Roslyn C# 与 Microsoft.Data.Sqlite。 |
| `Program.cs` | 顶级入口，只把参数交给 `NLCPGCli`。 |
| `Cli/NLCPGCli.cs` | 解析输入、局部视图参数和 JSON 输出；驱动构建并打印节点/边统计。 |
| `Cli/NLCPGYamlConfiguration.cs` | 把 `nlissn.yml` 解析为 NLCPG 的输入、局部视图和输出设置。 |
| `Configuration/UnifiedYamlDocument.cs` | 加载 `nlissn.yml`，并提供映射、序列、标量读取与路径、整数解析辅助。 |
| `Contracts/NLCPGCapability.cs` | 定义可请求的图能力及其按位组合。 |
| `Contracts/NLCPGDecisionRelationKind.cs` | 描述决策相关的关系种类。 |
| `Contracts/NLCPGDispatchKind.cs` | 定义调用分派类别、标志和决策动作种类。 |
| `Contracts/NLCPGEdgeKind.cs` | 定义语法、语义、操作、CFG、DataFlow 和叠加层边类型。 |
| `Contracts/NLCPGInterproceduralBridgeKind.cs` | 标识实参到形参、返回值等跨过程桥边。 |
| `Contracts/NLCPGNodeKind.cs` | 定义语法、符号、抽象方法和 Roslyn operation 节点类型。 |
| `Contracts/NLCPGViewDirection.cs` | 定义局部视图的入边、出边和双向遍历。 |

### Model

| 文件 | 职责 |
| --- | --- |
| `Model/NodeId.cs` | 封装紧凑的运行期节点标识。 |
| `Model/NLCPGNode.cs` | 不可变节点载体，保存类型、字符串表 ID、稳定身份及源码定位。展示文本由 `NLCPGGraph` resolver 提供。 |
| `Model/NLCPGEdge.cs` | 不可变边载体，保存两端 NodeId、边类型和结构化上下文。 |
| `Model/NLCPGEdgeLabel.cs` | 为边提供可比较的结构化标签。 |
| `Model/NLCPGCallSiteContext.cs` | 保存调用点文件、跨度和显示信息。 |
| `Model/NLCPGContextId.cs` | 表示边的稳定上下文标识。 |
| `Model/NLCPGGraph.cs` | 收集节点/边、分配身份、冻结索引，并提供邻接、跨度和局部视图查询。 |
| `Model/NLCPGGraphIndex.cs` | 按方向、节点和边类型构建排序后的不可变查询索引；kind/path bucket 保存节点 ordinal。 |
| `Model/NLCPGLocalView.cs` | 局部子图查询的不可变结果。 |
| `Model/NLCPGGraph.cs` | 负责稳定 NodeId 分配、冻结图结构和建立查询索引。 |
| `Model/StableNodeAnchor.cs` | 表示可跨重建比较的节点锚点。 |
| `Model/StableNodeIdentityFactory.cs` | 从节点元数据产生稳定锚点。 |
| `Model/StableNodeRole.cs` | 区分稳定锚点的节点角色。 |
| `Model/DeterministicNodeIdTable.cs` | 将排序后的稳定锚点确定性映射为 NodeId。 |
| `Model/StringInterner.cs` | 为所属 graph 压缩重复字符串；`0` 表示空值，并支持按 ID 反查。 |

### Builder 核心与预分配

| 文件 | 职责 |
| --- | --- |
| `Builder/NLCPGBuilder.cs` | 构图总编排：选择管线和能力、调用 passes、冻结图、收集遥测并协调可选持久化。 |
| `Builder/NLCPGBuildContext.cs` | 集中保存 Roslyn 输入、当前图和构图期间的共享事实。 |
| `Builder/NLCPGBuilderOptions.cs` | 定义构图模式、并行度、能力、持久化、DataFlow 限制和全部构图遥测记录。 |
| `Builder/BoundedPartitionWorkWindow.cs` | 为可并行的分区工作提供有界窗口和顺序提交控制。 |
| `Builder/CpgBuildAdmissionBudget.cs` | 限制并发 CPG 构建的准入，避免目录级工作过量重叠。 |
| `Builder/CpgShardBuildCoordinator.cs` | 协调恢复已有基础图、导出分片、写入存储和发布构建。 |
| `Builder/CpgShardBuildSession.cs` | 管理流式分片构建会话、复用、暂存、检查点和最终发布。 |
| `Builder/Preallocation/CpgStableAnchorCollector.cs` | 预扫描稳定锚点，为确定性 NodeId 分配提供输入。 |

### Builder passes

| 文件 | 职责 |
| --- | --- |
| `Builder/Passes/INLCPGPass.cs` | 所有构图阶段共享的内部执行接口。 |
| `Builder/Passes/SyntaxPass.cs` | 建立语法树、语法节点、token、声明和基础语义关联。 |
| `Builder/Passes/PartitionedSyntaxPass.cs` | 按分区并行收集语法语义事实，再按稳定顺序提交。 |
| `Builder/Passes/MethodDecorationPass.cs` | 补充方法、参数、返回值和合成入口/出口节点。 |
| `Builder/Passes/OperationPass.cs` | 从 Roslyn `IOperation` 建立操作树和语法/符号/类型关系。 |
| `Builder/Passes/PartitionedOperationPass.cs` | 分区构建操作片段，并维持受控的顺序提交和缓存释放。 |
| `Builder/Passes/CallGraphPass.cs` | 建立调用点、目标方法及调用相关边。 |
| `Builder/Passes/MemberAccessPass.cs` | 建立字段、属性和索引器的成员访问抽象。 |
| `Builder/Passes/ControlFlowPass.cs` | 从方法内操作和 Roslyn CFG 投影 `CfgNext`、分支和方法边界流。 |
| `Builder/Passes/DataFlowPass.cs` | 在方法内以 CFG 为基础求解局部、参数和成员族的到达定义式数据流。 |
| `Builder/Passes/InterproceduralPlanRef.cs` | 跨过程桥计划的 8 B 惰性载体（池内序号 + 实参序）；端点在排序/发布期按序号从池现取。 |
| `Builder/Passes/InterproceduralDataFlowPass.cs` | 将已验证的计划写成有限的跨过程 DataFlow 边。 |
| `Builder/Passes/DominancePass.cs` | 用 Roslyn CFG 建立方法内支配和后支配关系。 |
| `Builder/Passes/ControlDependencePass.cs` | 根据后支配关系投影控制依赖边。 |

### Builder 流式辅助类型

| 文件 | 职责 |
| --- | --- |
| `Builder/Streaming/CpgNodeDescriptor.cs` | 描述可在流式阶段重建的节点事实。 |
| `Builder/Streaming/CpgEdgeCandidate.cs` | 在所有节点稳定落位前保存候选跨片边。 |
| `Builder/Streaming/OperationFragmentFacts.cs` | 保存一个操作片段的节点、边和复用所需事实。 |
| `Builder/Streaming/LocalFlowCandidateSet.cs` | 暂存方法局部的数据流候选。 |
| `Builder/Streaming/FragmentOwnershipIndex.cs` | 维护 NodeId 到唯一操作片段归属的索引。 |
| `Builder/Streaming/StreamingFragmentCommitter.cs` | 按源代码顺序提交操作片段到主图和持久化会话。 |
| `Builder/Streaming/SkeletonShardPublisher.cs` | 发布不含操作节点的文件骨架与方法边界分片。 |
| `Builder/Streaming/CrossShardEdgeCommitter.cs` | 在图冻结后按全局 NodeId 确定性记录跨片边。 |
| `Builder/Streaming/CrossShardSummary.cs` | 汇总跨片边和邻接发布所需信息。 |

### Analysis

| 文件 | 职责 |
| --- | --- |
| `Analysis/NLCPGSliceQuery.cs` | 根据边掩码和预算执行内存图或分片图的反向切片。 |
| `Analysis/NLCPGSliceQueryOptions.cs` | 定义切片预算、路径、不可用分片和结果记录。 |
| `Analysis/CpgShardQueryResolver.cs` | 通过目录和分片读取器按节点、符号或跨度加载并缓存冻结分片。 |
| `Analysis/FlowSummaries/NLCPGFlowSummary.cs` | 定义方法摘要端点、摘要、解析结果和注册表。 |
| `Analysis/FlowSummaries/NLCPGDefaultFlowSummaries.cs` | 提供内置、保守的方法数据流摘要。 |

### Persistence

| 文件 | 职责 |
| --- | --- |
| `Persistence/CpgShardContracts.cs` | 定义分片、冻结节点/边、租约、定位、边界邻接和存储/目录接口。 |
| `Persistence/CpgFrozenShardExporter.cs` | 从冻结图导出可持久化的分片及符号、边界信息。 |
| `Persistence/CpgFrozenShardReader.cs` | 把一个持久化分片打开为 `CpgFrozenShard`。 |
| `Persistence/CpgFrozenShardGraphReader.cs` | 把分片投影回可查询图事实，并处理入边投影。 |
| `Persistence/CpgShardStore.cs` | 读写 `.cpgbin` 文件，执行哈希和二进制结构校验。 |
| `Persistence/CpgShardStoreLock.cs` | 为同一 store root 提供进程内和命名信号量写锁。 |
| `Persistence/CpgBuildRoutingIndex.cs` | 定义构建路由索引的节点、符号、跨度和分片条目模型。 |
| `Persistence/CpgBuildRoutingIndexWriter.cs` | 序列化并哈希 `routing.cpgidx`。 |
| `Persistence/CpgBuildRoutingIndexReader.cs` | 读取、校验并反序列化 `routing.cpgidx`。 |

### Persistence/Sqlite 与文档

| 文件 | 职责 |
| --- | --- |
| `Persistence/Sqlite/SqliteCpgShardSchema.cs` | 创建和演进 SQLite 目录表及索引。 |
| `Persistence/Sqlite/SqliteCpgShardCatalog.cs` | 实现分片租约、构建会话、查询、路由索引校验、清理和恢复。 |
| `Persistence/Sqlite/CpgCatalogBatchWriter.cs` | 以单连接、预编译命令批量暂存和提交分片目录记录。 |
| `docs/code-layout.md` | 本文：架构、目录和逐文件导航。 |
| `docs/node-edge-catalog.md` | 节点、边、能力边界、局部视图和当前分析保证的详细目录。 |

## 5. 阅读顺序

首次阅读建议按 `NLCPGBuilder` -> `NLCPGBuilderOptions` -> `NLCPGGraph` -> `node-edge-catalog.md` 的顺序建立整体模型；需要理解某类关系时再进入相应 pass。需要追踪持久化时，从 `CpgShardBuildCoordinator`、`CpgShardBuildSession`、`CpgShardContracts` 依次读到 `CpgShardStore` 和 `SqliteCpgShardCatalog`。查询路径从 `NLCPGSliceQuery` 进入，分片读取再看 `CpgShardQueryResolver`。

## 6. 当前边界

- 图输入以单文件源码或已提供的 `SemanticModel` 为主，未包含完整工作区加载。
- 跨过程数据流只覆盖可确定的内部唯一目标；动态、外部、多目标、递归和别名场景保留为显式边界。
- CFG、支配和控制依赖以方法为单位；分片持久化不会改变冻结图的节点、边和排序契约。
- 分片目录保存定位和构建状态。节点、跨度、符号和边界定位优先使用已校验的 routing index，而非重新扫描所有分片。
