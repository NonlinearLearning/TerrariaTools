# LOH 大对象清单：从既有性能报告中定位

**日期：** 2026-09-24
**方法：** 按 [LOH 大对象优化指导](../loh-large-object-optimization-guide.md) §2 的诊断流程，
对**既有的**性能报告做离线挖掘（**未新增任何一次运行**）。
**数据源：** `D:\TRbackup\Version4\Build\NL-diagnostics-dop12\20260923-130737-dop12`
（运行 `20260923-130737`，DOP12，退出码 `0x80131506`）。

> **本文只回答"LOH 里到底有什么大对象"。** 不新增测量、不改代码。

---

## 0. 结论摘要

| # | 结论 | 强度 |
| --- | --- | :---: |
| L1 | 既有报告能看到 **LOH 常驻大对象**，但**看不到 LOH 暴涨那一刻的常驻现场**（见 §1.2） | 实测 |
| L2 | 按常驻峰值，第一大是 **`InterproceduralDataFlowPlan[]` 755.3 MiB**，单对象最大 **595.2 MiB** | 实测 |
| L3 | 按 60 s 分配，第一大调用点 `RunInterproceduralDataFlowPass` 占 **LOH 分配的 60.93%** | 实测 |
| L4 | 该类元素宽度是 **428 B，不是既有根因文档写的 48 B** ⇒ 既有文档里所有"元素数=字节/48"的推算**偏大约 8.9 倍** | 实测+源码 |
| L5 | 既有汇总文件 `gcdump-type-summary.csv` **混入了 2 个饱和样本**，使 `System.String` 峰值被**夸大约 6.58 倍** | 实测 |
| L6 | 清单中至少 **2 项已在工作区被消除**，但**尚未提交**；**HEAD 上全部仍然存在** | 实测 |

---

## 1. 数据卫生：三个必须先修的坑

用这些报告前，有三处会让结论直接出错。**这三条正是指导 §5.1 警告的东西在实际数据里的实例。**

### 1.1 坑一：`gcdump-type-summary.csv` 混入了饱和样本

`loh-composition-by-sample.csv` 明确标注了样本可用性：

| 样本 | 时刻 | 标注 |
| --- | ---: | --- |
| `0001`–`0023` | 0–152 s | **USABLE** |
| `0027-late-272s` | 272 s | **UNUSABLE：object cap hit；LOH buckets absent，覆盖 ~15%** |
| `0028-late-303s` | 303 s | 同上 |
| `0033-late-453s` | 453 s | DEGENERATE：崩溃前堆已清空（4,097 对象） |

但 `gcdump-type-summary.csv` 的 `Max*` 列**把 `0027`/`0028` 算了进去**：

| `System.String` | 值 |
| --- | ---: |
| 汇总里的 `MaxTotalBytes` | **1,675,723,674 B**（= 1,598.1 MiB）@ `0027` |
| 仅用可用样本重算的峰值 | **254,785,318 B**（= 243.0 MiB）@ `0015` |
| **夸大倍数** | **6.58×** |

⇒ **凡引用 `gcdump-type-summary.csv` 的 `Max*`/`Min*` 列，都必须先排除 `0027`/`0028`/`0033`。**
正确做法是自己从 `gcdump-type-stats.csv`/`gcdump-type-totals-per-sample.csv` 按 `Sample` 过滤后聚合
（本文全部数据均如此处理）。

### 1.2 坑二：**没有一份报告覆盖 LOH 暴涨的那一刻**

三条数据链的时间窗各不相同，且**都不覆盖 241–287 s**：

```text
分配 trace   :  0 ── 60 s          （LOH 分配榜来自这里）
gcdump 快照  :  0 ── 152 s         （最后可用样本 0023；之后样本全部饱和/退化）
counters     :  0 ── 全程          （唯一能看到 17.32 GiB 峰值的数据）
LOH 暴涨     :        241 ── 287 s  ← 台阶 +5,802 / +4,963 / +2,923 MB
```

**⇒ 关键限制**：本文清单里的"大对象"是**暴涨之前的常驻**（gcdump ≤ 152 s，峰值仅 **916.4 MiB**），
而真正的 **17.32 GiB LOH 峰值只存在于 counters**，**没有任何一份 gcdump 或 trace 拍到它的现场**。

这条与既有 `LOH-ROOTCAUSE` 的 R2 一致（"trace 看不到这次暴涨"），
**本文补充：gcdump 同样看不到**。

