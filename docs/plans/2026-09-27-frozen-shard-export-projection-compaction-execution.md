# ① 冻结分片导出投影压缩执行文档

日期：2026-09-27。性质：**只写文档**。本轮未改产品代码、未运行构建、未运行测试、未做任何内存采样。

**目标：** 把 `CpgFrozenGraphProjection` 从「三份并行物化的节点/边容器」收敛为
「一份已排序节点数组 + 对既有查询索引的引用」，消除持久化导出路径上
**整份节点载荷的第二份副本**与**整份边载荷的 `NLCPGEdge[]` 副本**。

**权威状态：** [feature_list.json](../../Context/feature_list.json) 是唯一来源。本切片**尚未登记**，
本文件不等于任何已验收结论。

> 📌 **行号基准**：`src/NLCPG/Persistence/CpgFrozenShardExporter.cs`（239 行）；
> `src/NLCPG/Model/NLCPGGraphIndex.cs`（1,284 行）；
> `src/NLCPG/Builder/CpgShardBuildCoordinator.cs`（425 行）。
> 下列引用**同时给出符号名**，行号漂移时以符号为准。

---

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 缺陷是什么？ | `Prepare` 在已有查询索引之上，**再**物化 NodeId 升序节点数组、`Dictionary<NodeId, NLCPGNode>`、以及**每节点一份** `NLCPGEdge[]` |
| 哪些是真重复？ | `NodesByNodeId` 与 `OutgoingEdgesByNodeId` —— 二者都被索引的既有结构**逐元素覆盖** |
| 哪些要保留？ | `Nodes`（NodeId 升序）与 `StringInterner` —— 前者决定分片 `LocalIndex`，**不能**换成输入序 |
| 生产可达吗？ | **可达但默认关闭**：仅在 `NLCPGBuilderOptions.Persistence is not null` 时执行（`NLCPGBuilderOptions.cs:33` 默认 `null`） |
| 收益有实测吗？ | **没有**。本文所有字节数为**由既有实测宽度推出的算术**（§5），标注【推导】 |
| 契约风险 | 低——改动全部落在 `Export(CpgFrozenGraphProjection, …)` 内部；分片字节应逐字段不变，但**必须靠既有 oracle 证明**，不得凭推理认定 |

---

## 1. 现状：同一批数据被并行物化三遍

`CpgFrozenShardExporter.Prepare(NLCPGGraph graph)`（`CpgFrozenShardExporter.cs:96-114`）：

```csharp
var nodes = graph.Nodes
  .OrderBy(node => node.NodeId)
  .ToArray();                                                          // ① :104-106
var nodesByNodeId = nodes.ToDictionary(node => node.NodeId!.Value);    // ② :107
var outgoingEdgesByNodeId = new Dictionary<NodeId, IReadOnlyList<NLCPGEdge>>(nodes.Length);
foreach (var node in nodes)
{
    outgoingEdgesByNodeId[node.NodeId!.Value] =
      graph.GetOutgoingEdges(node.NodeId!.Value).ToArray();            // ③ :108-112
}
return new CpgFrozenGraphProjection(graph.StringTable, nodes, nodesByNodeId, outgoingEdgesByNodeId);
```

投影类型本身只是四个成员的载体（`:218-239`）：

| 成员 | 类型 | 与索引既有状态的关系 | 判定 |
| --- | --- | --- | --- |
| `Nodes` | `IReadOnlyList<NLCPGNode>` | 内容 = 索引的 canonical 节点序（见 §1.1） | **保留**（决定 `LocalIndex`） |
| `NodesByNodeId` | `IReadOnlyDictionary<NodeId, NLCPGNode>` | `NodeOrdinals`（`NLCPGGraphIndex.cs:386`）+ `CanonicalNodes` 已提供同一查找 | **重复** |
| `OutgoingEdgesByNodeId` | `IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>>` | `Outgoing` CSR 表已能**零堆分配**投影同样的边（`:540-546`、`CsrEdgeTable.Get` `:40-46`） | **重复** |
| `StringInterner` | `StringInterner` | 索引不持有它 | **保留** |

### 1.1 `Nodes` 的内容确实等于 canonical 序（【源】）

