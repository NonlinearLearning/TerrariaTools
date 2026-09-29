# 持久图冻结主导项消除执行计划（C1+C2+C3 合一）

**Goal:** 把 `FreezeQueryIndex` 里 `NLCPGGraphIndex.Create` 与 `DeterministicNodeIdTable.Create` 的三项已定位优化，作为**一个整体改动**落地，使持久图冻结的节点 canonical 化与边元数据秩构建不再走比较排序与全程哈希探测，同时保持 `GraphSnapshotVersion`、节点/边载荷、canonical 边序**逐字段不变**。

**Architecture:** `Create` 当前对节点做比较排序（`Array.Sort` + `IComparer<NLCPGNode>`），并用 `ranks` 与 `valueClassOfEdge` 两份 `int[edgeCount]` 表达"元数据排序键"。本轮把这两处换成：**稠密 NodeId 就地环置换**（canonical 位置 = `NodeId − 1`）与**值类重编号**（池下标 ≡ rank，两份数组合一），并对边元数据构建只处理真正带元数据的边。三项落在同一调用路径、互相耦合，故合并为一次落地、一次验收。

**Tech Stack:** C#/.NET 10、`NLCPGGraphIndex`、`DeterministicNodeIdTable`、`CanonicalEdgeStore`、xUnit（ContractTests）。

日期：2026-09-27（计划）。**2026-09-28 已按本计划实施 C1+C2+C3 并完成 10% 核心测试验收**，
实施结果与偏差见上方"执行状态"、§3bis 与 §5.4。

> **执行状态（2026-09-28 更新）**：C1+C2+C3 **已作为一个整体改动实现**，代码见
> `src/NLCPG/Model/NLCPGGraphIndex.cs` 与 `src/NLCPG/Model/CanonicalEdgeStore.cs`
> （C1a 的 `DeterministicNodeIdTable.cs` 已在工作区中先行就位）。
> 按用户明确要求，验收**只做 10% 核心测试、不执行任何前置测试、不新增探针**，
> 结果 **388 通过 / 0 失败**（分组明细见 §5.4），`NLCPG` Release 构建 0 警告 0 错误。
> 因本轮无计时类前置测试，**本文不产生任何倍数或毫秒级结论**；§0.3 的探针数字仍只作方向说明。
> 未验证边界见 §8。登记见 `Context/feature_list.json` 的
> `persistent-freezequeryindex-dominant-term`（状态 `in_progress`）。

---

## 0. 结论先行

### 0.1 本项直击 §0 所说的真正 55.1% 那条线

55.1% 来自阶段表 `CPG.FreezeQueryIndex` 行，其唯一产生位置是
[`NLCPGBuilder.cs:1467-1476`](../../src/NLCPG/Builder/NLCPGBuilder.cs) 的
`MeasureStage("FreezeQueryIndex", () => context.Graph.FreezeQueryIndex())`，
即**每文件的持久图**冻结。本项改的正是该调用的实现内部：

```
NLCPGGraph.FreezeQueryIndex()                      // NLCPGGraph.cs:595-609
├── AssignDeterministicNodeIds()                   // :923-988
│   └── DeterministicNodeIdTable.Create(...)       // ← C1 作用点之一
└── NLCPGGraphIndex.Create(frozen.Nodes, ...)      // :432-620
    ├── Array.Sort(节点, inputOrdinals)            // ← C1 主作用点
    ├── nodeOrdinals 字典 + 两端 ordinal 解析      // ← C1 顺带消除
    └── BuildMetadataRanks(...)                    // :712-828 ← C2 作用点
```

### 0.2 三项不是三笔独立收益，而是一个重构

| 项 | 内容 | 与其它项的关系 |
| --- | --- | --- |
| **C1** | `DeterministicNodeIdTable.Create` 去 `Distinct()`+结构比较器；`Create` 用稠密 NodeId 就地环置换替代 `Array.Sort` | 独立 |
| **C2** | `BuildMetadataRanks` 只对带元数据的边建身份表；值类重编号使 rank ≡ 池下标，`ranks` 与 `valueClassOfEdge` 合为一个数组 | 内含原 P3+P4b+P4c；三者改同一段代码，**必须一次设计** |
| **C3** | `kindKeys` 由 `int[]` 收窄为 `byte[]` | 依赖 C2 的同一段局部变量，放在同一次改动里 |

C1 落地后 `NodeId` 稠密性已知，**原"`Dictionary<NodeId,int>` → `int[]`"方案（P2）自动消解**：canonical 位置就是 `NodeId − 1`，连那张 6 MiB 表都不需要。故 P2 不再单列。

### 0.3 已做过的一次性探针测量（**仅用于证明方向，不是本计划的验收证据**）

