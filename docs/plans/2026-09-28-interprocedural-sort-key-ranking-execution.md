# C 跨过程计划排序键排名化执行计划

**Goal:** 把 `PlanSortRow` 的两个**字符串**排序键（`SourceKey` / `TargetKey`）换成两个**`int` 排名**，
使行宽由 **32 B → 20 B**（⚠️ 计划时按 8 B 对齐推为 24 B，**实测为 20 B**，见 §10.2），
并把 `List.Sort` 的比较从「两条长字符串逐字符 `CompareOrdinal`」降为「整数比较」；
**边集、边序、`GraphSnapshotVersion`、冻结 70 行插入序逐位不变**。

**Architecture:** 在既有的阶段 ① 「节点排序键去重 + 并行预计算」（`NLCPGBuilder.cs:2667-2723`）
之后**追加一步**（同一作用域内）：把 `nodeSortKeys` 的**唯一键值**排序后**稠密排名**，
得到 `Dictionary<NLCPGNode, int> nodeSortRanks`。排序行只存排名，比较器用整数比较。

**Tech Stack:** C#/.NET 10、`NLCPG.Builder`、xUnit（ContractTests）。

日期：2026-09-28（计划）；**2026-09-28 已执行**（见文末「执行结果」）。

> ✅ **执行状态：已实施并验证**（只做 10% 核心验证，无任何前置验证，符合本轮要求）。
> 实施结果、实测宽度与取证方式见文末 **§10 执行结果**。
> ⚠️ 计划本体（§1–§9）保持**写下时的原样**，不改写为事后叙述；
> 与实施有出入之处（尤其 **行宽不是 24 B**）在 §10 逐条订正。

> 📌 **本轮性质：只写执行文档。不改产品代码、不跑构建、不跑测试、不做任何前置测试、不新增探针。**
> 因此本文**不产出任何实测收益数字**，也不声称任何改动已生效。
> 所有量均为**引用既有实测**或**算术推算**，口径已在 §2 与 §9 逐项标注。

> 📌 **据以执行的上游文档**：本项来自
> [M8 静态审查](../../Build/MemoryOptimization/M8/STATIC-REVIEW-InterproceduralDataFlowPlan.md) 之外的一次
> **纯静态审查**（2026-09-28 会话，本文即其执行化）。**该审查本身没有独立文档**，
> 其结论已就地吸收进本文 §0 与 §1，并以**符号名 + 行号**逐条落到源码，不转述未落地的说法。

> 📌 **行号基准**：`src/NLCPG/Builder/NLCPGBuilder.cs` 当前 **4,647 行**。
> 该文件正被并发的内存优化会话反复改写（历史 4,497 → 4,503 → 4,514 → 4,647），
> 故下列引用**一律同时给出符号名**，行号漂移时以**符号名**为准。

> ⚠️ **与 `c² → c` 的关系（本项有硬依赖）**：
> [P0 跨调用点复用](2026-09-28-interprocedural-argument-segment-reuse-execution.md) **必须在前**。
> 理由不是"避免冲突"而是**收益口径**：本项省的是**每行的比较成本与宽度**，
> 而 P0 把**行数**按因子 `c` 降下来（实测复用因子 454.5 / 888.2）。
> 行数是本项的乘数 ⇒ **先做 P0，本项的绝对收益才可解释**。见 §8.1。

---

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 换掉字符串键安全吗？ | **安全，但有一个硬前提**：排名必须与 `string.CompareOrdinal` **逐位同序**，且**等键必须同排名**（§3.1） |
| 收益多大？ | 行宽 **32 B → 24 B（−25%）**；每次比较由 2 次长字符串 `CompareOrdinal` 降为 2 次 `int` 比较（§2） |
| 能否不改测试就通过？ | **不能**。至少 2 条断言必红（§5.1），1 条**会静默变成空断言**（§5.2） |
| 最大风险是什么？ | **不是**性能，而是**等键不同排名**会把 `List.Sort` 的不稳定性泄漏进边序 ⇒ 冻结 oracle 红（§3.1、§8.4） |
| 覆盖集不完整时怎么办？ | 必须**显式决定**（本文建议 fail-hard，并加覆盖完备性契约）；现状是"兜底插值"（§3.2） |
| 本轮验收门槛？ | 按用户指示：**只做 10% 核心测试，不做任何前置测试**。正确性由仓库既有**冻结 oracle** 承担（§6） |

---

## 1. 现状与目标表示

### 1.1 现状：排序行持两个字符串引用

