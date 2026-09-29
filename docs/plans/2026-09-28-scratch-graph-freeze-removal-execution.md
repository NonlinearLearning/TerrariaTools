# scratch 图不再冻结查询索引（执行计划）

**Goal:** 让 `ControlFlowPass` / `DominancePass` / `ControlDependencePass` 的 worker 局部图（scratch 图）**不再调用 `FreezeQueryIndex()`**，改为经一条**具名只读取数通道**枚举 pending 边产出 `LocalCpgFragment`，从而消掉"每个方法建一次完整查询索引"的全部成本，同时保持 fragment **逐元素相同**。

**Architecture:** scratch 图是 L1 计算层的**纯中间产物**，唯一出口是 `LocalCpgFragment`（节点描述符 + 边候选）。查询索引（确定性 `NodeId`、CSR 邻接、按种类分桶、快照指纹）对这条出口**无用**——因为 `NodeId` 是在 **L2 归并层按锚点重新分配**的（§1.3 给出决定性证据）。故正确形态是给 scratch 图一条只读取数通道，而不是"建完整索引再拆掉"。

**Tech Stack:** C#/.NET 10、`NLCPGGraph`、`PendingEdgeBuffer`、`CpgNodeDescriptor`、`CpgEdgeCandidate`、`CpgFragmentReducer`。

日期：2026-09-28。本轮**只写文档**，不改产品代码、不跑构建与测试。

> **与 [2026-09-27 提案](2026-09-27-scratch-graph-freeze-elimination-execution.md) 的关系**
> 本文件是那份提案的**取代版**。提案的 §0 结论（"不直击 55.1%"）**仍然成立且已复核**，
> 但其**定位判断**已被 2026-09-28 的 `8f3c25e` 完整 diff 审读**改写**：
> 提案把本项当作"需要先测量、否则可能关闭"的**可疑项**；实际它是该提交
> **分层重构的确定副作用（回归）**。故 T1 的**性质**从"go/no-go 判据"改为"**定级判据**"（§4）。
> 提案的 §3.4 `ContextId` 守卫分析**经独立复核成立**，已保留并加强（§3.5、T3）。

---

## 0. 结论先行

| 问题 | 结论 | 依据 |
| --- | --- | --- |
| 本项是否直击 55.1%？ | **否**。55.1% 是**每文件持久图**的冻结，不在本项范围 | §0.1 |
| scratch 冻结是"有意引入的成本"吗？ | **不是**，是分层重构的副作用（**回归**） | §0.2 |
| 冻结在 scratch 图上有用吗？ | **四处工作全部为空转**，其中 `NodeId` 是纯往返 | §1 |
| 改动是"删一行"吗？ | **不是**。删掉冻结会让 `Edges` **静默变空** ⇒ 空 fragment | §3.2 |
| 会改变输出吗？ | **不会**。reducer 与 ControlFlow 发布器**都主动重排**节点与边 | §3.4 |
| 量级已知吗？ | **未知**。scratch 图逐方法 ⇒ 单图小、但方法数多；**必须测** | §4 T1 |

---

### 0.1 本项不直击 55.1%（复核通过）

55.1% 出自阶段表 `CPG.FreezeQueryIndex` 行，唯一产生点是**每文件持久图**：
`NLCPGBuilder.cs:1467` 的 `MeasureStage("FreezeQueryIndex", () => { ...; context.Graph.FreezeQueryIndex(); })`
（该处现经 `context.GraphRegistry` 分流，但仍在**持久图**侧）。
scratch 图的三处冻结在 worker 回调内，随 `ControlFlowPass` / `DominancePass` /
`ControlDependencePass` 的阶段计时走，**不在**该 `MeasureStage` 内。

研究报告（[2026-09-23](../research/2026-09-23-freezequeryindex-single-thread-tail-research.md)）测于
**2026-09-23**，而 scratch 图由 `8f3c25e`（**2026-09-27**）引入 ⇒ **55.1% 描述的是一个当时还不存在的代码路径**。

### 0.2 定位已改写：这是**回归**，不是"待议优化"

`8f3c25e`（"perf: 内存与 CPU 性能优化"，384 文件 / +83,437）对三个 pass 的改动**全部**是
**G0-P R-3 分层重构 + D1 按文件路由**。因果链（逐项已被 diff 坐实）：

1. 重构把"直接写 `context.Graph`"改为"`Plan*Stage` 只读规划 / `Commit*Stage` 唯一写图"；
2. 为此引入 `CpgWorkBatch` + `LocalCpgFragment` + `CpgFragmentReducer`；
3. 产 fragment 需要 `CpgNodeDescriptor[]` + `CpgEdgeCandidate[]`；
4. 从**局部图**取这两样，**唯一现成的通道**就是 `Nodes` / `Edges` 两个属性；
5. 而**同一提交**恰好把 `Edges` 从"字段直读"改成"**必须冻结才有值**"：