以下数字来自本轮之前已完成的一次性探针，**按用户要求，本计划不再新增探针、不做 A/B**，故它们只用于说明"为什么选这三项"，验收一律改由仓库既有测试承担（见 §5）：

| 观测 | 值 | 语料 |
| --- | --- | --- |
| `Create` 占冻结阶段 | 50.0%（2,460.66 / 4,920 ms 中位数） | WorldGen.cs |
| 段 1 节点 `Array.Sort` | 222.1 ms（占 Create 14.1%） | 同上 |
| `BuildMetadataRanks` 身份探测通道下界 | 301.8 ms | 同上 |
| 无元数据边占比 | 71.0%（3,724,351 / 5,244,473） | 同上 |
| 引用身份数 / 值类数 | 9,090 / 9,090 | 同上 |
| `NodeId` 稠密性 | `NODEID_MAX = 1,591,165 = N`，`DENSITY_RATIO = 1.0000` | 同上 |
| 稠密环置换 vs `Array.Sort`（节点段内） | 2.32–2.63×，`INPLACE_MISMATCHES=0` | 同上 |
| 值类重编号合并（算法等价性） | `PROJECTION_MISMATCHES=0`、`RANK_MISMATCHES=0` | 同上 |

> ⚠ **时间数字不可作为承诺**：同一次 `Array.Sort` 在不同轮次测得 222 / 624 / 679 / 689 ms。
> 波动源自每轮迭代约 158 MiB 的 LOH 分配所引发的 GC 状态差异。故本计划**不承诺任何倍数或毫秒数**，
> §5 的验收只认机制量与不变量。

---

## 1. 现状

### 1.1 `Create` 的节点 canonical 化（`NLCPGGraphIndex.cs:432-466`）

```csharp
var orderedNodes = nodes as NLCPGNode[] ?? nodes.ToArray();   // :438 接管数组
var inputOrdinals = new int[orderedNodes.Length];             // :446
for (...) inputOrdinals[index] = index;                       // :447-450
Array.Sort(orderedNodes, inputOrdinals, NodeIdOrderComparer.Instance);  // :452
var nodeInsertionOrder = new int[orderedNodes.Length];        // :456
for (canonical ...) nodeInsertionOrder[inputOrdinals[canonical]] = canonical;  // :457-460
var nodeOrdinals = new Dictionary<NodeId, int>(orderedNodes.Length);  // :462
for (ordinal ...) nodeOrdinals.Add(orderedNodes[ordinal].NodeId!.Value, ordinal);  // :463-466
```

`NodeId` 由 [`DeterministicNodeIdTable.Create`](../../src/NLCPG/Model/DeterministicNodeIdTable.cs) 按 `index + 1` 连续分配，故生产图上 `NodeId ∈ [1, N]` 且无重复 ⇒ `canonical = NodeId − 1`，比较排序与其后的字典都属可省的间接层。

### 1.2 `BuildMetadataRanks` 的两次遍历（`NLCPGGraphIndex.cs:712-828`）

- `:726-736` 先扫一遍判断"是否有任何边带元数据"，无则走快速路径（`metadataWidth = 1`）。
- `:746-767` 对**每条边**做一次 `Dictionary<EdgeMetadataKey,int>`（引用身份）探测，产出 `identityOfEdge`。
- `:769-784` 只在**候选集**上按值精去重，产出 `valueOfIdentity`。
- `:792-814` 对候选集排序得 `rankOfValue`。
- `:816-823` 产出 `ranks[edge]` 与 `valueClassOfEdge[edge]` —— **两份 `int[edgeCount]`**，而 `ranks` 只是 `valueClassOfEdge` 经 `rankOfValue` 的置换。

### 1.3 值类编号对外不可见（已由源码记载，是 C2 的合法性前提）

[`NLCPGGraphIndex.cs:711`](../../src/NLCPG/Model/NLCPGGraphIndex.cs)：

> 池的下标编号方式改变不影响结果——池只被 canonicalMetadataIds 间接索引，对外不可见。

[`CanonicalEdgeStore.cs:80`](../../src/NLCPG/Model/CanonicalEdgeStore.cs) 的消费方式也只有 `_metadataPool[metadataId - 1]`，无其它读者。

---

## 2. 改动设计（合一）

### C1：稠密 NodeId 快速路径 + 去掉恒等 `Distinct()`

**C1a —— `DeterministicNodeIdTable.Create`**

