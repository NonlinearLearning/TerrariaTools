# `uint NodeId` + 稳定锚点 Struct + 确定性分配表实施提案

**Goal:** 将当前 `MinimalRoslynCpg` 的 `string Id` 主连接体系重构为 `uint NodeId` 主键、`StableNodeAnchor` 稳定身份层和 `DeterministicNodeIdTable` 确定性分配层，在不改变分析语义、节点/边拓扑、图查询结果和规则结果的前提下，显著降低主图、边表和冻结索引中的字符串常驻内存与 string-key 索引开销。

**Architecture:** 图内所有节点、边、邻接、查询缓存和结构视图连接键统一使用 `uint NodeId`。节点真实稳定身份不再编码进一整条 `string Id`，而是通过 `StableNodeAnchor` 表达。构图阶段先收集稳定锚点，再由 `DeterministicNodeIdTable` 按固定排序规则分配 `NodeId`，确保同一输入源码在重复构图、不同 DOP、不同分片调度下稳定节点 `NodeId` 不漂移。`FilePath`、`FullName`、`Signature`、`SymbolIdentity` 等文本通过本地 interner 压缩成小整数 side tables，仅在调试、CLI、导出时按需恢复可读 identity。

**Tech Stack:** .NET 10、现有 Roslyn、BCL 集合和 `ArrayPool<T>`；不引入新的 NuGet 依赖，不采用直接哈希成 `uint` 的主键策略，不使用全局 `String.Intern` 作为主图 identity 方案。

---

## 0. 边界、不可变契约与非目标

### 必须保持

- 同一源码在 DOP `1/8/12/14/16` 下节点属性、边集合、稳定排序、图查询和删除规则结果等价。
- worker 继续只读 Roslyn semantic facts；全局节点编号、去重和最终图物化仍由稳定调用线程控制。
- `RoslynCpgGraph` 冻结后不可变；`GetOutgoingEdges`、`GetIncomingEdges`、`GetEdges`、`GetNodes`、局部视图、切片查询和 CLI 输出语义不变。
- 稳定节点在重复构图后 `NodeId` 不漂移。

### 非目标

- 本轮不改变规则算法，不降低 DOP，不删减 CFG/DataFlow/CallTarget 等图语义。
- 本轮不承诺删掉所有字符串字段；目标是去掉主图运行时对 `string Id` 的依赖。
- 本轮不接受“把现有 `string Id` 直接哈希成 `uint`”的方案。
- 本轮不引入磁盘持久化图数据库、外部 FlatGraph 库或新的序列化格式。

---

## 1. 当前问题与替换对象

当前内存热点不只在 `RoslynCpgNode.Id`，而在整套 string-key 图结构：

- `RoslynCpgNode.Id`
- `RoslynCpgEdge.SourceId` / `TargetId`
- `_nodes: Dictionary<string, RoslynCpgNode>`
- `RoslynCpgGraphIndex` 内多份 `Dictionary<string, ...>` / `HashSet<string>`
- `RoslynCpgSliceQuery`、`RoslynCpgStructureViewBuilder`、`DecisionModel.SyntaxBindings` 等上层对 `string nodeId` 的传播

当前 `Id` 语义也不统一：

- 稳定锚点型：
  - `syntax:{file}:{kind}:{start}:{end}`
  - `token:{file}:{kind}:{start}:{end}`
  - `method:{SymbolId(...)}`
- 顺序号型：
  - `op:{sequence}:...`
  - `ref:{sequence}:...`
  - `typeref:{sequence}:...`
  - `callsite:{sequence}:...`
  - `memberaccess:{sequence}:...`

实施顺序必须是：

1. 统一稳定身份模型
2. 替换图内主键为 `uint`
3. 替换边端点与冻结索引
4. 迁移上层绑定与查询缓存

---

## 2. 核心设计

### 2.1 图内主键

```csharp
public readonly record struct NodeId(uint Value);
```

用途：

- 代替 `RoslynCpgNode.Id`
- 代替 `RoslynCpgEdge.SourceId` / `TargetId`
- 代替所有邻接、查询缓存、结构视图、BFS、切片路径中的 `string nodeId`

### 2.2 稳定锚点 struct

