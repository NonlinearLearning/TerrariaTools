# scratch 图不再冻结查询索引，直接枚举 pending 边执行计划

**Goal:** 让 `ControlFlowPass` / `DominancePass` / `ControlDependencePass` 的 worker 局部图（scratch 图）不再调用 `FreezeQueryIndex()`，改为直接枚举 pending 边取 fragment，从而消掉"每个方法建一次完整查询索引"的全部成本，同时保持 fragment 逐字节不变。

**Architecture:** scratch 图是 L1 计算层的**纯中间产物**：它的唯一出口是 `LocalCpgFragment`（节点描述符 + 边候选），而片段的边只承载"两端稳定锚点 + 边种类 + 可选元数据"。查询索引（确定性 NodeId、CSR 邻接、按种类分桶、快照指纹）对这条出口**一点用都没有**。故正确形态是给 scratch 图一条**只读取数通道**，而不是让它走"建完整索引再拆掉"的路径。

**Tech Stack:** C#/.NET 10、`NLCPGGraph`、`PendingEdgeBuffer`、`CpgNodeDescriptor`、`CpgEdgeCandidate`。

日期：2026-09-27。本轮只写文档，不改产品代码。

---

## 0. 结论先行：本项**不直击 55.1%**

用户原始描述是"scratch 图不再 freeze，直接枚举 pending 边（改动小，直击 55.1%）"。**改动小成立；"直击 55.1%"不成立**，依据三条：

1. **55.1% 的归属是持久图的冻结，不是 scratch 图。**
   该数字来自阶段表的 `CPG.FreezeQueryIndex` 行。产生该行的唯一位置是
   [`NLCPGBuilder.cs:1436-1445`](../../src/NLCPG/Builder/NLCPGBuilder.cs)：

   ```csharp
   MeasureStage("FreezeQueryIndex", () =>
   {
       if (registry is not null) { registry.FreezeAll(); return; }
       context.Graph.FreezeQueryIndex();
   });
   ```

   即**每文件的持久图**。scratch 图的三处冻结不在这个 `MeasureStage` 之内。
   （研究报告自己也这样标注：`MeasureStage("FreezeQueryIndex", () => context.Graph.FreezeQueryIndex())` —— 单次同步调用，
   见 [研究报告第 959 行](../research/2026-09-23-freezequeryindex-single-thread-tail-research.md)。）

2. **scratch 图的冻结被记在别的阶段名下。** 三处调用都在 worker 回调内，随
   `ControlFlowPass` / `DominancePass` / `ControlDependencePass` 的阶段计时走。
   研究报告的阶段表里这三项极小：NPC.cs 的
   `ControlFlow / CallGraph / MethodModel = 0.7 s`（占 0.0%），967 文件全量为
   `其余 6 个 pass = 63.3 s`（1.8%）。

3. **测量早于 scratch 图的引入。** scratch 图的 `var localGraph = new NLCPGGraph(...)`
   由提交 `8f3c25e`（**2026-09-27**，"perf: 内存与 CPU 性能优化"）引入——
   `git log -S` 在 `ControlFlowPass.cs` / `DominancePass.cs` / `ControlDependencePass.cs`
   三个文件上的首个命中同为该提交。而研究报告测量于 **2026-09-23**，两者相隔 4 天。
   在该报告测量时的提交 `f765974`（2026-08-10）里，`ControlFlowPass` 第 37 行还是
   `var graph = context.Graph;` —— **直接写持久图，根本没有 scratch 图**。

> ⇒ **55.1% 描述的是一个当时还不存在的代码路径。** scratch 图的冻结是 2026-09-27 引入的**新增成本**，
> 目前**未经测量**，也**不可能**出现在 2026-09-23 的报告里。

**但这不削弱本项的价值**，只是改变了它的定位与验证方式：

| | 原假设 | 实际情况 |
| --- | --- | --- |
| 收益来源 | 削减既有 55.1% 的一部分 | 消除 2026-09-27 新增的、未测量的成本 |
| 归属阶段 | `FreezeQueryIndex` | `ControlFlowPass` / `DominancePass` / `ControlDependencePass` |
| 先决证据 | 研究报告阶段表 | **需要新测**（见 T1） |

因此本计划把**测量 scratch 冻结成本**列为 T1，而非直接实施。若 T1 显示该成本可忽略，
则应关闭本项而不是实施它——这个选项必须保留。

---