1. 删除 `Distinct()`。**恒等性证明**：`NLCPGGraph.AddNode` 仅在
   `!_mutableNodesByAnchor.TryGetValue(anchor, ...)` 成功时才 `_nodesByOrdinal.Add(...)`
   （[`NLCPGGraph.cs:282-286`](../../src/NLCPG/Model/NLCPGGraph.cs)），故每个锚点至多入列一次；
   `AssignDeterministicNodeIds` 的输入正是 `_nodesByOrdinal.Select(...)`（`:925-931`），
   故其锚点序列天然无重复。另两条调用路径用 `HashSet` 承载锚点
   （`CpgStableAnchorCollector._anchors`、`CpgFragmentReducer.materializedDescriptors.Keys`），同样无重复。
2. 排序改为 `Array.Sort(array, StableAnchorComparer.Instance)` + **相邻去重**。
   **等价性证明**：`StableNodeAnchor` 是 7 字段 `record struct`
   （[`StableNodeAnchor.cs:6`](../../src/NLCPG/Model/StableNodeAnchor.cs)），其 `Equals` 与 `CompareTo`
   基于同一组 7 字段 ⇒ `a.Equals(b)` ⟺ 7 键比较为 0 ⇒ 排序后相等者必然相邻，"跳过与前一个相等者"
   与 `Distinct` 集合语义相同；互不相等者全序唯一 ⇒ `NodeId = index + 1` 映射唯一。
   `Array.Sort` 不稳定，但比较为 0 的两个锚点彼此相等、不可区分，谁在前都不改变唯一锚点序列。
3. **必须复制入参**（`anchors.ToArray()`），不得 `as StableNodeAnchor[]` 接管：本方法是 `public` API，
   原实现走非破坏性 `OrderBy`；就地排序公开入参会给调用方留下隐藏副作用
   （`tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs` 直接传数组字面量）。
4. 比较器的 7 键顺序必须与原先 `OrderBy(...).ThenBy(...)×6` **逐字一致**；`Kind`/`Role` 显式转
   `int` 比较，与 `OrderBy` 对枚举的底层值比较保持一致。

**C1b —— `NLCPGGraphIndex.Create` 节点 canonical 化**

改为**带稠密性探测的就地环置换**，保留 `Array.Sort` 作为回退：

```csharp
// 伪代码：完整实现须内联在 Create 中，且保留原注释所述的"就地排序同一实例"契约。
// 1) 扫一遍：所有 NodeId 非 0、最大值 == 长度 ⇒ 候选稠密。
// 2) 就地环置换：inputOrdinals[i] 始终记录"位置 i 上的元素来自哪个输入下标"，
//    与节点同步交换；循环结束时它恰好是 canonical → 输入下标。
// 3) 交换预算：swaps > orderedNodes.Length 即判定"非稠密/有重复"并回退 Array.Sort。
```

**为什么必须就地**（两条既有断言同时约束，缺一不可）：

- `FrozenNodeStorageEquivalenceTests.Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace`（`:236-254`）
  断言 `Assert.Same(source, canonical)`，且断言入参数组本身按 `NodeId` 升序；
- `FrozenNodeStorageEquivalenceTests.Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath`
  （`:285-314`）断言一次 `Create` 的分配 **< 一份完整节点载荷**（`nodeCount × 104 B`）。

⇒ **"散射到新数组"会被这两条同时否决**；只有就地置换 + 不复制节点载荷才成立。

**为什么必须带交换预算**：`while(true)` 的终止性依赖"每个 `NodeId` 唯一"。已实测：注入重复
`NodeId` 后环置换**不收敛**（交换 1001 次仍被预算截断）。用**交换预算**（而非 `seen[]` 布尔数组）
既保证终止，又零额外分配 —— 正好避开上表里"多一份 `int` 级常驻"的风险。

**顺带消除**：`nodeOrdinals` 字典与 `:492-493` 的两端 `nodeOrdinals[...]` 探测。
稠密路径下段 3 变为 `edge.SourceNodeId.Value - 1` / `edge.TargetNodeId.Value - 1`（零哈希）。
`nodeOrdinals` 仍须构建并保留 —— 它是**查询期**按 ID 取节点的常驻结构
（`TryGetNode` `:625`、`GetKnownNode` `:639`），且被
`NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload`（`:491`、`:483-540`）计入保留字节。
**本计划不禁用它**。

### C2：`BuildMetadataRanks` 单趟化 + 值类重编号合并

1. **空元数据类显式预留 rank 0，其余类整体 +1。**

   > ⚠ **这是本计划最容易写错的点，且已验证会直接崩**：跳过无元数据边后，all-null 值类
   > **根本不会进入候选值类表**；若仍按"在值类表里找 all-null 类来定 rank"，会得到 `-1`，
   > 随后 `metadataPool[-1]` 抛 `IndexOutOfRangeException`（本轮探针第一版即栽在此处）。

   为什么预留 rank 0 恰好与现状一致：现状 `valueKeys` **含** all-null 类，而 `CompareMetadata`
   把 null `StableKey` 排在任意非 null 之前 ⇒ 现状该类的 rank **本来就是 0**，其余类占 `1..K-1`；
   候选侧候选表无 all-null，排序后占 `0..K-2`，整体 +1 后正好也是 `1..K-1`。两者逐项相同。