建议主结构：

```csharp
public readonly record struct StableNodeAnchor(
    RoslynCpgNodeKind Kind,
    uint FilePathId,
    int SpanStart,
    int SpanEnd,
    StableNodeRole Role,
    int Ordinal,
    uint ExtraKeyId);
```

字段含义：

- `Kind`: 节点种类
- `FilePathId`: 文件路径 interner 结果
- `SpanStart` / `SpanEnd`: 稳定源码锚点
- `Role`: 区分同一 span 下不同语义节点
- `Ordinal`: 同一锚点下稳定子序号
- `ExtraKeyId`: 需要补充 symbol/fullName/signature 时的压缩键

### 2.3 字符串 side tables

引入本地 interner：

- `FilePath -> uint FilePathId`
- `FullName -> uint FullNameId`
- `Signature -> uint SignatureId`
- `SymbolIdentity -> uint SymbolIdentityId`

说明：

- 节点上不再重复常驻完整字符串
- 可读 identity 通过 formatter 按需恢复
- `DisplayText`、CLI 展示、JSON 导出走 side tables 和源码切片恢复

### 2.4 确定性分配表

```csharp
public sealed class DeterministicNodeIdTable
{
    private readonly Dictionary<StableNodeAnchor, NodeId> _ids;
}
```

分配规则：

1. 收集本轮构图需要物化的全部稳定锚点
2. 使用固定比较器排序
3. 按排序结果分配 `NodeId`
4. 后续节点、边、索引只引用 `NodeId`

固定比较器顺序建议：

1. `Kind`
2. `FilePathId`
3. `SpanStart`
4. `SpanEnd`
5. `Role`
6. `Ordinal`
7. `ExtraKeyId`

禁止：

- 边遍历边用自增号发号
- 让 DOP 完成顺序影响 `NodeId`
- 用 32 位哈希直接代替稳定身份

---

## 3. 各类节点的稳定锚点规则

### 3.1 直接用文件 span 即可稳定的节点

- `SyntaxNode`
- `SyntaxToken`

锚点：

- `Kind + FilePathId + SpanStart + SpanEnd`

### 3.2 以 span 为主，还需 role/ordinal 区分的节点

- `Operation`
- `Reference`
- `TypeRef`
- `CallSite`
- `MemberAccess`

锚点：

- `Kind + FilePathId + SpanStart + SpanEnd + Role + Ordinal`

要求：

- 消灭 `sequence` 型 identity
- `Ordinal` 必须由固定规则导出，而不是由运行时创建顺序导出
- 同一宿主 span 下多节点场景必须先排序再编号

### 3.3 以 symbol identity 为主的节点

- `Symbol*`
- `Method`
- `MethodParameter`
- `MethodReturn`
- `MethodEntry`
- `MethodExit`
- `TypeDecl`

锚点：

- `Kind + SymbolIdentityId + Role + Ordinal`

说明：

- `SymbolIdentityId` 由本地 interner 分配
- 不再把完整 symbol 字符串常驻在 `Id`

---

## 4. 分阶段执行

## Phase 0：冻结契约与回归基线

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Create: `tests/RoslynDeletionPrototype.Tests/RoslynCpgNodeIdContractTests.cs`

1. 枚举所有 `Id` 生成点与消费点，分成稳定锚点型和顺序号型。
2. 为图快照、结构视图、切片查询、Decision syntax bindings 建立回归。
3. 增加重复构图稳定性测试：同一输入重复构图三次，记录现有节点集合和边集合。
4. 锁定 DOP `1/8/12/14/16` 完整图等价。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
```

**Done when:**

- 所有 `Id` 生成/消费点有清单
- 图等价、结构视图、切片查询、Decision 绑定基线锁定

**Risks:**

- 如果顺序号型节点未完整识别，后续稳定性测试会失真

---

## Phase 1：引入 `NodeId`、锚点和 interner 骨架，但不切主路径

**Files:**
- Create: `src/MinimalRoslynCpg/Model/NodeId.cs`
- Create: `src/MinimalRoslynCpg/Model/StableNodeAnchor.cs`
- Create: `src/MinimalRoslynCpg/Model/StableNodeRole.cs`
- Create: `src/MinimalRoslynCpg/Model/DeterministicNodeIdTable.cs`
- Create: `src/MinimalRoslynCpg/Model/StringInterner.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgNode.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`

1. 新增 `NodeId`、锚点 struct、本地 interner、确定性分配表骨架。
2. `RoslynCpgNode` 增加 `NodeId` 与锚点字段；保留旧 `string Id` 作兼容层。
3. `RoslynCpgEdge` 增加 `NodeId SourceNodeId/TargetNodeId`；保留旧 `SourceId/TargetId` 作兼容层。
4. 为 `FilePath`、`FullName`、`Signature`、`SymbolIdentity` 建立 side tables。

**Verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests"
```

