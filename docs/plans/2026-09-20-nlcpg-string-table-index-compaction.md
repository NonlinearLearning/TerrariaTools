# NLCPG 字符串表与序号索引压缩实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将冻结 CPG 的节点文本字段从每节点字符串引用改为图拥有的字符串表 ID，并让 `NLCPGGraphIndex` 通过节点序号和 `NodeId` 建立查询索引，降低高密度图的 retained memory，同时保持图语义、查询结果、DOP 结果和现有持久化格式等价。

**Architecture:** 构图入口先产生只在物化阶段存在的文本草稿；`NLCPGGraph` 统一把草稿中的字符串写入自己的 `StringInterner`，最终只把 `uint` ID 写入 `NLCPGNode`。`DisplayKind` 不再存储，由 `Kind` 的稳定格式化函数推导；查询和展示通过 graph 解析 ID。冻结索引只保留一份按 ordinal 排序的 canonical node 数组，`NodesByKind` 和 `NodesByFilePath` 保存 ordinal 列表，公开查询通过轻量视图解析到 canonical node。

**Tech Stack:** C#/.NET 10、现有 NLCPG/NLISSN builder、xUnit Contract/Unit/Host tests、现有 `Run-TestTiers.ps1` 性能与诊断 harness。

---

## 范围与硬约束

- 允许破坏 `NLCPGNode`、`NLCPGEdge` 的公开 API；本计划会明确删除 `DisplayKind`、`Name`、`FullName`、`Signature`、`TypeFullName`、`FilePath` 字符串属性，并改用 ID 与 graph resolver。
- `NLCPGNode` 最终只保存 `NLCPGNodeKind`、字符串 ID、span、分派标签、隐式标记、`NodeId` 和 `StableAnchor` 等稳定值；文本草稿不得进入冻结图、索引、缓存或长期集合。
- `0` 是所有可选字符串 ID 的空值；`StringInterner` 从 `1` 开始分配，禁止把 `0` 解析为字符串。
- 一个 `NLCPGGraph` 拥有且只拥有一个字符串表。相同文本在同一图内必须共享 ID；不同图之间的 ID 不得被当作可比较的稳定身份。
- `DisplayKind` 必须由 `Kind` 推导出与当前输出相同的名称；不能在节点中再保留一份字符串副本。
- 第一阶段保持 shard/SQLite/JSON 的外部 schema 不变：写出时由 graph resolver 还原字符串，读入时在 graph 物化边界重新 intern。若后续要持久化字符串 ID，另立 schema 版本计划。
- 不把 `NLCPGNode` 随意装箱，也不把大节点值重新复制进多个数组；所有热路径继续使用泛型集合。
- 没有同一输入、同一 SDK/runtime、同一配置下的 class 基线和重复测量，不得声称内存下降或访问速度提升了固定比例。

## 当前事实与主要风险

- `src/NLCPG/Model/StringInterner.cs` 已存在，但目前没有接入 `NLCPGNode` 的字段物化。
- `StableNodeAnchor.FilePathId` 已经使用整数化路径身份，但节点本身仍保存 `FilePath` 字符串；需要统一两者的 ID 来源，避免一个图存在两套路径编号。
- `NLCPGGraphIndex` 当前保存 `OrderedNodes`，并在 `NodesByKind`、`NodesByFilePath` 中重复保存完整 `NLCPGNode` 值；改为 ordinal 后必须保持查询返回顺序和 `IReadOnlyList<NLCPGNode>` 的调用契约。
- 当前源码和测试大量直接访问 `node.Name`、`node.FullName`、`node.FilePath` 和 `node.DisplayKind`。迁移必须集中在 graph resolver 或查询接口，不能在各调用点各自访问字符串表。
- 值类型变小后通常减少集合载荷，但查询时解析字符串、索引视图和字典 hash 可能增加 CPU；必须测量构图、冻结、查询、持久化和规则阶段，而不是只测 `sizeof`。

## 目标数据模型

最终节点形状采用以下语义，具体字段顺序以实际 `Marshal.SizeOf`、对齐和基准结果调整：