`NLCPGGraphIndex.Create` 用 `Array.Sort(orderedNodes, inputOrdinals, NodeIdOrderComparer.Instance)`
（`NLCPGGraphIndex.cs:436`）就地按 `NodeId` 升序排序，比较器为 `Nullable.Compare(x.NodeId, y.NodeId)`
（`:280-288`）。而 `Prepare` 的 `OrderBy(node => node.NodeId)` 与它同序（`NodeId` 唯一，
不存在并列；`OrderBy` 的稳定性因此无关）。故 `Nodes` 与 `OrderedNodes` **元素逐个相同**。

> ⚠️ 但 `graph.Nodes` **不是** canonical 序：冻结后它是 `_queryIndex.InputOrderedNodes`
> （`NLCPGGraph.cs:165-166`），即**输入序**视图（`NLCPGGraphIndex.cs:363-364`）。
> 这是 §2 中必须保留排序调用的原因。

### 1.2 唯一的真实消费者是 `Export`

`Export(CpgFrozenGraphProjection, CpgShardLookup, IReadOnlySet<NodeId>?)`（`:116-166`）读到：

| 位置 | 读的成员 | 用途 |
| --- | --- | --- |
| `:122` | `Nodes` | 未限定集合时的全量节点 |
| `:124-125` | `NodesByNodeId.ContainsKey` / 索引器 | 限定集合时筛出节点 |
| `:131` | `StringInterner` | `ToFrozenNode` 解析字符串 |
| `:135` | `OutgoingEdgesByNodeId[...]` | 逐节点取发出边 |

`Prepare` 的生产调用点只有一个：`CpgShardBuildCoordinator.PersistAsync`
（`CpgShardBuildCoordinator.cs:68`），随后 `exportProjection` 被传入
`CreateShard(..., exportProjection)`（`:112`）并以 `_options.MaxConcurrentShardExports` 并发消费（`:98-130`）。
`Export(NLCPGGraph, …)`（`:89-94`）是同一投影的一次性包装。

> ⇒ `Prepare` 的产物**跨整个并发导出窗口常驻**，不是短命中间量。这是本项的收益来源，
> 也是"为什么值得改"的理由。

---

## 2. 目标表示与改动清单

### 2.1 目标形态

```csharp
internal sealed class CpgFrozenGraphProjection
{
    // 仅保留两样：已按 NodeId 升序的节点数组（决定 LocalIndex），与字符串表。
    // NodeId→节点 与 节点→发出边 两类查找全部回落到图的查询索引，
    // 后者是既有 CSR 投影，读取零堆分配。
    internal CpgFrozenGraphProjection(
      StringInterner stringInterner,
      IReadOnlyList<NLCPGNode> nodes)
    { … }
}
```

`Export` 内的三处替换：

| 原读法 | 替换为 | 等价性依据 |
| --- | --- | --- |
| `projection.NodesByNodeId.ContainsKey(id)` | `graph.GetNode(id) is not null` | `NodesByNodeId` 由 `graph.Nodes` 全量建成，`GetNode` 走 `TryGetNode`（`NLCPGGraph.cs:550-558` → `NLCPGGraphIndex.cs:607-617`），两者对同一 NodeId 同真同假 |
| `projection.NodesByNodeId[id]` | `graph.GetNode(id)!.Value` | 前一项已保证存在 |
| `projection.OutgoingEdgesByNodeId[id]` | `graph.GetOutgoingEdges(id)` | `CsrEdgeTable.Get`（`:40-45`）对已知节点返回同一 CSR 切片；未知节点返回 `Array.Empty`，而循环只遍历 `nodes` 中已存在的节点，故差异不可达 |

**注意**：`projection.Nodes` 与 `StringInterner` **不动**。前者决定导出分片的
`localIndex`（`:128-131` 由数组位置生成），后者是 `ToFrozenNode` 的入参。

### 2.2 为什么 `Export` 需要持有 `graph`

新签名需要把图传进 `Export`。两种做法：

- **方案 1（推荐）**：把图放进投影——`CpgFrozenGraphProjection` 增加一个 `NLCPGGraph Graph { get; }`，
  由 `Prepare` 填入。`Export` 的公开签名与调用点**零改动**，`CreateShard` 也不用改。