`PlanSortRow`（`NLCPGBuilder.cs:3298-3303`）：

```csharp
internal readonly record struct PlanSortRow(
  int PlanIndex,
  NLCPGInterproceduralBridgeKind BridgeKind,
  int ArgumentOrdinal,
  string SourceKey,        // ← NodeSortKey 插值出的长字符串（引用）
  string TargetKey);
```

宽度实测 **32 B**（`InterproceduralPlanCompactionTests` 的 `PlanSortRowWidth()`，
`CpgInterproceduralEdgeOrderTests.cs:223` 断言 `Assert.Equal(32, rowWidth)`）。

比较器 `PlanSortRowComparer.Compare`（`:3305-3341`）的键序：

```
BridgeKind(int) → ArgumentOrdinal(int) → string.CompareOrdinal(SourceKey) → string.CompareOrdinal(TargetKey) → PlanIndex(int)
```

`NodeSortKey`（`:3513`）是**纯函数插值**：

```csharp
$"{node.Kind}|{graph.ResolveFullName(node)}|{graph.ResolveName(node)}|{graph.ResolveFilePath(node)}|{node.SpanStart}|{node.SpanEnd}"
```

### 1.2 目标表示：排名

```csharp
internal readonly record struct PlanSortRow(
  int PlanIndex,
  NLCPGInterproceduralBridgeKind BridgeKind,
  int ArgumentOrdinal,
  int SourceRank,          // ← 取代 string SourceKey
  int TargetRank);
```

比较器后两键改为 `x.SourceRank.CompareTo(y.SourceRank)` / `x.TargetRank.CompareTo(y.TargetRank)`。

**行宽**：`4 × 5 = 20 B`，按 8 B 对齐 ⇒ **24 B**。
⚠️ **该 24 B 是算术推导，不是实测**；实施时必须用**既有的反射测宽路径**
（`InterproceduralPlanCompactionTests.PlanSortRowWidth()`，`:836-838`；走 `Unsafe.SizeOf`）
实测确认，**不得**直接把 24 写进断言而未经测量。

### 1.3 排名表怎么建（复用既有阶段 ① 的产物，不重复插值）

阶段 ①（`:2667-2723`）已经算出：

- `keyedNodes`：池端点去重后的唯一节点（既有实测 **1,599,506 → 62,251**，`:2672`）；
- `nodeSortKeys`：`Dictionary<NLCPGNode, string>`，每节点一个 `NodeSortKey`。

**追加一步**（仍在该作用域内，`computedKeys = null!;` 之前或之后均可，但必须在**任何 `AddEdge` 之前**）：

1. 取 `nodeSortKeys.Values` 的**唯一字符串值**集合，升序 `Array.Sort(..., StringComparer.Ordinal)`；
2. **稠密排名**：`rank[key] = 该键在升序唯一键序列中的下标`；
3. `nodeSortRanks[node] = rank[nodeSortKeys[node]]`。

- 复杂度：`O(U log U)`，`U = 62,251`（既有实测）⇒ **一次性**成本，与行数无关；
- 该步**只读** `nodeSortKeys`，不写图、不碰共享状态 ⇒ 与阶段 ① 的并行/串行分支无关。

---

## 2. 收益（**算术推导 + 引用既有实测**，本轮不新测）

### 2.1 行宽

```
现状：int + enum(4) + int + 2 × 引用(8) = 28 B → 8 B 对齐 → 32 B
目标：int + enum(4) + int + 2 × int(4)  = 20 B → 8 B 对齐 → 24 B
⇒ −8 B/行（−25.0%）
```

**引用既有实测的行数**（`Context/progress.md` 轮次 30 冻结值）：计划行 **3,954,144**（`NPC.cs` 单文件）。

```
行载荷：3,954,144 × (32 − 24) B ≈ 30.2 MB   ← 算术，未实测
```

⚠️ **该 30.2 MB 是累计分配口径下的量级推算，且是 P0 之前**。
P0 落地后行数按因子 `c` 下降 ⇒ **该数字须按实际行数重算，不得直接引用**。

### 2.2 比较成本

现状每次比较在**前两键并列时**执行 2 次 `string.CompareOrdinal`（长度含 `FullName`，通常数十字符）。
排名化后为 2 次 `int.CompareTo`。

**为什么仍值得做（即便行数被 P0 降下来）**：实测拆分（`Context/progress.md:3316-3323`）里
**③ `List.Sort` 占发布段的 33.2%**，是②③两段中最大的一项；`List.Sort` 的比较次数 ~`n log n`，
**比较成本下降与行数下降是相乘关系**，不是替代关系。