```csharp
public readonly record struct NLCPGNode(
  NLCPGNodeKind Kind,
  uint NameId = 0,
  uint FullNameId = 0,
  uint SignatureId = 0,
  uint TypeFullNameId = 0,
  uint FilePathId = 0,
  int? SpanStart = null,
  int? SpanEnd = null,
  bool IsImplicit = false,
  NodeId? NodeId = null,
  StableNodeAnchor? StableAnchor = null,
  NLCPGDispatchKind? DispatchKind = null);
```

`DisplayKind`、`Name`、`FullName`、`Signature`、`TypeFullName` 和 `FilePath` 不再是节点字段。为保留构图输入的可读性，增加只存在于物化边界的 `NLCPGNodeDraft`：

```csharp
internal readonly record struct NLCPGNodeDraft(
  NLCPGNodeKind Kind,
  string? Name = null,
  string? FullName = null,
  string? Signature = null,
  NLCPGDispatchKind? DispatchKind = null,
  string? TypeFullName = null,
  string? FilePath = null,
  int? SpanStart = null,
  int? SpanEnd = null,
  bool IsImplicit = false);
```

草稿只能被 `NLCPGGraph` 的物化入口消费；它不能出现在 `Nodes`、`Edges`、`NLCPGGraphIndex`、streaming descriptor 或 persistence facts 中。若测试或外部调用必须创建节点，使用 graph 提供的受控工厂或 ID 构造，不直接伪造别的 graph 的字符串 ID。

## 实施任务

### Task 1: 建立失败的模型与字符串表契约

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGStringTableContractTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs`
- Test references: `src/NLCPG/Model/NLCPGNode.cs`, `src/NLCPG/Model/StringInterner.cs`

**Steps:**

1. 写失败测试，反射确认 `NLCPGNode` 不再暴露六个字符串字段/属性，暴露五个 ID，`DisplayKind` 由 `Kind` 解析。
2. 写失败测试，确认空字符串映射为 `0`、非空字符串 ID 从 `1` 开始、重复文本返回同一 ID、未知 ID 不会伪造字符串。
3. 写失败测试，确认同一 graph 中相同文本跨节点共享 ID，不同 graph 的同值文本不能直接作为稳定身份比较。
4. 串行运行定向测试：

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~NLCPGStringTableContractTests|FullyQualifiedName~NLCPGNodeIdContractTests"
```

Expected: 新测试因缺少 ID 字段、resolver 或字符串表接线而失败；现有测试失败必须记录，不得删除断言。

### Task 2: 收紧 StringInterner 与节点草稿边界

**Files:**
- Modify: `src/NLCPG/Model/StringInterner.cs`
- Create: `src/NLCPG/Model/NLCPGNodeDraft.cs`
- Modify: `src/NLCPG/Model/NLCPGNode.cs`
- Modify: `src/NLCPG/Model/StableNodeAnchor.cs`
- Modify: `src/NLCPG/Model/StableNodeIdentityFactory.cs`

**Steps:**

1. 为 `StringInterner` 增加 `TryIntern(string?)`、`TryResolve(uint, out string?)` 或等价的零值约定，明确 `0` 不进入反向表；保留序号分配的确定性。
2. 创建 `NLCPGNodeDraft`，迁移所有文本构造参数，但禁止它进入长期 graph collection。
3. 将 `NLCPGNode` 改为 ID 字段；删除 `DisplayKind` 和所有文本字段；保留 `Kind`、span、identity、dispatch 和 implicit 语义。
4. 让 stable anchor 从 graph 已解析/已 intern 的字段生成，确保 `FilePathId` 和 identity interner 的编号来源一致；不能因为 ID 重排改变同一输入的 NodeId。
5. 运行 Task 1 的定向测试并确认由 RED 进入 GREEN。

### Task 3: 让 NLCPGGraph 成为唯一字符串解析入口