- **方案 2**：给 `Export` 增加 `NLCPGGraph` 形参，同步改 `CpgShardBuildCoordinator.CreateShard`（`:344-353`）
  与 `Export(NLCPGGraph, …)`（`:89-94`）。

方案 1 的改动面更小，且不触碰 `CreateShard` 的签名。**本轮不预设结论，实施时二选一并记录。**

> ⚠️ 方案 1 会让投影持有 `NLCPGGraph` 强引用。该引用在 `PersistAsync` 作用域内本就是活的
> （`context.Graph`），故不延长任何生命周期；但**必须在实施时确认**这一点，而不是默认。

### 2.3 明确**不**做的事

- **不改** `NLCPGGraphIndex` 的存储表示，不新增常驻结构。
- **不删** `Prepare` 中的 `HasQueryIndex` 守卫（`:99-102`）——`Export_MutableGraph_Throws` 依赖它。
- **不**把 `Nodes` 换成 `graph.Nodes`：那会改变 `LocalIndex` 分配，进而改变分片字节。
- **不动** `ExportDescriptors`（`:8-86`）路径：它走描述符/候选者，不经过本投影。

---

## 3. 等价性分析

### 3.1 分片字节应当逐字段不变

导出序完全由「`nodes` 数组顺序」× 「每节点的发出边序」决定：

1. `nodes` 仍为 NodeId 升序数组（§2.1）⇒ `localIndex` 分配不变。
2. 发出边序：原为 `graph.GetOutgoingEdges(id).ToArray()`，新为 `graph.GetOutgoingEdges(id)`。
   二者是同一 `CsrEdgeTable.Slice` 结果的「物化」与「仅视图」之差，**枚举序相同**
   （`OrdinalEdgeList.cs:4-25` 按序数表顺序投影）。
3. `frozenEdges` 的追加顺序（`:133-153`）与被跳过条件（`:137-140`）不变。

⇒ 预期 `frozenNodes` / `frozenEdges` 及 `CpgFrozenShardIncomingEdgeIndex.Build`（`:156-158`）
的输入**逐元素相同**。**但这是推导，不是证据**——必须由 §4 的既有 oracle 证明。

### 3.2 已知的语义差异（须确认不可达）

| 差异 | 是否可达 | 依据 |
| --- | --- | --- |
| 字典索引器抛 `KeyNotFoundException` vs `GetNode` 返回 `null` | **不可达** | 循环只遍历 `nodes`（全部来自 `graph.Nodes`），键必然存在 |
| `CsrEdgeTable.Get` 对未知节点返回空列表 | **不可达** | 同上 |
| `nodesByNodeId` 的 `IReadOnlyDictionary` 接口分派开销 | 消失（改进） | — |

### 3.3 风险点

1. **`graph.GetNode` 是查询热点**（`NLCPGGraphIndex.cs:605` 注释）。本项把导出路径的
   「每节点一次字典索引」换成「每节点一次 `TryGetNode` + 一次 `NodeOrdinals` 探测 + 一次数组读」，
   常数略高。**导出是 I/O 主导的路径**，预期可忽略，但**未测量**；若 §4 的耗时门禁劣化 >5%，
   按 §6 回退。
2. `Prepare` 的分配量必须在改动后**下降**。若因方案 1 引入了新的装箱/闭包而上升，回退。

---

## 4. 验证门禁

### 4.1 既有 oracle（**必须全绿，不得修改期望值**）