2. **值类重编号为 rank**：把候选值类按 `order` 重排后**重新编号**，池按 rank 落位
   （`pool[rank] = valueKeys[order[rank]]`）。于是 `rankOfValue` 成为恒等映射，
   `ranks` 与 `valueClassOfEdge` **塌缩为一个 `int[edgeCount]`**（该数组同时充当池下标）。
   合法性见 §1.3。

3. **跳过无元数据边的身份探测**：对三元组全 null 的边直接赋 rank 0，不进入
   `Dictionary<EdgeMetadataKey,int>` 探测循环。

4. **保留原语义的不变式**：
   - 值去重仍用 `EdgeMetadataValueComparer`（同 6 个值键），只在候选集上做；
   - 候选集排序仍用 `CompareMetadata` + 下标补齐（`:798-808`），保持严格全序；
   - `StableKey` 仍按标签实例记忆化（`StableKeyOf` `:870-887`）；
   - `metadataWidth = valueKeys.Count` 与 `valueClassKeys` 的对外含义不变
     （`CanonicalEdgeStore` 只做 `pool[id-1]` 与长度校验 `:113-119`）。

### C3：`kindKeys` 收窄为 `byte[]`

`NLCPGEdgeKind` 现 36 个取值，`CanonicalEdgeStore.Create` 已断言
`kindWidth > byte.MaxValue + 1` 时抛异常（`:113-119`），故 `byte` 载体安全。

> ⚠ **不得通过"给 `CountingSortPass` 加一个 5 参 `byte[]` 重载"实现**：
> `FrozenNodeStorageEquivalenceTests.FindCountingSortPass`（`:857-868`）用
> `GetMethods(...).SingleOrDefault(Name == "CountingSortPass" && 形参个数 == 5)` 定位重载，
> 并断言 `parameters[2].ParameterType == typeof(int[])`（`:547-561`）。
> 新增一个同为 5 参的同名方法会让 `SingleOrDefault` 抛
> `InvalidOperationException`（多于一个匹配），**打挂该测试**。
> ⇒ C3 必须**另起方法名**（如 `CountingSortPassByte`），或让 `kindKeys` 以 `byte[]` 承载、
> 在调用处转成既有的 `int[]` 键列前先做一次稠密直方图。**倾向前者：另起方法名。**

`CanonicalEdgeStore.Create` 的 `int[] kindKeys` 形参（`:108`）与其 `:154` 的 `(byte)kindKeys[...]`
需同步改为 `byte[]` 入参；此方法无反射签名依赖（已确认 `tests/` 无相关断言）。

---

## 3. 不变量（改动前后必须逐项成立）

| # | 不变量 | 由谁保证 |
| --- | --- | --- |
| I1 | canonical 节点数组 = 传入的**同一实例**，且按 `NodeId` 升序 | C1b 就地置换 |
| I2 | 一次 `Create` 的分配 < 一份完整节点载荷 | C1b 不复制节点 |
| I3 | `GraphSnapshotVersion` 逐字节不变 | C1+C2 等价性 |
| I4 | canonical 边序不变（source → kind → target → metadata rank） | C2 rank 语义不变 |
| I5 | 每条边投影出的元数据三元组逐字段不变 | C2 池 = 按秩排布的非 null 值类 |
| I6 | 稀疏 / 重复 / 越界 `NodeId` 走 `Array.Sort` 回退，行为与改前一致 | C1b 交换预算 |
| I7 | `NodeOrdinals` 字典仍在且覆盖全部节点 | 不属本轮范围，仅需不破坏 |
| I8 | `CountingSortPass` 5 参重载签名与 `parameters[2] == int[]` 不变 | C3 另起方法名 |

---

## 3bis. 实际实现与 §2 设计的差异（**以实际代码为准**）

实现时对 §2-C2 的第 1 条做了**等价简化**，特此记录，避免文档与代码不一致：

| | §2 原设计 | 实际实现 |
| --- | --- | --- |
| 池结构 | 预留 `pool[0]` 为全 null 槽，非 null 值类整体 +1 落位 | **池只装非 null 值类**，`pool[rank - 1]` |
| 无元数据边 | 赋 `rank = 0` | 赋 `metadataId = 0`，`CanonicalEdgeStore.Project` 的 `metadataId == 0` 分支直接投影三个 null（既有分支，未改） |
| 池宽 | `K + 1`（含空槽） | `K + 1`（键域仍为 `[0, K]`） |

