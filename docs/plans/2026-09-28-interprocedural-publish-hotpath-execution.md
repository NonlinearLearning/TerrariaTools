# A+B+C 跨过程发布热路径合并执行计划

**Goal:** 在同一批改动内完成三件互相正交的热路径优化，使 `RunInterproceduralDataFlowPass`
的**④ 发布段**与**②③ 排序段**同时减负：

| 项 | 内容 | 攻击面 |
| --- | --- | --- |
| **A** | 标签三单例化（§2） | ⚠️ **前置未闭合**：每边 1 个 `NLCPGEdgeLabel` 分配（§2.3 有冲突待解） |
| **B** | 按序数直通 `AddEdge`（§3） | 每边 2 次 `AddNode` + 2 次 `ResolveOrdinal` |
| **C** | 排序键排名化（§4） | 每行宽度 + 每比较的字符串 `CompareOrdinal` |

**Architecture:** A 在 `NLCPGBuilder` 内加 3 个静态只读标签单例并替换唯一分配点；
B 给 `NLCPGGraph` 加一条「端点已是图内节点」的直通提交路径（跳过节点身份归并）；
C 在既有排序键预计算之后追加稠密排名，行载荷由 2 个 `string` 改为 2 个 `int`。

**Tech Stack:** C#/.NET 10、`NLCPG.Builder`、`NLCPG.Model`、xUnit（ContractTests）。

日期：2026-09-28（计划）。

> 📌 **本轮性质：只写执行文档。不改产品代码、不跑构建、不跑测试、不做任何前置测试、不新增探针。**
> 因此本文**不产出任何实测收益数字**，也不声称任何改动已生效。
> 所有量均为**引用既有实测**或**算术推算**，口径已在 §2.4 与 §9 逐项标注。

> 📌 **据以执行的上游文档**：本文是 2026-09-28 **纯静态审查**（无独立文档）的执行化。

> 📌 **C 项有独立详规**：[C 排序键排名化执行计划](2026-09-28-interprocedural-sort-key-ranking-execution.md)。
> 本文 §4 是**摘要 + 与 A/B 的接口约束**；C 的完整论证、测试修订清单与不变条件**以该文为准**，
> 本文**不重复**。若两份文档冲突，**以 C 专文为准**。

> 📌 **行号基准**：`src/NLCPG/Builder/NLCPGBuilder.cs` **4,647 行**；
> `src/NLCPG/Model/NLCPGGraph.cs` **1,366 行**；
> `src/NLCPG/Model/NLCPGGraphIndex.cs` **1,535 行**。
> 这些文件正被并发会话改写，故引用**一律同时给出符号名**，行号漂移时以**符号名**为准。

> ⚠️ **与 P0（`c² → c`）的关系**：A、B **不依赖** P0，可独立合入；
> **C 必须在 P0 之后**（收益口径与代码位置双重原因，见 §4.3 与 C 专文 §8.1）。
> 若 P0 尚未落地，**本文建议只做 A+B**，C 另行排期。

---

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| A 安全吗？ | **安全**。`EdgeMetadata`（pending 池）按**值**比较，故实例共享**不改变**待物化边的去重结果（§2.2） |
| A 的收益确定吗？ | ⚠️ **必须先解决一处与本仓既有实测的冲突**（§2.3）：`Context/progress.md:3686-3690` 记载"标签每条边新分配"这一假设**已被实测推翻**。本次重读源码后认为**推翻本身是误判**（判据取自去重**之后**），但**A 据此必须降级为「先补一次分配计数」**，见 §2.3 |
| B 安全吗？ | **不能直接断言安全**。真正的风险**不是**"锚点表冻结"，而是 **`MergeNode` 的写回可能覆盖已合并字段**（§3.2）。⇒ B **必须**先满足 §3.3 的证明义务 |
| B 有更稳的替代吗？ | **有**：保留 `AddNode` 但让锚点表**引用比较直通**（§3.4 选项 B2）。收益较小但**无需 §3.3 的重证明** |
| C 安全吗？ | 安全，**前提是稠密排名**（等键必须同排名），详见 C 专文 §3.1 |
| 三项能否同批？ | A 与 B 改**同一循环**（`:3090` 邻域）⇒ 建议**同批分两次提交**；C 改**另一段**（②③）⇒ 建议**后置** |
| 本轮验收门槛？ | 按用户指示：**只做 10% 核心测试，不做任何前置测试**（§7） |

---

## 1. 现状与定位

### 1.1 三段拆分（既有实测，`Context/progress.md:3316-3323`）

| 段 | 占比 | 可否并行 | 本项是否触碰 |
| --- | --- | --- | --- |
| ① 排序键插值 | 0.2% | 可 | C 追加一步（§4） |
| ② 构造排序行 | 15.0% | 可 | C |
| ③ `List.Sort` | 33.2% | 可 | C |
| ④ `AddEdge` 写图 | **51.6%** | **必须串行** | **A、B** |

⚠️ **`c² → c` 会放大 ④ 的相对占比**：P0 降低②③的**行数**，而最终边数**就是 `c²p`**
（`c` 倍重复不可删，每条带各自 `callSiteContext`）⇒ ④ 的 `AddEdge` 调用数**不变**。
故 **A、B 是 P0 之后该 pass 的主要可优化面**。
⚠️ 但注意二者确定性不同：**B2 的收益机制有源码直接支撑**（每边 2 次 `AddNode` + 2 次
`ResolveOrdinal` 确实存在），而 **A 的收益尚待 §2.3 的生产点计数裁决（可能为零）**。

### 1.2 ④ 段每条边的完整调用链（三处开销，逐一对应 A/B）