⇒ 要把清单从"暴涨前"升级为"暴涨时"，必须**重跑并把 trace 窗口与 gcdump 采样对准 200–320 s**。
这是本文最明确的后续动作。

### 1.3 坑三：size bucket 是**十进制**，且 `10K` 桶**跨越 85,000 B 阈值**

`gcdump-type-stats.csv` 的 `SizeBucket` 用 `1K/10K/100K/1M/...` 标签，但**不能**假设它是二进制——
实测 `1,010,304 B` 的实例被放进了 `1M` 桶（因为 > 1,000,000）：

| 桶 | 桶内 avg 实测 min | 是否入 LOH |
| --- | ---: | --- |
| `1K` | 1,024 | 否 |
| `10K` | 10,348 | **跨越**：1,468 行 < 85,000；**2 行 ≥ 85,000** |
| `100K` | 106,296 | **是（确定）** |
| `1M` | 1,010,304 | 是（确定） |
| `10M` | 11,573,020 | 是（确定） |
| `100M` | 109,051,928 | 是（确定） |

校验方法：对每个桶算 `TotalBytes / InstanceCount`，若假设二进制下界则 `1M` 桶有 **11 行违反**；
改为十进制下界后**全部一致** ⇒ 标签是**十进制下界**。

**⇒ 提取 LOH 常驻对象时，只能用 `100K`/`1M`/`10M`/`100M` 四个桶；`10K` 桶不可整体当 LOH。**

---

## 2. LOH 常驻大对象清单（gcdump，≥ 85,000 B）

### 2.1 按峰值排序

由 `100K`+ 桶按 `Type` 聚合，取每类型在**可用样本**（已排除 `0027`/`0028`/`0033`）中的最大 `TotalBytes`。
"实例"指该峰值样本内**落在 LOH 桶中的**实例数（不含同类型的小数组）；
"样本数"指该类型以 ≥100 KB 出现的可用样本数。

| # | 类型 | 峰值 | 实例 | 样本数 | 常驻度 |
| ---: | --- | ---: | ---: | ---: | :---: |
| 1 | `NLCPG.Builder.Passes.InterproceduralDataFlowPlan[]` | **755.3 MiB** | 2 | 2 | 尖峰 |
| 2 | `Entry<NLCPG.Model.NLCPGEdge>[]` | 106.5 MiB | 1 | 9 | 中 |
| 3 | `NLCPG.Model.NLCPGNode[]` | 104.0 MiB | 1 | **29** | **高** |
| 4 | `NLCPG.Model.NLCPGEdge[]` | 81.0 MiB | 6 | 9 | 中 |
| 5 | `PendingEdge[]` | 80.4 MiB | 1 | 5 | 尖峰 |
| 6 | `Entry<Microsoft.CodeAnalysis.IOperation,NLCPG.Model.NLCPGNode>[]` | 74.3 MiB | 2 | 19 | 中 |
| 7 | `Entry<NLCPG.Model.NLCPGGraph+PendingEdgeBuffer+PendingEdgeKey>[]` | 66.2 MiB | 1 | 15 | 中 |
| 8 | `Entry<NLCPG.Model.NLCPGNode,NLCPG.Model.NLCPGNode[]>[]` | 54.0 MiB | 10 | 8 | 中 |
| 9 | `Entry<NLCPG.Model.StableNodeAnchor,System.Int32>[]` | 53.2 MiB | 1 | 14 | 中 |
| 10 | `Entry<Microsoft.CodeAnalysis.SyntaxNode,NLCPG.Model.NLCPGNode>[]` | 37.1 MiB | 1 | 11 | 中 |
| 11 | `Entry<NLCPG.Model.NLCPGNode,System.Int32>[]` | 33.1 MiB | 5 | 4 | 低 |
| 12 | `System.Int32[]` | 28.8 MiB | 2 | **30** | **高** |
| 13 | `NLCPG.Builder.Streaming.CpgEdgeCandidate[]` | 24.2 MiB | 1 | 13 | 中 |
| 14 | `Entry<NLCPG.Model.NodeId,NLCPG.Model.NLCPGNode>[]` | 17.3 MiB | 1 | 9 | 中 |
| 15 | `System.String` | 13.5 MiB | 5 | **46** | **最高** |
| 16 | `Entry<Microsoft.CodeAnalysis.SyntaxNode,Roslyn.Utilities.OneOrMany<...BoundNode>>[]` | 11.5 MiB | 5 | 17 | 中 |
| 17 | `Entry<...MarkAnalysisSnapshot+GraphBindingKey,NLCPG.Model.NLCPGNode>[]` | 9.2 MiB | 1 | 4 | 低 |
| 18 | `NLCPG.Model.StableNodeAnchor[]` | 6.5 MiB | 24 | **36** | **高** |