两者**逐值等价**：键域与全部键值都相同，故基数排序结果与 canonical 边序逐条相同；
差异只在"空元数据不占池位"。这也让 `CanonicalEdgeStore` 里原先的 `valueClass + 1`
偏移**整体消失**（`metadataId` 直接就是"池下标 + 1"），少一次加法与一次语义转换。

> 实际实现**没有**"预留 rank 0"这一步，因此 §6 风险表中"空元数据类找不到"的风险
> 在最终形态里**不适用**——它只在我最初的探针版本中出现过（探针按"在值类表里查找
> all-null 类"定 rank，才会得到 −1 并崩溃）。此差异不影响 I3/I4/I5。

---

## 4. 执行步骤

### T0：登记（**先于改代码**）

- [x] 在 [`Context/feature_list.json`](../../Context/feature_list.json) 登记本 feature：
      `id` / `title` / `status: todo` / `definition_of_done` / `verification` / `notes`
      （`notes` 指向本文件）。该文件是 feature 状态与完成条件的唯一来源。
      实际登记为 `persistent-freezequeryindex-dominant-term`，状态 `in_progress`。
- [ ] ~~记录基线~~ **未做（用户明确要求"不执行任何前置测试"）**。故 388/388 只证明改动后通过，
      不构成"改前也通过"的对照；`CpgInterproceduralEdgeOrderTests` 的既有失败因此也未被本轮触及。

### T1：C1a —— `DeterministicNodeIdTable.Create`

- [x] 删除 `Distinct()`，改为 `Array.Sort(anchors.ToArray(), StableAnchorComparer.Instance)` + 相邻去重。
- [x] 新增私有 `StableAnchorComparer : IComparer<StableNodeAnchor>`，7 键顺序逐字对齐原 `OrderBy/ThenBy`。
- [x] 在方法注释中写下 §2-C1a 的两条证明（`Distinct` 恒等、排序等价），并注明入参必须复制的原因。
- [x] 构建 `src/NLCPG/NLCPG.csproj`，0 警告 0 错误。

### T2：C1b —— `Create` 稠密就地环置换

- [x] 实现稠密性探测 + 就地环置换 + 交换预算回退，保留 `Array.Sort` 分支原样（回退路径与改前逐字相同）。
- [x] 保持 `inputOrdinals`/`nodeInsertionOrder` 的**语义与长度**不变
      （`NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload` 断言其长度与字节数，已通过）。
- [x] 稠密路径下把两端解析改为 `ResolveDenseOrdinal`（`NodeId − 1`，零哈希）；
      **保留** `nodeOrdinals` 字典的构建（查询期 `TryGetNode`/`GetKnownNode` 依赖）。
      域外 ID 仍交回字典抛 `KeyNotFoundException`，与改前异常类型一致。
- [x] 注释写明：为何必须就地（两条测试断言）、为何用交换预算而非 `seen[]`（终止性 + 零额外分配）。
- [x] 构建通过。

### T3：C2 —— `BuildMetadataRanks` 单趟化与合并

- [x] 跳过三元组全 null 的边，不进身份探测循环。
- [x] ~~显式预留 empty-metadata 类的 rank 0~~ **改为等价简化**：池只装非 null 值类，
      无元数据边直接 `metadataId = 0`，由 `CanonicalEdgeStore.Project` 既有的 `metadataId == 0`
      分支投影三个 null。逐值等价、键域相同（见 §3bis）。
- [x] 候选值类重编号为 rank，使池下标 ≡ rank；`ranks` 与 `valueClassOfEdge` 合并为单个 `int[edgeCount]`。
- [x] 同步 `CanonicalEdgeStore.Create` 的元数据入参语义：形参改为 `metadataIdOfEdge` / `metadataPoolKeys`，
      去掉原先的 `valueClass + 1` 偏移（`metadataId` 现在直接就是"池下标 + 1"）。
- [x] 保留 `hasMetadata` 快速路径（全无元数据时 `metadataWidth = 1`、`metadataIds = null`、秩列全 0）；
      `FrozenEdgeProjectionEquivalenceTests.ZeroMetadataGraph_AllocatesNoMetadataPool` 已通过。
- [x] 更新值类编号语义的注释，写明这是 C2 的对外无感保证。
- [x] 构建通过。

### T4：C3 —— `kindKeys` 收窄

- [x] `kindKeys` 改为 `byte[]`；新增**另起方法名**的 `CountingSortPassByte`（2 个重载，
      不含任何 5 参 `int[]` 同名方法，见 §2-C3）。