| 提交 | `NLCPGGraph.Edges` | 冻结前可用？ |
| --- | --- | --- |
| `1e9486f`（父） | `public IReadOnlyCollection<NLCPGEdge> Edges => _edges;` | ✅ 可用 |
| `8f3c25e` | `_queryIndex is null ? Array.Empty<NLCPGEdge>() : _queryIndex.InsertionOrderedEdges` | ❌ **恒为空** |

6. 于是第 4 步"顺手"调了 `FreezeQueryIndex()`。

**独立佐证**：父提交的三个 pass **一次都没有**冻结，也没有 `localGraph`——
`ControlFlowPass` 当时是 `var graph = context.Graph;`，`DominancePass` 传 `context.Graph`，
`ControlDependencePass` 传 `context` 并在内部 `context.Graph.AddEdge(...)`。

> ⚠ **同一提交里 `EnumeratePendingEdgesLazily()` 被新增**（`git log -S` 首个命中即 `8f3c25e`），
> 即"正确的取数通道"与"错误的冻结调用"**同批出现**，前者被用在了 `RunInterproceduralDataFlowPass`，
> **没有**用在这三处。这降低了本项的实施成本（通道现成、已在生产路径上验证），
> 但**不得**表述为"通道本来就有"。

---

## 1. 冻结在 scratch 图上做了什么，以及为什么全部为空转

`FreezeQueryIndex()`（[`NLCPGGraph.cs:595-609`](../../src/NLCPG/Model/NLCPGGraph.cs)）
= `AssignDeterministicNodeIds()` + `NLCPGGraphIndex.Create()` + `ReclaimConstructionCapacity()`。

| # | 步骤 | 位置 | scratch 需要？ | 依据 |
| --- | --- | --- | --- | --- |
| 1 | `GetStableAnchor` 逐节点求锚点 | `:925-931` | ❌ **空转** | §1.1 |
| 2 | `DeterministicNodeIdTable.Create` 分配 `NodeId` | `:932` | ❌ **纯往返** | §1.3 |
| 3 | `frozenNodes[]` 全量节点副本（104 B/节点） | `:947-963` | ❌ 不需要 | |
| 4 | `RemapEdgesOrdinal` 投影 `NLCPGEdge[]`（72 B/边） | `:982`/`:991-1020` | ❌ 不需要 | |
| 5 | 清空 `_mutableNodesByAnchor` / `_nodesByOrdinal` | `:984-985` | ❌ 不需要 | |
| 6 | 节点按 `NodeId` 排序 + `nodeOrdinals` 字典 | `NLCPGGraphIndex.cs:449-538`/`:548-552` | ❌ 不需要 | |
| 7 | `BuildMetadataRanks` + 4 趟 `CountingSortPass` 基数排序 | `NLCPGGraphIndex.cs:581`/`:618-627` | ❌ 不需要 | |
| 8 | `BuildOffsets` / `BuildOrdinalsWithOffsets` 建 CSR 邻接 | `NLCPGGraphIndex.cs:671`/`:682` | ❌ 不需要 | |
| 9 | `CreateSnapshotVersion`：对全部节点与边做 SHA-256 | `NLCPGGraphIndex.cs:665`/`:1380` | ❌ 不需要 | |
| 10 | `ReclaimConstructionCapacity` 释放 pending 缓冲 | `:1052`（经 `:608`） | ❌ **反效果风险** | §3.3 |

三处出口只读 `Nodes` / `Edges`，**不调任何查询 API**（不用 `GetOutgoingEdges`、
不用 `GetNodes(kind)`、不读 `GraphSnapshotVersion`）⇒ 第 6–9 项**一个消费者都没有**。

### 1.1 第 1 项是空转（提前返回）

`GetStableAnchor` 在**两处**被调用，第三参数不同：

| 位置 | 形态 |
| --- | --- |
| `:867`（`MaterializeCompatibilityIdentity`，`AddNode` 时） | `GetStableAnchor(node, _stringInterner, stableIdentityText)` ← **带** |
| `:928`（`AssignDeterministicNodeIds`，冻结时） | `GetStableAnchor(node, _stringInterner)` ← **不带** |

看似风险，实为**提前返回**（[`StableNodeIdentityFactory.cs:23-26`](../../src/NLCPG/Model/StableNodeIdentityFactory.cs)）：

```csharp
if (node.StableAnchor is { } existing)
{
    return existing;          // ★ stableIdentityText 被完全忽略
}
```

