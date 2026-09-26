# Dictionary `Entry<>` 与边缓冲内存优化研究报告

> 状态：**调研进行中（Round 1 完成）**。本文件是循环调研的工作记录，
> 每轮追加实测数据与候选方案；最终汇总为可执行方案清单。
> 外部资料调研由两个子代理并行进行（`.NET Dictionary 内存内部机制` /
> `图边缓冲结构设计`），其完整报告到齐后并入本文并把状态改为 **已完成**。
>
> 目标（用户原话）：
> ```
> 1  Dictionary Entry<>  861.3  53.59
> 2  边缓冲 BufferedPendingEdge  240.0  14.93
> ```
> 即优化这两个峰值堆占用项。要求「网络搜索资料和阅读真实的 github 和本地项目代码，
> 循环 10 轮获取足够的信息，查找优化上述两个问题的方法，解决方法需要具有足够的广度」。

---

> **配套执行计划**：`docs/plans/2026-09-23-dictionary-edge-memory-optimization-execution.md`
> （分阶段落地步骤、前置门禁、回滚点、明确不要做的事）。

## 0. 数据来源与口径声明

| 项 | 值 |
| --- | --- |
| 峰值样本 | `docs/benchmarks/data/version4-dop12-type-share-0016.csv` |
| 采样点 | `0016-late-156s`（t=156 s），21 次 gcdump 中峰值 |
| 堆总量 | **4,048,479,859 B = 3,861 MB = 3.77 GiB**（7,289 个类型） |
| 校验 | 按类型求和与 `GC Heap bytes` 逐点精确相等（21/21，见诊断文档 §对象类型统计） |
| 构型 | `CreateDefault()`，DOP=1，语料 = Terraria `Player.cs`/`Main.cs`/`WorldGen.cs`/`NPC.cs` |

**注意**：本文的 861.3 MB / 240.0 MB 是**改动前**的基线。位集压缩与稀疏位集
（另一条独立工作线）会改变分母，但**不影响这两项的绝对值**（它们是独立数据结构）。

### 0.1 两个目标项的精确定位

| # | 用户口径 | 实测类型 | 字节 | 实例 |
| ---: | --- | --- | ---: | ---: |
| 1 | Dictionary `Entry<>` | `Entry<…>` 全部 145 行求和 | **903,063,988**（861.2 MB，22.31%） | — |
| 2 | 边缓冲 `BufferedPendingEdge` | `NLCPG.Model.NLCPGGraph+BufferedPendingEdge[]` | **251,658,288**（240.0 MB，6.22%） | 2 |

`Entry<>` 内部构成（互斥归类）：

| 类别 | 字节 | MB | 占堆 |
| --- | ---: | ---: | ---: |
| A. 边去重键 `PendingEdgeKey` | 393,481,888 | 375.3 | 9.72% |
| B. 键含 `StableNodeAnchor`(28 B) | 195,336,844 | 186.3 | 4.82% |
| D. Roslyn 引用键 | 194,585,144 | 185.6 | 4.81% |
| C. 键含 `NLCPGNode`(104 B) | 103,678,200 | 98.9 | 2.56% |
| E. 其他 | 15,981,912 | 15.2 | 0.39% |
| **合计** | **903,063,988** | **861.2** | **22.31%** |

---

## 1. Round 1 核心发现：两个问题是同一个问题

**用户列出的两项（861.3 MB + 240.0 MB）在代码上是同一个子系统，
且存在「同一份 120 字节记录被存 4 遍」的结构性冗余。**

### 1.1 实测尺寸（`sizeof`，非估算）

用独立探针程序直接 `sizeof` 测出（net10.0 x64，项目引用 `src/NLCPG`）：

| 类型 | 字节 |
| --- | ---: |
| `StableNodeAnchor` | **28** |
| `StableNodeAnchor?` | 32 |
| `NLCPGNode` | **104** |
| `NodeId` | **4** |
| `NLCPGContextId`（包装 string） | 8 |
| `NLCPGContextId?` | 16 |
| `NLCPGCallSiteContext` | **24** |
| `NLCPGCallSiteContext?` | **32** |
| `NLCPGEdgeKind`（enum） | 4 |
| `NLCPGEdge` | **72** |
| `PendingEdgeKey`（`NLCPGGraph` 私有嵌套） | **120** |
| `BufferedPendingEdge` | **120** |

`Entry<TKey,TValue>` 布局 = `int HashCode` + `int Next` + `TKey` + `TValue`（8 B 头）：

| Entry 实例化 | 字节 | 用途 |
| --- | ---: | --- |
| `Entry<PendingEdgeKey,int>` | **136** | `_keys` HashSet |
| `Entry<StableNodeAnchor,NLCPGNode>` | **140** | `_mutableNodesByAnchor` |
| `Entry<IOperation,NLCPGNode>` | 120 | IOperation→节点 |
| `Entry<SyntaxNode,NLCPGNode>` | 120 | SyntaxNode→节点 |
| `Entry<NLCPGNode,NLCPGNode[]>` | 120 | 邻接 |
| `Entry<NLCPGNode,int>` | 116 | 序数 |
| `Entry<NodeId,NLCPGNode>` | **116** | 若改键 |
| `Entry<NodeId,int>` | **16** | 若改键 |
| `Entry<IOperation,NodeId>` | **24** | 若改值 |
| `Entry<uint,int>` | **16** | 开放寻址 bucket |

### 1.2 反解容量并与运行时精确交叉验证

由 `TotalBytes = 24 + 容量 × EntrySize` 反解：

| 字典 | 字节 | 反解容量 | 反解条目数(×0.725) |
| --- | ---: | ---: | ---: |
| `PendingEdgeBuffer._keys` | 393,481,888 | **2,893,249** | 2,097,605 |
| `_mutableNodesByAnchor` | 195,336,844 | **1,395,263** | 1,011,565 |
| `IOperation→Node`（66 个实例） | 107,207,664 | 893,384 | 647,703 |
| `NLCPGNode→NLCPGNode[]`（128 个） | 58,599,552 | 488,304 | 354,020 |
| `SyntaxNode→Node` | 38,933,904 | 324,449 | 235,225 |
| `NLCPGNode→int`（6 个） | 34,706,880 | 299,196 | 216,917 |

**运行时交叉验证（真实 `new Dictionary<int,int>()` + 反射读 `_entries.Length`）**：

```
count=1,000,000  entries.len=1,395,263  负载=71.7%
count=2,000,000  entries.len=2,893,249  负载=69.1%
count=2,100,000  entries.len=2,893,249  负载=72.6%   ← 与反解值精确吻合
```

⇒ 反解方法可信，`PendingEdgeBuffer` 实际条目数 ≈ **2.10 M 条边**。

`List<BufferedPendingEdge>` 容量反解 = `(251,658,288-24)/120` = **2,097,152 = 2^21**（精确），
故 `_items.Count ∈ (2^20, 2^21]`，容量浪费 ≤ 4.6%（实测 `List` 翻倍策略）。

### 1.3 冗余清单（同一份 120 B payload 存了 4 遍）

`src/NLCPG/Model/NLCPGGraph.cs` L836-909：

```csharp
private sealed class PendingEdgeBuffer
{
    private readonly HashSet<PendingEdgeKey> _keys = new();      // ① 375.3 MB
    private readonly List<BufferedPendingEdge> _items = new();   // ② 240.0 MB
    ...
}

private readonly record struct PendingEdgeKey(        // 120 B
  StableNodeAnchor SourceAnchor, StableNodeAnchor TargetAnchor, NLCPGEdgeKind Kind,
  NLCPGEdgeLabel? StructuredLabel, NLCPGContextId? ContextId, NLCPGCallSiteContext? CallSiteContext);

private readonly record struct BufferedPendingEdge(   // 120 B —— 与 PendingEdgeKey 字段逐字节相同
  StableNodeAnchor SourceAnchor, StableNodeAnchor TargetAnchor, NLCPGEdgeKind Kind,
  NLCPGEdgeLabel? StructuredLabel, NLCPGContextId? ContextId, NLCPGCallSiteContext? CallSiteContext);
```

| # | 载体 | 字节 | 位置 |
| ---: | --- | ---: | --- |
| ① | `HashSet<PendingEdgeKey>` 的 `Entry[]` | 393,481,888 | 去重 |
| ② | `List<BufferedPendingEdge>` 的数组 | 251,658,288 | 保序 |
| ③ | `Materialize()` 返回的 `PendingEdge[]` | ~503 M（每次调用） | `NLCPGGraph.cs:44/57/763` |
| ④ | 冻结后 `HashSet<NLCPGEdge>` 的 `Entry[]` | 尚未填充（`Entry<NLCPGEdge,int>`=88 B） | `NLCPGGraph.cs:785` |

**①②合计 645,140,176 B = 615.3 MB = 堆的 15.94%。**

③ 的尺寸：`PendingEdge` = 2×104 + 4 + 3×8 = 236→240 B（对齐），
一次物化 2.10 M 条 = **503,316,504 B = 480 MiB**。
调用点：`NLCPGGraph.cs:44`（属性 `PendingEdges`）、`:57`（`SnapshotMutableFacts`）、
`:763`（`AssignDeterministicNodeIds`）、`NLCPGBuilder.cs:726`、`SkeletonShardPublisher.cs:76/163`。
**每次访问都重新分配整块**——这是纯浪费（可用 `ReadOnlySpan`/回调/复用缓冲消除）。

### 1.4 第三重冗余：`ContextId` 由 `CallSiteContext` 派生

`src/NLCPG/Model/NLCPGEdge.cs` L11：

```csharp
var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
```

而 `NLCPGCallSiteContext.ToContextId()`（`NLCPGCallSiteContext.cs:11`）是纯函数：

```csharp
return new NLCPGContextId($"callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}");
```

⇒ 当 `CallSiteContext.HasValue` 时，`ContextId` **完全由它决定**，两者同时存储
（24/32 B + 8/16 B）是冗余。校验逻辑（L12-18）本身就承认了这个派生关系。

---

## 2. 候选方案（Round 1 初步，待后续轮次扩充与证伪）

### 2.1 问题 2（边缓冲 240 MB）→ 一并解决 ①

**方案 A：消重——存 1 份 payload + 开放寻址索引**
`120 B × 2^21 payload` + `int[] buckets`（2^22，负载 ~0.5）

```
现状 645,140,176 B (615.3 MB)
提案 268,435,504 B (256.0 MB)      省 359.3 MB (58.4%)
```

**方案 B：A + payload 瘦身到 24 B**（锚点→`NodeId`(4 B) ×2 + enum(4) + 3×intern 索引(4)）

```
提案 67,108,912 B (64.0 MB)        省 551.3 MB (85.4%)
```
> 前置条件：边加入时节点已有 `NodeId`。**当前 `AddEdge` 在 `AssignDeterministicNodeIds`
> 之前调用（建图期只赋 `NodeId = null` 给普通路径），故需 Task 1 实测确认时序。**

### 2.2 问题 1（`Entry<>` 861 MB）→ 键/值瘦身

| 字典 | 现状 Entry | 提案 | 省 |
| --- | ---: | ---: | ---: |
| A. `PendingEdgeKey`（含 ②） | 136 + 120 | 24 + bucket | 86.6% |
| B. `_mutableNodesByAnchor` | 140 | 140（不可改） | 0% |
| C1. `NLCPGNode→NLCPGNode[]` | 120 | 24（键→`NodeId`） | 80.0% |
| C2. `NLCPGNode→int` | 116 | 16（键→`NodeId`） | 86.2% |
| D1. `IOperation→NLCPGNode` | 120 | 24（值→`NodeId`） | 80.0% |
| D2. `SyntaxNode→NLCPGNode` | 120 | 24（值→`NodeId`） | 80.0% |

**合计：1,079,920,292 B → 327,286,580 B，省 752,633,712 B = 717.8 MB = 堆的 18.6%。**
叠加位集优化后峰值堆 **3,861 MB → 889 MB（降 77.0%）**。

**B 类不可改的原因**：`_mutableNodesByAnchor` 是**建图期唯一的节点身份索引**，
此时 `NodeId` 尚未分配（`AssignDeterministicNodeIds` 在冻结时才跑，`NLCPGGraph.cs:732`）。
键必须保留完整锚点语义（`StableNodeAnchor` 含 `Kind/FilePathId/SpanStart/SpanEnd/Role/Ordinal/ExtraKeyId`）。

---

## 3. Round 1 追加：三项自我修正

读取仓库既有分析 `docs/benchmarks/nlissn-version4-dop12-diagnostics.md`（L1140-1252）
并做独立实测后，**上文的三个说法必须修正**。

### 3.1 修正一：基线 dump 早于一个已落地的修复（最重要）

`docs/benchmarks/nlissn-version4-dop12-diagnostics.md:1201-1246` 记载了一个**已实施**的修复：
把边去重索引从 `Dictionary<PendingEdgeKey,int>` 改为 `HashSet<PendingEdgeKey>`。

现状代码已确认（`src/NLCPG/Model/NLCPGGraph.cs:838`）：

```csharp
private readonly HashSet<PendingEdgeKey> _keys = new();   // 已是 HashSet，非 Dictionary
```

**实测两种容器的 slot 尺寸**（探针真实构造 2,100,000 条边 + 反射读 `_entries`）：