## 1. 现状：scratch 图付出了完整查询索引的代价

三处调用点结构完全相同。以 `DominancePass` 为例
（[`DominancePass.cs:527-578`](../../src/NLCPG/Builder/Passes/DominancePass.cs)）：

```csharp
var localGraph = new NLCPGGraph(
  identityFactory: context.Graph.IdentityFactory,
  stringInterner: context.Graph.StringTable);      // :527-529
... localGraph.AddNode(node);                       // :534
localGraph.FreezeQueryIndex();                       // :560
var nodesById = localGraph.Nodes                     // :561-563
  .Where(node => node.NodeId.HasValue)
  .ToDictionary(node => node.NodeId!.Value);
var descriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();  // :564
var edges = localGraph.Edges                         // :565-578
  .Select(edge => { var source = nodesById[edge.SourceNodeId];
                    var target = nodesById[edge.TargetNodeId]; ... })
  .ToArray();
```

`ControlFlowPass`（`:124-156`）与 `ControlDependencePass`（`:166-228`）逐字同构。
三者的**唯一**产物是 `CpgNodeDescriptor[]` 与 `CpgEdgeCandidate[]`。

而 `FreezeQueryIndex()`（[`NLCPGGraph.cs:595-609`](../../src/NLCPG/Model/NLCPGGraph.cs)）
为这个出口做了以下**全部**工作（行号以 2026-09-27 工作区为准；下表 `:N` 未标文件名的均指 `NLCPGGraph.cs`）：

| 步骤 | 位置 | scratch 是否需要 |
| --- | --- | --- |
| `GetStableAnchor` 逐节点求锚点 | `:925-931` | **不需要**——`AddNode` 时已设（见 §3.1） |
| `DeterministicNodeIdTable.Create` 分配确定性 NodeId | `:932` | **不需要**——只有 `nodesById` 字典在读它 |
| `frozenNodes[]` 全量节点副本（104 B/节点） | `:947-963` | **不需要** |
| `RemapEdgesOrdinal` 投影 `NLCPGEdge[]`（72 B/边） | `:982` / `:991-1020` | **不需要** |
| 清空 `_mutableNodesByAnchor` / `_nodesByOrdinal` | `:984-985` | **不需要** |
| 节点按 NodeId 排序 + `nodeOrdinals` 字典 | `NLCPGGraphIndex.cs:436`/`446-450` | **不需要** |
| `BuildMetadataRanks` + 4 趟 `CountingSortPass` 基数排序 | `NLCPGGraphIndex.cs:465-498` | **不需要** |
| `BuildOrdinals` / `BuildOrdinalsByTwoKeys` 建 CSR 邻接 | `NLCPGGraphIndex.cs:568` 起 | **不需要** |
| `CreateSnapshotVersion`：对全部节点与边做 SHA-256 | `NLCPGGraphIndex.cs:534`/`:1129` | **不需要** |
| `ReclaimConstructionCapacity` 释放 pending 缓冲 | `NLCPGGraph.cs:1052`（经 `:608`） | **不需要**（图随即被丢弃） |

> ⚠ **行号基线**：本表的 `NLCPGGraph.cs` 行号对应 2026-09-27 的工作区状态，
> 其中该文件含一个在途改动（新增 `CanonicalNodes` 属性，`:171`）。该改动与本文议题无关，
> 但使 `NLCPGGraph.cs` 在 201 行之后的所有行号相对 HEAD 上移 5 行。

**这些结构在执行完后立即随 `localGraph` 一起变成垃圾。** 逐方法、逐批地做一遍，是纯粹的浪费。

---

## 2. 目标形态

关键事实：**pending 边自带两端节点对象**，而这些节点在 `AddNode` 时**已经带上了稳定锚点**。
[`NLCPGGraph.cs:1288-1301`](../../src/NLCPG/Model/NLCPGGraph.cs)：

```csharp
private IEnumerable<PendingEdge> EnumerateLazilyCore(IReadOnlyList<NLCPGNode> nodesByOrdinal)
{
    foreach (var key in _keys)
    {
        var metadata = _metadataById[key.MetadataId];
        yield return new PendingEdge(
          nodesByOrdinal[key.SourceOrdinal],     // ← 真节点，带 StableAnchor
          nodesByOrdinal[key.TargetOrdinal],     // ← 真节点，带 StableAnchor
          key.Kind, metadata?.StructuredLabel, metadata?.ContextId, metadata?.CallSiteContext);
    }
}
```