而 `AddNode` 时锚点**已经设上**（`NLCPGGraph.cs:274-275`）⇒ 冻结期每个节点都在走这条提前返回
⇒ **第 1 项对 scratch 图是纯空转**。该结论独立于本项成立。

### 1.2 第 3–9 项无消费者

`Edges` / `Nodes` 两个属性是三处唯一读取点。索引的 CSR、分桶、`NodeOrdinals` 字典、
`SnapshotVersion` 在 scratch 图上**没有任何读取者**（已逐点核验三处调用点全文）。

> 旁证：`SnapshotVersion` 的成本在本仓**已被单独测量过**——即使经过缓冲化改造，
> 单文件 NPC.cs 仍需约 **7.2 s CPU**（`NLCPGGraphIndex.cs:411` 注释）。
> 正因如此，持久图侧也已把它改为**惰性**（`:405-415`，"并非所有调用方都读快照版本"）。
> scratch 图**从不读**它。

### 1.3 第 2 项是纯往返（**决定性证据**）

`CpgNodeDescriptor.FromNode`（[`CpgNodeDescriptor.cs:20-34`](../../src/NLCPG/Builder/Streaming/CpgNodeDescriptor.cs)）
**不读 `NodeId`**——它读的是 `StableAnchor` + `Kind` + 5 个 `uint` id + `DispatchKind` + 2 个 `Span` + `IsImplicit`。

scratch 图上 `NodeId` 的**唯一**用途，是在 `nodesById` 字典里把边的端点映射回节点
（`ControlFlowPass.cs:140-142` 等三处），**然后立刻换成 `StableAnchor`**：

```csharp
var nodesById = localGraph.Nodes.Where(n => n.NodeId.HasValue).ToDictionary(n => n.NodeId!.Value);
var edgeCandidates = localGraph.Edges.Select(edge => {
    var source = nodesById[edge.SourceNodeId];      // ← NodeId 进来
    var target = nodesById[edge.TargetNodeId];
    return new CpgEdgeCandidate(source.StableAnchor!.Value, ...);   // ← 锚点出去
});
```

**决定性佐证（已读代码）**：L2 归并层**按锚点重新分配 `NodeId`**——
[`CpgFragmentReducer.cs:121-123`](../../src/NLCPG/Builder/Concurrency/CpgFragmentReducer.cs)：

```csharp
var allocation = graph.HasPreallocatedNodeIds
  ? graph.RequirePreallocatedNodeIds()
  : DeterministicNodeIdTable.Create(materializedDescriptors.Keys);   // ← 键是 StableNodeAnchor
```

⇒ **持久图的 `NodeId` 完全由锚点派生，与 scratch 图的 `NodeId` 无关。**
scratch 图那份 `NodeId` 是"造出来 → 查一次字典 → 扔掉"的**纯往返**。

> 既有契约也已声明这一点：
> `LocalCpgFragmentContractTests.Fragment_StoresStableDescriptorsWithoutFinalNodeIds`
> —— 片段按设计**不携带最终 `NodeId`**。

---

## 2. 目标形态

pending 边**自带两端真节点**，而这些节点在 `AddNode` 时**已带稳定锚点**
（[`NLCPGGraph.cs:1288-1301`](../../src/NLCPG/Model/NLCPGGraph.cs)）：

```csharp
foreach (var key in _keys)
{
    var metadata = _metadataById[key.MetadataId];
    yield return new PendingEdge(
      nodesByOrdinal[key.SourceOrdinal],     // ← 真节点，带 StableAnchor
      nodesByOrdinal[key.TargetOrdinal],
      key.Kind, metadata?.StructuredLabel, metadata?.ContextId, metadata?.CallSiteContext);
}
```

于是三处可退化为（**无 `nodesById`、无冻结**）：

```csharp
var nodeDescriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
var edgeCandidates = localGraph.EnumeratePendingEdgesLazily()      // 或 T2 的具名入口
  .Select(edge => new CpgEdgeCandidate(
    edge.SourceNode.StableAnchor!.Value,
    edge.TargetNode.StableAnchor!.Value,
    edge.Kind, edge.StructuredLabel, edge.ContextId, edge.CallSiteContext))
  .ToArray();
```

`Nodes` 在**未冻结**时返回 `_nodesByOrdinal`（`:165-166`），枚举序与冻结后的
`_queryIndex.InputOrderedNodes` 相同（§3.1）。`nodesById` 与 `FreezeQueryIndex()` 一并消失。

---

## 3. 语义不变量（逐条给源码依据）

### 3.1 节点枚举序不变