```csharp
// NLCPGBuilder.cs:3090-3095（FlushParallelPublishWindow 的串行发布循环内）
graph.AddEdge(
  edge.SourceNode,
  edge.TargetNode,
  NLCPGEdgeKind.InterproceduralDataFlow,
  NLCPGEdgeLabel.ForInterproceduralBridge(BridgeKindOf(edge)),   // ← A：每条边 new 一个标签
  callSiteContext: callSiteContext);
```

`NLCPGGraph.AddEdge`（`:485-502`）内部：

```csharp
EnsureMutable();
var materializedSource = AddNode(source);   // ← B：查锚点字典 + 可能的 MergeNode 写回
var materializedTarget = AddNode(target);   // ← B：同上
if (_anchorDiscoveryObserver is not null) { return; }   // ← ⚠️ 锚点发现模式在此提前返回
_pendingEdges.Add(
  ResolveOrdinal(materializedSource),   // ← B：第二次查锚点字典
  ResolveOrdinal(materializedTarget),   // ← B：同上
  kind, structuredLabel, contextId, callSiteContext);
```

---

## 2. A：标签三单例化

### 2.1 现状

`NLCPGBuilder.cs:3094` 在**行循环内部**调用 `NLCPGEdgeLabel.ForInterproceduralBridge(...)`。
`NLCPGEdgeLabel` 是 **sealed record（引用类型）**，`ForInterproceduralBridge`（`NLCPGEdgeLabel.cs:60-63`）
**每次 new 一个实例**。

**取值空间只有 3 个**（`NLCPGInterproceduralBridgeKind` 的
`ArgumentToParameter` / `ReturnToMethodReturn` / `MethodReturnToCallResult`；
第 4 个 `SummaryMapping` 走**另一条**路径 `:3421` 的 `ForFlowSummaryBridge`，**不在本项范围**）。

### 2.2 为什么安全（**必须逐条核实，这是 A 的全部正确性依据**）

| # | 事实 | 位置 | 含义 |
| --- | --- | --- | --- |
| 1 | `NLCPGEdgeLabel` 是**不可变**且**无身份语义** | `NLCPGEdgeLabel.cs:9-42`（私有构造 + 只读属性） | 两个值相等的实例**不可区分** |
| 2 | pending 池的 `EdgeMetadata` 是 **`record class`** ⇒ **按值**比较 | `NLCPGGraph.cs:1340-1343` | 共享实例**不改变** `_metadataPool` 的命中集合 |
| 3 | `CanonicalEdgeStore` 只投影 Label 的**字段值** | `CanonicalEdgeStore.cs:85-87`、`:142-144` | 投影结果逐字段相同 |

⇒ **最终 `GraphSnapshotVersion`、边集、边序不变。**

### 2.3 ⚠️ A 的收益**只有一处**：消除生产点的逐边分配

> **我先前的两版说法都过头了**，两处订正如下。

**订正一（"pending 池几乎不命中"是错的）**：`EdgeMetadata`（`NLCPGGraph.cs:1340-1343`）
是 **`record class`**，`Dictionary<EdgeMetadata,int>`（`:1139`）走 record **自动值相等**
⇒ **pending 池本来就能命中**，与 Label 是否共享实例**无关**。

**订正二（"收敛索引身份数 / `StableKey` 插值"也是错的）**：
`progress.md:3688-3690` 记载，我（上一轮会话）曾据
`3,954,145 = 1 + 3,954,144` 断定"`NLCPGEdgeLabel` 每条边新分配"，
**实测标签不同实例=4,240、不同值=3 ⇒ 标签早已共享，假设完全错误**。

该实测口径 = `BuildMetadataRanks` 阶段 A 的输入 `NLCPGEdge[] edges`，
而该数组的 Label **来自 pending 池**（`_metadataById[...].StructuredLabel`，
`NLCPGGraph.cs:1293-1299`）⇒ 池已按 `EdgeMetadata` 值去重，
故索引侧**本来就只见 4,240 个实例**。于是：

- `EdgeMetadataIdentityComparer` 的 `ReferenceEquals`（`NLCPGGraphIndex.cs:126`）
  **早已**合并到池粒度 ⇒ **A 不会降低阶段 A 身份数**（真正的熵源是
  `ContextId.Value` 的逐边插值，已由 N3-d 修复，见 `progress.md:3652-3664`）；
- `StableKeyOf` 的记忆化（`:1051-1059`）与 `_encoded` 缓存（`:1447`）
  **本来就只插值约 4,240 次** ⇒ **A 不会降低 `StableKey` 插值次数**。

⇒ **A 的收益收缩为唯一一项**：`NLCPGBuilder.cs:3094` 处的
`NLCPGEdgeLabel.ForInterproceduralBridge(...)`（`NLCPGEdgeLabel.cs:60-63`）
**在行循环内无条件 `new`**，每条桥边分配 1 个标签对象；
这些实例**除每个池条目的首个之外全部即弃**（池命中时被丢弃）。
单例化后该分配**归零**。

⚠️ **这与 `progress.md:3689` 的"假设完全错误"并不矛盾**：那次推翻回答的是
"**是谁制造了阶段 A 的身份熵**"（答案：`ContextId.Value`，**不是**标签），
其 4,240 是**池保留的实例数**，**看不见生产点被丢弃的分配**。
⇒ 本次不是推翻那次实测，而是**指出它回答的是另一个问题**。

⚠️ **但本轮不据此断言 A 有效**（理由见下）——本节结论**未经计数验证**。
本仓有**同名教训**（`progress.md:3686-3690` "教训 15"：**算术拟合得越漂亮越危险**，
必须**对每个候选成分单独计数**）。故 **A 的第一件事是补一次生产点计数**：