| 测试 | 位置 | 锁住什么 |
| --- | --- | --- |
| `Export_FrozenGraph_PreservesOrderedNodeIdsAndEdges` | `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs:224-237` | 分片节点序 = `graph.Nodes.OrderBy(NodeId)`；边种类 |
| `Export_MutableGraph_Throws` | `:239-246` | `Prepare` 的冻结守卫 |
| `ExportDescriptors_PreallocatedLocalNodes_…` | `:248-299` | **`Export(graph, …)` 与 `ExportDescriptors` 两条路径产出的 `Nodes`/`Edges` 必须相等**（`:293-294`）——这是本项最强的等价性 oracle |
| `WriteAsync_ValidShard_WritesReadableCompleteShard` | `:14-42` | 分片落盘/读回 |
| `WriteAsync_BoundaryEdgeManifest_PreservesGlobalNodeIds` | `:43-71` | 边界边全局 NodeId |
| `BuildFromSource_StreamingPersistence_MatchesSerialSnapshotAtConfiguredDop` | `CpgShardBuildCoordinatorTests.cs:1112-1146` | 流式 vs 串行快照 |
| `BuildFromSource_PersistenceHit_RebuildsCompleteGraph` | `:821-876` | 还原后图完整 |
| `ReadFromPathAsync_CorruptIncomingEdgeCsr_…` | `CpgShardContractTests.cs:117-141` | 入边索引 CSR 校验 |

### 4.2 拟新增门禁（**新增**，判别力必须可证）

仿 `FrozenNodeStorageEquivalenceTests.NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload`
（`:320-377`）的「结构计数」手法：

1. **`Prepare_DoesNotMaterializeASecondCompleteNodePayload`**（新增）
   —— 冻结一张 `nodeCount = 8192` 的图，调用 `Prepare`，递归统计投影对象图内
   **非空 `NLCPGNode[]` 的实例数**，断言为 **1**。判别力：改动前为 1（投影的 `nodes`）
   加上索引的 canonical 数组（若同根遍历到）——**实施时必须先量改动前的真实计数**，
   再据以设阈值；不得先写死 1。
2. **`Prepare_DoesNotMaterializeEdgeArrays`**（新增）
   —— 断言投影对象图内不存在「元素数 = 该节点出度」的 `NLCPGEdge[]` 集合。
   建议以 `ReferenceEqualityComparer` 遍历并统计 `NLCPGEdge[]` 实例总数 = **0**。
3. **变异检验（必做）** —— 故意让替换后的 `GetOutgoingEdges` 少取一条边（或改 `Nodes` 排序键），
   确认至少 1 条 `CpgShardContractTests` 用例变红，然后**还原**。
   若一条都不红，说明该 oracle 对本改动无判别力，必须补测试而不是放行。