- 冻结路径：`InputOrderedNodes` 的序即 `Create` 的输入序（`NLCPGGraphIndex.cs:357-359`），
  输入是 `frozenNodes`，而 `frozenNodes[ordinal] = anchoredNodes[ordinal]`（`:947-963`），
  `anchoredNodes` 来自 `_nodesByOrdinal.Select(...)`（`:925-931`）。
- 未冻结路径：`Nodes => _nodesByOrdinal`（`:165-166`）。

⇒ 同一 `_nodesByOrdinal` 序，逐元素相同。

### 3.2 ⚠ 删掉冻结**不是**充分改动（静默失效形态）

`Edges`（`:175-176`）冻结前**恒返回 `Array.Empty<NLCPGEdge>()`**。因此
**"只删掉 `FreezeQueryIndex()` 这一行"是最危险的改法**：不抛异常、不报错，
**静默产出空边集** ⇒ 整个阶段的 CFG / 支配 / 控制依赖事实**全部丢失**。

⇒ 必须**换数据源**（§2），而不是换调用时机。

另外 `GetOutgoingEdges` 等查询 API 在未冻结时走 `RequireQueryIndex()` 抛异常
（`:822-825`），这是**良性的** fail-closed；`Edges` 的静默空集才是真正的坑。

### 3.3 ⚠ 先枚举、后（可选）冻结：`_released` 是硬约束

`FreezeQueryIndex()` 末尾经 `ReclaimConstructionCapacity()`（`:1040-1053`）调
`_pendingEdges.Release()`，而 `Release()`（`:1185-1200`）置 `_released = true`，
其后任何读取经 `ThrowIfReleased()`（`:1202-1209`）**抛异常**：

> `"The pending-edge buffer was released at freeze time and can no longer be read."`

⇒ 本项的取数**必须**发生在冻结**之前**。实施后三处**不再冻结**，故天然满足；
但**不得**写成"先冻结、再枚举 pending 边"。

### 3.4 输出等价：下游**主动重排**，不继承 fragment 序

这是本项风险评级下调的**关键发现**（相较 09-27 提案的"边序存疑"）：

| 消费方 | 节点排序 | 边排序 |
| --- | --- | --- |
| `CpgFragmentReducer.ReduceInto`（`Dominance` / `ControlDependence` 走这条） | **16 级** `OrderBy/ThenBy`（`:93-111`；键序重排 `:125-133`） | **15 级**（`:143-157`） |
| `ControlFlowPass.PublishControlFlowFragmentsIntoGraph`（唯一不走 reducer） | `GroupBy(Anchor)` + `OrderBy(Kind).ThenBy(NameId).First()`（`:200-207`） | **9 级**（`:215-225`） |

⇒ **fragment 内的节点序与边序对最终图没有影响**。故本项**不改变任何输出**。

> 独立佐证：`CpgFragmentReducerContractTests.ReduceInto_IsIndependentOfFragmentArrivalOrder_AndDeduplicatesStableFacts`
> —— "归并结果与 fragment 到达次序无关"**已是既有契约且有测试**。

### 3.5 `ContextId`：原始值与已解析值在此**断言为 null**（本项唯一有语义风险的点）

两条路径取 `ContextId` 的来源**不同**：

- 冻结路径经 `EnumerateOrdinalsLazily` 取**已解析**值：`_resolvedContextIdById[MetadataId]`（`:1325`），
  解析规则 `callSiteContext?.ToContextId() ?? contextId`（`:1238`）。
- pending 路径经 `EnumerateLazily` 取**原始**值：`metadata?.ContextId`（`:1298`）。

`PendingEdgeBuffer` 的注释（`:1142-1151`）明确说明该差别是**刻意保留**的。
对 scratch 图，**两条路径当前都产出 `null`**，逐点核验四类发射点：

| 发射点 | 位置 | 元数据 |
| --- | --- | --- |
| `AddControlFlowEdge` → `graph.AddEdge(source, target, edgeKind)` | `NLCPGBuilder.cs:3745-3747`（`graph.AddEdge` 在 `:3747`，3 参） | 全 null |
| `AddControlDependenceEdges` → `graph.AddEdge(controlNode, dependentNode, ControlDependence)` | `ControlDependencePass.cs:273`（3 参） | 全 null |
| `AddPostDominanceEdges` → `graph.AddEdge(sourceNode, targetNode, PostDominates)` | `DominancePass.cs:863`（3 参） | 全 null |
| `AddOverlayEdges` → `AddKnownNodeCartesianEdges` | `DominancePass.cs:847` → `NLCPGGraph.cs:596`（`:611-617` 显式 `null, null, null`） | 全 null |