> **A 的前置判据（唯一，且必须先做）**
> 在 `NLCPGBuilder.cs:3094` 处对 `ForInterproceduralBridge` 的调用**计数**
> （或对 `NLCPGEdgeLabel` 构造计数），与 `TotalInterproceduralBridges` 对照。
> 若计数 ≈ 桥边数 ⇒ A 成立；若计数 ≈ 3（或 ≈4,240）⇒ **A 已被上游共享，立即放弃本项**。
> ⚠️ 该计数**必须采在生产点**；采在池内或索引侧会**重犯** `progress.md:3689` 的误判。

### 2.4 收益量级（**算术推算，未实测；且以 §2.3 前置判据成立为前提**）

对象宽度：`NLCPGEdgeLabel` 有 6 个实例字段
（`NLCPGEdgeLabel.cs:32-42`）——**3 个可空枚举**（`NLCPGInterproceduralBridgeKind?`、
`NLCPGDecisionRelationKind?`、`FlowSummaryResolution?`，各 8 B）**+ 3 个引用**
（`FlowSummaryMethodKey`、`FlowSummarySource`、`FlowSummaryTarget`，各 8 B）
⇒ 载荷 48 B + 对象头 16 B = **64 B/实例**（算术）。
⚠️ 该宽度**未实测**，且**不受 A 的取值空间收缩影响**（单例化只减少**实例数**，
不改变**单实例宽度**）⇒ 断言不可锁 64 B。

```
Item.cs 跨过程桥边数 = 9,679,406（既有探针实测，Build/AB-8B-vs-216B/probe-single/summary.json）
⇒ 9,679,406 × 64 B ≈ 619.5 MB   ← 算术推算，未实测；只覆盖"生产点对象分配"这一项
```

⚠️ **该 619.5 MB 不可作为本项收益结论**，三条理由：
① 口径是**累计分配**、单文件；② 实例宽度 64 B **未实测**；
③ **其成立以 §2.3 的前置判据为前提**——若生产点计数显示标签已被共享，则**收益为零**。

### 2.5 改动清单（封闭）

| 位置 | 改动 |
| --- | --- |
| **前置（判据）** | 在 `NLCPGBuilder.cs:3094` 生产点**计数**，对照 `TotalInterproceduralBridges`（§2.3）——**不成立则放弃整个 A 项** |
| `NLCPGEdgeLabel`（`NLCPGEdgeLabel.cs`） | **新增** 3 个 `internal static readonly` 单例（或 `ForInterproceduralBridge` 内做 3 路 `switch` 返回缓存实例——二选一，**推荐前者**：调用点零分支） |
| `NLCPGBuilder.cs:3094` | 改为引用单例；`BridgeKindOf(edge)` 仍用于选择**哪一个**单例 |

**净变化**：约 +12 行，−0 行，无新增文件。

⚠️ **改动本身无需额外等价性证明**：由 §2.2 第 1、3 条，标签不可变且**索引侧只见池内实例**
（§2.3 订正二）⇒ 单例化后池内实例从"每池条目各 1 个"变为"每桥种类各 1 个"，
**值划分不变**，冻结哈希必逐位相同。这**使 A 的实现风险很低**——
**A 的真实风险在收益侧（可能为零），不在正确性侧**。

⚠️ 另须确认 `NLCPGEdgeLabel` 无任何**期望实例互异**的依赖方。全仓检索
`ReferenceEquals` + `NLCPGEdgeLabel` 的结果**只在产品代码**
（`NLCPGGraphIndex.cs:126`、`:258-261` 的 `ReferenceComparer`），**无测试依赖** ⇒ 安全。

### 2.6 **不**要顺手做的改动

| 诱惑 | 阻止理由 |
| --- | --- |
| 把 `SummaryMapping` 也单例化 | `ForFlowSummaryBridge`（`:3421`）带 `MethodKey`/`Source`/`Target`，**取值空间不是常数** ⇒ 需按内容缓存，**另属一项** |
| 给 `NLCPGEdgeLabel` 加 `GetHashCode` 缓存字段 | 会改类型布局与宽度，且该类**可能**被外部按值依赖 ⇒ 越界 |
| 把 `NLCPGEdgeLabel` 由 class 改 struct | `record class` 的**引用语义**被 `ReferenceEquals` 依赖（`NLCPGGraphIndex.cs:126`）⇒ 会整体改变身份判定 |

---

## 3. B：按序数直通 `AddEdge`

### 3.1 现状与动机

发布段的端点 `edge.SourceNode` / `edge.TargetNode` 来自**冻结边快照池**
（`edgeSnapshot.Edge(plan.PoolOrdinal)`，`NLCPGBuilder.cs:3089`），
而该池由 `EnumeratePendingEdgesLazily()`（`NLCPGGraph.cs:188`）构建 ⇒
**端点在池建立时就已经是图内的节点**。

然而 `AddEdge` 仍对两端各做一次 `AddNode`（`:488-489`）与一次 `ResolveOrdinal`（`:496-497`）。
`AddNode`（`:271-292`）在**命中**已存在锚点时走 `MergeNode` 分支（`:289-291`）：

```csharp
var merged = MergeNode(_nodesByOrdinal[existingOrdinal], materializedNode);
_nodesByOrdinal[existingOrdinal] = merged;   // ← 写回
return merged;
```

`ResolveOrdinal`（`:505-508`）再次以 `_mutableNodesByAnchor` 反查序数。
⇒ 每条边 **2 次字典查找 + 2 次可能的 104 B 结构改写 + 2 次字典反查**。

