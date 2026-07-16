# `NodeId` 全量迁移并删除 legacy string-id 兼容层执行提案

**Goal:** 在现有 `NodeId`、`StableNodeAnchor` 和 `DeterministicNodeIdTable` 已落地的基础上，完成一轮新的全量迁移：公共图模型、CLI、Decision、`RuleContext`、测试和文档统一切到 `NodeId` 主契约，并删除 legacy string-id 兼容层，而不是只停止扩大它的使用面。

**Architecture:** `RoslynCpgNode.Id`、`RoslynCpgEdge.SourceId/TargetId`、`DecisionModel.SyntaxBindings<string, SyntaxNode>`、`RuleContext` 的 string-id 图查询接口、CLI 的 `--anchor-id` 路径，全部退出运行时主契约。图运行时、规则绑定、局部视图、切片查询、决策片段关系和测试快照统一以 `NodeId` 连接。若仍需人类可读 identity，只在导出/日志层按 `StableNodeAnchor` 或节点展示字段恢复，不再保留“string id -> node”的兼容查询表。

**Tech Stack:** .NET 10、现有 Roslyn、BCL 集合；不引入新的 NuGet 依赖，不改变现有规则算法，不改 DOP 策略。

---

## 0. 边界、前提与非目标

### 前提

- 当前工作树已经具备：
  - `NodeId`
  - `StableNodeAnchor`
  - `DeterministicNodeIdTable`
  - DOP `1/8/12/14/16` 的 `legacy Id -> NodeId` 稳定性回归
- 本提案建立在 [2026-07-16-uint-nodeid-stable-anchor-deterministic-allocation-proposal.md](./2026-07-16-uint-nodeid-stable-anchor-deterministic-allocation-proposal.md) 已完成 Phase 0-4 大部分落地的事实上。

### 必须保持

- 同一源码在 DOP `1/8/12/14/16` 下图结构、规则结果和 rewrite 输出等价。
- `NodeId` 在重复构图和不同 DOP 下不漂移。
- worker 仍只读 Roslyn semantic facts；图物化和最终编号仍由稳定调用线程控制。
- CLI、局部视图、切片查询、决策绑定和 rewrite 结果不能因为删除兼容层而改变语义。

### 非目标

- 不要求本轮删除所有字符串字段，例如 `FullName`、`Signature`、`DisplayKind`。
- 不要求本轮改变规则算法、DataFlow 算法或 DOP 默认值。
- 不把 `NodeId` 重新换成 `int`、`long` 或 hash key。
- 不把“删除兼容层”与“内存一定下降”绑定成同一个完成条件；内存数据要记录，但是否下降要以实测为准。

---

## 1. 当前遗留问题

当前代码已经把不少内部连接迁到 `NodeId`，但 legacy string-id 兼容层仍然是真实运行路径的一部分：

- `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
  - `_nodes: Dictionary<string, RoslynCpgNode>`
  - `_nodeIdsByLegacyId`
  - `_legacyIdsByNodeId`
  - `GetNode(string)`
  - `GetOutgoingEdges(string)`
  - `GetIncomingEdges(string)`
  - `ExtractLocalView(string, ...)`
- `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`
  - `SourceId`
  - `TargetId`
- `src/MinimalRoslynCpg/Cli/MinimalRoslynCpgCli.cs`
  - `--anchor-id`
  - JSON/summary 里直接输出 `Id` / `SourceId` / `TargetId`
- `src/RoslynPrototype/Decision/DecisionModel.cs`
  - `SyntaxBindings: IReadOnlyDictionary<string, SyntaxNode>`
  - 决策关系去重仍按 `SourceId|TargetId|Kind|Label`
- `src/RoslynPrototype/RuleServices/RuleContext.cs`
  - `QuerySliceBackward(string sinkNodeId, ...)`
  - `GetGraphEdgesByKind(string sourceId, ...)`
  - `FindGraphNodeById(string nodeId)`
- 大量测试仍直接用 `node.Id`、`edge.SourceId`、`edge.TargetId`

结论：当前状态不是“兼容层只剩展示用途”，而是“兼容层仍参与模型、查询、规则和测试契约”。因此必须按完整迁移来做，不能再当 Phase 5 验收尾项处理。

---

## 2. 目标契约

迁移完成后，契约应收敛为：

- `RoslynCpgNode`
  - `NodeId` 是唯一图身份
  - 不再暴露 `Id` 作为图连接键
- `RoslynCpgEdge`
  - 只保留 `NodeId SourceNodeId` / `NodeId TargetNodeId`
  - 不再保留 `SourceId` / `TargetId`
- `RoslynCpgGraph`
  - 节点主存储键为 `NodeId`
  - 不再保留 legacy lookup 表
  - 不再公开 string-id 查询重载
- `DecisionUnit.SyntaxBindings`
  - 改为 `IReadOnlyDictionary<NodeId, SyntaxNode>`
- `RuleContext`
  - 所有图查询接口改为 `NodeId`
- CLI
  - anchor 主路径为 `NodeId`
  - 人类可读输出通过展示字段或 formatter 生成，但不再接受 legacy string-id 作为定位键
- 测试
  - 结构和等价断言全部切到 `NodeId`
  - 只在专门的展示测试里断言可读输出，不再把 legacy string-id 当运行时事实

---

## 3. 总体策略

采用“先锁行为，再切公共模型，再切上层绑定，最后删兼容层”的顺序。

实施规则：

1. 先把测试断言从 legacy id 依赖改成 `NodeId` 或稳定图快照辅助函数。
2. 再修改公共模型，避免一开始就让所有调用点同时失效。
3. 所有 string-id API 改签名时，同批修完全部调用方，不保留新的双轨过渡层。
4. 删除兼容层的提交里，不再新增任何 string-id fallback。

---

## 4. 分阶段执行

## Phase 0：冻结删除兼容层前的回归面

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgNodeIdContractTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/DecisionStructureValidationTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgDisplayTextTests.cs`