> ⚠ 上表行号于 **2026-09-28 14:25 复核**（`AddOverlayEdges` 调用在 `DominancePass.cs:847`、
> `AddControlDependenceEdges` 在 `:273`）。行号随后续提交漂移，**以符号名为准**。

三者全 null 时 `InternMetadata` 直接 `return 0`（`:1224-1227`）⇒ `metadata` 为 null、
`MetadataId = 0`，而 `_resolvedContextIdById[0]` 也是 null 哨兵（`:1152`）。

⇒ **两条路径逐字段相同，包括 `ContextId = null`。**

⚠ **必须加守卫**：若将来有人给 scratch 边挂上 `CallSiteContext`，冻结路径给出**非 null 已解析值**、
pending 路径给出 **null**，**不抛异常、不报错**——静默差异。收口见 T3。

### 3.6 其余机制不受影响

- `EnsureMutable` 的只读窗口 / worker 计算窗口守卫按**实例计数**，scratch 图深度天然为 0（`:1114-1117`）。
- `AddControlFlowEdge` 的 CFG 邻接缓存按 `_activeBuildGraphs.Contains(graph)` 判定，**与是否冻结无关**
  （`NLCPGBuilder.cs` 内 `AddControlFlowEdge`）。scratch 图不在该集合中 ⇒ 不缓存，行为不变。
- `public bool HasQueryIndex => _queryIndex is not null;`（`:212`）为 `false`
  不影响上述任一分支。

---

## 4. 执行步骤

### T1：先给 scratch 冻结**定级**（决定优先级；不可省略）

**性质已变**：定位已确定为**回归**（§0.2），故 T1 **不再**用来回答"该不该修"，
而是回答"**排在多前面**"。仍需保留"测出来可忽略 ⇒ 关闭本项"的出口，因为
scratch 冻结是**逐方法**的，单图规模远小于持久图。

- [ ] 取代表性输入（建议与研究报告同尺度，或至少含 1 个巨文件），跑一次基线。
- [ ] 读阶段表 `ControlFlowPass` / `DominancePass` / `ControlDependencePass` 三项
      （`NLCPGBuildMetrics.PassElapsedMilliseconds` → `CpgPerformanceFactMapper` → `Performance/summary.json`
      的 `items[].cpg.passSamples[]`，或 `stages[]` 按 `itemId` 归因）。
- [ ] ⚠ **这三项含大量非冻结工作**（CFG 构造、支配求解等），**不能**把阶段耗时当作冻结成本。
      必须补**临时探针**：在 `FreezeQueryIndex()` 外层按"是否 scratch 图"分桶计时与计分配。
- [ ] 报三个量：**墙钟**、**累计分配**、**`SnapshotVersion` 是否被触发**。
- [ ] 若 scratch 冻结占比可忽略（例如 <2%）⇒ **记录结论并关闭本项**，转去做持久图那条线
      （见 [persistent-freezequeryindex-dominant-term](2026-09-27-persistent-freezequeryindex-dominant-term-execution.md)）。
- [ ] 探针只用于测量，**不进最终 diff**。

**口径纪律**：本机墙钟噪声底约 ±3.5%（`BASELINE.md §5.4` 记录同二进制两次可差 16%–71%）
⇒ 低噪声判据用**累计分配字节**，且**同进程交错 A/B**。不得用跨运行墙钟相减。

**测量落点参考**（用于估算规模，非承诺）：
`frozenNodes[]` 104 B/节点、`RemapEdgesOrdinal` 72 B/边、节点排序 4 B/节点、
`NodeOrdinals` 字典、CSR 的多个 `int[边数]`、以及 SHA-256 的字符串构造。
逐方法求和 = Σ(每方法 scratch 规模) × 上述单位成本。

### T2：选定取数通道（A 具名入口 / B 复用既有）

**倾向 A**，理由是**收口 §3.5 的守卫**：

```csharp
// A：具名 scratch 取数入口，入口内校验"未冻结"与"元数据全 null"
internal IEnumerable<PendingEdge> EnumerateScratchEdges()
{
    if (_queryIndex is not null)
        throw new InvalidOperationException("scratch 取数要求图未冻结；冻结后 pending 缓冲已释放。");
    // §3.5 守卫：scratch 边不得携带元数据，否则 ContextId 原始/已解析语义分叉
    ...
    return _pendingEdges.EnumerateLazily(_nodesByOrdinal);
}
```

- [ ] 选 **A** 或 **B**（B = 三处直接用既有 `EnumeratePendingEdgesLazily()`，不新增 API）。
- [ ] 若选 B，§3.5 的守卫必须另找落点（三处调用点各断言），否则该风险**无收口**。
- [ ] ⚠ **不要用 `Materialize`**：它分配整份 `PendingEdge[]`（272 B/元素）；
      研究报告记录过该形态在 71 s 内分配 **641.4 MiB**。用 `EnumerateLazily` 直接投影。