> **"常驻度"列值得单独看**：`System.String`（46 样本）、`StableNodeAnchor[]`（36）、
> `System.Int32[]`（30）、`NLCPGNode[]`（29）**几乎每个样本都在**——
> 它们是**基线**；而 `InterproceduralDataFlowPlan[]` **只出现在 2 个可用样本里**（峰值 755.3 MiB），
> `PendingEdge[]` 也只 5 个——它们是**尖峰**，不是基线。
> 按指导 §2 Step 2，**基线才是 LOH 高位常驻的成因**，尖峰只决定锯齿高度。

> ⚠️ **不要用"样本数"排序就下结论**：`InterproceduralDataFlowPlan[]` 样本数只有 2，
> 但它是**峰值第一**、且占 LOH **分配**的 51.75%（§3）。**常驻峰值**与**分配量**是两个不同口径。

### 2.2 最大的单个对象

**先分清两个桶**（否则会像本文初稿一样把 `100M` 和 `10M` 混成一张表）：

**(a) `100M` 桶 —— 单对象 ≥ 100 MB，共 7 行**

| 单对象 | 该行合计 | 实例 | 类型 | 样本 |
| ---: | ---: | ---: | --- | --- |
| **377.7 MiB** | 755.3 MiB | 2 | `InterproceduralDataFlowPlan[]` | `0014-early-71s` |
| **249.5 MiB** | 249.5 MiB | 1 | `InterproceduralDataFlowPlan[]` | `0013-early-66s` |
| 106.5 MiB | 106.5 MiB | 1 | `Entry<NLCPG.Model.NLCPGEdge>[]` | `0015-early-77s` |
| 104.0 MiB | 104.0 MiB | 1 | `NLCPG.Model.NLCPGNode[]` | `0020`/`0021`/`0022`/`0023` |

> `NLCPGNode[]` 的 104.0 MiB 在 4 个连续样本里**逐字节相同**（均 109,051,928 B）
> ⇒ 这是**同一批稳定驻留数据**，不是抖动。

**(b) `10M` 桶 —— 单对象 ≥ 10 MB，取最大的行**

| 单对象 | 类型 | 样本 |
| ---: | --- | --- |
| 80.4 MiB | `PendingEdge[]` | `0013`/`0014` |
| 66.2 MiB | `Entry<...PendingEdgeBuffer+PendingEdgeKey>[]` | 4 个样本 |
| 64.4 MiB | `NLCPG.Model.NLCPGEdge[]` | `0015-early-77s` |
| 53.2 MiB | `Entry<StableNodeAnchor,int>[]` | 4 个样本 |
| 37.1 MiB | `Entry<SyntaxNode,NLCPGNode>[]` | `0020`/`0021` |
| 28.8 MiB | `System.Int32[]` | — |

**`InterproceduralDataFlowPlan[]` 一个类型就占了当时存活堆的 67.75%**
（`memory-ranking-by-peak.csv`：755.31 MiB，HeapSharePct 67.75）。

### 2.3 每样本 LOH 常驻合计（可用样本）

| 样本 | 时刻 | 托管堆 | LOH 常驻 | LOH 占比 |
| --- | ---: | ---: | ---: | ---: |
| `0001` | 0 s | 152.3 MiB | 25.1 MiB | 16.5% |
| `0009` | 43 s | 195.8 MiB | 32.4 MiB | 16.5% |
| `0013` | 66 s | 602.0 MiB | 403.5 MiB | 67.0% |
| **`0014`** | **71 s** | **1,114.9 MiB** | **916.4 MiB** | **82.2%** |
| `0019` | 98 s | 511.1 MiB | 261.0 MiB | 51.1% |
| `0020` | 104 s | 949.5 MiB | 634.7 MiB | 66.8% |
| `0023` | 152 s | 883.2 MiB | 583.1 MiB | 66.0% |

⇒ LOH 常驻从 **25.1 MiB 涨到 916.4 MiB（36.5×）**，而这一切都发生在**暴涨之前**。

---

## 3. LOH 分配清单（60 s trace）

来自 `LohAlloc`（按 `GCAllocationTick.AllocationKind == Large` 直筛，**不受 gcdump 对象数截断影响**）。

### 3.1 总量