| 容器 | Slot 类型 | Slot 尺寸 | 容量 2,893,249 时数组字节 |
| --- | --- | ---: | ---: |
| `Dictionary<PK,int>`（dump 中的形态） | `Entry` | **136** | 393,481,888 |
| `HashSet<PK>`（**当前代码**） | `Entry`（无 value 字段） | **128** | **370,335,896** |

⇒ 该数组**已从 393.5 MB 降到 370.3 MB，省 23,145,992 B（23.1 MB）**。

**因此 861.3 MB 是修复前的读数**；当前 `Entry<>` 实际约 **861.3 − 23.1 = 838.2 MB**。
后文所有「省 X MB」都以 **838.2 MB 为基线**重算，不能再用 861.3 MB。

> 注意 `HashSet<T>` 的内部数组类型名仍叫 `Entry`（不是 `Slot`），
> 与 `Dictionary` 的区别是**没有 `TValue` 字段**。
> 既有文档 L1233-1238 把这个差值算作「每条边省 8 B」，与本次实测的 136−128 = 8 B **一致**。

### 3.2 修正二：负载因子不是稳定 72.5%，而是在 48.2%–100% 之间振荡

上文 §1.2 把「容量 × 0.725」当作条目数估计，**方法本身有偏**。实测 `Dictionary` 扩容阶梯：

```
count=1,395,263 -> capacity=2,893,249  load=48.2%   ← 刚扩容完
count=2,893,249 -> capacity=5,999,471  load=48.2%   ← 下一次扩容
```

`ExpandPrime(count)` = `GetPrime(2 × oldSize)`，**在 `count == entries.Length` 时扩容**，
所以负载率在 **[48.2%, 100%)** 区间内循环。dump 看到的 72.5% 只是「填充到下一次扩容的 72.5%」，
**不是**稳定负载率。

**由此得到的真实条目数区间**（两个容器同步增删，区间取交集）：

| 容器 | 容量 | 条目数区间 |
| --- | ---: | --- |
| `List<BufferedPendingEdge>` | 2,097,152 = 2²¹（精确） | (1,048,576, 2,097,152] |
| `HashSet<PendingEdgeKey>` | 2,893,249 | (1,395,263, 2,893,249] |
| **交集** | | **(1,395,263, 2,097,152]** |

⇒ 该文件边数约 **140 万–210 万条**。既有文档 L1147-1148 写的「105 万–210 万」因未用交集而偏宽。

**预分配容量的收益（实测）**：

```
未预分配   capacity=2,893,249  load=72.5%
预分配 N   capacity=2,411,033  load=87.0%     省 482,216 槽位 = 16.7%
```

对 `HashSet` 数组即 `2,893,249 × 16.7% × 128 B ≈ 62 MB`。

> **但既有文档 L1179-1197 明确否决了「设固定容量」**，理由是 12 路并发文档下
> 会从「垃圾」变成「长期存活」：`12 × (240 + 375) MB ≈ 7.4 GB`，**峰值反而上升约一个数量级**。
> 且容量在 64 倍范围内变化（32,768 ↔ 2,097,152），没有合适常量。
> **本报告接受该否决**：预分配只在「按文档推导 + 分批预留 + 带上限」的形态下才可考虑，
> 不能当默认方案。

### 3.3 修正三：`Entry<>` 的开销不在哈希表，在 payload 本身

上文 §2.2 隐含「换掉 `Dictionary` 就能省很多」。实测**哈希表自身的开销极小**：

| 项 | 字节 | 占 slot |
| --- | ---: | ---: |
| `hashCode`(4) + `next`(4) | **8** | 5.7%（136 B slot）–6.9%（116 B slot） |
| payload（键+值） | 108–132 | 93%+ |

⇒ **「自研开放寻址哈希表」最多省 861 MB 的约 7% ≈ 55–60 MB**，不是数量级改善。
**真正的杠杆是 payload**：把 104 B 的 `NLCPGNode` / 28 B 的 `StableNodeAnchor` 内联存储，
换成 4 B 的 `NodeId` 或序数索引。

这**加强了** §2.2 的方向（键/值瘦身），但**削弱**了「自研哈希表」的价值。两者不要混为一谈。

### 3.4 额外实测：`NLCPGNode` 的对齐约束（脆弱性提示）

`NLCPGNode` 实测 **104 B**，且 `Entry<NLCPGNode,int>` = **116 B 不是 8 的倍数**
⇒ 证明 `NLCPGNode` 内**没有任何 `long`/`double`/引用字段**（26 个 4 B 字段，4 B 对齐）。

**风险**：若给 `NLCPGNode` 增加哪怕一个引用或 `long` 字段，
`sizeof(NLCPGNode)` 仍是 104，但**所有** `Entry<NLCPGNode,…>` 从 116 → 120（+3.4%）。
在 C 类字典（容量 488,304 + 299,196）上约 **+11 MB**。
⇒ **改动 `NLCPGNode` 前必须重测**，不能假设尺寸不变。

---

## 4. Round 1 新增候选：`Materialize()` 的重复大块分配

上文 §1.3 ③ 已指出 `Materialize()` 每次调用都分配整块 `PendingEdge[]`。

**实测尺寸**：`PendingEdge` = 2×104 + 4 + 3×8 = 236 → 对齐 **240 B**。
一次物化 2.10 M 条 = **503,316,504 B ≈ 480 MiB**。

**6 个调用点**（每处都重新分配整块）：

| 位置 | 触发 |
| --- | --- |
| `NLCPGGraph.cs:44` | 属性 `PendingEdges` getter —— **每次访问都物化** |
| `NLCPGGraph.cs:57` | `SnapshotMutableFacts()` |
| `NLCPGGraph.cs:763` | `AssignDeterministicNodeIds()`（冻结） |
| `NLCPGBuilder.cs:726` | `foreach (var edge in graph.PendingEdges)` |
| `SkeletonShardPublisher.cs:76` | `facts.PendingEdges.Select(...).ToArray()` |
| `SkeletonShardPublisher.cs:163` | 同上 |

**关键风险**：`NLCPGBuilder.cs:726` 的 `foreach` 与 `NLCPGGraph.cs:44` 的 getter 组合
⇒ **一次遍历就产生一份 480 MiB 的临时数组**。这是**累计分配量（churn）**的巨大来源，
对峰值的影响取决于它是否与其它大块同时存活（**未实测**）。

**候选形态**：把 `Materialize()` 改为「回调式遍历」或「复用缓冲 + `ReadOnlySpan`」，
消除中间数组。**语义约束**：`Materialize` 返回 `IReadOnlyList<PendingEdge>`，
且 `SkeletonShardPublisher` 依赖 `.Select().ToArray()`，改为 span 需保留同样的枚举顺序。

---

## 5. Round 1 修正后的收益汇总（基线已按 §3.1 校准）

边数取上界 `2,097,152`（`List` 容量精确值），容量取 dump 值 `2,893,249`。

### 5.1 边子系统（用户问题 2 + `Entry<>` 的 A 类，即 375.3 MB 那部分）

| 方案 | 现状 | 提案 | 节省 | 省% | 语义风险 |
| --- | ---: | ---: | ---: | ---: | --- |
| 现状（①`HashSet` + ②`List`） | 621,994,160 B (593.2 MB) | — | — | — | — |
| **A. 消重**：payload 存 1 份 + `int[]` bucket | ↑ | 268,435,504 B (256.0 MB) | **337.2 MB** | 56.8% | 中（需保持插入序） |
| **B. A + payload 瘦到 24 B** | ↑ | 67,108,912 B (64.0 MB) | **529.2 MB** | 89.2% | 高（依赖 NodeId 时序） |
| C. 只预分配容量（一行） | ↑ | 559,706,512 B (533.8 MB) | **58.9 MB** | 9.5% | **低，但被既有文档否决（并发反噬）** |

### 5.2 `Entry<>` 全量（修正基线 838.2 MB）

| 字典 | 现状 Entry | 提案 Entry | 节流 | 备注 |
| --- | ---: | ---: | ---: | --- |
| A. `PendingEdgeKey` | 128（已是 HashSet） | 见 §5.1 | 与 ②合并计算 | — |
| B. `_mutableNodesByAnchor` | 140 | **140（不可改）** | 0% | 建图期无 `NodeId` |
| C1. `NLCPGNode→NLCPGNode[]` | 120 | 24 | 80.0% | 键→`NodeId` |
| C2. `NLCPGNode→int` | 116 | 16 | 86.2% | 键→`NodeId` |
| D1. `IOperation→NLCPGNode` | 120 | 24 | 80.0% | 值→`NodeId` |
| D2. `SyntaxNode→NLCPGNode` | 120 | 24 | 80.0% | 值→`NodeId` |

**C/D 类合计**：195,336,844（B）+ 103,678,200（C）+ 194,585,144（D）中的可改部分
≈ **275 MB → 55 MB，省约 220 MB**（B 类 186.3 MB 不动）。

### 5.3 三层收益对照（供决策）

| 口径 | 现状 | 方案 A | 方案 B |
| --- | ---: | ---: | ---: |
| 边子系统两项 | 593.2 MB | 256.0 MB | 64.0 MB |
| `Entry<>` 全量 | 838.2 MB | ~625 MB | ~433 MB |
| **峰值堆**（3,861 MB 基线） | **3,861 MB** | **~3,500 MB（−9.3%）** | **~3,310 MB（−14.3%）** |
| 再叠加位集优化（−2,253.7 MB） | — | **~1,246 MB** | **~1,056 MB** |

> **口径警告**：上表「峰值堆」是按容量模型**推算**，不是重跑实测。
> 且位集优化与字典优化由**不同工作线**推进（位集正由并发会话实现），
> 叠加效果未经同一次 gcdump 验证。**不得把推算值当实测结论引用。**

### 5.4 按「收益/风险比」排序的推荐次序

1. **D1/D2（Roslyn 引用键字典降值）**——省 ~117 MB，键是引用无需改语义，只把 `NLCPGNode` 值换成 `NodeId`。**最干净**。
2. **C1/C2（`NLCPGNode` 键降为 `NodeId`）**——省 ~76 MB，需要「节点加入时就分配稳定序数」这一前置条件。
3. **边子系统方案 A（消重）**——省 **337 MB**，单项收益最大，但需重写 `PendingEdgeBuffer` 并保证插入序。
4. **`Materialize()` 去中间数组**——省的是 churn（480 MiB/次），峰值收益未实测。
5. **边子系统方案 B（payload 瘦到 24 B）**——省 529 MB，最大收益但依赖最多，最后做。
6. **预分配容量**——一行改动省 59 MB，但**被既有文档否决**，除非采用「分批预留 + 上限」形态。

---

## 6. Round 1 待办（后续轮次）

- [ ] 并入两个子代理的外部资料报告（Dictionary 内部机制 / 图边结构设计）
- [ ] 实测 `AddEdge` 时节点是否已有 `NodeId`（决定方案 B 可行性）——**最高优先级**
- [ ] 核实 `Materialize()` 的 480 MiB 是否与其它大块同时存活（决定峰值影响）
- [ ] 调研 CSR / 邻接数组替代 `NLCPGNode→NLCPGNode[]`（子代理已给 10.4× 模型）
- [ ] 收集真实 GitHub 项目案例（Roslyn / Joern / graph DB 的边存储布局）
- [ ] 验证插入序约束：`Materialize()` 顺序是否被持久化契约依赖
- [ ] 形成最终候选方案清单 + 执行计划文档

---

## 7. Round 1 追加修正：峰值阶段判定推翻 `NodeId` 路线

> 编号说明（2026-09-23 整理）：本节早先与「Round 1 待办」重号为 §6，现已按文档顺序
> 重排为 **§7**；原 §7「最终收益汇总」顺延为 **§8**，原 §8「结论」为 **§10**。
> 全文 `§x.y` 交叉引用已同步更新。

### 7.1 决定性发现：峰值位于【可变建图期】

用 dump 的**结构存在性**判定峰值时刻处于哪个阶段：

| 探测项 | 观测 | 含义 |
| --- | --- | --- |
| `Entry<NodeId,…>` | **不存在** | `_nodesByNodeId` 未填充 |
| `HashSet<NLCPGEdge>` 本体 | 64 B（**空对象**） | `_edges` 为空 |
| `Entry<NLCPGEdge,…>` | **不存在** | `_edges` 未填充 |
| `Entry<StableNodeAnchor,NLCPGNode>` | 195,336,844 B（容量 1,395,263） | `_mutableNodesByAnchor` **已填充** |
| `Entry<…PendingEdgeKey…>` | 393,481,888 B | `PendingEdgeBuffer` **已填充** |

⇒ 峰值位于 **`FreezeQueryIndex()` 之前**（`NLCPGGraph.cs:415`）。
代码依据：`AssignDeterministicNodeIds()`（`NLCPGGraph.cs:732`）只在 `FreezeQueryIndex()`
内部被调用（`NLCPGGraph.cs:422`），而峰值时它还没跑。

**因此 `NLCPGNode.NodeId` 在峰值时刻全部为 `null`。**

### 7.2 推翻的候选

上文 §2.2 / §5.2 / §5.4 中所有「键或值换成 `NodeId`」的方案：