且该通道**已经存在**并被生产代码使用：
`internal IEnumerable<PendingEdge> EnumeratePendingEdgesLazily()`（`NLCPGGraph.cs:188`），
现由 `RunInterproceduralDataFlowPass` 调用（`NLCPGBuilder.cs:2590`）。

于是三处调用点可退化为：

```csharp
var nodeDescriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
var edgeCandidates = new List<CpgEdgeCandidate>();
foreach (var edge in localGraph.EnumeratePendingEdgesLazily())
{
    edgeCandidates.Add(new CpgEdgeCandidate(
        edge.SourceNode.StableAnchor!.Value,
        edge.TargetNode.StableAnchor!.Value,
        edge.Kind, edge.StructuredLabel, edge.ContextId, edge.CallSiteContext));
}
```

`Nodes` 在**未冻结**时返回 `_nodesByOrdinal`（`NLCPGGraph.cs:165-166`），枚举序与冻结后的
`_queryIndex.InputOrderedNodes` 相同（见 §3.2）。`nodesById` 字典、`FreezeQueryIndex()` 全部消失。

---

## 3. 语义不变量（逐条给出源码依据）

### 3.1 锚点在 `AddNode` 时已确定，与冻结时求值等价

`AddNode(NLCPGNode, string?)`（`:271`）→ `MaterializeCompatibilityIdentity`（`:865-900`）
在**入图时**就写入 `StableAnchor`（`:895-899`）。冻结期的
`_identityFactory.GetStableAnchor(node, _stringInterner)`（`:928`）第一句是
`if (node.StableAnchor is { } existing) return existing;`（`StableNodeIdentityFactory.cs:23`）——
**直接返回既有锚点，不重算**。故两路锚点逐字段相同。

⇒ `CpgNodeDescriptor.FromNode` 要求锚点非空（`CpgNodeDescriptor.cs:23`）这一前置条件，
在未冻结路径上**同样成立**。

### 3.2 节点枚举序不变

- 冻结路径：`InputOrderedNodes` 的序即 `Create` 的输入序（`NLCPGGraphIndex.cs:357-359`），
  而输入是 `frozenNodes`，其 `frozenNodes[ordinal] = anchoredNodes[ordinal]`（`:947-963`），
  `anchoredNodes` 来自 `_nodesByOrdinal.Select(...)`（`:925-931`）。
- 未冻结路径：`Nodes => _nodesByOrdinal`（`:165-166`）。

⇒ 同一 `_nodesByOrdinal` 序，**逐元素相同**。

### 3.3 边枚举序不变

`EnumerateLazilyCore` 遍历 `_keys`（`:1290`）；`EnumerateOrdinalsLazilyCore` 遍历**同一个 `_keys`**（`:1314`）。
代码注释明确："`EnumerateOrdinalsLazily` 与 `Materialize`/`EnumerateLazily` 枚举同一 `_keys`、顺序完全一致"（`:980-981`）。
冻结路径的 `graph.Edges = _queryIndex.InsertionOrderedEdges`，其插入序"即冻结时输入枚举序"
（`NLCPGGraphIndex.cs:370-372`），而输入序正是 `RemapEdgesOrdinal` 按 `EnumerateOrdinalsLazily` 的顺序写入的（`:991-1020`）。

⇒ 两条路径的边序列**同序同内容**。

### 3.4 `ContextId`：原始值与已解析值在此**断言为 null**

这是本改动**唯一**有语义风险的点，必须显式守卫。

- 冻结路径经 `EnumerateOrdinalsLazily` 取的是**已解析**值：
  `_resolvedContextIdById[key.MetadataId]`（`:1325`），其解析规则为
  `callSiteContext?.ToContextId() ?? contextId`（`:1238`）。
- pending 路径经 `EnumerateLazily` 取的是**原始**值：`metadata?.ContextId`（`:1298`）。

`PendingEdgeBuffer` 的注释（`:1142-1151`）明确说明这个差别是**刻意保留**的，并指出
"Materialize / EnumerateLazily 的消费方……此前拿到的是**原始（生产侧恒为 null）**值"。

对 scratch 图，"生产侧恒为 null"**当前成立**，逐点核验：