| 指标 | 值 |
| --- | ---: |
| LOH 分配总量 | **6,417,705,512 B = 5.98 GiB** |
| 占全部分配 | **46.49%** |
| 占 tick 数 | **10.13%** |
| 最大单对象 | **624,099,352 B = 595.2 MiB** |
| 最大单对象类型 | `InterproceduralDataFlowPlan[]` |
| 不同 LOH 类型 / 调用点 | 57 / 45 |

### 3.2 按类型 Top 6

| # | 类型 | 分配 | 占比 |
| ---: | --- | ---: | ---: |
| 1 | `InterproceduralDataFlowPlan[]` | **3,167.45 MiB** | **51.75%** |
| 2 | `PendingEdge[]` | 624.44 MiB | 10.20% |
| 3 | `NLCPGEdge[]` | 510.88 MiB | 8.35% |
| 4 | `Entry<InterproceduralDataFlowPlan>[]` | 504.11 MiB | 8.24% |
| 5 | `Entry<NLCPGEdge>[]` | 303.35 MiB | 4.96% |
| 6 | `NLCPGNode[]` | 227.14 MiB | 3.71% |

### 3.3 按调用点 Top 5

| # | 调用点 | 分配 | 占比 |
| ---: | --- | ---: | ---: |
| 1 | `RunInterproceduralDataFlowPass` | **3,729.40 MiB** | **60.93%** |
| 2 | `PendingEdgeBuffer.Materialize` | 621.65 MiB | 10.16% |
| 3 | `AssignDeterministicNodeIds` | 580.90 MiB | 9.49% |
| 4 | `NLCPGGraphIndex.Create` | 450.95 MiB | 7.37% |
| 5 | `BuildCsr` | 309.80 MiB | 5.06% |

### 3.4 按叶函数 Top 6（定位到具体语句）

| # | 叶函数 | 分配 | 占比 |
| ---: | --- | ---: | ---: |
| 1 | `List<InterproceduralDataFlowPlan>.set_Capacity` | **2,283.96 MiB** | **37.32%** |
| 2 | `PendingEdgeBuffer.Materialize` | 621.65 MiB | 10.16% |
| 3 | `HashSet<InterproceduralDataFlowPlan>.Initialize` | 508.54 MiB | 8.31% |
| 4 | `Enumerable.ToArray` | 465.21 MiB | 7.60% |
| 5 | `OrderedIterator<InterproceduralDataFlowPlan>.ToArray` | 441.74 MiB | 7.22% |
| 6 | `HashSet<NLCPGEdge>.Resize` | 246.53 MiB | 4.03% |

⇒ **同一类型 `InterproceduralDataFlowPlan` 同时出现在 5 个叶子函数里**
（`List` 扩容、`HashSet` 初始化、`ToArray`×2），正是指导 §2 Step 3 说的
**"同一批逻辑数据在多个容器里各存一份"**。

### 3.5 与基线对比（可复现性）

| 指标 | 本次 | 基线 | 判定 |
| --- | ---: | ---: | --- |
| LOH 分配总量 | 5.977 GiB | 5.959 GiB | 同量级 |
| 最大单对象 | **624,099,352 B** | **624,099,352 B** | **逐字节相同** |
| 第一大调用点 | 3,729.40 MiB | 3,729.40 MiB | 逐字节相同 |

⇒ **既存问题，非本次回归。**

---

## 4. 第一原因：`InterproceduralDataFlowPlan[]`

按指导 §2 Step 4，判死字节与放大：

```csharp
// src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs:9-16
internal readonly record struct InterproceduralDataFlowPlan(
  NLCPGNode CallSiteNode,        // 104 B
  NLCPGNode TargetMethodNode,    // 104 B
  NLCPGNode SourceNode,          // 104 B
  NLCPGNode TargetNode,          // 104 B
  NLCPGInterproceduralBridgeKind BridgeKind,  //   4 B
  int ArgumentOrdinal = -1,      //   4 B
  int StableCallSiteOrder = 0);  //   4 B
// 合计 428 B/元素
```

| 放大环节 | 实测 |
| --- | --- |
| 元素宽度 | **428 B** |
| 单对象最大 | **595.2 MiB = 624,099,352 B** |
| 该单对象元素数 | **1,458,176** |
| 该类型 LOH 分配占比 | **51.75%** |
| 其调用点 LOH 分配占比 | **60.93%** |
| 其 `Entry<>` 数组额外占 LOH | **8.24%**（第二个容器） |
| 常驻峰值 | **755.3 MiB**（`0014` 样本：LOH 桶内 2 个 100 MB 级数组；该类型含小数组共 6 实例、合计 792,000,020 B） |