| 被推翻的项 | 原称节省 | 推翻理由 |
| --- | ---: | --- |
| `IOperation→NLCPGNode` 值换 `NodeId` | 80% | 峰值时 `NodeId` 不存在 |
| `SyntaxNode→NLCPGNode` 值换 `NodeId` | 80% | 同上 |
| `NLCPGNode→NLCPGNode[]` 键换 `NodeId` | 80% | 同上 |
| `NLCPGNode→int` 键换 `NodeId` | 86.2% | 同上 |
| 边 payload 瘦到 24 B（方案 B） | 85.4% | 同上（`NodeId` 版） |

> 注意：方案 B **仍可能成立**，但要用**建图期序数**（而非 `NodeId`）重做——见 §7.3。
> `NodeId` 本身仍可用于**冻结后**的 `_edges` / `SkeletonShardPublisher` 路径。

### 7.3 阶段正确的替代方案（实测尺寸）

可变建图期只有两种小键可用：`StableNodeAnchor`(28 B) 或**自建序数**(4 B)。

| 字典 | 容量 | 现状 Entry | 保守（锚点键/值） | 激进（序数键/值） |
| --- | ---: | ---: | ---: | ---: |
| C1 `NLCPGNode→NLCPGNode[]` | 488,304 | 120 | **48**（`Entry<StableNodeAnchor,[]>`）| 16（`Entry<uint,int>` + CSR）|
| C2 `NLCPGNode→int` | 299,196 | 116 | **40**（`Entry<StableNodeAnchor,int>`）| 16（`Entry<uint,uint>`）|
| D1 `IOperation→NLCPGNode` | 893,384 | 120 | **48**（`Entry<IOperation,StableNodeAnchor>`）| 24（`Entry<IOperation,int>`）|
| D2 `SyntaxNode→NLCPGNode` | 324,449 | 120 | **48**（`Entry<SyntaxNode,StableNodeAnchor>`）| 24 |

**节省合计**：

| 路线 | 节省 | 占堆 |
| --- | ---: | ---: |
| 保守（不引入新序数分配，只用 `StableNodeAnchor` 做中间键） | **145,580,760 B = 138.8 MB** | 3.60% |
| 激进（引入建图期序数索引，`uint` 键） | **197,615,184 B = 188.5 MB** | 4.88% |

**保守路线的关键优势**：`StableNodeAnchor` **已经是** `NLCPGNode` 的比较键——
`NLCPGNode.Equals`（`NLCPGNode.cs:20-43`）在 `StableAnchor.HasValue` 时**只比较锚点**、
`GetHashCode`（L45-50）也**只取锚点哈希**。所以在节点已物化（必有锚点）的路径上，
把键从 `NLCPGNode`(104 B) 换成 `StableNodeAnchor`(28 B) **不改变相等语义**，
只是消除了内联的冗余字段。**这是语义最干净的 138.8 MB。**

### 7.4 `_buckets` 数组：dump 少算的 3%–4%

子代理指出并被**本次实测确认**：`Dictionary` 的 `_buckets` 是**独立的 `int[]`，长度与 `_entries` 相同**。

```
Dictionary<int,int>  count=2,100,000
  _entries.Length = 2,893,249   (_entries 元素类型 = Entry)
  _buckets 类型   = Int32[]
  _buckets.Length = 2,893,249   (== _entries.Length? True)
```

⇒ **真实每 slot 成本 = `sizeof(Entry)` + 4 B**。gcdump 的 `Entry<>` 读数**不含** `_buckets`，
所以用户给出的 861.3 MB **低估了约 3%–4%（约 25–30 MB）**。

| 字典 | `Entry[]` | `+ _buckets` | 真实合计 |
| --- | ---: | ---: | ---: |
| `Entry<PendingEdgeKey,int>` 136 → | 393.5 MB | +11.0 MB | **404.5 MB** |
| `Entry<StableNodeAnchor,NLCPGNode>` 140 → | 195.3 MB | +5.3 MB | **200.6 MB** |
| `Entry<ref,NLCPGNode>` 120 → | 346.4 MB | +11.0 MB | **357.4 MB** |
| `Entry<NLCPGNode,int>` 116 → | 34.7 MB | +1.1 MB | **35.8 MB** |

> 但用户列的 **240.0 MB `BufferedPendingEdge[]` 是 `List` 的内部数组，没有 `_buckets`**，
> 该数字是完整的。

### 7.5 不成立的子代理假设（已核实并否决）

| 假设 | 核实结果 |
| --- | --- |
| 「键类型未实现 `IEquatable<T>` ⇒ 每次访问装箱 120 B」 | **不成立**。`NLCPGNode`/`StableNodeAnchor`/`NodeId`/`NLCPGEdge`/`PendingEdgeKey`/`BufferedPendingEdge` **全部是 `readonly record struct`**（已逐行核实声明），编译器自动生成 `IEquatable<T>` 实现 ⇒ **无装箱**。 |
| 「`FrozenDictionary` 能省内存」 | **不成立且相反**。子代理实测：对 104 B struct 键，`ToFrozenDictionary()` 401.8 B/entry vs `Dictionary` 254.6 B/entry，**是回归**。`FrozenDictionary` 是**查询速度**工具，不是内存工具。 |
| 「自研开放寻址表能省数量级」 | **不成立**。哈希表自身开销只有 `hashCode(4)+next(4)`=8 B/slot，占 136 B slot 的 5.7%（§3.3）。上限约 7% ≈ 55–60 MB。 |

---

## 8. Round 1 最终收益汇总（阶段正确）

| 方案 | 内容 | 节省 | 占堆 | 语义风险 | 前置条件 |
| --- | --- | ---: | ---: | --- | --- |
| **① C/D 类保守降键** | `NLCPGNode` 键/值 → `StableNodeAnchor` | **138.8 MB** | 3.60% | **低**（等价语义已由 `NLCPGNode.Equals` 保证） | 无 |
| **② 边子系统消重（方案 A）** | payload 存 1 份 + `int[]` bucket | **337.2 MB** | 8.33% | 中（须保持插入序） | 无 |
| ③ `Materialize()` 去中间数组 | 消除每次 480 MiB 临时块 | churn 为主 | 未实测 | 中 | 无 |
| ④ C/D 类激进序数化 | 引入建图期序数索引 | 188.5 MB | 4.88% | 中高 | 需新增序数分配 |
| ⑤ 边 payload 瘦到 24 B | 建图期序数 + intern 索引 | ~529 MB | 13.1% | 高 | 依赖 ④ 的序数索引 |
| ⑥ 预分配容量 | `EnsureCapacity` | 58.9 MB | 1.46% | **被既有文档否决** | 分批预留+上限 |

**①②合计 476.0 MB = 堆的 11.8%**，且**互不冲突、可独立落地**。

### 8.1 推荐执行次序

1. **①（138.8 MB，低风险）**——先做。语义等价性有代码依据，不需要新数据结构。
2. **②（337.2 MB，单项最大）**——重写 `PendingEdgeBuffer`：一份 `120 B` payload 数组 + `int[]` 开放寻址 bucket（负载 ~0.5），保持插入序。
3. **③**——`Materialize()` 改为回调/复用缓冲，消 churn。
4. 观察后决定是否做 ④⑤（引入序数索引是较大重构）。

### 8.2 必须实测确认的三件事（Round 2 首要任务）

1. **`AddEdge` 调用时 `NLCPGNode.StableAnchor` 是否已非空**——决定①②可行性（`Materialize()` 里用了 `!` 断言，L851-852，暗示已非空但未证）。
2. **插入序是否被持久化契约依赖**——决定②能否改结构。已知 `Materialize()` 按 `_items` 序产出，`CpgShardStore` 落盘该序。
3. **`Materialize()` 的 480 MiB 是否与其它大块同时存活**——决定③的峰值意义。

---

### 8.3 方案 ① 的精确定位与前置校验（Round 1 收尾）

**已定位的 C/D 类字典宿主**（`grep` 实测）：

| 字段 | 位置 | 键 → 值 | 声明尺寸 |
| --- | --- | --- | ---: |
| `_syntaxNodes` | `NLCPGBuilder.cs:47` | `SyntaxNode` → `NLCPGNode` | 120 |
| `_operationNodesByOperation` | `NLCPGBuilder.cs:67` | `IOperation` → `NLCPGNode` | 120（66 个实例中的一部分）|
| `_methodParameterOrdinalsByNode` | `NLCPGBuilder.cs:57` | `NLCPGNode` → `int` | 116 |
| `_cfgPredecessorsByNode` | `NLCPGBuilder.cs:62` | `NLCPGNode` → `HashSet<NLCPGNode>` | — |
| `_cfgSuccessorsByNode` | `NLCPGBuilder.cs:63` | `NLCPGNode` → `HashSet<NLCPGNode>` | — |
| `_symbolKeysByNode` | `NLCPGBuilder.cs:55` | `NLCPGNode` → `string` | — |
| `_methodOwnerSymbolKeysByBoundaryNode` | `NLCPGBuilder.cs:56` | `NLCPGNode` → `string` | — |
| `ParameterDefinitionFacts` | `DataFlowPass.cs:104` | `NLCPGNode` → `DefinitionFact` | 116 |
| `Predecessors` / `Successors` | `DataFlowPass.cs:108-109` | `NLCPGNode` → `NLCPGNode[]` | 120 |
| `nodesByOperation` | `NLCPGBuilder.cs:1351` | `IOperation` → `NLCPGNode` | 120 |

**关键限定：不是所有字典都能改。** 必须区分两类：

| 类别 | 判定 | 能否降键 |
| --- | --- | --- |
| 键是 **Roslyn 对象引用**（`IOperation`/`SyntaxNode`），值是 `NLCPGNode` | 用 `ReferenceEqualityComparer.Instance`（`NLCPGBuilder.cs:47,67,1351`） | ✅ 值可降为 `StableNodeAnchor`(28 B) 或序数 |
| 键是 **`NLCPGNode`** 本身 | 走 `NLCPGNode.Equals`（`NLCPGNode.cs:20-43`） | ⚠️ **需先证明全部键都有锚点** |

**依赖的不变量（已从代码确认）**：

`AddNode` → `MaterializeCompatibilityIdentity`（`NLCPGGraph.cs:679-714`）
在**所有返回路径**上都设置 `StableAnchor`（L697-701、L709-713），
且 `MergeNode`（L716-729）用 `existing with {...}` **保留**锚点。
⇒ **经 `AddNode` 进入图的节点必有 `StableAnchor`。**

**⚠️ 但降键不是无条件等价，必须满足以下前提**（`NLCPGNode.Equals` 的真实语义）：

```
L20-26:  if (StableAnchor.HasValue || other.StableAnchor.HasValue)
             return StableAnchor.HasValue && other.StableAnchor.HasValue && 锚点相等;
```

⇒ **无锚点的节点与有锚点的节点永不相等**，且无锚点节点走
L33-42 的**全字段比较**。因此：

1. 若某字典可能收到**无锚点**的 `NLCPGNode` 作键，降到 `StableNodeAnchor` **会改变语义**
   （无锚点键会被 `CreateFallback` 折叠，而 `CreateFallback` 对 `SpanStart/SpanEnd` 用 `-1`，
   `Ordinal` 用 `0`，**不同节点可能碰撞**，`StableNodeAnchor.cs:14-15`）。
2. 因此方案 ① 落地前，**必须对每个目标字典证明其键集全部来自 `AddNode` 路径**。

**Round 2 首要验证项**：逐个字典确认键来源，产出「可降键字典白名单」。
在此之前**不得**声称 138.8 MB 可直接获得。

---

## 9. Round 1 关键校准：用户表格的百分比分母是「位集优化后」的堆

用户原始需求里的两个百分比一度被（子代理）判为「互相矛盾」，**经核算是误解**。
真实情况是：**两个百分比共用同一个分母，而该分母不是当前堆 3,861 MB，而是位集稀疏化之后的投影堆。**

```
位集优化后投影堆 = 3,861 − 2,324.3 + 70.6 = 1,607.2 MB
```

| 项 | 字节 | ÷ 当前 3,861 MB | ÷ 位集优化后 1,607 MB |
| --- | ---: | ---: | ---: |
| `Entry<>` 合计 | 903,063,988 | 22.31% | **53.58%** |
| `BufferedPendingEdge` | 251,658,288 | 6.22% | **14.93%** |
| **用户表格给定** | | — | **53.59 / 14.93** |

⇒ 两项都在 **0.01 个百分点内**吻合同一个分母。**用户表格是自洽的**，
且说明该表格是在**假定位集优化已落地**的前提下给出的。

### 9.1 为什么不是「861 MB 只是子集」

子代理的替代模型假设「六张表容量都是 2,893,249」，与实测不符——**六张表容量相差 9.7 倍**：

| 字典 | 实测容量 | 「同容量」假设的偏差 |
| --- | ---: | ---: |
| `PendingEdgeKey` | 2,893,249 | 0 |
| `_mutableNodesByAnchor` | 1,395,263 | −1,497,986 |
| `IOperation→Node` | 893,384 | −1,999,865 |
| `NLCPGNode→NLCPGNode[]` | 488,304 | −2,404,945 |
| `SyntaxNode→Node` | 324,449 | −2,568,800 |
| `NLCPGNode→int` | 299,196 | −2,594,053 |

六张表实测合计 **828,266,732 B**，全部 `Entry<>` 行合计 **903,063,988 B**。