### 3.2 ⚠️ 真正的风险**不是**"锚点表冻结"，而是 `MergeNode` 的**写回非幂等**

> **我此前的说法**："需证锚点表冻结"。**方向对，但点错了。**
> 锚点表冻结只保证 `ResolveOrdinal` 的**结果**稳定；真正的危险在 `MergeNode`。

`MergeNode`（`:902-916`）的每个字段都是 **`candidate.X != 0 ? candidate.X : existing.X`** ——
**candidate 非空时【覆盖】existing**：

```csharp
FullNameId = candidate.FullNameId == 0 ? existing.FullNameId : candidate.FullNameId,
```

⇒ **`MergeNode` 不是幂等/可交换的**：若 `existing` 在早先已被**另一个** candidate
填入了更"完整"的值，此时再重放一个**较旧、字段较少但非零**的 candidate，
**会把这些字段覆盖回去**。

**这直接威胁 B 的正确性**：B 跳过 `AddNode` ⇒ 跳过这次重放 ⇒
**若该重放本来会改变节点**，B 就**改变了图**。

⚠️ **必须如实记录**：我**无法**仅凭静态阅读判定"池内端点在发布期是否会被重放到改变状态"。
这需要 §3.3 的证明或 §3.4 的规避。

**另有两个必须先处理的事实**：

1. **锚点发现模式**（`_anchorDiscoveryObserver is not null`）：`AddEdge`（`:490-493`）
   在两次 `AddNode` **之后**提前返回，**不写 pending 边**，且 `AddNode`（`:276-280`）
   **只回报、不存储**。B 的直通路径**必须逐位复刻**这一分支，
   否则预检 builder 的行为会变。⚠️ 预检 builder 经 `CreateAnchorDiscoveryOptions`（`:1563-1578`）
   构造，**保留** `RequestedCapabilities`（`:1570`）⇒ **很可能**会跑到本 pass。**必须核实**。
2. **发布期确实会新增节点**：`AddExternalSummaryMappings`（`:3349`）→
   `ResolveInvocationEndpointNode`（`:3431`）→ `GetOrCreateOperationNode`（`:3773`）→
   `graph.AddNode(...)`（`:3804` 等）。它在**逐调用点循环内**被调用（`:2780/2794/2802`），
   故 **`_mutableNodesByAnchor` 在发布循环期间会增长**。

### 3.3 B 的**证明义务**（任一不满足即**不得**实施 B）

| # | 义务 | 为何必须 |
| --- | --- | --- |
| **B-1** | **池内端点全集在发布期内不因 `MergeNode` 而改变**（即重放恒为 no-op） | §3.2 的覆盖语义 |
| **B-2** | 池内端点的**构图期序数**在发布期内不变 | `ResolveOrdinal` 的稳定性；注意 §3.2 第 2 点表明**新增**节点不改变**既有**序数（`_nodesByOrdinal.Add` 只追加），故本条**很可能是真的**——但**必须**显式确认没有任何 `Remove`/重排 |
| **B-3** | 锚点发现模式下直通路径**逐位复刻**"`AddNode` → observer → return，不写边" | §3.2 第 1 点 |
| **B-4** | `EnsureMutable()` 的两个守卫（`:1090-1124`，冻结检查 + L0/L1 分层窗口）在直通路径上**照旧执行** | 否则绕过"唯一收口"——该守卫的**设计意图**就是"新入口不可能绕过"（`:1097-1099`） |

⚠️ **B-4 尤其重要**：`EnsureMutable` 的注释明确宣称它是**全部构图入口共用的唯一收口**。
B **必须**调用它，否则会**破坏该不变量的设计承诺**，即使当下不触发也应视为缺陷。

### 3.4 两条路线

| 选项 | 做法 | 收益 | 风险 | 建议 |
| --- | --- | --- | --- | --- |
| **B1** | 池构建时**一次性**把每个端点解析为构图期序号，新增 `AddEdgeByOrdinal(srcOrd, tgtOrd, ...)` 直通 | 省**全部** 2×`AddNode` + 2×`ResolveOrdinal` | **需 B-1..B-4 全部成立** | 仅在 §3.3 被**证明**后采用 |
| **B2（推荐先做）** | 保留 `AddNode` 调用，但让其走**引用/锚点直通**：命中锚点时**跳过 `MergeNode` 写回**（只返回 existing），并以"重放恒为 no-op"**可断言**的方式固化 | 省 2×`MergeNode` 结构改写 + 2×`ResolveOrdinal`（若同时缓存序数） | **低**：不改变"是否新增节点"的判定 | **先做 B2**：它把 B1 的收益里**最安全的那部分**拿走，并把 B-1 变成**可观测断言** |

**B2 的关键设计**：不靠注释声称"重放是 no-op"，而是**显式检测**——
在命中锚点且 `merged` 与 `existing` **逐字段不同**时，维持**原有写回行为**
（即"慢路径"），仅当**确实相同**时才跳过写回。这样 B2 **在任何语料上都逐位安全**，
**无需 B-1 的前置证明**，而收益在"重放恒为 no-op"的真实语料上**自动兑现**。

> ⚠️ B2 的代价：需要一次 `NLCPGNode` 的**逐字段相等比较**（其 `Equals` 已有
> `StableAnchor`/`NodeId` 短路，见 `NLCPGNode.cs:20-31`）⇒ 用 `existing.Equals(merged)`
> 判断即可。⚠️ 但 `Equals` 的短路分支意味着"字段不同但锚点相同"会被判**相等**
> ⇒ **B2 必须用逐字段比较而非 `Equals`**，否则会漏掉真实差异。**这是 B2 最容易写错的地方。**

### 3.5 改动清单（以 **B2** 为准）