| 调用点 | 形式 | 元数据 |
| --- | --- | --- |
| `AddControlFlowEdge` → `graph.AddEdge(source, target, kind)` | `NLCPGBuilder.cs:3633` | 全 null |
| `AddControlDependenceEdges` → `graph.AddEdge(controlNode, dependentNode, NLCPGEdgeKind.ControlDependence)` | `ControlDependencePass.cs:280` | 全 null |
| `AddPostDominanceEdges` → `graph.AddEdge(source, target, NLCPGEdgeKind.PostDominates)` | `DominancePass.cs:869` | 全 null |
| `AddOverlayEdges` → `AddKnownNodeCartesianEdges` | `DominancePass.cs:853` → `NLCPGGraph.cs:516`/`:531-537` | 显式 null×3 |

三条元数据全为 null 时 `InternMetadata` 直接返回 `0`（`:1224-1227`），
故 `metadata` 为 null、`MetadataId = 0`，而 `_resolvedContextIdById[0]` 也是 null 哨兵（`:1152`）。

⇒ **两条路径产出逐字段相同，包括 `ContextId = null`。**

⚠ **必须加守卫**，否则这将是一个静默差异：若将来有人给 scratch 边挂上 `CallSiteContext`，
冻结路径会给出非 null 的已解析 `ContextId`，pending 路径给出 null，**不抛异常、不报错**。
见 T2。

### 3.5 无查询索引不影响其它机制

`HasQueryIndex` 为 false 不影响：`EnsureMutable` 的只读窗口与 worker 计算窗口守卫按实例计数，
scratch 图深度天然为 0（`NLCPGGraph.cs:1114-1116`，守卫在 `:1117`）；`AddControlFlowEdge` 的 CFG 邻接缓存按
`_activeBuildGraphs` 判定（`NLCPGBuilder.cs:3636`），与是否冻结无关。

---

## 4. 执行步骤

### T1：先测量 scratch 冻结的实际成本（**决定本项是否实施**）

**动机**：55.1% 与 scratch 图无关（§0），故本项收益**没有既有数据支撑**。不测量就实施等于押注。

- [ ] 取一个代表性输入（建议与研究报告同尺度的目录，或至少含 1 个巨文件），跑一次基线。
- [ ] 读出阶段表里 `ControlFlowPass` / `DominancePass` / `ControlDependencePass` 三项
      （`NLCPGBuildMetrics.PassElapsedMilliseconds`，经 `CpgPerformanceFactMapper` 输出）。
- [ ] 若三项合计占比很小（例如 <2%），**记录结论并关闭本项**，改为去做 §0 所说的真正 55.1% 那条线。
- [ ] 若三项合计显著，再进 T2。此时应额外补一个临时探针：在 `FreezeQueryIndex()` 外层按
      图身份（是否 scratch）分桶计时，把"scratch 冻结"从三阶段总耗时中分离出来。

**说明**：scratch 图目前无法从阶段表单独识别——这正是需要临时探针的原因。探针只用于测量，
不进最终 diff。

### T2：给 scratch 图加显式只读取数通道（**替代"不调用 freeze"的隐式约定**）

**做法**：在 `NLCPGGraph` 上新增一个**具名**的 scratch 取数入口，而不是靠"忘了调用 freeze"来实现。
理由：不调用 `FreezeQueryIndex` 会让图停在可变态，而 `Edges` 在可变态**静默返回空集合**
（`NLCPGGraph.cs:175-176`）。将来任何人给 scratch 图加一句查询，都会拿到空结果而不是异常。

建议形态（二者取一，倾向 A）：

```csharp
// A：返回两端带节点的 pending 边，并在入口校验"未冻结"
internal IReadOnlyList<PendingEdge> SnapshotScratchEdges()
{
    if (_queryIndex is not null)
        throw new InvalidOperationException("scratch 取数要求图未冻结。");
    var edges = _pendingEdges.Materialize(_nodesByOrdinal);   // 或 EnumerateLazily + ToArray
    // §3.4 守卫：scratch 边不得携带元数据，否则 ContextId 原始/已解析语义分叉
    ...
    return edges;
}
```

- [ ] 选定 A（新增具名入口，含"未冻结"断言 + 元数据为 null 断言）或 B（三处直接改用
      既有 `EnumeratePendingEdgesLazily()`，不新增 API）。
- [ ] 若选 B，`ContextId` 守卫必须另找位置（例如在三处调用点断言），否则 §3.4 的风险无收口。
- [ ] 若选 A 且用 `Materialize`，注意它分配整份 `PendingEdge[]`（272 B/元素，研究报告记录过
      该形态在 71 s 内分配 641.4 MiB）；**优先用 `EnumerateLazily` 直接 `Select(...).ToArray()`**，
      避免中间数组。