- [x] `CanonicalEdgeStore.Create` 的 `kindKeys` 形参改为 `byte[]`，`:154` 直传不再二次转换。
- [x] 确认 `FrozenNodeStorageEquivalenceTests.FindCountingSortPass` 仍能唯一命中原 5 参 `int[]` 重载
      （该用例已执行且通过）。
- [x] 额外加固：因为 `(byte)edge.Kind` 的转换点提前到了 `Create` 内，此处**补了一道与
      `CanonicalEdgeStore` 同措辞的 `kindWidth` 守卫**，防止枚举超过 256 个取值时在源头静默截断。
- [x] 构建通过。

### T5：验收与收尾

- [x] 按 §5 执行验收命令集（限于 10% 核心选择），逐项记录实际输出 → §5.4。
- [x] 回读本文与 `Context/feature_list.json`，检查交叉引用与状态一致。
- [ ] 运行 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1`：
      **本环境 exit 1**，原因是脚本 `finally` 里删 `$env:TEMP\nlissn-yaml-smoke-<guid>` 报"拒绝访问"
      （脚本第 127 行）。该脚本不读取 `docs/plans/`，文档断言（12–88 行）与配置冒烟（94–122 行）
      均已通过后才到清理步骤，故为环境性收尾问题。详见 §8 第 7 条。
- [x] 更新 `feature_list.json` 状态与 `verification`（写入实际执行的命令与未验证边界）。

---

## 5. 验收口径（**不新增探针、不做 A/B**）

原则：收益方向由 §0.3 的一次性探针说明；**本轮验收只认仓库既有测试与不可变量**，
全部通过现有测试项目执行，不写任何新探针、不做改前/改后两臂对比。

### 5.1 命令

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj -c Release -p:UseSharedCompilation=false

dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  -c Release --no-restore -p:UseSharedCompilation=false

dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj `
  -c Release --no-restore -p:UseSharedCompilation=false
```

按 [`docs/contributing.md`](../contributing.md) 与 [Harness 验证矩阵](../harness-verification-matrix.md)
的要求记录实际执行的命令与未验证边界。

### 5.2 判别力最强的既有用例（**这些是等价性的主要证据**）

| 用例 | 锁住的不变量 |
| --- | --- |
| `FrozenNodeStorageEquivalenceTests.Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace`（`:236`） | I1（`Assert.Same` + 就地升序，输入故意降序） |
| `...Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath`（`:285`） | I2（分配 < 一份载荷） |
| `...Create_CopiesANonArrayInputSoTheCallersCollectionIsNotReordered`（`:258`） | `?? nodes.ToArray()` 回退分支 |
| `...NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload`（`:483`） | I7 + `nodeInsertionOrder` 宽度 |
| `DataFlowCandidateScanPruningTests`（`:44-55` 冻结的 golden 哈希） | I3 + I4 + I5（含节点/边计数与 DataFlow 边数） |
| `DataFlowPlanConstructionReuseTests`（`:41-59` 冻结的 golden 哈希） | I3（含 serial vs parallel 一致） |
| `NLCPGGraphIndexConstructionReuseTests`（`:154`、`:176`、`:242`、`:269`） | I3、I4；`PermutedInsertionOrder_*` 锁插入序不变性 |
| `FrozenEdgeProjectionEquivalenceTests`（`:29`） | I4 + I5（投影边 vs 冻结前边集逐条对拍） |
| `PendingEdgeOrdinalizationDeterminismTests` | 边序确定性（含 `InsertionOrder == PendingOrder`） |
| `DocumentShardingEquivalenceTests`（`:205` 处注释) | 逐节点与**逐边同序**比较（弥补 `GraphSnapshotVersion` 对节点集/边序不敏感的缺口） |
| `DominanceRaceReachabilityProbe` | 同输入跨 DOP/多次构建的 `GraphSnapshotVersion` 一致 |
| `CpgShardBuildCoordinatorTests`（多处 snapshot 相等） | 分片/持久化/复用路径的图身份一致 |
| `NLCPGNodeIdContractTests`、`CpgShardContractTests` | `DeterministicNodeIdTable` 的 NodeId 指派语义（C1a） |
| `FrozenNodeStorageEquivalenceTests.CountingSortPass_TakesCallerProvidedScratch`（`:547`） | I8（C3 不得新增 5 参同名重载） |

### 5.3 稀疏 / 重复回退的覆盖

稠密快速路径的**回退分支**必须被既有用例真实走到，不能只靠推理：

- `FrozenNodeStorageEquivalenceTests.BuildFrozenNodes(nodeCount, sparseStride)`（`:786-809`）
  以 `NodeId = index * sparseStride + 3` 构造稀疏输入，`:60` 与 `:85` 分别用 `sparseStride = 7` / `3`
  驱动 `CreateFrozen`。这两个用例是本计划回退分支的**主要覆盖**。
- `Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace` 用 `NodeId = nodeCount − index`
  （严格降序、取值恰为 `1..N` 的置换）——**它落在稠密路径上**，且是有判别力的（若实现改为复制后排序副本，
  `Assert.Same` 与升序断言同时失败）。

> ⚠ **未覆盖**：**重复 `NodeId`** 的输入在现有测试中**没有**直接用例。I6 中"重复走回退"这一支
> 只能由交换预算保证终止性与 §5.2 的稀疏用例间接保证行为一致。本计划**不新增**该用例
> （用户要求不新增测试设施），故此项在 §8 记为未验证边界。

### 5.4 实际执行记录（2026-09-28，10% 核心测试，无前置测试、无新探针）

命令（`DOTNET_CLI_HOME` 指向仓库内 `.dotnet-home`，全部 `-c Release -p:UseSharedCompilation=false`）：

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj            # 0 警告 0 错误
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --filter "..."
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj
```