### 4.3 命令

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardContractTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardBuildCoordinatorTests'
```

定向绿后再按 [测试入口](../../tests/AGENTS.md) 执行 `Run-TestTiers.ps1 -Fast`。
TRX 必须包含预期测试条数；测试返回 0 而 0 tests 不算通过。

### 4.4 耗时门禁

对齐[内存优化执行总索引](2026-09-25-memory-optimization-execution-index.md) §4 的统一口径：
受控小批次 1 次预热 + ≥3 次交错配对，`PersistenceWrite` / `PersistenceRestoreFacts`
中位数劣化 **≤5%**；小于计时分辨率或噪声大时标记 **Inconclusive**，追加至多 2 对后仍不确定
则**保留文档、不宣称通过**。

---

## 5. 收益口径（**均为算术推导，未实测**）

采用仓库既有实测宽度：`NLCPGNode` = **104 B**（`docs/loh-large-object-optimization-guide.md:56`）、
`NLCPGEdge` = **72 B**（同文件 `:55`）、`Dictionary<TKey,TValue>` 条目 = `12 B 头 + key + value`、
x64 SZArray 头 = `24 B`。

| 项 | 消除量（【推导】） |
| --- | --- |
| `NodesByNodeId`（`Dictionary<NodeId, NLCPGNode>`，条目 ≈ `12 + 4 + 104` = **120 B/节点**） | `nodeCount × 120 B`（另加桶数组） |
| `OutgoingEdgesByNodeId`：每节点一个 `NLCPGEdge[]` | `edgeCount × 72 B` + `nodeCount × 24 B` 数组头 |
| 其字典本身（`NodeId → 引用`，条目 ≈ 16 B） | `nodeCount × 16 B` |

> ⚠️ **不得**把上表写成"峰值下降 X MiB"。三项都是**受控容器存活字节**口径；
> 端到端峰值未测，且本路径默认关闭。**唯一的合格结论形式**是
> 「在上述口径下，导出窗口内少持有 ≥ N 字节，由 §4.2 的结构门禁守住」。

---

## 6. 回退条件

任一条成立即回退本补丁（只涉及 `CpgFrozenShardExporter.cs` 与调用它的两个文件）：

1. 任一分片的 `frozenNodes` / `frozenEdges` / `IncomingEdgeOffsets` / `IncomingEdgeIndexes`
   出现任何差异。
2. §4.4 耗时中位数劣化 > 5%（且非 Inconclusive）。
3. `Prepare` 的分配量**上升**。
4. 方案 1 的 `NLCPGGraph` 强引用被证明超出 `PersistAsync` 作用域。

回退方式是应用本项自身的反向补丁，**不得**对共享工作树做 `reset --hard`、`clean` 或整文件覆盖。

---

## 7. 本文件不得越读的边界

1. ~~本轮**只写文档**~~ **已过期**：本轮**已改产品代码并跑测试**，见 §8。
   §4 的门槛**已执行**。
2. §5 的字节数全部是**算术推导**，**不含任何本轮实测**（本轮**未做内存采样**）。
3. 「索引已覆盖同一查找」是**静态阅读 + 符号检索**的结论，未在运行时对拍验证。
4. 工作树在审计期间存在**大量并发修改**（见 [未接线设计审计报告](2026-09-26-unwired-design-audit-report.md) §0）。
   本文件的源码行号是 **2026-09-27 20:0x** 的读数；符号名是稳定锚点。
5. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)；本文件不构成验收证据。
6. 持久化路径**默认关闭**（`NLCPGBuilderOptions.cs:33` 默认 `null`），故本项收益在默认配置下**不体现**。

---

## 8. 执行结果（本轮实测）

### 8.1 改动

| 文件 | 净变化（`git diff --numstat`） |
| --- | --- |
| `src/NLCPG/Persistence/CpgFrozenShardExporter.cs` | +16 / −26（239 → 229 行；`Prepare` 收敛，投影砍掉两个字段） |
| `src/NLCPG/Model/NLCPGGraph.cs` | +5 / −0（新增 `internal CanonicalNodes` 访问器，`:171`） |
| `tests/NLISSN.ContractTests/Cpg/FrozenNodeStorageEquivalenceTests.cs` | +163 / −0（两条计数守卫 + 边数组遍历助手） |

`Prepare`（`:96-109`）**只做**三件事：空值守卫、`!HasQueryIndex` 抛 `InvalidOperationException`、
`return new CpgFrozenGraphProjection(graph, graph.StringTable);`。原先的
`nodes.ToArray()` + `ToDictionary` + 逐节点 `GetOutgoingEdges().ToArray()` 全部删除。

投影（`:215-229`）收敛为 `Graph` / `Nodes` / `StringInterner` 三项，其中
`Nodes => Graph.CanonicalNodes`（`:226`）是**计算属性**（不产生后备字段）。采用 **§2.2 方案 1**：
投影持有 `NLCPGGraph` 引用，故 `Export` 的签名与 `CpgShardBuildCoordinator.CreateShard`
**均未改动**。

### 8.2 §3.2 表格：三处"语义差异"的运行时确认

| 差异 | 本轮确认 |
| --- | --- |
| 字典索引器抛 `KeyNotFoundException` vs `GetNode` 返回 `null` | **不可达**（编译 + 138 例绿；循环只遍历 `graph.CanonicalNodes`） |
| `CsrEdgeTable.Get` 对未知节点返回空列表 | **不可达**（同上） |
| `nodesByNodeId` 的接口分派开销 | **已消失**（字段本身被删） |

### 8.3 编译证据

```
build tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj
  → 0 错误, 21 警告（全部为既有 xUnit 分析器规则，与本项无关；警告数与 ③/④ 基线一致）
```

### 8.4 测试证据（定向 ~10% 核心子集）

```
--filter FrozenNodeStorageEquivalenceTests|CpgRelationQueryTests
  → 已通过! 失败 0, 通过 35, 总计 35

--filter CpgShardContractTests|NLCPGNodeIdContractTests|CpgPersistenceStateTests
       |CpgShardBuildCoordinatorTests|NLCPGSliceQueryTests
       |CpgRelationQueryTests|FrozenNodeStorageEquivalenceTests
  → 已通过! 失败 0, 通过 138, 总计 138, 耗时 1 m 13 s