- [ ] 若选 A，守卫的**失败路径**要有测试（见 T4）。
- [ ] 注意 `EnumerateLazily` 的**析构前提**（`:186-187`）：枚举期间**不得改图**
      （`_keys` 是 `HashSet`，枚举中修改抛 `InvalidOperationException`）。

### T3：改造三处调用点

对 `ControlFlowPass.cs:136-156`、`DominancePass.cs:560-578`、`ControlDependencePass.cs:210-228`：

- [ ] 删除 `localGraph.FreezeQueryIndex();`
- [ ] 删除 `nodesById` 字典（三处唯一用途是把边端点映射回节点，现在端点就在 `PendingEdge` 里）
- [ ] 边投影改为读 `PendingEdge.SourceNode` / `.TargetNode` 的 `StableAnchor`
- [ ] `nodeDescriptors` 构造方式**逐字保留**（`localGraph.Nodes.Select(CpgNodeDescriptor.FromNode)`）
- [ ] **顺序约束**：取数必须在任何（可选的）冻结之前（§3.3）
- [ ] 复核三处是否还有对"冻结态"的隐含依赖（本轮审读**未发现**）

### T4：等价性验证

- [ ] **核心断言**：同一输入，改动前后 `LocalCpgFragment` 的 `Descriptors` / `EdgeCandidates`
      **逐元素相同**（含顺序）。这是本项的主判据。
- [ ] 覆盖：空图、单方法、含泛型/接口分派的方法、多文件批次（跨文件分组）、巨文件中的大方法。
- [ ] 断言 scratch 边**元数据全为 null**（锁住 §3.5）。
- [ ] 断言 `FreezeQueryIndex` 在 scratch 图路径上**不再被调用**（用反射/计数钩子；
      `HLCPGGraph.HasQueryIndex` 可作观测面）。
- [ ] **变异验证**（本仓纪律）：把取数通道改回"冻结后读 `Edges`"或让它返回空集，
      新断言**必须失败**；否则护栏是装饰性的。
- [ ] 复用现有判据：`CpgWorkBatchLocalPassTests.BatchedLocalPasses_PreserveCfgAndMemberFacts`、
      `.BatchedDominanceBarrier_PreservesDominanceAndControlDependenceFacts`、
      `CrossFileBatchEquivalenceTests`、`DominancePassContractTests`、
      `LocalCpgFragmentContractTests`、`CpgFragmentReducerContractTests`。

**测试可见性**：`src/NLCPG/Properties/AssemblyInfo.cs` 已对
`RoslynDeletionPrototype.ContractTests` 与 `DataFlowTailMeasurement` 开放 `internal`
⇒ 若选 T2-A，具名入口**可直接被 Contract 测试驱动**，无需新增 `InternalsVisibleTo`。

### T5：登记与收尾

- [ ] 本计划**尚未登记**进 [`Context/feature_list.json`](../../Context/feature_list.json)；
      实施前须先登记状态与完成条件（该文件是 feature 状态的**唯一**来源）。
- [ ] 按 [`docs/contributing.md`](../contributing.md) 与
      [Harness 验证矩阵](../harness-verification-matrix.md) 执行匹配的构建与测试层级，
      如实记录**实际执行**的命令与**未验证边界**。
- [ ] 实施后在本文件就地补记实际改动、实测数字与未验证项（本仓惯例：执行报告回写进计划）。

---

## 5. 风险

| # | 风险 | 形态 | 收口 |
| --- | --- | --- | --- |
| R1 | **静默空边集** | 只删 `FreezeQueryIndex()` ⇒ `Edges` 返回空，**不报错** | §3.2；T3 换数据源；T4 逐元素断言 |
| R2 | **`ContextId` 原始/已解析分叉** | 将来给 scratch 边挂元数据 ⇒ 静默 null vs 非 null | §3.5；T2 守卫 + T4 元数据全 null 断言 |
| R3 | **撞 `_released` 异常** | 先冻结再枚举 pending 边 | §3.3 顺序约束 |
| R4 | **枚举期改图** | `_keys` 是 `HashSet`，枚举中 `AddEdge` 抛异常 | T2 析构前提 |
| R5 | **内存不降反升** | 误用 `Materialize` 分配整份 `PendingEdge[]` | T2 要求 `EnumerateLazily` |
| R6 | 误以为在修 55.1% | 收益归因错误 ⇒ 砍错优化 | §0.1 已纠正 |
| R7 | 收益可忽略 | scratch 冻结本来就便宜（逐方法、单图小） | T1 保留"关闭本项"出口 |
| R8 | 混淆两处冻结 | 改动波及持久图冻结 ⇒ 影响 `GraphSnapshotVersion` | 本项**只**动三个 pass，**不碰** `MeasureStage("FreezeQueryIndex")` |