**结构性问题（源码可读出）**：
- `plans` 列表**只有"每调用点"上限（10,000），没有全局上限**。
- 数据流预算（`MaxFlowNodesPerMethod` 等）**在产品里从未被设置** ⇒ 取 `int.MaxValue`，**等于没有预算**。
- 该链把**百万级**计划**分多次物化 → 全局 `Distinct()` → 8 级 `OrderBy` → `ToArray()`**，
  `DistinctIterator` 与 8 个 `OrderedIterator` **与数组同时存活**。

⇒ 对应指导的**模式 C（删除重复载体）** + **模式 A（换表示）**，且**必须先有全局预算**。

---

## 5. 纠错：元素宽度是 **428 B**，不是 48 B

既有 `LOH-ROOTCAUSE-20260923-dop12.md`（R5、§3.2、§3.3）称该结构体是 **48 B**。
**这是错的，实测与源码都指向 428 B。** 三重证据：

**(a) 字段构成**

```text
4 × NLCPGNode(104) = 416 B
+ NLCPGInterproceduralBridgeKind (int)   4 B
+ int ArgumentOrdinal                    4 B
+ int StableCallSiteOrder                4 B
= 428 B
```

**(b) 源码注释（同一份源码，`NLCPGBuilder.cs:831-832`）**

> `InterproceduralDataFlowPlan` 是 record struct，含 4 个 `NLCPGNode`（各 104 B）
> ⇒ **≈428 B/元素**

**(c) 两个真实单对象的整除性检验**

| 观测（`inst=1`，真实单对象） | ÷428 | ÷48 |
| --- | --- | --- |
| 624,099,352 B | **1,458,176 整** ✔ | 13,002,069.33 ✘ |
| 261,648,408 B | **611,328 整** ✔ | 5,451,008 整 |

⇒ `48 B` 对最大的那个真实对象**不是整数**，故**必错**。

**影响面**——既有文档中所有用 `÷48` 得来的数都要**除以 8.92（= 428/48）**重算：

| 既有表述 | 应为 |
| --- | --- |
| "约 **1,300 万**个 48 B 元素" | 约 **146 万**个 428 B 元素（精确：1,458,176） |
| "`OrderedIterator.ToArray()` 15 次共 **9,664,375 个元素**" | 约 **1,083,855** 个元素 |
| "`List.set_Capacity` 累计 **5,391 万槽位**" | 约 **6,045,981** 槽位 |

> ⚠️ **注意**：`48 B` 这个错值很可能来自把 `NLCPGNode` 当**引用**（4×8=32）而非
> **104 B 的值类型**——与指导 §1.2 记录的 `InterproceduralDataFlowPlan` 被推导为 48 B
> 是**同一个错**。指导 §1.2 的"永远实测，不要推导"正是针对它。

**结论方向不变**：元素仍是百万级、仍是 LOH 第一大。
但**量级叙述必须改**（1,300 万 → 146 万），否则会误导容量规划。

---

## 6. 每项的当前状态（关键）

**HEAD 是 `925161d`（2026-09-21）；诊断运行是 2026-09-23。**
下列"已消除"均指**工作区未提交改动**——`git log -S` 证明这些修复**不在任何提交里**。

| LOH 大对象 | 峰值/占比 | 状态 |
| --- | ---: | --- |
| `InterproceduralDataFlowPlan[]` | 755.3 MiB / LOH 分配 51.75% | **HEAD 存在**；工作区有逐调用点流式重构（+284/−90 行，未提交） |
| `Entry<NLCPGEdge>[]` | 106.5 MiB | **工作区已消除**（`_edges` HashSet 已删 + `CanonicalEdgeStore`）；HEAD 存在 |
| `NLCPGEdge[]` | 81.0 MiB | **工作区已换表示**（SoA，9~13 B/边）；HEAD 存在 |
| `NLCPGNode[]` | 104.0 MiB | **未处理**（文档记录收益 ~179 MB 但**改指纹**，故不做） |
| `PendingEdge[]` | 80.4 MiB | 部分（`PendingEdgeKey` 16 B 侧已做） |
| `Entry<StableNodeAnchor,int>[]` | 53.2 MiB | **已在本次运行中收窄**（旧 `->NLCPGNode` 186.29 MiB → 53.23 MiB） |
| `Entry<IOperation,NLCPGNode>[]` | 74.3 MiB | **未处理** |
| `System.String`（常驻度最高，46 样本） | 13.5 MiB | 小对象问题，**非 LOH 主因**（既有文档已修正其权重） |