1. 给当前 `NodeId` 主路径补齐断言，不再让核心回归依赖 `Id` / `SourceId` / `TargetId`。
2. 为 CLI、slice、structure view、decision syntax binding 建立 `NodeId` 断言入口。
3. 保留少量“展示输出可读”的测试，但它们只能断言展示文本，不再断言 legacy string-id 是公共契约。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~MinimalRoslynCpgDisplayTextTests"
```

**Done when:**

- 核心测试不再依赖 legacy string-id 作为主断言键。

**Risks:**

- 如果测试基线没先锁住，后续删兼容层时很难判断是行为变了还是断言口径变了。

---

## Phase 1：公共图模型彻底切到 `NodeId`

**Files:**
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgNode.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgLocalView.cs`

1. `RoslynCpgEdge` 删除 `SourceId` / `TargetId`。
2. `RoslynCpgGraph` 删除：
   - `_nodes: Dictionary<string, ...>` 主存储
   - `_nodeIdsByLegacyId`
   - `_legacyIdsByNodeId`
   - 全部 string-id 查询重载
3. 节点主存储改为 `Dictionary<NodeId, RoslynCpgNode>`。
4. 若 `RoslynCpgNode.Id` 仍保留，降级为非身份展示字段；若没有现实展示需求，直接删除。
5. `RoslynCpgLocalView` 和索引只暴露 `NodeId` 连接。

**Verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgNodeIdContractTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests"
```

**Done when:**

- 公共图模型不再保留 legacy string-id lookup 和 edge endpoint 字段。

**Risks:**

- 影响面最大的是所有依赖 `edge.SourceId/TargetId` 的排序、去重和测试辅助函数。

---

## Phase 2：Builder、Slice、CFG/DataFlow 内部辅助状态切到 `NodeId`

**Files:**
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/CallGraphPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DominancePass.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`

1. `_cfgPredecessorsByNodeId` / `_cfgSuccessorsByNodeId` 这类仍用 `string` 的辅助表改成 `NodeId`。
2. `DataFlowPass` 的 `NodesById`、worklist key、definition key 里与图节点相关的部分切到 `NodeId`。
3. `SliceQuery` 对外签名改为 `QueryBackward(NodeId sinkNodeId, ...)`。
4. 内部路径、去重、排序和 query key 全部只用 `NodeId`。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~PerformanceOptimizationRegressionTests"
```

**Done when:**

- Builder 和 slice 内部不再把图节点当 string key。

**Risks:**

- DataFlow 里的“定义事实 identity”不能误改成 `NodeId`。只有图节点连接键要改，语义事实键仍保持原意。

---

## Phase 3：Decision、RuleContext、StructureView 全量切到 `NodeId`

**Files:**
- Modify: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Modify: `src/RoslynPrototype/Decision/DecisionModel.cs`
- Modify: `src/RoslynPrototype/RuleServices/RuleContext.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `src/Rules/Implementations/Propose/*.cs`
- Modify: `src/Rules/Implementations/Propagate/*.cs`
- Modify: `src/Rules/Implementations/Lift/*.cs`

1. `DecisionUnit.SyntaxBindings` 改为 `IReadOnlyDictionary<NodeId, SyntaxNode>`。
2. `DecisionCpgFactory.CreateSyntaxBindings(...)` 和所有 proposal rule 一起改。
3. 决策片段关系去重键改为 `SourceNodeId|TargetNodeId|Kind|Label`。
4. `RuleContext` 删除：
   - `QuerySliceBackward(string, ...)`
   - `GetGraphEdgesByKind(string, ...)`
   - `FindGraphNodeById(string)`
5. `MarkAnalysisSnapshot` 改为缓存 `NodeId` sink key。
6. `StructureViewBuilder` 的排序与连通路径恢复只保留 `NodeId`。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests"
```

**Done when:**

- 规则运行时和决策模型不再暴露 string-id 图绑定接口。

**Risks:**

- Proposal rules 数量多，最容易漏掉 `CreateSyntaxBindings` 和 `fragment.Id` 的旧写法。

---

## Phase 4：CLI、导出与用户可见契约收口

**Files:**
- Modify: `src/MinimalRoslynCpg/Cli/MinimalRoslynCpgCli.cs`
- Modify: `src/MinimalRoslynCpg/Program.cs`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`