---

## 6. 预期收益（**未实测，不给具体数字**）

单次 scratch 冻结省下：确定性 `NodeId` 分配、`frozenNodes[]`（104 B/节点）、
`RemapEdgesOrdinal`（72 B/边）、基数排序的多个 `int[边数]`、CSR 邻接与分桶、
以及**对全部节点与边的 SHA-256**。代价变为**一次 `_keys` 枚举**。

**具体倍数必须由 T1 实测**，本文不承诺。

⚠ **同时声明不降什么**：本项**不**降低**常驻峰值**——scratch 图与它的索引本来就随即变垃圾；
它降的是**累计分配与 CPU**。不得把"省了索引"写成"峰值下降"。

---

## 7. 不采用项

| 方案 | 不采用理由 |
| --- | --- |
| 保留冻结，只优化 `NLCPGGraphIndex.Create` 内部 | 对 scratch 图整条索引都是**无用产物**；优化无用产物不如不建 |
| 给 `FreezeQueryIndex` 加 `bool scratch` 参数分叉 | 同一方法承载两种语义；scratch 分支会绕过 `ReclaimConstructionCapacity` 等收尾，易腐化 |
| 靠"忘了调用 freeze"隐式实现 | `Edges` 在可变态**静默返回空集**（§3.2），是静默失效形态 |
| 改为在 `AddEdge` 时立刻产出 `CpgEdgeCandidate` | 会改变 pending 缓冲的去重语义（`HashSet<PendingEdgeKey>`，`:1153`/`:1179`），可能改变边集 |
| 顺带删掉 `ControlFlowPass` 发布器的 9 级排序 | 它是**输出等价性的来源之一**（§3.4），不是冗余；删除属独立语义改动 |

---

## 8. 行号基线

`src/NLCPG/Builder/NLCPGBuilder.cs` 当前 **4,647 行**；`NLCPGGraph.cs` **1,366 行**；
`NLCPGGraphIndex.cs` **1,535 行**。工作树含大量未提交改动，**行号会漂移**——
引用一律**同时给符号名**，冲突时**以符号为准**。

三处调用点（当前工作树）：`ControlFlowPass.cs:136`、`DominancePass.cs:560`、
`ControlDependencePass.cs:210`。

**已核实的关键锚点**（2026-09-28 工作树，符号优先）：
`NLCPGGraph.cs` — `Nodes:184`、`Edges:194`、`EnumeratePendingEdgesLazily:198`、
`EnumerateScratchEdges:217`、`HasQueryIndex`（`_queryIndex is not null`）、
`AddKnownNodeCartesianEdges:596`、`FreezeQueryIndex:675`、
`AssignDeterministicNodeIds`、`ReclaimConstructionCapacity`、`Release`、`ThrowIfReleased`、
`PendingEdgeBuffer.EnumerateScratchEdges:1397`、`EnumerateScratchEdgesCore:1403`。
`NLCPGBuilder.cs` — `MeasureStage("FreezeQueryIndex"):1467`、`AddControlFlowEdge:3745`。

> ⚠ **行号会随并发编辑漂移**：本轮实测 `NLCPGGraph.cs` 在工作期间被另一方从 1,466 行改到 1,489 行
> （对方另加了 `_redundantMergeSkipCount` 与 `CanonicalNodes`，与本项访问器互不冲突），
> 上述锚点已按 14:25 的状态复核。**引用冲突时一律以符号名为准。**

---

## 9. 执行报告（2026-09-28）

按用户指示执行本计划：**跳过全部前置验证（T1 测量整段不执行），只做 10% 核心验证**。