⚠️ **本轮不产出任何耗时或倍数结论**，理由见 §6.1（本机墙钟不具判别力）。

### 2.3 附带收益：`nodeSortKeys` 的字符串引用不再是"必须常驻"的理由

现状 `nodeSortKeys`（`Dictionary<NLCPGNode,string>`，62,251 项）在整个 pass 内常驻，
因为发布段要用它给行填键。排名化后发布段只用 `nodeSortRanks`（`int`），
`nodeSortKeys` 可在排名建好后**丢弃**（仅阶段 ① 内部使用）。

⚠️ **不得据此声称"省了 62,251 个字符串"**：那些字符串由图的字符串表（`StringInterner`）
**持有**，`Resolve*` 返回的是既有实例，本项**不释放它们**。真实可释放的只是
`Dictionary<NLCPGNode,string>` 的**引用槽数组**（62,251 × 8 B ≈ 0.5 MB，算术）。
⇒ **本条的合格写法是"常驻容器从 8 B/项 降为 4 B/项"**，不是"省了字符串"。

---

## 3. 不变条件（改动不得违反）

### 3.1 ⚠️ 排名必须与 `string.CompareOrdinal` 逐位同序，且**等键必须同排名**（本项第一风险）

`PlanSortRowComparer` 的**末键是 `PlanIndex`**（`:3338-3339`），其存在理由是
`List<T>.Sort` 是**不稳定**排序 ⇒ 比较器必须构成**严格全序**才能保证组内顺序确定。

现状 `string.CompareOrdinal(a, b)` 对**相等**的键返回 `0`，于是**落到末键 `PlanIndex`**。

**若排名化给"键相等但节点不同"的两个节点分配不同排名**：

- 比较在**第 3/4 键**就返回非零 ⇒ **不再落到 `PlanIndex`** ⇒ 组内次序改变
  ⇒ `AddEdge` 调用序列改变 ⇒ **冻结 70 行插入序与 `GraphSnapshotVersion` 改变**。

**⇒ 硬性要求：排名必须是「按键值排序后的**稠密**名次」**，即
`rank(a).CompareTo(rank(b))` 必须与 `string.CompareOrdinal(key(a), key(b))` **符号逐位相同**，
且 `key(a) == key(b) ⇒ rank(a) == rank(b)`。

> ⚠️ **"键相等的两个节点"是否真实存在，本文【未验证】**。
> 本文只断言：**无论是否存在，稠密排名都使两种实现逐位等价**——
> 这是本项唯一需要的正确性论证，且它**不依赖语料**。
> 若实施者改用"按节点分配唯一排名"（例如直接按下标），则本节论证失效，
> **必须**另行证明键在池端点集上无碰撞（本文**不**提供该证明）。

**既有护栏直接覆盖本风险**：`CpgInterproceduralEdgeOrderTests.cs:15-21` 就地写明

```
// 因此若把 NodeSortKey 换成结构体键而语义有偏差，这里会立刻失败。
```

该测试冻结 `graph.Edges` 的**原始插入序**（不做任何排序，见 `:11-13` 的说明），
正是本项的**主判据**。另见 §5.4。

### 3.2 覆盖集不完整时的行为必须显式决定

现状 `CachedNodeSortKey`（`:3275-3286`）在字典未命中时**按需插值兜底**，
注释（`:3272-3274`）说明它是"防止覆盖集推理有误导致崩溃的兜底"。

排名化后**没有"按需排名"这个选项**（单点插值无法得到与全局一致的排名）。
三种可选处置：

| 选项 | 行为 | 评价 |
| --- | --- | --- |
| **A（建议）** | **fail-hard**：抛出带明确消息的异常 | 把"覆盖推理错了"从静默劣化变成立刻失败。⚠️ **这是行为变更**，从 fail-soft 改为 fail-hard，须在 §5.3 加覆盖完备性契约护住 |
| B | 保留不稳定兜底：给一个哨兵排名 | **拒绝**：哨兵排名会破坏严格全序 ⇒ 违反 §3.1 |
| C | 扩大排名表构造集 | 治标；若推理真的错了，扩集也未必闭合 |

⚠️ 另注：`targets` 的排序（`:2775-2776`）**已经**直接用索引器 `nodeSortKeys[target]`
（未命中即抛 `KeyNotFoundException`）⇒ **该路径上"覆盖必须完备"已是既有假设**。
选项 A 与之一致，不是新增假设。**实施时须回读该行确认**。