| 位置 | 改动 |
| --- | --- |
| `NLCPGGraph.AddNode`（`:271-292`） | 命中锚点分支：先算 `merged`，**逐字段比较** `merged` 与 `existing`；相同则**跳过写回**并返回 `existing`，不同则**保持现状** |
| `NLCPGGraph.AddEdge`（`:485-502`） | 可选：把 `ResolveOrdinal(materializedX)` 改为 `AddNode` **返回序数**（消掉 2 次反查）。二选一，**推荐本项独立提交** |
| `NLCPGGraph`（新增） | 可选：`AddEdgeByOrdinal`（**仅 B1**，B2 不做） |

**净变化（B2）**：约 +10 行（逐字段比较），−0 行。

---

## 4. C：排序键排名化（**摘要；完整论证见 C 专文**）

### 4.1 内容

在既有阶段 ① 之后追加**唯一键升序 + 稠密排名** ⇒ `Dictionary<NLCPGNode,int> nodeSortRanks`；
`PlanSortRow` 的 `string SourceKey`/`TargetKey` 改为 `int SourceRank`/`TargetRank`；
比较器后两键改为整数比较。

### 4.2 与本文件 A/B 的接口约束

| 约束 | 理由 |
| --- | --- |
| C **只**改②③段（行载荷与比较器），**不得**触碰 ④ 段 | A/B 改 ④ 段；混改会让红/绿**不可归因** |
| C 的排名表构造**必须在任何 `AddEdge` 之前** | 阶段 ① 已满足（`:2683-2723` 在发布循环之前）；B2 不改变该时序 |
| C **不得**依赖 `NLCPGEdgeLabel` 实例身份 | A 会把 Label 变为单例；C 的排名基于**节点**，与 Label 无关（无冲突，但须确认无隐式耦合） |
| C 的 fail-hard 选择（C 专文 §3.2）**不得**影响 B2 的"慢路径" | 二者独立 |

### 4.3 顺序

**A → B2 →（P0 落地后）→ C**。

- A 与 B2 **都改 `:3090` 邻域** ⇒ **建议同批、分两次提交**以便独立回退；
- C 改**另一段**，但**必须在 P0 之后**（行数被 P0 降 `c` 倍，否则 C 的收益无法归因）。

---

## 5. 改动文件汇总

| 文件 | A | B2 | C |
| --- | --- | --- | --- |
| `src/NLCPG/Model/NLCPGEdgeLabel.cs` | **改**（+3 单例） | — | — |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | **改**（`:3094`） | — | **改**（`:2667-2723`、`:3298-3341`、`:3225-3270`、`:3275-3286`） |
| `src/NLCPG/Model/NLCPGGraph.cs` | — | **改**（`:271-292`） | — |

**无新增产品文件。**

---

## 6. 测试修订清单（分级）

### 6.1 **必红**（计划内修订）

| 项 | 文件:符号 | 内容 | 级别 |
| --- | --- | --- | --- |
| C | `CpgInterproceduralEdgeOrderTests.PlanSortRow_CarriesPlanIndexInsteadOfFullPlan`（`:183-227`） | 字段集断言（`:196-200`）、宽度 `Assert.Equal(32, ...)`（`:223`） | **断言级红**——**详见 C 专文 §5.1** |
| A | 无 | — | — |
| B2 | 无 | — | — |

### 6.2 **会静默变成空断言**（**必须主动修**）

| 项 | 文件:符号 | 为何失效 |
| --- | --- | --- |
| **A** | — | 无已知空断言。⚠️ 但**必须**确认不存在"断言 Label 实例不共享"的测试：全仓检索 `ReferenceEquals` + `NLCPGEdgeLabel` 的结果**只有产品代码**（`NLCPGGraphIndex.cs:126`、`:258-261`），**无测试** ⇒ 安全 |
| **C** | `InterproceduralPlanCompactionTests.PublishedSlots_ReleaseSortRowKeyReferences`（`:532-544`） | 立论是"行扣住**字符串**引用"；排名化后行内无字符串 ⇒ **详见 C 专文 §5.2** |
| **B2** | — | 无已知空断言。⚠️ B2 把"重放是 no-op"变成**可观测**（跳过写回的分支被走到）⇒ 建议**新增**计数以便未来断言（§6.3 N4） |

### 6.3 **必须新增的护栏**

| # | 项 | 建议名称 | 判别内容 |
| --- | --- | --- | --- |
| **N1** | C | 排名同序性 | `rank(a).CompareTo(rank(b)) == Math.Sign(string.CompareOrdinal(keyA, keyB))`，且等键 ⇒ 等排名。**C 专文 §5.3 N1** |
| **N2** | C | 行载荷不含字符串 | 反射 `PlanSortRow`，断言**无 `string` 字段**。**C 专文 §5.3 N2** |
| **N3** | C | 覆盖完备性 | 排名表覆盖池端点全集。**C 专文 §5.3 N3** |
| **N4** | **A** | 桥标签实例受控 | 对同一 `BridgeKind` **多次**取标签，断言**同一实例**（`ReferenceEquals`）。这是 A 的**唯一直接判据**——否则"三单例"只是注释里的说法。⚠️ 本判据**只证明实现正确**，**不证明收益**（收益由 §2.3 的生产点计数裁决） |
| **N5** | **A** | 标签取值空间 | 断言 `ForInterproceduralBridge` 对 3 个桥种类各返回**非 null** 且**两两不同**的实例（防"退化成 1 个单例"这一真实误写） |
| **N6** | **B2** | 跳过写回不改图 | 构造"同一节点重复 `AddNode`"序列，断言最终 `_nodesByOrdinal` 中该节点**逐字段**与首次相同（含内部字段，**不得**用 `Equals` 的短路语义）。这是 B2 的**唯一直接判据** |