**Done when:**

- 新类型与兼容层编译通过
- 不改变现有运行路径输出

**Risks:**

- 双轨阶段字段重复，短期内内存可能先升后降

---

## Phase 2：统一稳定锚点规则，清除 sequence identity

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/CallGraphPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/MemberAccessPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/MethodDecorationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/SyntaxPass.cs`

1. 为 `Operation`、`Reference`、`TypeRef`、`CallSite`、`MemberAccess` 定义 `Role + Ordinal` 规则。
2. 去除 `op:{sequence}`、`ref:{sequence}`、`typeref:{sequence}`、`callsite:{sequence}`、`memberaccess:{sequence}` 作为主身份来源。
3. `Method*`、`Symbol*`、`TypeDecl` 改为 `SymbolIdentityId` 驱动。
4. 增加重复构图稳定性测试，验证稳定锚点集合一致。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
```

**Done when:**

- 所有节点类型都能导出稳定锚点
- sequence 不再决定节点身份

**Risks:**

- `Role/Ordinal` 规则定义不完整会导致同 span 冲突或构图漂移

---

## Phase 3：主图、边表和冻结索引整数化

**Files:**
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgLocalView.cs`

1. `_nodes` 改为 `Dictionary<NodeId, RoslynCpgNode>`。
2. `RoslynCpgEdge.SourceId/TargetId` 主路径切换到 `NodeId`。
3. 冻结索引全部改整数键：
   - `OutgoingByNodeId`
   - `IncomingByNodeId`
   - `(NodeId, EdgeKind)` 索引
4. `GraphSnapshotVersion` 改为基于稳定锚点和整数边端点计算，而不是基于 legacy string id。
5. `RoslynCpgSliceQuery` 的状态、缓存 key、路径结果内部全部改用 `NodeId`。

**Verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
```

**Done when:**

- 主图和冻结索引不再依赖 `string` 作为主连接键
- 同输入重复构图 `NodeId` 稳定

**Risks:**

- `GraphSnapshotVersion` 数值会变化；必须只要求新体系下稳定，不要求与旧值字面相等

---

## Phase 4：迁移结构视图、Decision 和上层绑定

**Files:**
- Modify: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Modify: `src/RoslynPrototype/Decision/DecisionModel.cs`
- Modify: `src/RoslynPrototype/Decision/DeleteDecisionFactory.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/DecisionStructureValidationTests.cs`

1. `StructureViewBuilder` 的 node selection、BFS、neighbor 表全部改为 `NodeId`。
2. `DecisionModel.SyntaxBindings` 从 `Dictionary<string, SyntaxNode>` 迁移到 `Dictionary<NodeId, SyntaxNode>`。
3. `PipelineComponentTests` 中的 view node set、`PrimaryGraphNode.Id` 断言切到 `NodeId`。
4. 对外调试和测试辅助层保留 legacy identity formatter。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~DecisionStructureValidationTests"
```

**Done when:**

- 结构视图、Decision、RuleContext 不再把 `string Id` 当主绑定键
- 上层回归通过

**Risks:**

- 这是跨项目影响面最大的一步，必须在底层整数化稳定后再做

---

## Phase 5：收尾、兼容裁剪与内存验收

**Files:**
- Modify: `src/MinimalRoslynCpg/Cli/MinimalRoslynCpgCli.cs`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`
- Modify: `progress.md`
- Modify: `feature_list.json`