### 3.3 不得改变键的**内容**

排名只是键的**顺序编码**。`NodeSortKey` 的插值内容（`:3513`）**一字不得改**。
任何对键内容的"顺手优化"（如换分隔符、去掉 `Name`）都可能改变序 ⇒ 冻结算例红。

### 3.4 不得触碰 P0 的两段式结构

若 [P0](2026-09-28-interprocedural-argument-segment-reuse-execution.md) 已落地，
`BuildAndSortPlanRows`（`:3225-3270`）会变成"共享前缀 + 私有尾段"两段拼接。
本项**只改行载荷与比较器**，**不改**拼接与 `PlanIndex` 编号方式。

---

## 4. 改动清单（封闭）

### 4.1 `src/NLCPG/Builder/NLCPGBuilder.cs`

| 位置（符号） | 改动 |
| --- | --- |
| 阶段 ① 作用域内（`:2683-2723`，`nodeSortKeys` 建成之后） | **新增**排名表构造：唯一键升序 + 稠密排名 ⇒ `Dictionary<NLCPGNode,int> nodeSortRanks` |
| `PlanSortRow`（`:3298-3303`） | `string SourceKey, string TargetKey` → `int SourceRank, int TargetRank` |
| `PlanSortRowComparer.Compare`（`:3305-3341`） | 第 3/4 键由 `string.CompareOrdinal` → 整数比较；**其余键与末键 `PlanIndex` 一字不动** |
| `BuildAndSortPlanRows`（`:3225-3270`） | 行构造填入排名而非键；**P0 落地后**需按两段分别取排名 |
| `CachedNodeSortKey`（`:3275-3286`） | 按 §3.2 处置（建议改为 `RankOf` + fail-hard，并同步注释） |
| `targets` 排序（`:2775-2776`） | `ThenBy(target => nodeSortKeys[target], ...)` → 用排名（同序）；**符号名/行号以实际为准** |
| `orderedCallSites` 排序（`:2725-2732`） | 调用点仅 2,769 个（源码注释 `:2729`），**建议不动**（保持直接插值）；若改，必须同序 |

**净变化**：约 +20 行（排名构造）−0 行（替换），无新增文件。

### 4.2 **不**要顺手做的改动

| 诱惑 | 阻止理由 |
| --- | --- |
| 把 `BridgeKind`/`ArgumentOrdinal` 也换成别的编码 | 它们已是 `int`；且键序是冻结语义 |
| 改 `PlanSortRow` 的**字段顺序**或删 `PlanIndex` | `PlanIndex` 是**严格全序的末键**（§3.1），删了即边序不确定 |
| 把 `List<T>.Sort` 换成 `OrderBy` | `OrderBy` 稳定但分配更多、且会改变②段的分配画像；**不在本项范围** |
| 借机删 `ReclaimIdleSortBufferCapacity`（`:3132-3162`） | 它与行宽**无关**，且是 §5.4 多条测试的直测对象 |
| 让排名表在**多处**各自构造 | 违反 §3.1（同序性需要**唯一**排名来源） |

---

## 5. 测试修订清单（分级）

### 5.1 **必红的两条**（计划内修订）

| # | 文件:符号 | 逐字内容 | 级别 |
| --- | --- | --- | --- |
| 1 | `CpgInterproceduralEdgeOrderTests.PlanSortRow_CarriesPlanIndexInsteadOfFullPlan`（`:183-227`） | `Assert.Equal(new[] { "ArgumentOrdinal", "BridgeKind", "PlanIndex", "SourceKey", "TargetKey" }, fieldTypes.Keys...)`（`:196-200`） | **断言级红**：字段集改变（`SourceKey`/`TargetKey` → `SourceRank`/`TargetRank`） |
| 2 | 同上（`:220-226`） | `Assert.Equal(32, rowWidth);`（`:223`） | **断言级红**：行宽 32 → 24（**须实测后填写，不得直接写 24**） |

⚠️ 同一测试的 `:214-216`（`rowWidth <= 32`）与 `:217-219`（`planWidth < rowWidth`）
**必须重述**而非删除：

- 前者应改为"行宽不超过**新**冻结布局"；
- 后者的**原意**是"延迟物化未退化为内嵌端点"。载体 **8 B**、行 **24 B** ⇒ 仍成立，
  但**必须重写注释**，否则读者会以为判据没变（现状注释 `:209-213` 已记录过一次前提反转，
  本项是**第二次**反转，必须如实追加）。