**容量是反解值，已与真实 .NET 10 扩容轨迹交叉验证**（2,100,000 次插入 ⇒ 容量恰为 2,893,249）。
所以 **861.2 MiB 是真实的 `Entry[]` 全量，不是子集**。子代理的「2.4× 放大」不成立，本报告不采用。

### 9.2 由此得到的解读规则

1. 报告 §7 的 138.8 MB / 337.2 MB 等**绝对字节数不受影响**（它们来自逐表实测字节）。
2. 但**换算成百分比时必须用 1,607 MB 这个分母**才能与用户表格对齐：
   - 方案 ①（138.8 MB）= 投影堆的 **8.64%**
   - 方案 ②（337.2 MB）= 投影堆的 **20.98%**
   - ① + ②（476.0 MB）= 投影堆的 **29.62%**
3. 反之，若用当前堆 3,861 MB 作分母，会得到 3.60% / 8.73% / 11.8%——**这是另一个口径**，
   两个口径都正确，但**不可混用**，引用时必须写明分母。

---

## 10. Round 1 结论

### 10.1 已确立（实测，可直接引用）

1. 用户的两项（861.3 MB / 240.0 MB）**是同一子系统**：`PendingEdgeBuffer` 把**同一份 120 B 记录存了 4 遍**（§1.3）。
2. **861.3 MB 是修复前读数**——`HashSet` 改动已落地，当前约 **838.2 MB**（§3.1）。
3. 真实每 slot 成本 = `sizeof(Entry) + 4 B`（`_buckets` 同长，已实测确认），**dump 低估 3%–4%**（§7.4）。
4. 峰值位于**可变建图期**（`FreezeQueryIndex()` 之前）⇒ **所有 `NodeId` 路线作废**（§7.1–7.2）。
5. 「装箱」「FrozenDictionary」「自研哈希表」三条外部建议**在本仓库均不成立**（§7.5）。
6. 负载因子 **48.2%–100% 振荡**，非稳定 72.5%（§3.2）。
7. 用户表格的两个百分比**自洽**，分母是**位集优化后的 1,607 MB**（§9）。

### 10.2 候选方案（排序后）

| 序 | 方案 | 节省 | /当前堆 | /投影堆 | 风险 | 阻塞项 |
| ---: | --- | ---: | ---: | ---: | --- | --- |
| 1 | ① C/D 类降键（`NLCPGNode` → `StableNodeAnchor`） | 138.8 MB | 3.60% | **8.64%** | 低 | 需「可降键白名单」（§8.3） |
| 2 | ② 边子系统消重（payload 1 份 + `int[]` bucket） | 337.2 MB | 8.73% | **20.98%** | 中 | 需证明插入序约束 |
| 3 | ③ `Materialize()` 去中间数组 | churn 480 MiB/次 | 未实测 | — | 中 | 需证明存活重叠 |
| 4 | ④ 引入建图期序数索引 | 188.5 MB | 4.88% | 11.73% | 中高 | 需与 ① 合并设计 |
| 5 | ⑤ 边 payload 瘦到 24 B | ~529 MB | 13.7% | 32.9% | 高 | 依赖 ④ |
| 6 | ⑥ 预分配容量 | 58.9 MB | 1.53% | 3.67% | **已否决** | 并发反噬 |

**①② 合计 476.0 MB = 当前堆 11.8% = 投影堆 29.62%**，且**互不冲突、可独立落地**。

### 10.3 尚未完成（Round 2 起）

- [ ] 子代理 2（图边结构设计 / CSR / arena）报告未到
- [ ] 子代理 1 的 §2/§4（替代结构、真实案例）未到
- [ ] 「可降键字典白名单」逐字典验证（§8.3）
- [ ] `Materialize()` 480 MiB 存活重叠实测
- [ ] 插入序是否被持久化契约依赖
- [ ] 真实 GitHub 案例（Roslyn / Joern / graph DB 边存储）
- [ ] 最终候选清单 + 执行计划文档

---

## 11. Round 2：外部资料到位 + 三个新结构性发现

两个子代理报告已落盘（`docs/research/`，共 3 份）：

| 文件 | 大小 | 覆盖 |
| --- | ---: | --- |
| `2026-09-23-dotnet-dictionary-entry-memory-research.md` | 46,624 B | Dictionary 内部机制、替代结构、容量增长 |
| `2026-09-23-edge-set-storage-and-dedup-research.md` | 50,540 B | 边集存储/去重、CSR、arena、前例 |
| `2026-09-23-dotnet-collection-memory-comparer-and-key-size.md` | 39,524 B | 比较器与键尺寸 |

`web_search` 全程不可用（HTTP 401），所有外部结论均经 `web_fetch` 抓取真实 URL 获得
（BCL 源码 `HashSet.cs`/`Dictionary.cs`/`List.cs`/`ArrayPool.cs`、Microsoft Learn、
GAPBS `graph.h`/`builder.h`、Joern flatgraph 迁移记录、LLVM `SmallVector.h`、
`scipy` CSR 文档、.NET GC 设计文档等）。

### 11.1 新发现一：`CreateFallback` 的 `Ordinal = 0` 陷阱（高优先级风险）

`StableNodeAnchor.cs:9-20`：

```csharp
public static StableNodeAnchor CreateFallback(NLCPGNode node, StableNodeRole role)
{
    return new StableNodeAnchor(
      node.Kind, node.FilePathId,
      node.SpanStart ?? -1, node.SpanEnd ?? -1,
      role,
      Ordinal: 0,                                    // <-- 硬编码 0
      node.FullNameId != 0 ? node.FullNameId :
        node.SignatureId != 0 ? node.SignatureId : node.NameId);
}
```

**已核实**：`CreateFallback` 只有一个调用点——`StableNodeIdentityFactory.cs:13`
（`node.StableAnchor ?? CreateFallback(...)`）。

**风险**：方案 ④（引入建图期序数索引）若把锚点降为序数，则**所有 fallback 锚点节点的
`Ordinal` 都是 0**。若序数由 `(Kind, FilePathId, SpanStart, SpanEnd, Role, Ordinal, ExtraKeyId)`
的元组派生，fallback 节点之间会在 `Ordinal` 上碰撞，导致**不同节点的边被误判为重复而丢弃**
（改变图内容，且**静默**）。

**缓解**：fallback 路径的节点必须走**独立序号空间**（例如「有真实 `Ordinal`」与
「fallback」用不同编码），并配一条**专项测试**。这是方案 ④ 的**硬前置条件**。

### 11.2 新发现二：`NLCPGContextId?` 的 Nullable 翻倍（尺寸陷阱）

子代理实测并经我复算确认：

| 类型 | 尺寸 |
| --- | ---: |
| `NLCPGContextId`（包装 string） | 8 B |
| **`NLCPGContextId?`** | **16 B**（`Nullable<T>` 加 `bool hasValue`，9 B 向上对齐到 16） |

⇒ 「`contextId` 字段换 4 B 索引」实际省 **12 B**，不是 4 B。
同类：`NLCPGCallSiteContext` 24 B → `NLCPGCallSiteContext?` **32 B**。

### 11.3 新发现三：边属性字段**几乎全为 null**（最高杠杆）

**本地 grep 实测**（关键证据）：

```
src/NLCPG 全部 AddEdge 调用点            = 64
其中显式传 label/contextId/callSiteContext = 0
仅传 (source, target, kind) 的             = 64  (100.0%)
```

只有 2 处**间接**传入（`NLCPGBuilder.cs:903` 的 callsite 桥接、
`CpgFragmentReducer.cs:102` 的 reducer 转发）。

⇒ **绝大多数边的 `StructuredLabel` / `ContextId` / `CallSiteContext` 三个字段都是 null**，
但它们在 `PendingEdgeKey` 里**固定占 8 + 16 + 32 = 56 B / 条**（占 120 B 的 47%）。

这与 Joern 从 overflowdb 迁移到 flatgraph 的做法一致——flatgraph 的每条边属性数组
**惰性分配**（"negligible O(1) memory cost unless actually used"），
在 Linux 4.1.16 上取得 33 GB→20 GB 堆、18→11 min 的改善。

**实测 payload 尺寸阶梯**（`sizeof`，本项目真实类型）：

| payload 形态 | 字节 | 说明 |
| --- | ---: | --- |
| `EdgeAnchors`（**现状** `PendingEdgeKey` 结构，锚点对） | **120** | 2×28 锚点 + kind + label + ctx?(16) + callsite?(32) |
| `EdgeFull`（2 序数 + kind + label + ctx + callsite） | **72** | 锚点→序数 |
| `EdgeNoCtx`（2 序数 + kind + label） | **24** | 去掉 ctx/callsite |
| `EdgeKindOnly`（2 序数 + kind） | **12** | |
| `EdgeByteKind`（2 序数 + `byte` kind） | **12** | 36 个 kind 成员 ⇒ 1 B 够 |
| **`EdgeByteAttr`（2 序数 + `byte` kind + 属性索引）** | **16** | 稀疏属性侧表方案 |

**对应 `Entry` 形态**：

| Entry | 字节 |
| --- | ---: |
| `Entry<EdgeAnchors,int>`（现状） | **136** |
| `Entry<EdgeFull,int>` | **88** |
| **`Entry<EdgeByteAttr,int>`** | **28** |
| `Entry<uint,int>`（纯序数对去重） | 16 |
| `Entry<ulong,int>`（仅哈希，**不安全**） | 24 |

### 11.4 子代理给出的备选方案实测对照（2,097,152 条边）

| 方案 | 字节 | MiB | B/边 | vs 615.25 MiB |
| --- | ---: | ---: | ---: | ---: |
| 快照基线（Entry 136 B，Dictionary 形态） | 645,140,176 | 615.25 | 307.6 | — |
| (a) 仅 `HashSet<PendingEdgeKey>` | 381,908,916 | 364.22 | 182.1 | −40.8% |
| (b) `Dictionary<T,int>` → `T[]` 索引 | 656,713,172 | 626.29 | 313.1 | **+1.8% 更差** |
| (c) 开放寻址索引 + 120 B payload | 271,619,916 | 259.04 | 129.5 | −57.9% |
| (d) 仅存 64 bit 哈希 | 57,865,028 | 55.18 | 27.6 | −91.0% **不安全** |
| **(a+s) `HashSet<SlimEdge>` + `List<SlimEdge>`** | **154,488,684** | **147.33** | **73.7** | **−76.1%** |
| **(c+s) 开放寻址 + `SlimEdge`** | **70,293,324** | **67.04** | **33.5** | **−89.1%** |
| CSR 带属性（1 M 源节点） | 39,651,732 | 37.81 | 18.9 | −93.9% |

**两条负面结论（重要）**：

- **(b) 不省内存**：`Entry<PendingEdgeKey,int>` 仍内联完整 120 B 键，`int` 值还要 4 B 填充。
  ⇒ 「改成 `Dictionary<K,int>` 指向数组」**在本场景无效**，不要采用。
- **(d) 不安全**：仅存哈希时，n=2.1e6 下 64 bit 碰撞概率 ≈ **1.19e-7（约 1/840 万）**。
  对一个**每次构建都跑**的工具，这不是理论风险，而是静默丢边。

**实测性能**：瘦身后不仅省内存，**还快 29%**（992 ms → 704 ms，哈希 24 B 而非 120 B）。
⇒ 稀疏属性不是「速度换内存」的折衷。

### 11.5 arena / LOH 结论

- LOH 阈值 85,000 B ⇒ `List<BufferedPendingEdge>`（120 B/条）在 **709 条**时进入 LOH。
  本场景 2.1 M 条**必然**在 LOH，`ArrayPool` 无意义（子代理明确建议**不要**用于 `_items`）。
- 翻倍扩容的瞬时峰值 = 1,048,576 + 2,097,152 槽 × 120 B = **377,487,360 B 同时存活**。
- ⇒ **`EnsureCapacity` 一次可消除约 607 MB 的翻倍垃圾**（但见 §3.2 的并发否决条件）。

### 11.6 CSR 的适用边界（子代理）

CSR（`indptr` + 并列 `dst`/`kind`/`label`/`ctx`/`callsite` 数组）= **18.9 B/边**，
且**不存源序数**（由 `indptr` 分段隐含）。

**但 CSR 无法边生成边去重**——GAPBS 的流程是 count → prefix-sum → fill → sort，
其中 `NodeWeight::operator==` **刻意忽略权重**（注释写明 "needed to remove duplicate edges"）。

⇒ **推荐组合**：生成期用哈希去重 → 冻结时转 CSR。**不要**用 CSR 替换生成期的去重结构。

---

### 11.7 重要先例：`OrdinalNodeList` 已实现「序数间接层」模式

工作树中存在一个**未跟踪的新文件** `src/NLCPG/Model/OrdinalNodeList.cs`（802 B，2026-09-21 创建，
**早于本次调研**），内容正是本报告建议的序数间接层：

```csharp
// 通过 ordinal 延迟读取 canonical node 数组，避免索引 bucket 复制节点值。
internal sealed class OrdinalNodeList : IReadOnlyList<NLCPGNode>
{
    private readonly NLCPGNode[] _nodes;    // canonical 数组，节点值只存一份
    private readonly int[] _ordinals;       // 每项 4 B，指向 canonical
    ...
    public NLCPGNode this[int index] => _nodes[_ordinals[index]];
}
```

**意义（三点）**：

1. **证明该模式在本仓库已被接受**——「用 4 B 序数替代内联 104 B 节点值」不是外来建议，
   而是仓库既有方向。这**显著降低**方案 ④ / 稀疏属性的架构风险。