### T3：改造三处调用点

对 `ControlFlowPass.cs:136-156`、`DominancePass.cs:560-578`、`ControlDependencePass.cs:210-228`：

- [ ] 删除 `localGraph.FreezeQueryIndex();`
- [ ] 删除 `nodesById` 字典（三处的唯一用途是把边端点映射回节点；现在端点就在 `PendingEdge` 里）
- [ ] 边投影改为读 `PendingEdge.SourceNode` / `.TargetNode` 的 `StableAnchor`
- [ ] 保留 `nodeDescriptors` 的构造方式（`localGraph.Nodes.Select(CpgNodeDescriptor.FromNode)`），
      逐字不变
- [ ] 检查三处是否还有其它对 `localGraph` 冻结态的隐含依赖（本次审读**未发现**）

### T4：等价性验证

- [ ] 同一输入，改动前后 `LocalCpgFragment` 的 `Descriptors` / `EdgeCandidates` **逐元素相同**
      （含顺序）。这是本项的核心断言。
- [ ] 至少覆盖：空图、单方法、含泛型/接口分派的方法、多文件批次（跨文件分组）、
      巨文件中的大方法。
- [ ] 断言 scratch 边元数据全为 null（锁住 §3.4）。

### T5：登记与收尾

- [ ] 本计划尚未登记进 [`Context/feature_list.json`](../../Context/feature_list.json)；
      实施前须先登记状态与完成条件（该文件是 feature 状态的唯一来源）。
- [ ] 按 [`docs/contributing.md`](../contributing.md) 与
      [Harness 验证矩阵](../harness-verification-matrix.md) 执行匹配的构建与测试层级，
      并如实记录实际执行的命令与未验证边界。

---

## 5. 风险

| 风险 | 形态 | 收口 |
| --- | --- | --- |
| **`ContextId` 原始/已解析分叉** | 将来给 scratch 边挂元数据 ⇒ 静默 null vs 非 null | T2 的元数据为 null 断言 |
| 误以为在修 55.1% | 收益归因错误，可能砍掉错误的优化 | §0 已纠正；T1 先测 |
| 在可变态误用查询 API | `Edges` 静默返回空；`GetOutgoingEdges` 抛 `RequireQueryIndex` | T2 具名入口 + "未冻结"断言 |
| 内存峰值不降反升 | 若用 `Materialize` 分配整份 `PendingEdge[]` | T3 要求用 `EnumerateLazily` + 直接投影 |
| 收益可忽略 | scratch 冻结本来就便宜 | T1 允许直接关闭本项 |

---

## 6. 预期收益（**未验证，不给具体数字**）

单次 scratch 冻结省下：确定性 NodeId 分配、104 B/节点副本、72 B/边副本、基数排序的多个
`int[边数]`、CSR 邻接构建、以及**对全部节点与边的 SHA-256**。代价变为一次 `_keys` 枚举。

按方法计一次，全量收益 = Σ(每方法 scratch 规模) × 上述单位成本。研究报告给出的缩放参照：
`FreezeQueryIndex` 的分配约 353 B/边（优化后）、持久图单文件瞬时分配曾达 14.17 GB，
排序分配 1,031.5 MB @N=430k。scratch 图的边数远小于持久图，但数量级上的常数结构是同一套。

**具体倍数必须由 T1 实测**，本文不承诺。

---

## 7. 不采用项

| 方案 | 不采用理由 |
| --- | --- |
| 保留 `FreezeQueryIndex`，只优化 `NLCPGGraphIndex.Create` 内部 | 对 scratch 图而言整条索引都是无用产物；优化无用产物不如不建 |
| 给 `FreezeQueryIndex` 加 `bool scratch` 参数分叉 | 同一方法承载两种语义，且 scratch 分支会绕过 `ReclaimConstructionCapacity` 等收尾，易腐化 |
| 靠"不调用 freeze"隐式实现 | `Edges` 在可变态静默返回空集合，是静默失效形态（§4 T2） |
| 改为在 `AddEdge` 时立刻产出 `CpgEdgeCandidate` | 会改变 pending 缓冲的去重语义（`HashSet<PendingEdgeKey>`，`NLCPGGraph.cs:1153` 声明 / `:1179` 插入去重），可能改变边集 |