1. 删除 `--anchor-id`，改成 `--anchor-node-id`。
2. CLI 查 anchor 时直接按 `NodeId`、`FullName` 或 `Name` 定位。
3. JSON 输出里明确区分：
   - `nodeId`
   - 展示字段，如 `displayText`、`fullName`
4. 帮助文本、quick start、developer guide 同步更新。

**Verification:**

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj -- --help
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MinimalRoslynCpgDisplayTextTests"
```

**Done when:**

- 用户可见接口不再宣传或接受 legacy string-id。

**Risks:**

- CLI 契约变化会影响现有命令示例和测试基线，文档必须同批更新。

---

## Phase 5：删除兼容层、全量验证与实测记录

**Files:**
- Modify: `progress.md`
- Modify: `feature_list.json`
- Modify: `docs/plans/2026-07-16-uint-nodeid-stable-anchor-deterministic-allocation-proposal.md`

1. 删除所有剩余 legacy string-id 兼容代码。
2. 回读旧提案，明确它的“兼容查询保留”结论已经被新提案替代。
3. 运行全量测试和真实源码 dry-run。
4. 在 `progress.md` 和 `feature_list.json` 记录：
   - 已删除兼容层
   - 验证命令
   - 真实源码 wall-clock / memory 数据
   - 仍存在的风险

**Verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build
pwsh -File .\scripts\check-harness-consistency.ps1
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff --runtime-metrics-log .\Build\nodeid-full-runtime-<stamp>.jsonl --per-file-memory-diagnostics-log .\Build\nodeid-full-memory-<stamp>.jsonl --per-file-phase-timing-log-directory .\Build\nodeid-full-phases-<stamp>
```

**Done when:**

- 代码中不再存在 legacy string-id 兼容查询层。
- 公共模型、CLI、Decision、RuleContext 和测试都只以 `NodeId` 连接。
- 全量测试通过，真实源码 dry-run 成功。

**Risks:**

- 如果真实源码 dry-run 暴露新的内存峰值或性能回退，这不自动否定迁移正确性，但必须在 handoff 中如实记录。

---

## 5. 实施顺序建议

建议按以下提交顺序拆小：

1. 测试基线和辅助函数先切到 `NodeId`
2. `RoslynCpgEdge` / `RoslynCpgGraph` 公共模型收口
3. Builder / Slice / DataFlow 内部键迁移
4. Decision / RuleContext / StructureView / rules 批量迁移
5. CLI、文档、进度文件收尾

不要把所有层面塞进一个超大 patch。每一批都要能独立编译并跑过对应 focused tests。

---

## 6. 验证矩阵

### 公共模型

- `RoslynCpgNodeIdContractTests`
- `MinimalRoslynCpgPartitionedBuilderTests`

### 查询与局部视图

- `RoslynCpgSliceQueryTests`
- `StructureViewBuilderTests`
- `MinimalRoslynCpgDisplayTextTests`

### 规则与决策

- `DecisionStructureValidationTests`
- `PipelineComponentTests`

### 全量回归

- `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build`

### 真实源码

- Terraria `PlayerInput` dry-run

---

## 7. 主要风险与缓解

### 风险 1：误把展示字段也一并删掉，导致调试和 JSON 输出不可读

缓解：

- 区分“删除 identity 兼容查询”与“保留可读展示字段”
- JSON/CLI 统一输出 `nodeId + displayText/fullName`

### 风险 2：Proposal / Decision 侧漏改

缓解：

- 先统一改 `DecisionUnit.SyntaxBindings`
- 再批量修 `CreateSyntaxBindings(...)` 调用点
- 用编译错误驱动剩余迁移

### 风险 3：测试仍暗含 string-id 排序

缓解：

- 所有排序改成 `NodeId`
- snapshot helper 统一收口，不允许测试直接拼 `SourceId|TargetId`

### 风险 4：Builder 内部辅助状态仍残留 string key

缓解：

- 单独设一阶段检查 `_cfg*ByNodeId`、`NodesById`、slice state、query key 和 DataFlow candidate key

---

## 8. 完成条件

- `RoslynCpgNode.Id` 不再是图身份契约；若保留，也只是展示字段。
- `RoslynCpgEdge.SourceId/TargetId` 已删除。
- `RoslynCpgGraph` 不再提供 string-id 查询重载和 legacy lookup 表。
- `DecisionModel.SyntaxBindings`、`RuleContext`、CLI、测试全部改为 `NodeId`。
- 全量测试通过，真实源码 dry-run 成功。
- 文档和 handoff 明确说明 legacy string-id 兼容层已删除。

---

## 9. 建议决策

建议把这项工作作为**新特性/新子提案**执行，不要继续挂在原提案的 Phase 5 下伪装成“验收收尾”。

原因很简单：

- 它会改公共模型
- 会改 CLI 契约
- 会改 RuleContext 和 Decision 契约
- 会改大量测试

这已经是一次完整迁移，不是兼容层小清理。