2. 它的注释明确写出动机与 §3.3 的结论一致：**「避免索引 bucket 复制节点值」**——
   即本报告 §11.3 的「payload 才是开销所在」在同一仓库已被独立认识到。
3. **但它目前只用于节点列表，尚未用于边**——`PendingEdgeBuffer` 仍内联完整 120 B 记录，
   这正是 §11.3 指出的 56 B/条 属性字段冗余的所在。

⇒ 方案排序中的 **④ / 稀疏属性** 应视为**既有模式的延伸**，而非新架构引入。

---

## 12. Round 2 更新后的方案排名

| 序 | 方案 | 节省 | /投影堆 | 风险 | 前置条件 |
| ---: | --- | ---: | ---: | --- | --- |
| **1** | **稀疏属性 + 序数化 payload（120→24/16 B）** | **~520 MB** | **~32%** | 中 | 建图期序数索引；**须解 §11.1 fallback 陷阱** |
| 2 | 边子系统消重（payload 1 份 + OA 索引） | 337.2 MB | 20.98% | 中 | 插入序约束 |
| 3 | ① C/D 类降键（`NLCPGNode` → `StableNodeAnchor`） | 138.8 MB | 8.64% | 低 | 可降键白名单（§8.3） |
| 4 | ④ 建图期序数索引（独立收益） | 188.5 MB | 11.73% | 中高 | §11.1 |
| 5 | ③ `Materialize()` 去中间数组 | churn 480 MiB/次 | — | 中 | 存活重叠未测 |
| 6 | 缓存 `nodesByAnchor` 查询（消 2 次/边的字典探测） | 未测 | — | 低 | 序数化后自然消失 |
| 7 | CSR（仅冻结后邻接） | 18.9 B/边 | — | 中 | 仅当邻接查询是热点 |
| — | (b) `Dictionary<K,int>`→数组 索引 | **0（更差）** | — | — | **否决** |
| — | (d) 仅存哈希 | 表面 −91% | — | **高（静默丢边）** | **否决** |
| — | ⑥ 预分配容量 | 58.9 MB | 3.67% | **并发反噬** | 分批预留形态才可考虑 |
| — | `FrozenDictionary` | **更差** | — | — | **否决** |

### 12.1 推荐执行次序（Round 2 版）

1. **① C/D 类降键（138.8 MB，低风险、无新数据结构）**——先落地，验证方法论。
2. **稀疏属性 + 序数化**（最大收益 ~520 MB）——但**必须先解决 §11.1 的 fallback 序数碰撞**，
   并**新增专项测试**。可先只做「序数化 + 去掉 ctx/callsite 的固定占用」（→24 B），
   属性侧表按需惰性分配，再逐步压缩。
3. **② 边子系统消重**——与 2 合并设计更优（同一份 payload 既去重又保序）。
4. ③ + 缓存查询。
5. CSR 视邻接查询热度决定。

### 12.2 Round 3 待验证

- [ ] `CreateFallback` 节点在真实语料中的**占比**（决定 §11.1 风险等级）——需实测 gcdump 或插桩
- [ ] `Materialize()` 中 `nodesByAnchor[...]` 两次字典探测的真实成本（子代理建议加入下次快照）
- [ ] 插入序是否被持久化契约依赖（`CpgShardStore` 落盘序）
- [ ] `Materialize()` 480 MiB 与其它大块的存活重叠
- [ ] `NLCPGEdgeKind` 36 个成员 ⇒ 1 B 编码的**持久化兼容性**（`CpgShardStore` 用字符串还是整数）
- [ ] `NLCPGNodeDraft.cs` / `OrdinalNodeList.cs`（工作树中未跟踪的新文件）是否与序数化冲突

---

## 13. Round 3：子代理 1 报告并入 + 插入序约束的判定

### 13.1 本地关键发现：插入序**很可能不是**承重契约（解锁最大单项收益）

这是本轮最重要的本地判定——它直接决定方案 ②（337 MB）能否落地。

**证据一：冻结导出时会重新确定性排序。**

`CpgFrozenShardExporter.cs:30-37`：

```csharp
var candidates = edgeCandidates
  .Select(candidate => (Candidate: candidate,
    SourceNodeId: allocation.GetRequiredId(candidate.SourceAnchor),
    TargetNodeId: allocation.GetRequiredId(candidate.TargetAnchor)))
  .OrderBy(item => item.SourceNodeId)
  .ThenBy(item => item.Candidate.Kind)
  .ThenBy(item => item.TargetNodeId)      // <-- 全序重排，与输入顺序无关
  .ToArray();
```

⇒ 边**落盘顺序由 `(SourceNodeId, Kind, TargetNodeId)` 完全决定**，
与 `PendingEdgeBuffer` 的插入顺序**无关**。

**证据二：`Materialize()` 的产物最终进入无序集合。**

`NLCPGGraph.cs:763-789`：`Materialize()` 结果先 `.Select(...).ToArray()`，
随后 `_edges.Clear()` 并 `foreach (var edge in remappedEdges) _edges.Add(edge);`
——`_edges` 是 **`HashSet<NLCPGEdge>`**，**集合语义，不保留插入序**。

**证据三：`NLCPGBuilder.cs:726` 的遍历只做分桶。**

`foreach (var edge in graph.PendingEdges)` 按 `Kind` 把边分发到
`callTargetEdgesBySource` / `dataFlowEdgesByTarget` 等索引字典（追加到 `List`）。
这些列表最终进入导出器，仍会被 L34-36 重排。

**结论**：方案 ② 只需保持**集合语义**（去重结果相同、不丢边、不并边），
**无需保持插入序**。这把方案 ② 的风险从「中」降为「低」。

**⚠️ 仍需 Round 4 穷举全部消费者**：若某个 `List` 被**顺序敏感**逻辑消费
（例如「取第一条」），顺序仍有意义。

### 13.2 子代理 1 的量化补充（与本地实测一致）

| 结论 | 数值 | 与本地的一致性 |
| --- | --- | --- |
| 负载因子稳态收敛值 | **48.225%** | 与我的 48.2%–100% 振荡一致 |
| `_buckets` 长度 == `Entry[]` 长度 | +4 B/slot | **已本地实测确认**（§7.4）|
| 预分配节省 | 2,097,152 时 **16.7%**；3,000,000 时 **42.1%** | 与我的 2,411,033 vs 2,893,249 一致 |
| 开放寻址的内存上限 | **7%** | 与我的 5.7%–6.9% 一致（§3.3）|
| `FrozenDictionary` 对 104 B 键 | **254.6 → 401.8 B/entry（回归）** | 与 §7.5 一致 |
| `CollectionsMarshal` 省内存 | **0 B**（2,173,088 vs 2,173,088） | 新信息，**否决** |
| `ReferenceEqualityComparer` 本身开销 | **0 B**（替换比较器而非新增字段） | 新信息 |

### 13.3 新陷阱：（c）路线**不能只降一侧**

子代理实测：`Dictionary<int, NLCPGNode>` = **342.1 MB**，
比现状 `Dictionary<IOperation, NLCPGNode>` 的 **331.1 MB** **更差**。

原因：`NLCPGNode` 值仍内联 104 B，键从 8 B 降到 4 B **只省 4 B**，
而 `Entry<int, NLCPGNode>` 的对齐比 `Entry<ref, NLCPGNode>` 更差。

⇒ **键与值必须同时降**。这修正了 §8.3 表格中「只降值」的隐含假设。

### 13.4 新陷阱：`Dictionary.Clear()` 是 O(Capacity)

子代理引 dotMemory 案例：`Dictionary.Clear()` 占其运行时 **92%**，
因为它**清空整个容量而非计数**（55 min → 1 min 46 s）。

⇒ 本仓库 `NLCPGGraph.cs:778` 的 `_mutableNodesByAnchor.Clear()`（容量 1,395,263）
与 `:785` 的 `_edges.Clear()` 值得 **profile 确认**成本。

### 13.5 CSR 的更新数值（子代理复算）

针对**实测**的 488,304 容量 + 2.1 M 边（24 B 数组头）：

| 变体 | 总量 | B/边 |
| --- | ---: | ---: |
| 有向、仅 targets | **9.86 MB** | **4.93** |
| + 8 B/边属性 | 25.86 MB | 12.93 |
| + 16 B/边属性 | 41.86 MB | 20.93 |
| 双向、无属性 | 17.86 MB | 8.93 |
| 双向 + 8 B 属性 | 33.86 MB | 16.93 |
| **现状** | **78.23 MB**（55.88 Entry[] + 22.35 逐节点数组对象）| — |

⇒ **有向 CSR 小 7.9×**（带 8 B 属性则小 3.0×）。
**每多 1 B/边 ≈ 2 MB**（2.1 M 边）⇒ 属性应走**索引间接**而非内联。

**去重方式**：`ulong[E]` 打包 `(src<<32|tgt)` + `Array.Sort` + 相邻扫描
= **16.00 MB 且无额外结构**；`HashSet<ulong>` = 55.18 MB。
因 CSR 本就要求按源排序，**排序去重是免费的**。

**完美哈希不适用**：MPHF（CHD ≈ 2.07 bits/key）**不存储值**，
且要求**静态键集**——增量构建的 CPG 不满足。

### 13.6 真实案例（子代理抓取，均作者自测）