**⇒ 两条最重要的判断：**

1. **HEAD 上这些 LOH 大对象一个都没少**——所有修复都在工作区，**未提交**。
2. 清单里**已被处理的是"边"侧**（`Entry<NLCPGEdge>[]` + `NLCPGEdge[]`），
   而**"计划"侧（`InterproceduralDataFlowPlan[]`）才是第一大**，其修复也**未提交**。

---

## 7. 与指导模式的对应

| LOH 大对象 | 指导模式 | 依据 |
| --- | --- | --- |
| `InterproceduralDataFlowPlan[]` | 模式 C（删重复载体）+ 模式 A | 同类型出现在 5 个叶子（`List`/`HashSet`/`ToArray`×2）⇒ 复制系数 >1 |
| `Entry<NLCPGEdge>[]` + `NLCPGEdge[]` | 模式 A + B + C | 已落地：210.06 → 13.01 B/边 |
| `NLCPGNode[]` | — | 改指纹，**已明确否决** |
| `Entry<IOperation,NLCPGNode>[]` | 模式 A（待评估） | 未做 |

**指导 §1.2 的临界元素数**对本案的直接应用：

| 元素类型 | 宽度 | LOH 临界元素数 | 本案实测元素数 |
| --- | ---: | ---: | ---: |
| `InterproceduralDataFlowPlan` | **428 B** | **199** | **1,458,176**（单个对象） |

⇒ 单对象元素数是阈值的 **7,328 倍**。**这类对象进 LOH 是结构性的，不是偶发。**

---

## 8. 边界

1. **没有新增测量。** 本文全部数字来自既有报告，**未跑任何新实验**（机器共享，遵循最小负载）。
2. **清单只覆盖暴涨前的 0–152 s。** 真正的 LOH 峰值（`counters`：新 18,600,825,400 B = **17.32 GiB**，
   旧 14,392,677,168 B = 13.40 GiB）**没有任何 gcdump/trace 现场**，其大对象构成**未知**。
3. **`InterproceduralDataFlowPlan[]` 是"最强假设"，非已证因果。** 241–287 s 那一刻的调用栈**未被拍到**；
   本文的 755.3 MiB / 60.93% 都是 **≤152 s / ≤60 s** 的观测。
4. **`gcdump-type-summary.csv` 的 `Max*` 列不可直接引用**（§1.1）；本文数据已排除饱和样本。
5. **工作区状态会变。** §6 的"已消除"是**本文写作时刻**的工作区事实，**未提交**；
   引前请重新 `git status` / `git log -S` 核对。
6. **未验证修复效果。** 工作区的流式重构**没有在完整语料上测过 LOH**；
   其效果（尤其 `set_Capacity` 的 2,283.96 MiB = 37.32% 是否归零）**尚无实测**。

---

## 9. 建议的后续动作

按性价比排序：

1. **重跑并把 trace 窗口与 gcdump 采样对准 200–320 s** —— 这是唯一能把"最强假设"变成因果的动作，
   也是**唯一能拿到暴涨时刻大对象构成**的办法（§1.2 / §8.2）。
2. **提交工作区已有的边侧与计划侧修复**，然后在**同样的受控小输入**上按指导 §5.1 做字节验收。
3. **修 `LOH-ROOTCAUSE` 的 48 B → 428 B**（§5），并重算所有 `÷48` 派生数。
4. **给 `gcdump-type-summary.csv` 的生成加饱和样本过滤**（§1.1），否则后续会反复被夸大 6.58 倍。
5. 评估 `Entry<IOperation,NLCPGNode>[]`（74.3 MiB）是否可用模式 A。

---

## 10. 参考

- [LOH 大对象优化指导](../loh-large-object-optimization-guide.md) —— 本文所用的诊断流程与模式
- [Version4 DOP12 诊断](../benchmarks/nlissn-version4-dop12-diagnostics.md) —— 同一运行的仓库内记录
- [冻结边投影化设计](../plans/2026-09-24-frozen-edge-projection-design.md) —— 边侧修复（`Entry<NLCPGEdge>[]`/`NLCPGEdge[]`）
- `D:\TRbackup\Version4\Build\NL-diagnostics-dop12\20260923-130737-dop12\Analysis\LOH-ROOTCAUSE-20260923-dop12.md` —— 既有根因文档（**§5 纠错对象**）
- `...\Analysis\LOH-MEMORY-WALL.md` —— 同上的配套分析