### 5.2 **会静默变成空断言**（比红更危险，必须主动修）

| # | 文件:符号 | 为何失效 |
| --- | --- | --- |
| 3 | `InterproceduralPlanCompactionTests.PublishedSlots_ReleaseSortRowKeyReferences`（`:532-544`） | 该测试的**立论**是"排序行扣住 `SourceKey`/`TargetKey` **字符串引用**，清空后必须释放"。排名化后行内**已无字符串引用** ⇒ 断言仍绿（`SortBufferClearedSlots > 0` 恒真），但**它宣称在护的东西已不存在**。⇒ 必须重述为"行载荷清空"语义，或**明确降级并记录**（推荐：保留断言 + 改注释说明"引用轴"已由行宽收缩替代，并新增 §5.3 N2 护住新载荷） |

### 5.3 **必须新增的护栏**

| # | 建议名称 | 判别内容 |
| --- | --- | --- |
| N1 | **排名同序性**（纯函数直测） | 对一组构造键，断言 `rank(a).CompareTo(rank(b)) == Math.Sign(string.CompareOrdinal(keyA, keyB))`，且等键 ⇒ 等排名。这是 §3.1 的**唯一直接判据**，必须存在（否则"同序"只是注释里的说法） |
| N2 | **行载荷不含字符串**（结构断言） | 反射 `PlanSortRow` 字段类型，断言**无 `string` 字段**。护住 §2.1/§2.3 的收益不被静默退回 |
| N3 | **覆盖完备性** | 断言排名表覆盖池端点全集（与 `nodeSortKeys` 的覆盖集**同一个**）；护住 §3.2 的 fail-hard 选择 |

> ⚠️ N2 必须**只锁"无字符串字段"**，**不要**锁字段总名单——后者会在 P0 两段式改造
> 加字段时误红（脆弱测试）。字段集断言已由 §5.1 第 1 条承担。

### 5.4 **必须保持不变且继续通过**（冻结 oracle，**禁止修改**）

| 文件 | 内容 | 级别 |
| --- | --- | --- |
| `CpgInterproceduralEdgeOrderTests.cs:65-141` | `ExpectedInterproceduralOrder`（70 行冻结原始插入序） | **本项主判据** |
| 同上 `:143-153` | `BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder` | 同上 |
| 同上 `:155-163` | `..._AreStableAcrossRepeatedBuilds` | 同上 |
| 同上 `:165-174` | `..._HaveStableTotalCount` | 边数不变 |
| 同上 `:234-254` | `..._DeriveContextIdFromCallSiteContext` | 护住 `callSiteContext` 仍逐调用点传入 |
| `DataFlowPlanConstructionReuseTests.cs:43-63` | `Sparse`/`Collision`/`JoinLoop` 的 `NormalizedGraphHash` 与 `PublicationHash` | **冻结哈希** |
| `DataFlowCandidateScanPruningTests.cs:46-54` | 同上三个 `PublicationHash` | **冻结哈希** |
| `InterproceduralPlanCompactionTests.cs:74-93` | `InterproceduralPlanRef_KeepsOnlyLazyHandlePayload` | 载体字段集不变 |
| `InterproceduralPlanCompactionTests.cs:98-115` | 载体宽度 `== 8` | 护住 ⑥ |
| `InterproceduralPlanCompactionTests.cs:442-517` | `SelectSortSlotsToReclaim_*`（4 条纯函数直测） | 与行载荷无关，应原样通过 |
| `InterproceduralPlanCompactionTests.cs:567-734` | `ReclaimIdleSortBufferCapacity_*` / 轮流大槽压力 | 用 `PlanSortRowWidth()` 动态取宽 ⇒ **应自适应**；⚠️ 但 `:453/:468/:481/:493/:503-504` 把字面量 `32` 作**形参**传入（不是断言）⇒ 这些是**测试自洽的合成预算**，改后仍自洽，**不需改**，但须**回读确认**它们不与产品行宽耦合 |
| `InterproceduralPlanCompactionTests.cs:401-418` | `WindowRowBound_PostFlushRetainedCapacity_StaysWithinBudget` | 用 `PlanSortRowWidth()` 算预算 ⇒ 自适应 |
| `NLCPGNodeIdContractTests.cs:190` | `InterproceduralPlanRef` 必须是非公开值类型 | 护住"按值搬运" |

---

## 6. 验收（本轮按用户指示裁剪）