### 9.1 实际改动

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Model/NLCPGGraph.cs` | 新增 `EnumerateScratchEdges()`（图级，含未冻结守卫）+ `PendingEdgeBuffer.EnumerateScratchEdges(IReadOnlyList<NLCPGNode>)` 与 `EnumerateScratchEdgesCore`（含 `ThrowIfReleased()` 前置 + `CallSiteContext` 守卫）。净 +127/−1 |
| `src/NLCPG/Builder/Passes/ControlFlowPass.cs` | 删 `FreezeQueryIndex()` 与 `nodesById`，边改走 `EnumerateScratchEdges()`。+32/−? |
| `src/NLCPG/Builder/Passes/DominancePass.cs` | 同上 |
| `src/NLCPG/Builder/Passes/ControlDependencePass.cs` | 同上 |

三处 pass 文件合计 **+36 / −51**。`NLCPG` 构建 **0 error 0 warning**。

**与 §4 T2 的偏差（一处收窄，是改进）**：T2 原写守卫判据为"元数据全 null"。
实施时收缩为**只拒 `CallSiteContext`**：重读解析规则
`callSiteContext?.ToContextId() ?? contextId` 后确认，**只有 `CallSiteContext` 非空时两路才分叉**；
仅有显式 `ContextId` 时 `resolved == 原始值`，两路一致。
故按原判据会把"其实不发散"的合法形状一并误拒。已加 `EnumerateScratchEdges_WithExplicitContextIdOnly_IsAccepted`
锁住这一收窄。

### 9.2 验证证据

- **新增 6 条护栏测试** `tests/NLISSN.ContractTests/Cpg/ScratchGraphEdgeAccessContractTests.cs`：**6/6 通过**。
  覆盖：scratch 边 ≡ 冻结 `Edges` 路径（逐字段逐序）、scratch 节点 ≡ 冻结 `Nodes` 路径、
  生产形态元数据全 null、冻结后抛错、带 `CallSiteContext` 抛错、仅带 `ContextId` 被接受。
- **变异验证（两处，均被杀死）**：
  - 变异 1：未冻结时**静默返回空集**（正是本设计要防的失效形态）⇒ **4/6 失败**。
  - 变异 2：**只关掉 `CallSiteContext` 守卫** ⇒ **恰好 1 条失败**（该守卫自己的用例），其余 5 条不受影响。
  - 两次均已恢复为**逐字节一致**（SHA-256 `AA1512EE5DF1B3011DAC788FFDF36184FB8BA40F33B1DD959AAB6122442EDCBF`）。
- **聚焦回归 69/69 通过**：`CpgWorkBatchLocalPassTests`、`LocalCpgFragmentContractTests`、
  `CpgFragmentReducerContractTests`、`CrossFileBatchEquivalenceTests`、`DominancePassContractTests`、
  `ControlDependence*`、`PendingEdgeOrdinalizationDeterminismTests`。
- **全 Contract 套件 980 通过 / 2 失败**。两个失败**均为既有、已记载**，非本次引入：
  `CpgInterproceduralEdgeOrderTests`（`156:167` vs `167:178`，见 `Context/progress.md:34`）
  与 `LayoutArchitectureTests.ProductionProjectReferences`。

### 9.3 未验证边界（如实声明）

- **T1 整体未执行** ⇒ **本项收益量级完全未知**。§6 已声明不给数字，此处重申：
  不能据此说"省了多少"，只能说"这条路径上的无用工作已被移除"。
- **未做端到端内存/耗时对比**（无 A/B），故**不支持任何性能结论**。
- **未跑真实 967 文件语料**。
- §0.1 的"本项不直击 55.1%"仍是**基于代码阅读的推断**，未用测量复核。

### 9.4 本轮踩到的两个流程陷阱（供后来者）

1. **变异后恢复文件必须 touch/重建**。`Copy-Item` 恢复会**保留原 mtime**，
   若原 mtime 早于变异构建产物，MSBuild 判定"已最新"而**跳过重编译**，
   于是 `dotnet test --no-build` **静默测试了仍含变异代码的旧程序集**——
   我曾据此把一次"仍失败"误读为真实失败。判据：比较源文件与产物的 mtime，或直接强制重建。
2. **探针编码**：.NET 程序集元数据字符串是 **UTF-8**，用 `Encoding.Unicode` 读会恒为 `False`，
   包括本就存在的符号名。故"在 dll 里搜中文串"这类探针本身不可靠，**以测试为准**。

### 9.5 并发编辑碰撞（重要，非本项缺陷）

本轮末尾 `src/NLCPG` 构建**被同工作树内另一方的 P0 在途重构打断**：
`InterproceduralDataFlowPlanGroup.cs`（14:07–14:08 被改）把 `Plans` 拆成
`SharedArgumentPlans` + `GroupTailPlans`，而 `NLCPGBuilder.cs` **尚未跟进**。

- 稳定错误集（14:12–14:17 连续 6 次采样恒为 10 条，去重后 5 处）：
  `NLCPGBuilder.cs(2907)` CS7036、`(2944)` CS7036、`(3036)` CS1061、`(3051)` CS1061、`(3065)` CS1061。
- **本项 4 个文件（3 个 pass + `NLCPGGraph.cs`）零错误**——已按文件归因核实。
- ⇒ 这是**共享工作树的并发写入**，不是本项引入的编译失败。
  上述 980/2 的全套件结果取自**该碰撞发生之前**的干净构建，仍然有效。