```

§4.1 的既有 oracle 全部在跑（含最强的
`ExportDescriptors_PreallocatedLocalNodes_ExportsLocalEdgesAndReturnsBoundaryEdges`，
即 `Export` 与 `ExportDescriptors` 两路 `Nodes`/`Edges` 逐项相等），**期望值未做任何修改**。

### 8.5 §4.2 新增门禁与变异检验（**判别力已证**）

| 新增门禁 | 断言 | 变异 | 结果 |
| --- | --- | --- | --- |
| `Prepare_DoesNotMaterializeASecondCompleteNodePayload` `:326` | 投影可达的非空 `NLCPGNode[]` **恰好 1 个**；`Nodes` 与索引 canonical **同一实例**；无自定义字段、仅 2 个后备字段 | 把 `nodesByNodeId` 物化回投影 | **变红**：`Expected 1, Actual 2` ✅ |
| `Prepare_DoesNotMaterializeEdgeArrays` `:378` | 投影可达的非空 `NLCPGEdge[]` **恰好 0 个** | 把逐节点 `GetOutgoingEdges().ToArray()` 物化回投影 | **变红**：`Expected 0, Actual 512` ✅ |

两次变异随后**均已还原**，还原后 138 例全绿。

> ⚠️ **一处实现细节值得记录**：首版 `Prepare_DoesNotMaterializeEdgeArrays` 的遍历器先做
> 「只下钻 NLCPG 程序集」过滤，导致装在 BCL
> `Dictionary<NodeId, IReadOnlyList<NLCPGEdge>>` 里的边数组**整棵树被漏掉**，
> 变异时该用例**仍绿**——即它当时**没有判别力**。修正为"先下钻 `IDictionary.Values`、
> 再做程序集过滤"后才观测到 `0 → 512`。这正是 §4.2 第 3 条"若一条都不红，说明该 oracle
> 对本改动无判别力，必须补测试而不是放行"所要防的情形。

### 8.6 边界更新

- §7 第 1 条"本轮只写文档"**已过期**（见上）。
- §4.1 第 3 行的 `ExportDescriptors_PreallocatedLocalNodes_…` 行号在**审计快照**为 `:248-299`，
  当前文件实际为 `:249-299`（因本轮上游 `using` 增删）。**符号名是稳定锚点**。
- 本项**未**跑全量测试套件（按用户指示只执行 ~10% 核心子集）。

### 8.7 未验证边界

1. **未做任何内存采样**：§5 三项字节收益仍**全部是算术推导**，本轮**未**测量分配或峰值。
   §4.2 守住的是**结构形态**（数组实例计数），不是字节数。故 §5 的
   `nodeCount × 120 B` 等数字**未经实测**，不得引用为已证结论。
2. **未测耗时**：§4.4 的"中位数劣化 ≤5%"门槛**未执行**，§3.3-1 的
   「`graph.GetNode` 常数略高」**仍是未测量风险**。本轮无任何耗时数据可报告。
3. **未跑全量测试**：仅上述 7 个类共 138 例。`HostTests`、`UnitTests`、`PerformanceTests`
   **未运行**。
4. **未验证** §6 第 4 条（`NLCPGGraph` 强引用是否超出 `PersistAsync` 作用域）：
   §2.2 方案 1 确实让投影新增了一个 `NLCPGGraph` 强引用，其**生命周期未被测量**；
   但 `CpgFrozenGraphProjection` 仅在 `Export(graph, …)` 的调用帧内存活
   （`Prepare` 不缓存任何静态/字段），**静态阅读如此**，运行时未证。
5. **未验证** §4.1 列出的 `ReadFromPathAsync_CorruptIncomingEdgeCsr_…` 是否**被本轮过滤器覆盖**：
   它位于 `CpgShardContractTests` 内，故应已执行；但报告只给出聚合计数，**未逐条核对 TRX**。
6. 持久化默认关闭 ⇒ 本轮所有测试都**未**在真实持久化配置下端到端验证本项收益。