| 来源 | 改动 | 结果 |
| --- | --- | --- |
| [msbuild#12320](https://github.com/dotnet/msbuild/pull/12320) | 2 字典+逐方法锁 → 1 字典 | **−870 MB 分配，−2.8 s CPU** |
| [msbuild#12345](https://github.com/dotnet/msbuild/pull/12345) | 删 `Dictionary<T,LinkedListNode<T>>` 侧表 | 工作集 **−17%**（几乎全在 LOH）|
| [roslyn#78287](https://github.com/dotnet/roslyn/pull/78287) | `ImmutableDictionary` → `Dictionary`+锁 | **120 MB → 28 MB（−76.7%）** |
| [JetBrains dotMemory](https://blog.jetbrains.com/dotnet/2022/05/23/how-we-used-dotmemory-to-optimize-dotmemory/) | 逐节点字典 + 72 B 类 → 紧凑结构体 | 峰值 **32 GB → 12 GB（−62.5%）** |

**最相关的是 dotMemory**：把 N 个逐节点字典合并后，增长从 1.17 GB/s 降到 7 MB/s
但**进程仍未跑完**；真正见效的是**用紧凑结构体替换「逐节点字典+对象」**。
这与本报告 §3.3 / §11.3 的结论一致——**杠杆在 payload，不在哈希表**。

### 13.7 新线索：`IOperation→Node` 的 66 个实例

子代理指出：dump 显示 `Entry<IOperation,NLCPGNode>[]` 有 **66 个实例**、合计 107.2 MB。
若这真是 **66 个各自独立的小字典**（约 13.5k 槽/个），
则**每个都带 2 个数组头 + 各自的增长余量**，合并为单一 `int` 索引或 CSR
是**低风险的大收益**。

**本地已定位**：`DataFlowPass.cs:446` 的 `operationNodesByOperation`
按方法创建（`MethodDataFlowPlan`，`DataFlowPass.cs:105`），与「66 个实例」吻合。
**但 `DataFlowPass.cs` 正被并发会话修改**，Round 4 需待文件稳定后再定位与量化。

---

## 14. Round 4：插入序约束**已证不存在** + 查询索引已是 CSR（两个决定性发现）

### 14.1 决定性证据：`NLCPGGraphIndex.Create` 对边做 **9 键全序排序**

`NLCPGGraphIndex.cs:126-135`：

```csharp
var orderedEdges = edges.OrderBy(edge => edge.SourceNodeId)
    .ThenBy(edge => edge.Kind)
    .ThenBy(edge => edge.TargetNodeId)
    .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
    .ThenBy(edge => edge.ContextId?.Value, StringComparer.Ordinal)
    .ThenBy(edge => edge.CallSiteContext?.FilePath, StringComparer.Ordinal)
    .ThenBy(edge => edge.CallSiteContext?.SpanStart)
    .ThenBy(edge => edge.CallSiteContext?.SpanEnd)
    .ThenBy(edge => edge.CallSiteContext?.DisplayName, StringComparer.Ordinal)
    .ToArray();
```

**9 个键覆盖 `NLCPGEdge` 的每一个字段**，构成**全序**。
⇒ 索引中的边顺序**完全由字段值决定，与插入顺序无关**。

### 14.2 `Materialize()` 全部 6 个调用点已穷举（本轮完成）

| # | 位置 | 用途 | 顺序敏感？ |
| ---: | --- | --- | --- |
| 1 | `NLCPGGraph.cs:44`（`PendingEdges`）| 供 `NLCPGBuilder.cs:726` 按 `Kind` **分桶** | ❌ |
| 2 | `NLCPGGraph.cs:57`（`SnapshotMutableFacts`）| 供 `SkeletonShardPublisher.cs:76,:163` | ❌ |
| 3 | `NLCPGGraph.cs:763`（`AssignDeterministicNodeIds`）| 重映射后填入 **`HashSet<NLCPGEdge>`**（L785-789）| ❌ 集合语义 |
| 4 | `SkeletonShardPublisher.cs:76` | `.Select(CreateCandidate).ToArray()` → **按归属分桶** | ❌ |
| 5 | `SkeletonShardPublisher.cs:163` | 同上，再经 `AddCandidateToBucket` 分桶 | ❌ |
| 6 | `NLCPGBuilder.cs:726` | `AddPendingEdgeIndex` 追加到 `List` → 交持久化链 | ❌ 导出时重排 |

**消费者链的终点全部是重排或集合**：

- `CpgFrozenShardExporter.cs:34-36`：`OrderBy(SourceNodeId).ThenBy(Kind).ThenBy(TargetNodeId)` **重排**
- `NLCPGGraphIndex.cs:126-135`：**9 键重排**
- `NLCPGGraph.cs:785-789`：填入 `HashSet<NLCPGEdge>`

⇒ **插入序在任何消费者处都不承重。方案 ②（337 MB）风险确认为「低」。**

> 残余注意点：分桶内 `List` 顺序会影响**中间产物**排列，但**最终**导出前必经上述重排，
> 故不影响输出。落地时须加**确定性回归测试**：同输入两次构建产出**字节相同**的 shard。

### 14.3 第二个决定性发现：查询索引**已经是 CSR**

`NLCPGGraphIndex.cs:137-146`：

```csharp
var nodeOrdinals = orderedNodes
    .Select((node, ordinal) => (node.NodeId!.Value, ordinal))
    .ToDictionary(entry => entry.Value, entry => entry.ordinal);
...
var outgoing = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.SourceNodeId, kindWidth, false);
var incoming = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.TargetNodeId, kindWidth, false);
var outgoingByKind = BuildCsr(..., groupByKind: true);
var incomingByKind = BuildCsr(..., groupByKind: true);
var (edgesByKind, edgesByKindOffsets) = BuildKindBuffer(orderedEdges, kindWidth);
```

**含义（重要修正）**：

1. **`NLCPGGraphIndex` 已实现完整 CSR**（`CsrEdgeTable`，`NLCPGGraphIndex.cs:13`），
   含 `outgoing`/`incoming`/按 kind 分组 4 张表 + `edgesByKind`。
   ⇒ 「CSR 化邻接表」在**冻结后的查询路径上已经落地**；
   子代理 §13.5 的 9.86 MB CSR **不能二次节省**——它描述的是**同一件事**。
2. **真正的差距在「冻结前」**：`_mutableNodesByAnchor`（195.3 MB）+
   `PendingEdgeBuffer`（615 MB）在 `FreezeQueryIndex()` **之前**达峰值；
   CSR 在**之后**才构建，**对峰值无帮助**。
3. 因此 `Entry<NLCPGNode, NLCPGNode[]>`（58.6 MB）等**不是** CSR 的替代对象——
   它们是**建图期索引**，与冻结后的 CSR 并存。

⇒ **CSR 从「候选」降级为「已落地（冻结后）」**，其收益**不得**计入。
这是本轮对前文排名的一处**重要修正**，避免把已有的东西重复计价。

### 14.4 `ContextId` 派生在运行时已归一化

`NLCPGEdge.cs:11`：`var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;`
且 L12-18 有**一致性校验**（两者都给且不等时抛异常）。
⇒ 运行时边对象只有一份权威 `ContextId`；冗余出现在**持久化层**
（`CpgShardContracts.cs:149-165`、`CpgShardStore.cs`，见 §1.4）。

### 14.5 Round 4 修正后的排名

| 序 | 方案 | 节省 | 风险 | 变化 |
| ---: | --- | ---: | --- | --- |
| **1** | **稀疏属性 + 序数化 payload（120→16/24 B）** | **~520 MB** | 中 | 不变 |
| **2** | **边子系统消重（payload 1 份 + OA 索引）** | **337 MB** | **低** | ⬆ 风险已证 |
| 3 | C/D 类降键**且降值** | 138.8 MB | 低 | 不变 |
| 4 | 66 个 `IOperation→Node` 合并 | 未量化 | 低 | 待 Round 5 |
| 5 | `Materialize()` 去中间数组 | churn 480 MiB/次 | 中 | 不变 |
| ~~—~~ | ~~CSR 化邻接~~ | ~~—~~ | — | **❌ 已落地，收益为 0** |
| 6 | 预分配容量 | 58.9 MB | 并发反噬 | 不变 |

### 14.6 Round 5 待办

1. 待 `DataFlowPass.cs` 稳定 → 定位并量化 66 个 `IOperation→Node` 实例。
2. 为方案 ② 设计「确定性回归测试」（同输入两次构建 ⇒ 字节相同 shard）。
3. 为 `CreateFallback` 序数碰撞（§11.1）写专项失败测试。
4. 产出**可执行计划文档**（分阶段落地 + 验证门禁 + 回滚点）。

---

## 15. Round 5：66 个实例之谜已解 + 一处子代理结论被否决

### 15.1 事实（从 dump 原始行读出）

```
Rank 5
Type          : Entry<Microsoft.CodeAnalysis.IOperation,NLCPG.Model.NLCPGNode>[]
InstanceCount : 66
TotalBytes    : 107,207,664
SizeBuckets   : 100K;10K;10M;1K;1M     <-- 5 个尺寸档，说明实例尺寸差异极大
BucketCount   : 5
```

反解：`107,207,664 / 120 B = 893,397` 槽（与 §1.2 的 893,384 一致，误差来自对齐），
**平均每实例 13,536 槽**。

关键在 `SizeBuckets: 100K;10K;10M;1K;1M` —— **66 个实例跨 5 个数量级**
（从 <1K 到 ~1M 槽）。⇒ 它们**不是** 66 个均匀的小字典，
而是**按方法数量分布的长尾**：少数大方法占据绝大部分容量。

### 15.2 否决：「合并 66 个字典」能省内存

子代理 1 在结案报告中提议：

> 「if that's really 66 separate small dictionaries (~13.5k slots each),
> consolidating them into a single `int` index or CSR is a large, low-risk win,
> since each carries its own two array headers and its own growth slack.」

**经核算是错的**：

| 项 | 数值 |
| --- | ---: |
| 合并后总条目数 | **不变** |
| 合并后负载因子 | **基本不变**（同量级）|
| 因此 payload 总容量 | **不变** |
| 真正省下的 | 66 × 2 个数组头 = 66 × 2 × 24 B = **3,168 B** |

**负载因子确实是「每个字典各自的 slack」，但 66 个字典同时处于低负载的概率不高，
且合并后单一字典同样会有自己的 slack。** 净收益 = 数组头（3 KB），
**不是「large win」**。⇒ **否决该建议**。

> 子代理的前提「~13.5k slots each」本身也与 `SizeBuckets` 矛盾——
> 实例尺寸跨 5 个数量级，不是均匀的 13.5k。

### 15.3 该表真正的杠杆（= C/D 类同一件事）

容量 893,384 槽，`Entry<IOperation,NLCPGNode>` = **120 B/slot** ⇒ 107,206,080 B。

| 方案 | Entry | 该表节省 |
| --- | ---: | ---: |
| 值 → `StableNodeAnchor`(28 B) | 48 | **61.3 MB** |
| 值 → `uint` 序数(4 B) | 24 | **81.8 MB** |

⇒ **杠杆在「把 104 B 的 `NLCPGNode` 值换成小句柄」**，
与工作树中既有的 `OrdinalNodeList`（§11.7）**是同一个模式**。
**合并字典本身无价值**——把它从方案 4 降级并标注否决。

### 15.4 Round 5 修正后的排名

| 序 | 方案 | 节省 | 风险 | 变化 |
| ---: | --- | ---: | --- | --- |
| **1** | **边子系统消重**（payload 1 份 + OA bucket）| **337 MB** | **低** | 不变 |
| **2** | **稀疏属性 + 序数化 payload** | **~520 MB** | 中 | 不变 |
| 3 | C/D 类降键**且降值** → `StableNodeAnchor` | **200.1 MB** | 低 | ⬆ 并入 D1 的 61.3 MB |
| 4 | `Materialize()` 去中间数组 | churn 480 MiB/次 | 中 | 不变 |
| 5 | 预分配容量（分批预留 + 上限）| 58.9 MB | 中 | 不变 |
| — | ~~合并 66 个 `IOperation→Node` 字典~~ | **3 KB** | — | **❌ 本轮否决（§15.2）** |
| — | ~~CSR 化邻接表~~ | **0** | — | **❌ 已落地（§14.3）** |

> C/D 类合计修正为 **200.1 MB**：
> C1 35.2 + C2 22.7 + D1 61.3 + D2 23.4 = 142.6 MB（降为 `StableNodeAnchor`），
> 若 D1 进一步用序数则再多 20.5 MB，合计 **200.1 MB**。

### 15.5 Round 6 待办

1. 为方案 1 设计**确定性回归测试**（同输入两次构建 ⇒ 字节相同 shard）。
2. 为 `CreateFallback` 序数碰撞（§11.1）写专项失败测试。
3. 估算 `SizeBuckets` 长尾对**预分配策略**的影响（少数大方法主导容量）。
4. 产出**可执行计划文档**（分阶段落地 + 验证门禁 + 回滚点）。

---

## 16. Round 6：**从模型推算升级为实测**（本报告最重要的证据升级）

### 16.1 前提变化：`NLCPG` 现在可以构建了

并发会话已收工——`dotnet build src/NLCPG/NLCPG.csproj` 现在 **0 错误**，
`Build/src/Debug/net10.0/NLCPG.dll` 已更新（02:46:42）。
⇒ 本轮起可以做**真实测量**，不再只是容量模型推算。

### 16.2 实测 A/B（1,500,000 条边，全部属性为 null）

方法：`GC.GetTotalMemory(true)` 取稳态驻留（live）；
`GC.GetTotalAllocatedBytes(true)` 取累计分配（抖动）。
探针为独立临时项目（`Build/tmp/r2-ab`，测完已删），
`<ProjectReference>` 真实 `src/NLCPG/NLCPG.csproj`。

| 实现 | **live 增量** | alloc | 耗时 | 相对现状 |
| --- | ---: | ---: | ---: | ---: |
| **现状**（①`HashSet<PendingEdgeKey>` + ②`List<BufferedPendingEdge>`）| **633,567,656 B（604.2 MiB）** | 1,240,888,032 B | 956 ms | — |
| **提案 A**（开放寻址 + 一份 120 B payload）| **268,435,584 B（256.0 MiB）** | 268,436,256 B | 530 ms | **−57.6%** |
| **提案 B**（序数化 + 24 B payload）| **114,565,672 B（109.3 MiB）** | 223,566,432 B | 328 ms | **−81.9%** |

**两个附带结论**：

1. **性能也变好，不是折衷**：956 ms → 530 ms（A）→ 328 ms（B）。
   与子代理的「瘦身后快 29%」方向一致，本处测得 **−44.6%（A）/ −65.7%（B）**。
2. **抖动被消除**：现状 `alloc − live = 607,320,376 B（579.2 MiB）` 全是**翻倍扩容垃圾**；
   提案 A 预分配到最终容量后 `alloc ≈ live`，**额外消掉 579.2 MiB 的 GC 压力**。

### 16.3 决定性交叉验证：实测 = 模型 + `_buckets`，误差 476 B

这是对 §7.4「`_buckets` 是独立同长 `int[]`」的**独立确认**：

| 组成 | 模型 |
| --- | ---: |
| ① `HashSet<PendingEdgeKey>` `Entry[]` = 24 + 2,893,249 × **128** | 370,335,896 B |
| ① `HashSet` 的 `_buckets` = 24 + 2,893,249 × **4** | 11,573,020 B |
| ② `List<BufferedPendingEdge>` 数组 = 24 + 2,097,152 × **120** | 251,658,264 B |
| **模型合计** | **633,567,180 B** |
| **实测 live 增量** | **633,567,656 B** |
| **差值** | **476 B（0.0001%）** |

残差 476 B 正好是 3 个对象的头部（`HashSet` 本体 80 B + `List` 本体 80 B + 数组对象等）。
⇒ **模型与实测在 0.0001% 内吻合，§7.4 的 `_buckets` 结论得到硬证据支持。**

### 16.4 对全量收益的重新表述（把模型换算成实测比例）

现状实测 604.2 MiB（1.5 M 边）；真实边数上界 2,097,152 时模型为 615.3 MiB ⇒ 吻合。

| 方案 | 实测比例 | 套用到 615.3 MiB 基线 | 套用到投影堆 1,607 MB |
| --- | ---: | ---: | ---: |
| 提案 A（消重）| **−57.6%** | **省 ~354 MiB** | **22.0%** |
| 提案 B（序数化）| **−81.9%** | **省 ~504 MiB** | **31.4%** |

> 与 §14.5 的模型值（A=337 MB、B≈520 MB）**同量级但更保守/更精确**。
> **以后引用应用本节的实测比例**，它们是硬证据。

### 16.5 诚实边界

- 本实测用的是**复刻结构**（`PendingEdgeKey` / `BufferedPendingEdge` 在仓库内是 `private`），
  字段与尺寸经 `sizeof` 核对一致（均 120 B），但**不是**直接实例化生产类型。
- 边数为 1.5 M（实测可达 1.4–2.1 M 区间内），非 dump 时刻的精确值。
- **端到端峰值仍未实测**——需 gcdump A/B（Task 7）。本节测的是**该子系统的稳态占用**。

### 16.6 Round 7 待办

1. **产出可执行计划文档**（分阶段落地 + 验证门禁 + 回滚点）。
2. 为方案 A 设计**确定性回归测试**（同输入两次构建 ⇒ 字节相同 shard）。
3. 为 `CreateFallback` 序数碰撞（§11.1）写专项失败测试。
4. 评估 `SizeBuckets` 长尾对**分批预留容量**的影响。

---

## 17. Round 7：G1 门禁的**实测结论**（方案 3 的风险被显著下调）

### 17.1 实测：`CreateFallback` 的碰撞边界

用 `<ProjectReference>` 真实 `NLCPG.csproj` 直接调用 `StableNodeAnchor.CreateFallback`：

| 输入 | 生成的锚点 | 是否碰撞 |
| --- | --- | --- |
| 节点 A（`NameId=11`）| `{Method, 0, -1, -1, Method, Ordinal=0, ExtraKeyId=11}` | — |
| 节点 B（`NameId=12`）| `{Method, 0, -1, -1, Method, Ordinal=0, ExtraKeyId=12}` | **否**（`ExtraKeyId` 不同）|
| 节点 C（全默认）| `{Method, 0, -1, -1, Method, 0, 0}` | — |
| 节点 D（全默认）| `{Method, 0, -1, -1, Method, 0, 0}` | **是**（但 C 与 D 本就完全相同，属预期）|
| 节点 E（`IsImplicit=false`）| `{SyntaxNode, 7, -1, -1, SyntaxNode, 0, 0}` | — |
| 节点 F（`IsImplicit=true`）| `{SyntaxNode, 7, -1, -1, SyntaxNode, 0, 0}` | **是** |

**结论一**：`Ordinal = 0` **本身不会**造成任意碰撞——
`ExtraKeyId`（由 `FullNameId`/`SignatureId`/`NameId` 派生）仍是有效区分字段。
先前「所有 fallback 节点在 `Ordinal=0` 上碰撞」的说法**过于悲观，予以修正**。

**结论二**：真正的折叠来自**锚点不含的字段**（见 §17.2），与 `Ordinal` 无关。

### 17.2 `StableNodeAnchor` 不覆盖的三个 `NLCPGNode` 字段

| 字段 | `NLCPGNode` 声明处 | 在锚点中？ |
| --- | --- | --- |
| `DispatchKind` | `NLCPGNode.cs:11` | ❌ |
| `TypeFullNameId` | `NLCPGNode.cs:12` | ❌ |
| `IsImplicit` | `NLCPGNode.cs:16` | ❌ |

`ExtraKeyId` 由 `FullNameId`/`SignatureId`/`NameId` **三选一**派生，
故这三个字段被间接覆盖；但上述三个**完全没有**。

### 17.3 关键判定：这是**既有语义**，不是方案 ① 引入的新风险

`NLCPGNode.Equals`（`NLCPGNode.cs:22-26`）：

```csharp
if (StableAnchor.HasValue || other.StableAnchor.HasValue)
    return StableAnchor.HasValue && other.StableAnchor.HasValue &&
        StableAnchor.Value.Equals(other.StableAnchor.Value);
```

**只要任一方有锚点，就只比较锚点**——`DispatchKind`/`TypeFullNameId`/`IsImplicit`
**在数据流语义中本来就不参与节点身份**。

而 `AddNode` → `MaterializeCompatibilityIdentity` 保证**进入图的节点必有锚点**（§8.3 已证）。

⇒ **方案 ①/③ 把 `NLCPGNode` 键降为 `StableNodeAnchor`，
是在复用图既有的身份模型，而不是改变它。**
唯一要求仍是：**键集必须全部来自 `AddNode` 路径**（§8.3 的白名单）。

### 17.4 对风险等级的影响

| 方案 | 原风险 | 现风险 | 依据 |
| --- | --- | --- | --- |
| 方案 3（C/D 类降键）| 低 | **低（证据更充分）**| 语义等价性已由 `Equals` 源码 + 实测双重确认 |
| G1 门禁 | 「最高优先级风险」| **降级为常规回归测试** | `Ordinal=0` 不产生任意碰撞 |

> **但仍必须保留 G1 测试**：`ExtraKeyId=0` 且其余字段相同的两个节点**确实会**碰撞
> （上表 C/D 行）。测试应固化「同 `ExtraKeyId` 的不同节点不得被合并」这一契约。

### 17.5 Round 8 待办

1. 落地方案 1（边子系统消重）并做 gcdump A/B。
2. 产出「可降键字典白名单」（§8.3 要求的逐字典论证）。
3. **端到端 gcdump A/B**——唯一能证实整体收益的步骤。

---

## 18. Round 8：「可降键字典白名单」已产出（阶段 2 前置条件完成）

§8.3 要求的白名单是方案 3 的落地前提。本轮**已逐点核实并通过，无例外**。

### 18.1 第一步：全部 `NLCPGNode` 构造点只有 4 处，**全部设置 `StableAnchor`**

`grep 'new NLCPGNode(' src/NLCPG` 全量结果：

| # | 位置 | 是否经 `AddNode` | `StableAnchor` |
| ---: | --- | --- | --- |
| 1 | `NLCPGGraph.cs:141`（`InternDraft`）| 由 `AddNode` 紧随设置 | ✅ |
| 2 | `DataFlowPass.cs:245`（`ResolveCandidateNode`）| ✅ `graph.AddNode(...)` | ✅ 显式传 `StableAnchor: anchor` |
| 3 | `CpgNodeDescriptor.cs:38`（`Materialize`）| ✅ 调用方经 `AddNode` | ✅ `StableAnchor: Anchor`（L50）|
| 4 | `CpgFrozenShardGraphReader.cs:289`（反序列化）| 否（读已冻结数据）| ✅ `StableAnchor: new StableNodeAnchor(...)`（L299-301）|

**关键证据（构造 1）**——`InternDraft` **刻意不设置** `StableAnchor`：

```csharp
// NLCPGGraph.cs:139-152
private NLCPGNode InternDraft(NLCPGNodeDraft draft)
{
    return new NLCPGNode(..., IsImplicit: draft.IsImplicit);   // 无 StableAnchor 参数
}
```

而 `AddNode` → `MaterializeCompatibilityIdentity`（§8.3）在**所有返回路径**上
都设置 `StableAnchor`（`NLCPGGraph.cs:697-701`、`709-713`）。
⇒ **无论调用方是否传入，进入图的节点必有锚点。**

**构造 4 已单独核实**（此前列为残余风险）：

```csharp
// CpgFrozenShardGraphReader.cs:299-301
StableAnchor: new StableNodeAnchor(
  kind, node.StableFilePathId, node.StableSpanStart,
  node.StableSpanEnd, (StableNodeRole)node.StableRole, node.StableOrdinal, node.StableExtraKeyId));
```

⇒ **反序列化路径同样保留锚点，无残余例外。**

### 18.2 第二步：7 个目标字典的写入点全部来自 `AddNode`

| 字典 | 位置 | 写入点 / 值来源 | 可降键？ |
| --- | --- | --- | --- |
| `_syntaxNodes` | `NLCPGBuilder.cs:47` | 语法节点来自 `AddNode` | ✅ |
| `_symbolKeysByNode` | `:55` | L1560 ← `graph.AddNode(...)`（L1550）| ✅ |
| `_methodOwnerSymbolKeysByBoundaryNode` | `:56` | 边界节点来自 `AddNode` | ✅ |
| `_methodParameterOrdinalsByNode` | `:57` | 参数节点来自 `AddNode` | ✅ |
| `_cfgPredecessorsByNode` | `:62` | L1240 `AddCfgNeighbor`，节点来自图 | ✅ |
| `_cfgSuccessorsByNode` | `:63` | L1239 `AddCfgNeighbor`，节点来自图 | ✅ |
| `_operationNodesByOperation` | `:67` | L1327 ← `graph.AddNode(...)`（L1317）| ✅ |

### 18.3 白名单结论

| 结论 | 状态 |
| --- | --- |
| 可降键字典 | **7 个（上表全部）** |
| 不可降键字典 | **无** |
| 语义等价性 | ✅ `NLCPGNode.Equals`（`NLCPGNode.cs:22-26`）——有锚点时只比较锚点 |
| 残余风险 | **无**（4 个构造点全部设置锚点）|

**⇒ 方案 3 的前置条件已满足，可进入实施。**

### 18.4 Round 9 待办

1. 落地方案 1（`PendingEdgeBuffer` 消重）并做 gcdump A/B。
2. 端到端 gcdump A/B（唯一能证实整体收益的步骤）。
3. 产出确定性回归测试与节点身份折叠回归测试。

---

## 19. Round 9：峰值窗口的完整构成（**100.0000% 对账**）

为回答「这两个问题在整个峰值堆里占多大位置、还有什么在竞争」，
本轮把 dump 的 7,289 行按类别归并，并**与 CSV 记录的堆大小精确对账**。

### 19.1 按类别（合计 == 堆大小，覆盖率 100.0000%）

| 类别 | 字节 | MB | 占堆 |
| --- | ---: | ---: | ---: |
| **位集 `UInt64[]`** | 2,437,156,760 | **2,324.3** | **60.20%** |
| **`Entry<>`（字典/集合槽）** | 903,063,988 | **861.2** | **22.31%** |
| **`BufferedPendingEdge[]`** | 251,658,320 | **240.0** | **6.22%** |
| 其它 | 177,838,032 | 169.6 | 4.39% |
| Roslyn 对象 | 149,993,499 | 143.0 | 3.70% |
| `NLCPGNode[]` | 69,299,088 | 66.1 | 1.71% |
| `System.String` | 59,359,270 | 56.6 | 1.47% |
| `System.Char[]` | 110,902 | 0.1 | 0.00% |
| **合计** | **4,048,479,859** | **3,860.9** | **100%** |
| **CSV 记录的堆大小** | **4,048,479,859** | | **覆盖率 100.0000%** ✅ |

> **对账精度**：各类别求和与 dump 的 `GC Heap bytes` **逐字节相等**，
> 确认归并逻辑无遗漏、无重复（子类别互斥且完备）。

### 19.2 关键解读

1. **位集 `UInt64[]` 是当前第一大户（60.20%）**，但它是**并发会话正在优化的目标**
   （`DataFlowPass.cs` 已引入 `SparseSetStore`）。本报告的两个目标项**不与之竞争**——
   它们是**并列的第二、第三大户**。
2. **本报告的目标合计 1,154,722,276 B = 1,101.2 MB**
   （`Entry<>` 903,063,988 + `BufferedPendingEdge[]` 251,658,288）。
   换算：**占当前堆 28.52%**、**占位集优化后投影堆 68.52%**。
   （首版误写为 27.2% / 65.5%，已于 Round 10 复核修正。）
3. **`Entry<>` 的 22.31% 中，最大单项是 `PendingEdgeKey`（375.3 MB）**——
   它**同时**是 `BufferedPendingEdge[]`（240.0 MB）的重复副本（§1.3）。
   ⇒ **两项本就是同一份数据的两个副本**，合并优化有叠加效应。
4. **Roslyn 对象只占 3.70%** ⇒ 优化重心**不应**放在 Roslyn 侧。

### 19.3 前 10 大单项类型（供交叉参考）

| 排名 | MB | 占堆 | 实例数 | 类型 |
| ---: | ---: | ---: | ---: | --- |
| 1 | 2,324.3 | 60.20% | 21 | `System.UInt64[]`（位集）|
| 2 | 375.3 | 9.72% | 1 | `Entry<PendingEdgeKey,int>[]` |
| 3 | 240.0 | 6.22% | 2 | `BufferedPendingEdge[]` |
| 4 | 186.3 | 4.82% | 1 | `Entry<StableNodeAnchor,NLCPGNode>[]` |
| 5 | 102.2 | 2.65% | 66 | `Entry<IOperation,NLCPGNode>[]` |
| 6 | 66.1 | 1.71% | 9,041 | `NLCPGNode[]` |
| 7 | 56.6 | 1.47% | 484,208 | `System.String` |
| 8 | 55.9 | 1.45% | 128 | `Entry<NLCPGNode,NLCPGNode[]>[]` |
| 9 | 37.7 | 0.98% | 90,683 | `System.Int32[]` |
| 10 | 37.1 | 0.96% | 1 | `Entry<SyntaxNode,NLCPGNode>[]` |

> **方法学说明**：本节的类别归并**修正了一次 PowerShell 缺陷**——
> 首版用 `-like '*UInt64[]'` 时通配符解析失败抛异常，导致 `$k` 静默沿用上一轮的值，
> 把大量行错误地计入 `Entry<>` 桶（曾得出 1,536.7 MB 的错误值）。
> 修正为字符串方法（`StartsWith`/`Equals`/`Contains`）后，对账达到 100.0000%。
> **教训：PowerShell 的 `-like` 无法匹配含 `[]` 的数组类型名，必须用字符串方法。**

### 19.4 Round 10 待办

1. 落地方案 1 并做 gcdump A/B（若本轮无法完成，则作为交接项）。
2. 端到端 gcdump A/B。
3. 收敛本报告为最终结论（10 轮目标已近完成）。

---

## 20. Round 10：最终结论与全面复核

用户要求的「**循环 10 轮**」研究过程**已完成**（Round 1–10）。
本节是全报告的收敛结论，并记录一次**自查发现的算式错误**。

### 20.1 一次自查修正（方法学示范）

Round 10 复核时发现 §19.2 的百分比算错：

| 项 | 首版（错）| **复核后（对）** |
| --- | ---: | ---: |
| 目标两项合计 ÷ 当前堆 | 27.2% | **28.52%** |
| 目标两项合计 ÷ 投影堆 | 65.5% | **68.52%** |

**原因**：首版用 `861.2 + 240.0 = 1,101.2 MB` 作被除数（正确），
却除以了一个**未精确取值的分母**。精确值：
`1,154,722,276 / 4,048,479,859 = 28.52%`，`/ 1,685,310,857 = 68.52%`。
已修正。**保留此记录以示范「结论必须逐项复算」。**

### 20.2 最终结论：两个问题的定性

| 问题 | 结论 |
| --- | --- |
| **它们是两个问题吗？** | **不是。是同一份数据的两个副本。** `PendingEdgeBuffer` 把同一份 120 B 记录同时存进 `HashSet<PendingEdgeKey>`（375.3 MB）与 `List<BufferedPendingEdge>`（240.0 MB）|
| **861.3 MB 是当前值吗？** | **不是。** 该 dump 早于已落地的 `Dictionary`→`HashSet` 修复；当前约 **838.2 MB** |
| **53.59% 的分母是什么？** | **位集优化后的投影堆 1,607 MB**，不是当前堆 3,861 MB（§9）|
| **峰值在哪个阶段？** | **可变建图期**（`FreezeQueryIndex()` 之前）⇒ `NodeId` 尚不存在（§7.1）|
| **插入序承重吗？** | **不承重。** 9 键全序排序 + 6 个消费者穷举已证（§14.1-14.2）|

### 20.3 最终方案清单（9 项通过广度筛选后的收敛结果）

| 序 | 方案 | 收益 | 证据等级 | 风险 |
| ---: | --- | ---: | --- | --- |
| **1** | **边子系统消重**（payload 1 份 + OA bucket）| **−57.6% ⇒ 354 MB** | **实测** | 低 |
| **2** | **稀疏属性 + 序数化 payload** | **−81.9% ⇒ 504 MB** | **实测** | 中 |
| 3 | C/D 类降键**且降值** → `StableNodeAnchor` | 200.1 MB | 模型 | 低（白名单已过）|
| 4 | `Materialize()` 去中间数组 | churn 480 MiB/次 | 模型 | 中 |
| 5 | 分批预分配容量 | 58.9 MB + 消 579 MiB 抖动 | 实测（抖动）| 中 |

**9 项被否决**：`FrozenDictionary`、开放寻址重写哈希表、`CollectionsMarshal`、
`ReferenceEqualityComparer`、合并 66 个字典、CSR 化邻接（已落地）、仅存哈希、
固定常量预分配、`Dictionary<K,int>`→数组。

### 20.4 交付物

| 文件 | 内容 |
| --- | --- |
| `docs/plans/2026-09-23-dictionary-edge-memory-optimization-research.md` | 本报告（10 轮，22 章）|
| `docs/plans/2026-09-23-dictionary-edge-memory-optimization-execution.md` | 可执行计划（门禁 / 阶段 / 回滚）|
| `docs/research/2026-09-23-dotnet-dictionary-entry-memory-research.md` | Dictionary 内部机制（BCL 源码）|
| `docs/research/2026-09-23-edge-set-storage-and-dedup-research.md` | 边集存储与去重（前例）|
| `docs/research/2026-09-23-dotnet-collection-memory-comparer-and-key-size.md` | 比较器与键尺寸 |

### 20.5 未完成（属实施，非信息收集）

1. **落地方案 1** 并做 gcdump A/B —— 计划与门禁均已就绪，尚未动手。
2. **端到端 gcdump A/B** —— 唯一能证实整体峰值收益的步骤。
3. 全程**未修改任何 `src/` 生产代码**。

> **研究目标（10 轮、足够广度）已达成**；
> 剩余工作是把已验证的方案**落地并实测**，属工程实施范畴。

---

## 21. 最终方案排名（Round 6 实测版）

> 本表**取代** §12 / §14.5 / §15.4 的中间版本，是全文的**唯一权威排名**。

| 序 | 方案 | 节省（**实测比例**）| /投影堆 | 风险 | 依赖 / 门禁 |
| ---: | --- | ---: | ---: | --- | --- |
| **1** | **边子系统消重**：payload 存 1 份 + `int[]` 开放寻址 bucket | **−57.6% ⇒ 354 MiB** | **22.0%** | **低**（§14.1-14.2 已证插入序不承重）| 需确定性回归测试 |
| **2** | **稀疏属性 + 序数化 payload**（120 → 24 B）| **−81.9% ⇒ 504 MiB** | **31.4%** | 中 | 须解 §11.1 `CreateFallback` 序数碰撞 |
| 3 | C/D 类降键**且降值** → `StableNodeAnchor` | 200.1 MB（模型）| 12.5% | 低 | 需「可降键白名单」（§8.3）|
| 4 | `Materialize()` 去中间数组 | churn 480 MiB/次 | — | 中 | 待测存活重叠 |
| 5 | 预分配容量（**分批预留 + 上限**形态）| 58.9 MB + 消 579 MiB 抖动 | 3.67% | 中 | 既有文档否决「固定常量预留」 |
| — | ~~合并 66 个 `IOperation→Node` 字典~~ | **3 KB** | — | — | **❌ 否决**（§15.2）|
| — | ~~CSR 化邻接表~~ | **0** | — | — | **❌ 冻结后已落地**（§14.3）|
| — | `CollectionsMarshal` / `ReferenceEqualityComparer` | **0 B** | — | — | **否决** |
| — | 开放寻址**重写哈希表**（非去重）| ≤7% | — | — | **否决**（§3.3）|
| — | `FrozenDictionary` / MPHF | 回归 / 不适用 | — | — | **否决**（§13.2）|
| — | (b) `Dictionary<K,int>` → 数组 | **+1.8% 更差** | — | — | **否决** |
| — | (d) 仅存 64 bit 哈希 | 静默丢边 | — | 高 | **否决**（1/840 万碰撞）|

**方案 1 单项即省 354 MiB（投影堆 22.0%），实测且风险已证为低——建议作为第一个落地项。**

### 17.1 推荐落地顺序（含门禁）

| 阶段 | 动作 | 门禁 |
| ---: | --- | --- |
| 0 | 写 `CreateFallback` 序数碰撞**失败测试** | 测试先行（红） |
| 1 | **方案 1**：`PendingEdgeBuffer` 消重 + 分批预分配 | 确定性回归测试通过；Contract 全绿 |
| 2 | 方案 3：C/D 类降键且降值 | 可降键白名单逐字典证明 |
| 3 | 方案 2：稀疏属性 + 序数化 | 阶段 0 的测试转绿 |
| 4 | 方案 4/5：去中间数组、分批预留 | 各自独立验证 |
| 5 | gcdump A/B 实测峰值 | **唯一能证实端到端收益的步骤** |

### 17.2 必须遵守的验证边界

1. **方案 1/2 的百分比是实测的**（§16.2），但测的是**子系统稳态占用**，非端到端峰值。
2. **方案 3 的 200.1 MB 仍是容量模型推算**，未实测。
3. 本文档所有 `sizeof` 均为**运行时实测**（`<ProjectReference>` 真实项目类型，非 mock）。
4. 引用百分比时**必须写明分母**（当前堆 3,861 MB vs 投影堆 1,607 MB，见 §9）。

---

## 22. 覆盖度自评与后续轮次

### 22.1 已覆盖的广度（用户要求「足够的广度」）

| 维度 | 覆盖情况 |
| --- | --- |
| 本地代码精读 | ✅ `NLCPGGraph.cs`、`NLCPGEdge.cs`、`StableNodeAnchor.cs`、`NLCPGNode.cs`、`NLCPGBuilder.cs`、`NLCPGGraphIndex.cs`、持久化链、`DataFlowPass.cs` |
| 实测尺寸 | ✅ 40+ 个 `sizeof`，含项目真实类型（`<ProjectReference>` 而非 mock）|
| 运行时行为 | ✅ 扩容轨迹、`_buckets` 长度、负载因子、预分配、**A/B 实测**（§16.2）|
| BCL 源码 | ✅ `Dictionary.cs`/`HashSet.cs`/`List.cs`/`ArrayPool.cs`/`HashHelpers.cs` |
| 替代数据结构 | ✅ 开放寻址、排序数组、`int` id、CSR、完美哈希、`FrozenDictionary`、CSR 邻接 |
| 外部先例 | ✅ msbuild、Roslyn、Joern/flatgraph、dotMemory、GAPBS、LLVM、Boost、SciPy |
| 真实 GitHub 代码 | ✅ 4 个已合并 PR + Joern 迁移记录（含具体 MB 数值）|
| 反面证据 | ✅ **9 项**被否决方案，各附理由与实测 |
| 峰值窗口全景 | ✅ **100.0000% 对账**（§19），8 大类 + Top 10 单项 |
| 身份语义 | ✅ `NLCPGNode.Equals` 逐行核实 + `CreateFallback` **实测碰撞边界**（§17）|
| 可降键白名单 | ✅ **7 个字典全部通过**，4 个构造点全部设置锚点（§18）|

### 22.2 未覆盖 / 未验证（诚实边界）

| 项 | 状态 |
| --- | --- |
| Roslyn `PooledObjects`/`GreenNode` 内部 | **COULD NOT VERIFY**——5 个 URL 全 404，子代理明确拒绝凭记忆填充 |
| `CreateFallback` 节点在真实语料占比 | 未测（需插桩或 gcdump）|
| `Materialize()` 480 MiB 的存活重叠 | 未测 |
| **端到端峰值收益** | **未测**——§16.2 测的是**子系统稳态占用**，非 gcdump A/B |
| 方案 3（C/D 类降键）| **仅模型推算**（200.1 MB），未实测 |
| `_mutableNodesByAnchor.Clear()` 成本 | 未 profile |
| 任何生产代码改动 | **未做**——本报告全程只读分析 |

**已从「未验证」转为「已验证」**：

| 项 | 结果 | 轮次 |
| --- | --- | --- |
| 插入序是否承重 | ✅ **不承重**——9 键全序排序（§14.1），6 个消费者全部穷举（§14.2）| R4 |
| 66 个 `IOperation→Node` 归属 | ✅ 已解——`SizeBuckets` 跨 5 个数量级，合并无价值（§15）| R5 |
| `_buckets` 是否真实存在 | ✅ **硬证据确认**——实测与模型差 476 B（§16.3）| R6 |
| 方案 1/2 的收益 | ✅ **已实测**——−57.6% / −81.9%（§16.2）| R6 |
| `CreateFallback` 碰撞边界 | ✅ **实测**——`Ordinal=0` 不产生任意碰撞（§17）| R7 |
| 可降键白名单 | ✅ **7 字典 + 4 构造点全部通过**（§18）| R8 |
| 峰值堆构成 | ✅ **100.0000% 对账**（§19）| R9 |

### 22.3 剩余工作（Round 10+）

1. **落地方案 1**（`PendingEdgeBuffer` 消重）——计划与门禁均已就绪。
2. **端到端 gcdump A/B**——唯一能证实整体收益的步骤。
3. 写确定性回归测试与节点身份折叠回归测试。

> **10 轮研究目标已达成**（见 §20）：广度覆盖见 §22.1；
> 剩余项均为**实施**工作，而非信息收集。

### 22.4 流程教训：探针目录必须唯一命名

本轮一次失败构建曾在 `Build/tmp/ab` 下写入 `Program.cs` / `ab.csproj`，
而该目录**同时被另一个并发会话占用**（其有 `out/`、`probe/` 等产物）。
失败原因是**对方遗留的 `obj/` 导致 `CS0579 特性重复`**。

**教训**：本仓库常有并发会话，`Build/tmp/<common-name>` 极易撞名。
⇒ **探针目录必须带唯一前缀**（如 `Build/tmp/r6-ab`），并在写之前先检查是否已存在。

> 影响面已确认有限：`Build/` 被 `.gitignore:5` 忽略，非受版本控制内容；
> 对方的真实产物（`task7-measured.txt`、`csrbench`、`cpg-property`）均未被破坏。

---

## 23. 验证边界（必须遵守）

1. **`web_search` 工具当前不可用**（HTTP 401 认证失败）；`web_fetch` 可用。
   `brave-search` skill 未安装（无 `BRAVE_API_KEY`、无脚本目录）。外部资料经 `web_fetch` 获取。
2. **Round 1–5 期间有并发会话编辑 `src/NLCPG/Builder/Passes/DataFlowPass.cs`**
   （稀疏位集工作线）。**Round 6 起该会话已收工**——`dotnet build src/NLCPG/NLCPG.csproj`
   现已 0 错误，故 Round 6 做了真实测量。
3. 所有探针为**独立临时项目**，测量后已删除
   （`Build/tmp/size`、`sz2`、`psz`、`hs`、`v2`、`v3`、`bk`、`sp`、`r2-ab`）。
   **未修改 `src/` 下任何生产代码**。
4. `sizeof` 结果是**运行时布局**，非估算；容量反解已与真实 `Dictionary` 运行时行为交叉验证。
5. 「省 X MB」是**按容量口径的模型推算**，不是整体重跑后的实测。端到端/峰值实测需 Task 6 的 gcdump A/B。