| 组 | 选择 | 结果 |
| --- | --- | --- |
| 1 | `FrozenNodeStorageEquivalenceTests` + `FrozenEdgeProjectionEquivalenceTests` + `NLCPGNodeIdContractTests` + `NLCPGGraphIndexConstructionReuseTests` | 56 / 56 通过 |
| 2 | `DataFlowCandidateScanPruningTests` + `DataFlowPlanConstructionReuseTests` + `PendingEdgeOrdinalizationDeterminismTests` + `DocumentShardingEquivalenceTests` + `CpgShardContractTests` | 68 / 68 通过 |
| 3 | `DominanceRaceReachabilityProbe` + `CpgShardBuildCoordinatorTests` + `CpgExecutionMatrixTests` + `InterproceduralEdgeIndexSnapshotTests` | 93 / 93 通过 |
| 4 | `CpgPropertyEquivalenceTests` + `CpgRelationQueryTests` + `NLCPGGraphIndexStorageContractTests` + `InterproceduralEdgeIndexSnapshotTests` + `NLCPGStringTableContractTests` | 33 / 33 通过 |
| 5 | `NLISSN.UnitTests` 全项目 | 138 / 138 通过 |
| | **合计** | **388 通过 / 0 失败** |

原始日志：`Build/core10/run1.log` ~ `run5.log`、`run4-unit-retry.log`（`Build/` 已被 gitignore）。

**等价性的主要证据**是仓库自带的冻结 oracle，而不是新探针：`DataFlowCandidateScanPruningTests`
的 `Sparse = D0537910…`、`Collision = 5071C7F2…`、`JoinLoop = 139EC264…` 与
`DataFlowPlanConstructionReuseTests` 的同类常量**逐一命中**，并连带其节点数/边数/DataFlow 边数断言。
另已**逐条确认下述用例真实执行且通过**（而非仅能编译）：
`Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace`、`Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath`、
`Create_CopiesANonArrayInputSoTheCallersCollectionIsNotReordered`、`NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload`、
`CountingSortPass_TakesCallerProvidedScratch`、`CountingSortPass_ReusesScratchAcrossAlternatingKeyWidths`、
`CreateFrozen_RejectsDuplicateNodeIds`、`CreateFrozen_RejectsEdgesWithUnknownEndpoints`、
`CreateFrozen_PreservesShuffledSparseInputOrder`、`Freeze_WithDegenerateNodeCounts_StaysConsistent(0/1)`。

> **本轮未做**：无基线留存、无改前/改后 A/B、无任何计时或内存验收。故 388/388 只证明
> **等价性与结构约束**成立，**不证明**任何性能收益。

---

## 6. 风险

| 风险 | 形态 | 收口 |
| --- | --- | --- |
| **重复 `NodeId` 使环置换不收敛** | `while(true)` 死循环（已实测：1001 次交换未收敛） | C1b 交换预算 = `长度`，超限即回退 `Array.Sort`（T2） |
| **空元数据类找不到 ⇒ `IndexOutOfRangeException`** | 跳过无元数据边后 all-null 类不在候选表中（已实测崩溃） | 显式预留 rank 0 + 其余类 +1（T3） |
| **散射到新数组** | 同时打挂 `Assert.Same` 与"分配 < 一份载荷"两条断言 | C1b 必须就地置换（T2） |
| **破坏边序确定性** | 元数据秩语义被改（rank 与池下标不一致） | 值类**重编号**使 rank ≡ 池下标；I4 由 golden 哈希与 `FrozenEdgeProjectionEquivalenceTests` 兜底 |
| **`CountingSortPass` 重载歧义** | 新增 5 参同名方法使 `SingleOrDefault` 抛异常 | C3 另起方法名，不改既有 5 参重载（T4） |
| **丢失 `hasMetadata` 快速路径** | 单趟化时顺手删掉"全无元数据"短路 | T3 明确要求保留 |
| **`NodeOrdinals` 被误删** | 稠密路径下看似冗余，实为查询期常驻结构 | §2-C1b 明确保留；I7 由 `:483` 用例兜底 |
| **时间收益被高估** | 用探针毫秒数当承诺 | 本计划不承诺倍数/毫秒；§0.3 已标注噪声量级 |
| **基线既有失败被误算** | `CpgInterproceduralEdgeOrderTests` 在未改动基线上同样失败 | T0 要求留存基线输出并如实区分 |