> ⚠️ **N4/N5 必须用 `ReferenceEquals` 断言**，不能用 `Equals`/`==`——
> `NLCPGEdgeLabel` 是 record，值相等**恒真**，那样的断言**没有判别力**。
>
> ⚠️ **N4/N5 与 §2.3 的计数都不能省一项**：N4/N5 护住"实现写对了"，
> §2.3 计数护住"收益真的存在"。**两者互不可替代**。

### 6.4 **必须保持不变且继续通过**（冻结 oracle，**禁止修改**）

| 文件 | 内容 | 护住谁 |
| --- | --- | --- |
| `CpgInterproceduralEdgeOrderTests.cs:65-141` | 70 行冻结**原始插入序** | A、B2、C（**主判据**） |
| 同上 `:143-174` | 保序 / 跨重复构建稳定 / 总边数稳定 | A、B2、C |
| 同上 `:234-254` | `DeriveContextIdFromCallSiteContext` | A、B2（`callSiteContext` 仍逐调用点传入） |
| `DataFlowPlanConstructionReuseTests.cs:43-63` | 三个 `NormalizedGraphHash` + `PublicationHash` | **A**（身份/值阶段不变）、B2、C |
| `DataFlowCandidateScanPruningTests.cs:46-54` | 三个 `PublicationHash` | 同上 |
| `InterproceduralEdgeIndexSnapshotTests`（"导出与三段产生者一致" Fact） | `BridgeKindOf` 的三段一致性 | A（单例选择依据） |
| `InterproceduralPlanCompactionTests.cs:74-115` | 载体字段集 + 宽度 `== 8` | C（不得连带改载体） |
| `InterproceduralPlanCompactionTests.cs:442-517`、`:567-734` | 槽回收纯函数直测 + 压力 | C（**详见 C 专文 §5.4**） |
| `NLCPGNodeIdContractTests.cs:190` | 载体非公开值类型 | C |

---

## 7. 验收（本轮按用户指示裁剪）

### 7.1 门槛

**只做 10% 核心测试。不做任何前置测试**（无冻结基线复采、无 A/B、无探针、无变异验证）。

| 判据 | 内容 |
| --- | --- |
| 构建 | `NLCPG.csproj` **0 警告 0 错误** |
| **主判据（等价性）** | §6.4 全部**冻结 oracle** 逐字节通过：70 行边序、`NormalizedGraphHash`/`PublicationHash`、`GraphSnapshotVersion`、节点/边数 |
| 核心定向 | §6.1 修订 + §6.3 新增全部通过 |
| **A 的前置判据** | §2.3 的**生产点计数**必须先成立（计数 ≈ 桥边数）；**不成立即整个 A 项作废** |
| **A 的判别力** | N4/N5 必须**先立**——否则 A 的收益机制无护栏 |
| **B2 的判别力** | N6 必须**先立**——否则"跳过写回不改图"只是注释 |
| 未验证边界 | §9 逐条如实登记 |

⚠️ **本轮不产出任何耗时或倍数结论**。理由不是"没空做"，而是**本机墙钟不具备该判别力**：
`BASELINE.md` §5.4 实测同二进制同输入三次运行差 **16%–71%**；DoD#4 阳性对照
（注入已知 +3.5% 忙等）CI **跨 0**；反推分辨 3% 需约 **41 对**、1% 需约 **361 对**。
⇒ 若将来要**主张**收益，唯一有判别力的口径是 `allocBytes` **配对**（配对差 <0.05%）。

### 7.2 建议的聚焦过滤器

```
FullyQualifiedName~CpgInterproceduralEdgeOrderTests
FullyQualifiedName~InterproceduralPlanCompactionTests
FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests
FullyQualifiedName~CpgWorkBatchInterproceduralTests
FullyQualifiedName~NLCPGNodeIdContractTests
FullyQualifiedName~DataFlowPlanConstructionReuseTests
FullyQualifiedName~DataFlowCandidateScanPruningTests
```