1. CLI anchor 支持 `NodeId` 主路径；legacy string identity 仅用于兼容查询和诊断输出。
2. 移除主图内部对 `string Id` 的硬依赖；保留按需 formatter。
3. 对比旧实现与新实现的内存和 wall-clock：
   - `managedHeapBytes`
   - `privateBytes`
   - 图节点数、边数
   - `FreezeQueryIndex` 耗时
4. 记录实测结果并更新文档与进度文件。

**Verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
```

如需真实源测量，额外执行：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- <input-path> --target-name <name> --max-degree-of-parallelism 32
```

**Done when:**

- 主路径不再使用 `string Id`
- 内存下降证据齐全
- 规则输出与图查询回归通过

**Risks:**

- 文档和 CLI 兼容层保留时间过长，会让双体系长期共存

---

## 5. 后续说明

本提案当时建议在迁移期间保留一层 legacy identity 兼容查询。

该结论已被 `docs/plans/2026-07-16-nodeid-full-legacy-removal-proposal.md` 取代，并已在 2026-07-16 的后续实现中执行完成：

- 公共图查询和边端点契约已统一收口到 `NodeId`
- CLI 已改为 `--anchor-node-id`
- 不再保留 `TryFindLegacyIdentity(string legacyId)` / `FormatLegacyIdentity(NodeId id)` 这类兼容查询接口

保留 `RoslynCpgNode.Id` 的唯一目的，是为构图阶段和调试输出提供可读 identity；它不再是冻结图、规则运行时或局部视图的连接契约。

---

## 6. 验证矩阵

### 功能一致性

- `MinimalRoslynCpgPartitionedBuilderTests`
- `RoslynCpgSliceQueryTests`
- `StructureViewBuilderTests`
- `PipelineComponentTests`
- `DecisionStructureValidationTests`

### 稳定性

- 同输入重复构图 3 次，`NodeId` 一致
- DOP `1/8/12/14/16` 下稳定节点 `NodeId` 一致

### 内存

- 节点总数、边总数不变
- `managedHeapBytes` 下降
- `privateBytes` 下降
- 冻结索引内存下降

### 性能

- 构图总耗时不明显恶化
- `FreezeQueryIndex` 不明显恶化
- `SliceQuery`、`StructureViewBuilder` 热路径不出现显著回退

---

## 7. 主要风险与缓解

### 风险 1：稳定锚点规则不完整

后果：

- 同一逻辑节点无法稳定映射
- 重复构图 `NodeId` 漂移

缓解：

- Phase 2 单独完成并锁回归
- 为每个节点类型建立身份规则表

### 风险 2：顺序号语义残留

后果：

- 名义上整数化，实际上仍依赖构图顺序

缓解：

- 在 Phase 2 清空 sequence identity 再推进

### 风险 3：上层 string-key 迁移不彻底

后果：

- 主图内存降了，但结构视图、Decision、查询缓存仍被 string 集合拖住

缓解：

- Phase 4 把上层绑定一并迁移

### 风险 4：兼容层滞留太久

后果：

- 双体系长期共存，复杂度回升

缓解：

- Phase 5 明确 legacy identity 只保留调试路径

---

## 8. 完成条件

- `NodeId` 取代主图、边表、冻结索引中的 `string Id` 主连接体系。
- 所有稳定节点在重复构图、不同 DOP 下 `NodeId` 不漂移。
- 结构视图、切片查询、Decision 绑定和规则结果保持不变。
- 内存下降证据齐全，至少覆盖主图、边、冻结索引三部分。
- legacy string identity 降级为兼容/调试能力，不再是主运行路径。

---

## 9. 建议决策

建议按两段批准：

1. 先批准 `Phase 0-2`
   - 先统一稳定身份模型
   - 先清掉 sequence identity
2. 再批准 `Phase 3-5`
   - 再做真正的整数化和上层迁移

原因：

- 这个问题的核心难点不是 `uint` 本身
- 而是“在不漂移前提下重建图身份系统”

一句话总结：

这不是字段类型替换，而是图身份系统重构。正确顺序必须是：

1. 稳定锚点
2. 确定性发号
3. 边与索引整数化
4. 上层绑定迁移
5. 兼容层收尾