---

## 7. 不做项

| 方案 | 不采用理由 |
| --- | --- |
| 新增探针或改前/改后 A/B | 用户明确要求；收益方向已由一次性探针说明，验收改由既有测试承担 |
| 单独实施 P2（`Dictionary<NodeId,int>` → `int[]`） | C1b 落地后 canonical 位置 = `NodeId − 1`，该表整体不再需要；单独做属重复 |
| `seen[]` 布尔数组验稠密 | 需要 `N+1` 布尔常驻；交换预算同样保证终止且零额外分配 |
| 移除 `NodeOrdinals` 常驻字典 | 查询期按 ID 取节点依赖它；且 `:483` 用例计入其保留字节 |
| 给 `Create` 加 `bool dense` 参数分叉 | 同一方法承载两种语义，调用方可能传错；探测放在方法内更安全 |
| 非空容器 `TrimExcess` / 数组池化换内存 | 本仓库已有历史结论：非空容器收缩会制造"新旧两份同存"，反而抬高峰值 |
| 并行化 / SIMD 化基数排序 | 四趟 LSD 计数排序实测仅占 `Create` 约 7.0%；C1b 已消除比较排序，边际收益低 |
| 顺手优化 `SnapshotHasher.WriteString` 的 `Encoding.UTF8.GetBytes`（`:1252`） | 与本节三项不共享代码路径，混入会使等价性归因不清；应另立一项 |

---

## 8. 未验证边界（**必须随实现结果一并记录**）

1. **重复 `NodeId` 输入无直接用例**：I6 的"重复走回退"仅有交换预算的终止性保证与稀疏用例的间接覆盖（§5.3）。
2. **`Create` 仍有约 1,105 ms（70%）未归因**：§0.3 的分段只覆盖 466.7 / 1,571.5 ms。
   C2 只触及其中 `BuildMetadataRanks` 的 301.8 ms 下界。**本项不是冻结成本的全部答案。**
3. **时间收益无本轮证据**：按用户要求不新增探针，故本计划不给出、也不承诺任何倍数或毫秒数。
   若日后需要量化，须以配对 A/B 在**无观测者**条件下重测（研究报告已记载观测者会放大被测阶段 1.33–1.92×）。
4. **`kindKeys` 收窄的实际内存收益**依 `edgeCount` 而定，本轮不做端到端内存验收。
5. **全仓测试的既有失败**：`NLISSN.Tests.Architecture.LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
   与 `CpgInterproceduralEdgeOrderTests.BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder`
   为既有失败。因本轮按要求**未做基线、也未跑全仓**，这两个用例本轮**根本没被执行到**，
   故既不能据此说"已修复"，也不能据此说"被引入"——它们仍属未知状态。
6. ~~本计划尚未登记进 `Context/feature_list.json`~~ **已完成**：已登记为
   `persistent-freezequeryindex-dominant-term`（`in_progress`），`lastUpdated` 更新为 `2026-09-28`。
7. **`check-harness-consistency.ps1` 在本环境无法正常退出**：脚本在 `finally` 里对
   `$env:TEMP\nlissn-yaml-smoke-<guid>` 执行 `Remove-Item -Recurse -Force` 时报"拒绝访问"
   （脚本第 127 行），故整体 exit code 为 1。经检查，该脚本**不读取 `docs/plans/`**（匹配数为 0），
   其文档一致性断言位于第 12–88 行、配置冒烟位于第 94–122 行，均**已通过**后才执行到清理步骤。
   因此该失败是**环境性的收尾清理问题**，与本计划文档无关；本项在 T5 需如实记录，
   不得表述为"文档一致性检查通过"。
8. **本轮未做的验证维度（汇总）**：无基线对照、无改前/改后 A/B、无计时与内存验收、
   无全仓测试、无端到端语料运行。故 388/388 的结论域**仅限等价性与结构约束**。
9. **测试项目选择是 10% 抽样，不是全集**：所选 5 组覆盖了冻结索引的等价性与结构契约，
   但 `NLISSN.HostTests` / `NLISSN.PerformanceTests` / 全量 `ContractTests` 本轮未执行。