### 7.3 命令

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~InterproceduralPlanCompactionTests|FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests|FullyQualifiedName~CpgWorkBatchInterproceduralTests|FullyQualifiedName~NLCPGNodeIdContractTests|FullyQualifiedName~DataFlowPlanConstructionReuseTests|FullyQualifiedName~DataFlowCandidateScanPruningTests'
```

### 7.4 建议的分步提交（红/绿可归因）

| 步 | 内容 | 提交后应全绿 |
| --- | --- | --- |
| 0 | **§2.3 生产点计数**（判据） | 是（未改产品） |
| 1 | **N4/N5**（A 的护栏，先立） | 是（尚未改产品） |
| 2 | **A** 产品改动（**仅当第 0 步成立**） | 是 |
| 3 | **N6**（B2 的护栏，先立） | 是 |
| 4 | **B2** 产品改动 | 是 |
| 5 | **C**（P0 落地后；含 C 专文 §5.1/§5.2 修订 + N1/N2/N3） | 是 |

⚠️ **第 0 步必须先做**：本仓有直接教训（`progress.md:3686-3690`，教训 15）——
"标签每条边新分配"这一**算术上很漂亮**的假设曾被实测推翻。
⇒ **A 不做计数就实施，就是在重犯该教训**。

⚠️ **第 1、3 步"先立护栏再改产品"不是形式**：A 的收益机制（实例共享）与
B2 的收益机制（跳过写回）**都不会通过任何既有断言失败来暴露**——
它们只在**行为改变**时才会红，而收益本身**不可观测**。
⇒ 不先立 N4/N5/N6，就无法区分"优化生效"与"优化没写对"。

---

## 8. 与其它切片的关系

| 切片 | 关系 |
| --- | --- |
| [P0 `c² → c`](2026-09-28-interprocedural-argument-segment-reuse-execution.md) | A、B2 **独立**；C **必须后置** |
| [C 排序键排名化](2026-09-28-interprocedural-sort-key-ranking-execution.md) | 本文 §4 是其摘要；**冲突以该文为准** |
| **D 反向字典**（`AddExternalSummaryMappings` 的 `FirstOrDefault` 按值全表扫描，`:3359-3360`） | **不在本文范围**。⚠️ 但 B 的 §3.2 第 2 点（发布期会新增节点）与 D **同源** ⇒ 若做 D，**必须**与 B 一并考虑锚点表增长的时序 |
| `SummaryMapping` 标签（`:3421`） | **不在** A 的范围（取值空间非常数） |

---

## 9. 未验证边界与风险（**不得越读**）

1. **本轮零执行** ⇒ 全文结论均为**源码阅读 + 引用既有实测**。
   §2.4 的 `619.5 MB`、`64 B/实例` 是**算术推算**；`9,679,406`、`3,954,144`、
   `3,954,145 → 4,241`、`51.6%/33.2%` 来自**既有**实测与源码注释。
2. **A 的收益**是本文**最不确定**的一项（比 B 的 B-1 更不确定）：
   §2.3 的结论"标签在生产点逐边新分配"**是读源码得出的，未经计数**。
   既有实测（`progress.md:3689`：不同实例=4,240、不同值=3）**只覆盖池内实例**，
   按本文分析**看不见生产点的即弃分配**——但该分析**本身也可能错**。
   ⇒ **A 必须由 §2.3 的生产点计数先行裁决**；在计数出来之前，A 的收益视为**未知（可能为零）**。
   ⚠️ 本文**不**声称"已推翻 `progress.md:3689`"——只声称它回答的是**另一个问题**（谁制造身份熵）。
3. **B 的 B-1 未被证明**（§3.2）。这是本文**最重要的未决问题**：
   池内端点在发布期是否会因 `MergeNode` 重放而改变，**我无法仅凭静态阅读判定**。
   ⇒ 这是**推荐 B2 而不是 B1** 的直接原因；若选择 B1，**必须**先完成 §3.3 的 B-1..B-4。
4. **§3.2 第 1 点（锚点发现模式是否会跑到本 pass）未核实**。预检 builder 保留
   `RequestedCapabilities`（`:1570`），故**很可能**会；但**未验证**。
   ⇒ B **必须**处理该分支（B-3），无论是否可达。
5. **B-2 很可能为真但未验证**：`_nodesByOrdinal.Add` 只追加（`:285`），
   未见到 `Remove`/重排，故既有序数应稳定；但**未逐条穷尽**。
6. **B2 的"逐字段比较"必须避开 `NLCPGNode.Equals` 的短路语义**（`NLCPGNode.cs:20-31`：
   `StableAnchor` 或 `NodeId` 存在时**只**比该字段）⇒ 用 `Equals` 会**漏掉真实差异**，
   使 B2 **静默改变图**。这是 B2 实现中**最容易写错**的一处（§3.4 已就地标注）。
7. **§6.3 N1/N2/N3 的完整设计在 C 专文**，本文不重复；C 专文自身的边界同样适用。
8. **未做**：多文件/全语料、多 DOP、跨 Build 复用、常驻峰值、GC 次数、墙钟。
9. **并发会话**：三个产品文件都在被其他会话改写。实施前须**确认文件哈希**
   与本文行号基准一致；不一致时**以符号名为准**并重读。
10. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)。
    `Context/feature_list.json` 是 feature 状态**唯一**来源；本文件**不**修改任何 feature 状态，
    **也不构成验收证据**。

---

## 10. 执行结果（2026-09-28，**A + B2 已落地；C 未做**）

> ⚠️ **本轮明确不产出任何耗时/倍数/内存结论**（§7.1 已裁定本机墙钟无判别力）。
> 下面只有**等价性**与**机制存在性**两类证据。

### 10.1 落地范围

| 项 | 状态 | 说明 |
| --- | --- | --- |
| **A** 标签三单例化 | ✅ 已实现 | 分配点由"每桥边 1 次"降为"每桥种类 1 次" |
| **B2** 冗余写回短路 | ✅ 已实现 | 命中锚点且 `MergeNode` 不改变任何字段时跳过写回 |
| **C** 排序键排名化 | ⛔ **未做** | **P0（`c²→c`）未落地**：`PlanSortRow` 仍是 `string SourceKey`/`string TargetKey`（`NLCPGBuilder.cs:3399-3400`）⇒ 按 §4.3「只做 A+B」 |

**改动文件**（净 +164 行 / −2 行）：

| 文件 | 净变化 |
| --- | --- |
| `src/NLCPG/Model/NLCPGEdgeLabel.cs` | +36 −2（3 个 `internal static readonly` 单例 + 工厂 3 路 `switch`） |
| `src/NLCPG/Model/NLCPGGraph.cs` | +130（B2 短路 + 逐字段比较 + `RedundantMergeSkipCount`） |
| `tests/NLISSN.ContractTests/Cpg/NLCPGEdgeLabelIdentityContractTests.cs` | **新增 161 行**（N4/N5，7 个用例） |
| `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdentityContractTests.cs` | N6-a/b/c/d（4 个用例） |

**未新增** `InternalsVisibleTo`（沿用既有友名 `RoslynDeletionPrototype.ContractTests`），
**未改** `SchemaVersion`、**未改**哈希算法、**未新增**产品文件。

### 10.2 A 的实现（与 §2.5 的一处偏差，理由如下）

§2.5 在「`internal static readonly` 单例」与「`ForInterproceduralBridge` 内做 `switch` 返回缓存实例」
之间**推荐前者**（调用点零分支）。本轮**两者都做**：字段按 §2.5 取 `internal static readonly`，
**同时**把 `switch` 留在工厂内。理由：

- **工厂内缓存覆盖第二个调用方**。§2.5 只提到 `NLCPGBuilder.cs:3094`，但
  `CpgFrozenShardGraphReader.cs:283`（分片反序列化）**也**调用本工厂。把选择逻辑留在工厂，
  两个调用方都受益，且**不会因将来新增调用点而漏掉**——这恰是 A 的唯一收益机制所在。
- **可见性取 `internal`**（而非 `private`）：使 N4/N5 能直接断言"三个单例本身两两不同"，
  而不必只经工厂间接推断。
- 调用点**未改**（仍是 `ForInterproceduralBridge(BridgeKindOf(edge))`）⇒ 零分支的诉求同样满足。

### 10.3 ⚠️ A 的收益**仍未证明**（如实登记）

§7.1 与 §9 第 2 条要求 A 以 §2.3 的**生产点计数**先行裁决，否则收益视为**未知（可能为零）**。
本轮按用户指示「**不做任何前置验证**」⇒ **该计数未做**。

⇒ **A 的当前状态是"实现已正确落地、收益未经验证"**：
`NLCPGBuilder.cs:3094` 处原先每条桥边一次 `new`，现在恒定返回 3 个共享实例之一——
这是**源码层面确定的事实**；但这些实例**在改动前是否已被上游去重**，
**本轮未测**。若已被去重，则 A 的收益**为零**（此时 A 不造成损害，只是白做）。

### 10.4 B2 的收益机制**已在真实路径上证实**（强于 §7.1 的最低要求）

§7.1 只要求 N6「跳过写回不改图」。本轮**额外**加了 N6-d：在真实构建路径上断言短路**确实被走到**。

**实测（DOP=1，最小跨过程夹具）**：

```
edges = 245,  nodes = 101,  RedundantMergeSkipCount = 546   （约 2.2 次/边）
```

546 ≈ 2 × 245 与 §1.2「每条边 2 次 `AddNode`（source + target）」**逐一对应** ⇒
B2 的收益机制**不是注释、也不依赖我手写的合成序列**，它在真实发布路径上成立。

⚠️ 这**只是机制存在性**，**不是收益大小**：省下的字节/纳秒本机无判别力（§7.1）。
⚠️ 短路**未**消除 `AddEdge` 末尾的 2 次 `ResolveOrdinal` 反查（那属 B1，本轮不做）。

### 10.5 测试证据（定向 10% 核心子集）

```
--filter（§7.2 七个 + 本轮新增三个）
  → 失败 1，通过 142，总计 143