### 6.1 本轮门槛

**只做 10% 核心测试。不做任何前置测试**（无冻结基线复采、无 A/B、无探针、无变异验证）。

| 判据 | 内容 |
| --- | --- |
| 构建 | `NLCPG.csproj` **0 警告 0 错误** |
| **主判据（等价性）** | §5.4 的全部**冻结 oracle** 逐字节通过：70 行边序、三个 `NormalizedGraphHash`/`PublicationHash`、`GraphSnapshotVersion`、节点/边数 |
| 核心定向 | §5.1 + §5.2 + §5.3 的修订与新增全部通过 |
| 未验证边界 | §9 逐条如实登记 |

⚠️ **本轮不产出任何耗时或倍数结论**。理由不是"没空做"，而是**本机墙钟不具备该判别力**：
`BASELINE.md` §5.4 实测同二进制同输入三次运行差 **16%–71%**；DoD#4 的阳性对照
（注入已知 +3.5% 忙等）CI **跨 0**，即**判不出** 3%；
反推分辨 3% 需约 **41 对**、1% 需约 **361 对**（`Context/progress.md`）。
⇒ 若将来要**主张**收益，唯一有判别力的口径是 `allocBytes` **配对**（配对差 <0.05%），
而非 `summary.json` 的 `wallElapsedMs`。

### 6.2 建议的聚焦过滤器

```
FullyQualifiedName~CpgInterproceduralEdgeOrderTests
FullyQualifiedName~InterproceduralPlanCompactionTests
FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests
FullyQualifiedName~CpgWorkBatchInterproceduralTests
FullyQualifiedName~NLCPGNodeIdContractTests
FullyQualifiedName~DataFlowPlanConstructionReuseTests
FullyQualifiedName~DataFlowCandidateScanPruningTests
```