**Files:**
- Modify: `src/NLCPG/Model/NLCPGGraph.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuildContext.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/Passes/SyntaxPass.cs`
- Modify: `src/NLCPG/Builder/Passes/OperationPass.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/NLCPG/Builder/Passes/MethodDecorationPass.cs`
- Modify: `src/NLCPG/Builder/Passes/MemberAccessPass.cs`
- Modify: `src/NLCPG/Builder/Passes/CallGraphPass.cs`
- Modify: `src/NLCPG/Builder/Passes/ControlFlowPass.cs`
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`
- Modify: `src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs`
- Modify: `src/NLCPG/Builder/Preallocation/CpgStableAnchorCollector.cs`

**Steps:**

1. 在 `NLCPGGraph` 增加 graph-owned string table，并提供内部 `MaterializeNode(NLCPGNodeDraft)`；该入口负责 intern、分配/验证 stable anchor 和现有 anchor 去重。
2. 提供集中解析方法，例如 `ResolveName(NLCPGNode)`, `ResolveFullName(NLCPGNode)`, `ResolveSignature(NLCPGNode)`, `ResolveTypeFullName(NLCPGNode)`, `ResolveFilePath(NLCPGNode)` 和 `ResolveDisplayKind(NLCPGNode)`；禁止消费者直接访问字符串表。
3. 重写 `GetDisplayText`、`DescribeNode`、源码 Span 回切和 `RegisterSource`，让路径查找以 `FilePathId` 为主，字符串只在外部路径进入 graph 时 intern 一次。
4. 将 builder/pass/streaming 的 `new NLCPGNode(...)` 改为 draft 物化；descriptor 只传 ID、anchor 和稳定 scalar，不保留文本草稿。
5. 把所有排序、筛选、稳定 key 和日志输出改成通过 graph resolver；保留相同 ordinal、字符串比较器和空值顺序。
6. 运行 NLCPG build、模型契约和 CPG display/partitioned builder 定向测试。

### Task 4: 迁移查询、验证、CLI 和 NLISSN 消费者

**Files:**
- Modify: `src/NLCPG/Analysis/CpgRelationQuery.cs`
- Modify: `src/NLCPG/Analysis/CpgRelationQueryService.cs`
- Modify: `src/NLCPG/Analysis/NLCPGSliceQuery.cs`
- Modify: `src/NLCPG/Cli/NLCPGCli.cs`
- Modify: `src/NLCPG/Validation/CpgGraphValidator.cs`
- Modify: `src/NLISSN.Core/Analysis/View/NLCPGStructureViewBuilder.cs`
- Modify: all current `src/NLISSN/**` consumers that read node text fields
- Test: `tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs`
- Test: `tests/NLISSN.UnitTests/Application/CpgRelationQueryUnitTests.cs`

**Steps:**

1. 将 `node.Name`、`node.FullName`、`node.Signature`、`node.TypeFullName`、`node.FilePath`、`node.DisplayKind` 的直接访问改为 graph resolver 或基于 ID 的比较。
2. 将 CLI 的 `--anchor-full-name`、`--anchor-name`、JSON 输出和排序改为 resolver 访问；用户可见输出必须与当前文本完全一致。
3. 将 relation query 的 `FilePath`、`SymbolKey` 和名称筛选在进入图时 intern，查询阶段比较 ID；需要支持跨 graph 查询时先确认 graph ownership，不直接比较异图 ID。
4. 更新 structure view、validator、evidence/decision 适配器；不要把解析后的字符串重新写回节点。
5. 运行 UnitTests 中结构视图、关系查询和 graph validator 的定向测试，确认查询顺序和空值行为不变。

### Task 5: 将 NLCPGGraphIndex 改为 canonical nodes + ordinal views

**Files:**
- Modify: `src/NLCPG/Model/NLCPGGraphIndex.cs`
- Modify: `src/NLCPG/Model/NLCPGGraph.cs`
- Create: `src/NLCPG/Model/OrdinalNodeList.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGSliceQueryTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgRelationQueryTests.cs`
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexStorageContractTests.cs`

**Steps:**

1. 保留一份按 `NodeId` 排序的 `NLCPGNode[] nodesByOrdinal` 和 `Dictionary<NodeId, int> nodeOrdinals`；所有 kind/path 索引只保存 `int` ordinal 数组。
2. 将 `NodesByKind` 改为 `Dictionary<NLCPGNodeKind, int[]>`，将 `NodesByFilePath` 改为 `Dictionary<uint, int[]>`；文件路径字符串只在输入 resolver 阶段转换为 `FilePathId`。
3. 实现 `OrdinalNodeList`，按 ordinal 延迟读取 canonical node，不在每次索引构建或查询时创建完整节点数组；保留稳定排序和 `IReadOnlyList<NLCPGNode>` 的读取行为。
4. 更新 `GetNodes`, `GetNodesInFileSpan`, `NodesByKind`, `TryGetNodesByFilePath` 和 local view 使用 ordinal；未知 ID、空路径和不存在 kind 必须返回现有空集合语义。
5. 增加存储契约测试：内部 kind/path buckets 的元素类型为 `int`，canonical node 只存在一份；测试查询结果与旧快照完全相同。
6. 评估 CSR edge table：本计划第一目标是节点索引去重；如果分配诊断显示 outgoing/incoming 的 `NLCPGEdge[]` 重复成为主要成本，单独把 CSR bucket 改为 edge ordinal 视图，避免把两个优化混成无法定位的改动。

### Task 6: 迁移持久化边界并保持 schema 兼容

**Files:**
- Modify: `src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs`
- Modify: `src/NLCPG/Persistence/CpgFrozenShardExporter.cs`
- Modify: `src/NLCPG/Persistence/CpgShardStore.cs`
- Modify: `src/NLCPG/Persistence/CpgShardContracts.cs`
- Modify: `src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs` only if ID lookup requires it
- Test: `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- Test: `tests/NLISSN.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

**Steps:**

1. 读取旧 shard 的字符串字段为 draft，交给 graph intern；不要把 shard 中的文本直接保存进 compact node。
2. 导出时通过 graph resolver 还原 `FilePath`、`DisplayKind`、`Name`、`FullName`、`Signature` 和 `TypeFullName`，保持当前字节格式、字段顺序和 hash 输入不变。
3. 验证旧 shard、当前 shard、空字段、重复字段和跨 shard edge 的 round-trip；若必须改变 schema，先停止并建立独立 schema/version 设计，不在本任务中隐式升级。
4. 运行 CPG persistence、routing、SQLite 和 reviewed snapshot Contract 测试，比较 NodeId、edge snapshot、catalog manifest 和 restore 结果。

### Task 7: 证明语义等价和 DOP 等价

**Files:**
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGExecutionMatrixTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgPropertyEquivalenceTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs` only for resolver-based assertions
- Add/update: `tests/NLISSN.ContractTests/Cpg/NLCPGStringTableContractTests.cs`

**Steps:**

1. 用相同源码输入比较 DOP `1/2/8/12/14/16` 的 node identity、node text projection、edge snapshot、local view、rule decision、rewrite source 和 diff。
2. 增加跨 graph、空 ID、未知 ID、同文本不同节点、同 path 多 span、没有 source registration 的 display fallback 负例。
3. 运行：

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: Contract、Unit 和相关 snapshot 全部通过；失败时先定位是 ID 物化、解析排序还是 index 视图问题，不用更新快照掩盖差异。

### Task 8: 运行小批量内存与访问性能对照

**Files:**
- Create or modify: `tests/NLISSN.PerformanceTests/Performance/NLCPGNodeStoragePerformanceTests.cs`
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs` only if the existing harness owns the fixture
- Create: `docs/benchmarks/2026-09-20-nlcpg-string-table-index-compaction.md`

**Steps:**

1. 固定同一份小型 C# 输入、同一 SDK/runtime、Release 配置和 DOP，构造三组：当前 string carrier、string-table carrier、string-table + ordinal index。
2. 对每组预热并重复测量构图、冻结、按 kind/path 查询、name/full-name 查询、local view、export/restore；记录 elapsed、allocated bytes、GC、峰值 working set/retained heap 和查询吞吐。
3. 使用现有 `PerformanceTests`/`Run-TestTiers.ps1 -Performance` 入口；只有仓库已存在并批准 BenchmarkDotNet 依赖时才增加 BenchmarkDotNet 微基准，不为了单次实验引入重复 harness。
4. 记录原始命令、运行时版本、输入规模、重复次数、平均值/中位数/离散度；分开报告首次运行和 warmed 样本。
5. 若 string table 降低字符串 retained heap 但使查询变慢，保留数据并按访问频率决定是否引入解析缓存；不得只优化平均耗时而破坏峰值内存或语义。

### Task 9: 文档、验证和交付收口

**Files:**
- Modify: `设计docs/目前设计/cpg-architecture.md`
- Modify: `src/NLCPG/docs/node-edge-catalog.md`
- Modify: `docs/research/2026-09-18-csharp-class-struct-experiment-research.md` only to link actual measurements
- Modify: this plan with actual verification evidence

**Steps:**

1. 在当前设计中记录 graph-owned string table、ID `0` 空值、resolver ownership、canonical node/ordinal index 和持久化边界。
2. 更新节点/边目录，明确文本属性已删除、展示需经过 graph resolver、ID 不可跨 graph 比较、草稿不是冻结领域载体。
3. 串行运行：

```powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Host
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Performance
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

4. 只把实际运行的结果写入计划和研究报告；保留 Host 已知 fixture 失败，不把未执行的 tier 写成通过。
5. 检查公开类型、持久化 schema、快照和未提交工作树边界；不回退用户已有改动，不提交 `.agent-workplace/`。

## 完成条件

- `NLCPGNode` 不再存储 `DisplayKind` 或任何节点文本字符串，只存 ID 和稳定 scalar。
- 同一 graph 内重复字符串只保留一份反向文本和多个紧凑 ID；空值统一为 ID `0`。
- 所有展示、排序、CLI 选择、验证、查询和 NLISSN 消费者通过 graph resolver 或 ID 查询，不直接依赖已删除属性。
- `NLCPGGraphIndex` 只保留一份 canonical node storage；kind/path 索引只保存 ordinal/ID，查询结果顺序和类型语义不变。
- 旧 shard 和当前 shard 可以按现有 schema 读写，NodeId、edge、snapshot、规则和 rewrite 结果等价。
- DOP 1/2/8/12/14/16 的图、查询、规则和改写等价测试有实际证据。
- 小批量基准同时给出 absolute metrics、relative change、环境、重复次数和未验证边界；没有测量就不声称收益。
- 当前设计、节点目录、研究报告和本计划彼此一致，harness consistency 与 `git diff --check` 通过。

## 当前执行证据

- 2026-09-21：`dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false`
  通过，0 warning、0 error。
- 2026-09-21：`dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false`
  通过，0 warning、0 error。
- 2026-09-21：`dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false`
  通过，0 warning、0 error。
- 2026-09-21：完整 UnitTests 通过 `117/117`；当前源文件集合的 ContractTests 通过
  `399/399`（由 Fast tier 执行）。此前直接增量 Contract 运行显示 `395/395`，但没有
  包含后来被 clean 编译发现的 4 个 work-batch contract 源文件。
- 2026-09-21：字符串表、节点身份、ordinal index、关系查询、slice 查询和分区构图定向
  Contract 通过 `96/96`。控制流局部 graph、dominance 和 control-dependence 的 descriptor
  已统一复用主 graph 的字符串表和稳定身份工厂；预分配 NodeId 模式复用主 graph 的分配表。
- 2026-09-21：PerformanceTests 小批量 `PerformanceSummaryHarnessTests` 通过 `2/2`，覆盖
  四组 directory/CPG DOP，每组一个 warmup 和三个 measurement；`Run-TestTiers.ps1
  -Performance` 最终为 `71` 通过、`4` 个失败、`0` 个跳过，共 `75`；其中 3 个失败来自
  未安装 `dotnet-trace`、`dotnet-gcdump`、`dotnet-counters`，另 1 个并发 worker 调度
  断言单独重跑通过 `1/1`。未过滤外部测试的直接 PerformanceTests 仍为 `71` 通过、
  `7` 个环境型失败、`1` 个跳过，共 `79`，额外失败来自缺少外部 Terraria fixture；
  新增的两个存储测量均通过。
- 2026-09-21：完整 HostTests 为 `647/655`；8 条失败属于既有配置根目录、规则语义/fixture
  边界，未作为本次字符串表优化的通过证据。
- 小批量 carrier 测量已记录在
  `docs/benchmarks/2026-09-20-nlcpg-string-table-index-compaction.md`。该测量不是
  BenchmarkDotNet，也不证明 NLCPG retained heap 或端到端速度收益。
- 2026-09-21：新增的
  `NLCPGNodeStoragePerformanceTests` 定向测量通过 `2/2`，命令为
  `dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~NLCPGNodeStoragePerformanceTests" --logger "console;verbosity=detailed"`。
  环境为 `.NET 10.0.11` / SDK `10.0.400`、Debug、DOP=1、2 次 warmup 和 5 次
  measurement。固定源码得到 `1424` 节点和 `4481` 条边；graph 中位数为构图
  `176132 us`、查询/文本投影 `2473 us`、export/restore `58283 us`、总分配
  `45167288 B`、采样后 GC heap `31165936 B`、峰值 working-set 增量
  `116498432 B`，GC 为 Gen0/1/2=`4/3/2`。原始样本和恢复一致性断言已写入
  benchmark 文档。
- 同一轮 carrier 原始样本的中位数为：class `1097 us` / `3618472 B`，含字符串
  引用的 struct `1114 us` / `2406400 B`，字符串 ID struct `1163 us` /
  `1206464 B`。相对 class，分配量分别低约 `33.8%` 和 `66.8%`，耗时分别高约
  `4.8%` 和 `9.4%`；本轮未观察到 carrier 额外 GC。由于没有旧 class NLCPG
  pipeline 基线，以上不能证明 graph 端到端内存下降或访问提速。
- 2026-09-21：首次 harness smoke 发现当前工作树 `CallGraphPass.cs` 的条件表达式混用
  `IMethodSymbol[]` 与 `List<IMethodSymbol>`（`CS0173`）；将属性访问器候选结果统一为数组
  后，`dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false`
  通过，0 warning、0 error，随后 `check-harness-consistency.ps1` 通过。该修复只统一
  容器类型，不改变调用图语义。
- 2026-09-21：clean Fast 进一步发现 CallGraph WorkBatch worker 并发写入普通
  `_baseTypeCache` 导致 streaming persistence 回归中的 `NullReferenceException`；缓存已
  改为 `ConcurrentDictionary<string, IReadOnlyList<INamedTypeSymbol>>`，并将
  `ControlFlowPass`、`ControlDependencePass` 及 lifecycle contract 的 `ExecuteAsync`
  调用显式指定为 `LocalCpgFragment`。回归用例通过 `1/1`，最终 `Run-TestTiers.ps1
  -Fast` 通过 Unit=`117/117`、Contract=`399/399`。
- 2026-09-21：`Run-TestTiers.ps1 -Fast` 通过，Unit=`117/117`、Contract=`399/399`；
  `Run-TestTiers.ps1 -Host` 为 `647/655`，8 个失败仍是配置根目录、规则语义或 fixture
  边界，未作为本次字符串表优化的通过证据；`Run-TestTiers.ps1 -Performance` 为
  `71/75` 通过，3 个缺失外部诊断工具的失败和 1 个已单独复跑通过的 worker 调度失败
  已单独记录。

## 建议的提交边界

执行时按以下可审查批次拆分；当前仅保存计划，不创建提交：

1. 模型契约、字符串表和 `NLCPGNodeDraft`。
2. graph materialization 与所有消费者迁移。
3. canonical node/ordinal index。
4. persistence compatibility 与语义回归。
5. 性能对照、文档和验证证据。

每个批次完成后先运行其 owning project 的最小测试，再进入下一批次；任何快照、ID 或 schema 差异都应在进入下一批次前停止并处理。