--filter NLCPGEdgeLabelIdentityContractTests|NLCPGNodeIdentityContractTests
  → 已通过! 失败 0，通过 22，总计 22
```

**唯一失败是既有失败，与本轮无关**——已用对照实验证明：

1. 在 **pristine HEAD**（未加 A/B2）的独立 git worktree 上单跑
   `CpgInterproceduralEdgeOrderTests` ⇒ **同样失败**，且差异**逐字节相同**
   （`156:167` vs `167:178`，正是 §9/§6.4 登记的既有失败）。
2. 加上 A+B2 后，失败集合**仍只有这一条**，差异不变。

⇒ **本轮改动新增失败数 = 0。**

### 10.6 护栏判别力（含一条**反向**验证）

| 变异 | 结果 |
| --- | --- |
| 在 worktree 中 revert 产品改动、保留新增测试 | **编译失败** `CS0117 NLCPGEdgeLabel 未包含 ArgumentToParameterBridge`、`CS1061 NLCPGGraph 未包含 RedundantMergeSkipCount` ✅ ⇒ 护栏确实挂在产品符号上，不是恒真断言 |

### 10.7 施工环境（**必须如实记录**）

`NLCPGGraph.cs` 在实施期间被并发会话**反复改写**（本轮至少观测到 3 次，
行数 1,437→1,487→1,490），我的 B2 编辑**曾被整段覆盖一次并重新施加**。
`NLCPGBuilder.cs` / `InterproceduralDataFlowPlanGroup.cs` 亦被并发改写，
期间主树**一度无法编译**（10–12 个错误，全部位于这两个文件，
`NLCPGGraph.cs`/`NLCPGEdgeLabel.cs` **0 错误**）。

⇒ 为取得**不被并发改动污染**的证据，本轮在 HEAD 的独立 worktree
（`NL-abverify`，已删除）中复现 A+B2 并跑测试；结论与主树一致。

### 10.8 未验证边界（本轮**新增**，接续 §9）

1. **A 的收益未测**（§10.3）——这是本轮**最重要的**未闭合项。
2. **未测**多文件/全语料、多 DOP 下的短路计数（仅 DOP=1 单夹具）。
3. **未测**常驻峰值、GC 次数、`allocBytes` 配对（§7.1 裁定该口径为将来唯一可主张者）。
4. **C 整项未做**（P0 未落地），C 专文全部结论**仍未执行**。
5. `Context/feature_list.json` **未修改**；本切片**未登记**，本文**不构成验收证据**。
6. 并发会话仍在改写同一批文件 ⇒ 本文行号**已被本轮自身改动推移**
   （`NLCPGGraph.cs` 由 1,366 → 1,490 行）⇒ 后续引用**一律以符号名为准**。