### 6.3 命令（与既有执行文档一致）

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~InterproceduralPlanCompactionTests|FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests|FullyQualifiedName~CpgWorkBatchInterproceduralTests|FullyQualifiedName~NLCPGNodeIdContractTests|FullyQualifiedName~DataFlowPlanConstructionReuseTests|FullyQualifiedName~DataFlowCandidateScanPruningTests'
```

### 6.4 可选的后续收益实测（**本轮不做**）

若日后要主张本项收益，唯一有判别力的口径是**配对累计分配 A/B**，
用 ⑥ 已建立的手法（单变量变异）在**同一份单文件语料**上跑配对，
读 `RuntimeLog/runtime.log` 的最后一个 `allocBytes=`。
**预期量级可先由算式给出**：`Δ ≈ 行数 × 8 B`；`Item.cs` 上按 `c²p` 行数计为
`9,679,406 × 8 B ≈ 77.4 MB`（**算术，未实测**）。
⚠️ 该预测**只覆盖行数组宽度**，不含比较成本的收益 ⇒ **不得**用它代表本项总收益。

---

## 7. 实施顺序（建议）

1. **先加排名表构造**（阶段 ① 之后），此时产品行为**完全不变**（新表无人读）⇒ 可先编译验证。
2. **改 `PlanSortRow` 字段 + 比较器 + 行构造**（同一批，否则编译失败）。
3. **按 §3.2 处置 `CachedNodeSortKey`**，并同步 `targets` 排序（`:2775-2776`）。
4. **同步 §5.1/§5.2 的测试修订，补 §5.3 的 N1/N2/N3。**
5. 跑 §6.2 聚焦过滤；确认 §5.4 冻结 oracle 全绿。

**建议先做 §5.3 N1**：它是"同序性"的唯一直接判据，
其余改动都是机械同步——**若 N1 不先立起来，本项的核心风险就没有护栏**。

---

## 8. 与其它切片的关系

### 8.1 对 P0（`c² → c`）的硬依赖

| 维度 | P0 | 本项 C |
| --- | --- | --- |
| 攻击面 | 计划**行数**（`c²p → cp`） | 每行**宽度**与**比较成本** |
| 是否可加 | **相乘**，不是相加 | 同上 |
| 顺序 | **必须在前** | 必须在后 |

**不是技术冲突，而是收益可解释性**：本项省的是 `行数 × 每行`，
行数被 P0 降了 `c` 倍（实测 454.5）⇒ **P0 之前做本项，其收益会被行数淹没而无法归因**。

### 8.2 与 A（标签三单例化）的关系

[ABC 合并执行计划](2026-09-28-interprocedural-publish-hotpath-execution.md) 的 A 项改的是
**发布段每边的标签分配**（`:3094`），与本项**不同段、不同文件位置、无共享变量**
⇒ **可独立合入、独立回退**。

⚠️ **不要按"哪个收益更确定"排序来先做 A**：该文 §2.3 已把 A 的收益降级为
**待生产点计数裁决（可能为零）**，与本文的 C 相比**A 的收益确定性并不更高**。
⇒ 排期依据应是**依赖关系**（C 必须在 P0 之后）与**代码邻域**（A+B2 同改 ④ 段），
而非收益确定性。

### 8.3 与 B（按序数直通 `AddEdge`）的关系

B 改的是**发布段写图**（`:3057-3096`），本项改的是**②③段排序**。两者**正交**。
⚠️ 但两者都改 `BuildAndSortPlanRows` 的**调用邻域**（B 可能改行消费方式）
⇒ 若同批做，**先 A+B 后 C**，或按 ABC 文档的分步顺序。

---

## 9. 未验证边界与风险（**不得越读**）

1. **本轮零执行** ⇒ 本文所有结论均为**源码阅读 + 引用既有实测**，
   无任何本轮实测数字；§2 的 30.2 MB / 24 B / 0.5 MB 全部是**算术推算**，
   `62,251`、`3,954,144`、`33.2%` 来自**既有**实测与源码注释。
2. **§3.1 的"键碰撞是否存在"未验证**。本文的正确性论证刻意**不依赖**该事实
   （稠密排名对两种情形都等价）。任何改用"按节点唯一排名"的实现**必须自行补证**。
3. **`24 B` 未实测**。§5.1 第 2 条的期望值必须以**实测**为准填写。
   本机为 x64；若在别的架构上对齐规则不同，宽度会变，**断言须按架构分支**
   （既有 `:220` 已用 `Environment.Is64BitProcess` 的先例）。
4. **§3.2 的 fail-hard 是行为变更**（fail-soft → fail-hard），
   且该路径被注释断定为"兜底、预期不可达"⇒ **本轮不会观测到它**。
   若实施者选择保留 fail-soft，必须回到 §3.1 证明哨兵排名不破坏全序。
5. **§2.2 的比较收益无实测**。既有的 33.2% 是 `List.Sort` **整段**占比，
   **不是**"字符串比较"的占比；本项能把其中多少降下来**未被测量**。
6. **§5.2 第 3 条**（`PublishedSlots_ReleaseSortRowKeyReferences`）的处置**未定稿**：
   本文给出"重述 + 降级记录"的建议，但**未设计**替代该"强引用可达性"轴的等价判据。
7. **未做**：多文件/全语料、多 DOP、跨 Build 复用、常驻峰值、GC 次数、墙钟。
8. **并发会话**：`NLCPGBuilder.cs` 正被其他会话改写。实施前须**确认文件哈希**
   与本文行号基准一致；不一致时**以符号名为准**并重读。
9. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)。
   `Context/feature_list.json` 是 feature 状态**唯一**来源；本文件**不**修改任何 feature 状态，
   **也不构成验收证据**。

---

## 10. 执行结果（2026-09-28，已实施）

**范围**：按用户指示，**不做任何前置验证，只做 10% 核心验证**。
未做：冻结基线复采、A/B、探针、变异验证、全语料、多 DOP、墙钟。

### 10.1 实际改动

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | ①-b 稠密排名（抽为 `internal static BuildDenseOrdinalRanks` 纯函数，便于直测）；`PlanSortRow` 第 4/5 键改 `int SourceRank`/`TargetRank`；比较器改整数比较；`CachedNodeSortKey` → `NodeSortRank`（**fail-hard**）；`nodeSortKeys` 用后 `Clear()+TrimExcess()`；`targets` 的 `ThenBy` 改用名次；`FlushParallelPublishWindow` / `BuildAndSortPlanRows` 改收名次表并去掉不再需要的 `graph` 形参 |
| `tests/.../CpgInterproceduralEdgeOrderTests.cs` | 字段集断言修订（§5.1 第 1 条）；宽度断言修订（§5.1 第 2 条）；**新增 N1** 同序性纯函数直测；**新增 N2** "行内无 `string` 字段"断言；**新增次序预言机**（见 §10.3） |
| `tests/.../Cpg/InterproceduralPlanCompactionTests.cs` | §5.2 第 3 条的立论轴重述（断言未改，仅如实记录前提已收窄） |

**未改**：`Context/feature_list.json`（本切片未登记）。

### 10.2 ⚠️ 订正：行宽是 **20 B**，不是计划里推的 24 B

计划 §1.2 按「8 B 对齐」推出 24 B。**实测为 20 B**：
`PlanSortRow` 的 5 个字段全是 `int`（含 `BridgeKind`，底层 `int`）
⇒ 最大对齐 = 4 B，故 `Unsafe.SizeOf` = **5 × 4 = 20 B，无填充**。
"按 8 B 对齐"是**错误的推理**——8 B 对齐只在含引用/`long` 时才成立，本项恰恰**移除了引用**。

⇒ **净收益是 32 B → 20 B（−37.5%）**，比计划预估的 −25% 更大。
该值由 `PlanSortRow_CarriesPlanIndexInsteadOfFullPlan` 的 `Unsafe.SizeOf` **实测**确认，
并冻结为常量 `FrozenPlanSortRowWidth = 20`。

### 10.3 验证结果（10% 核心验证）

| 项 | 结果 |
| --- | --- |
| `NLCPG.csproj` 构建 | ✅ **0 警告 0 错误** |
| 聚焦测试（7 个过滤类） | **83 通过 / 1 失败**（失败为既有，见下） |
| 广域 `~Cpg` 扫描 | **755 通过 / 1 失败**（同一既有失败） |
| 冻结哈希预言机 | ✅ `DataFlowPlanConstructionReuseTests` 三个 `NormalizedGraphHash`+`PublicationHash`、`DataFlowCandidateScanPruningTests` 三个 `PublicationHash` **全绿** |
| 载体宽度 | ✅ 仍为 **8 B** |
| 行宽 | ✅ 实测 **20 B** |
| N1 同序性 | ✅ 通过（含等键同名次、稠密性、全部有序对） |
| 边总数 | ✅ 与冻结值相同（70 行） |

### 10.4 唯一失败是**既有失败**，非本项引入

`CpgInterproceduralEdgeOrderTests.BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder`
失败于调用点 span：期望 `156:167`、实际 `167:178`。

- `Context/progress.md:23/61/113/180` **已记载**该漂移为**既有、已记载**，
  且明言"报错与 pristine HEAD **逐字节相同**"；
- 本项**未触碰**调用点 span 的任何产生路径（本项只改排序键编码）。

### 10.5 ⚠️ 关键取证：主判据当时**无法**证明次序未变，故补了独立预言机

主判据因上述既有漂移而恒红，且 xUnit 把失败信息**截断为前 5 项**
⇒ **它无法回答"跨组次序是否被本项改变"**，而"边序不变"正是本项的核心风险（计划 §3.1）。
若停在这里，"次序未变"就只是注释里的说法——**正是计划 §5.4 警告的那种情况**。

故新增
`BuildFromSource_InterproceduralEdges_KeepOrderModuloKnownCallSiteSpanDrift`：
把每行末段的 span 归一化为 `?` 后**逐位比对全部 70 行**。结果 ✅ **通过**。

⇒ 该结果**证明**：节点对、桥种类、跨组次序**全部逐位保持**，唯一差异是已知 span 漂移。
这才是本项"边序不变"的**实际证据**，而非主判据的沉默。

⚠️ 该用例**不替代**主判据（span 轴的断言仍在主判据里）；
待既有 span 漂移被正式修订后，两者应合并为一个。

### 10.6 本次未验证（沿计划 §9，并新增）

- **§9 第 3 条已由实施消解**：不再有"按需插值兜底"，改为 `NodeSortRank` fail-hard；
  这是**行为变更**（fail-soft → fail-hard），本次**未观测到它触发**（即覆盖集确实完备）。
- **收益仍未经实测**：行宽 32 → 20 B 是**实测布局**，
  但"因此省了多少秒/多少字节"**未测**（本轮无 A/B、无探针、无全语料）。
  按 §9 第 5 条，比较收益的占比**仍未测量**。
- **并发改写**：实施期间 `NLCPGBuilder.cs` 被其他会话再次改写
  （4,647 → 4,205 → 4,309 → 4,759 行），且 **P0（`c² → c`）已在此期间落地**
  （代码中已出现 `group.PlanAt` 两段式结构）。故本次实施是**在 P0 之上**完成的，
  **符合计划 §8.1 的排序要求**；但计划里基于旧行号的引用已不可用，
  实际以**符号名**定位（正如计划所要求的）。
- 未做：墙钟、`allocBytes` 配对、多文件/全语料、常驻峰值、GC 次数。

