# ② 分片合并三实现收敛与载荷双持消除执行文档

日期：2026-09-27。性质：**只写文档**。本轮未改产品代码、未运行构建、未运行测试、未做任何内存采样。

**目标：** 把仓库中**三份并行的「把分片合并成冻结图」实现**收敛为一份，并消除合并过程中
**字典/哈希集与最终数组同时存活**造成的节点载荷第二份副本与边载荷第二份副本。

**权威状态：** [feature_list.json](../../Context/feature_list.json) 是唯一来源。本切片**尚未登记**。

> 📌 **行号基准**：`src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs`（419 行）；
> `src/NLCPG/Analysis/CpgShardRelationQueryService.cs`（242 行）；
> `src/NLCPG/Analysis/NLCPGSliceQuery.cs`（406 行）；
> `src/NLCPG/Model/NLCPGGraph.cs`（1,361 行）。
> 引用**同时给出符号名**，行号漂移时以符号为准。

---

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 重复实现有几份？ | **3 份**合并逻辑 + **1 份**累加器（见 §1 表） |
| 真重复在哪？ | (a) 三处各自建 `Dictionary<NodeId, NLCPGNode>` + `HashSet<NLCPGEdge>`；(b) 把非数组集合喂给 `CreateFrozen`，触发 `ToArray()` 再复制一遍 |
| 单分片路径也重复吗？ | **不重复**。`ReadGraph(CpgFrozenShard, StringInterner)` 传的是真数组，直通 `NLCPGGraphIndex.Create` 的**接管**分支（§1.3）——这个不对称必须写进文档，否则容易误改 |
| 生产可达吗？ | **部分**：(i) 在持久化恢复路径上可达（同①默认关闭）；(ii)/(iii) 在 `src/` 内**无实例化**，只有测试与性能测试构造（§1.2） |
| 能改 `Create` 的 `?? ToArray()` 吗？ | **不能**。两个既有测试分别锁住数组接管与非数组复制两条分支（§2.3） |
| 收益有实测吗？ | **没有**。全部为【推导】 |

---

## 1. 现状

### 1.1 三份并行实现

| # | 位置 | 集合构造 | 终态调用 | 生产可达性 |
| --- | --- | --- | --- | --- |
| (i) | `CpgFrozenShardGraphReader.ReadGraph(IEnumerable<CpgFrozenShard>)` `:125-164` | `Dictionary<NodeId, NLCPGNode>` `:128`、`HashSet<NLCPGEdge>` `:130` | `:163` `NLCPGGraph.CreateFrozen(nodes.Values, edges, stringInterner)` | **可达**（恢复路径） |
| (ii) | `CpgShardRelationQueryService.BuildGraph` `:155-181` | `Dictionary<NodeId, NLCPGNode>` `:157`、`HashSet<NLCPGEdge>` `:158` | `:180` 同上 | **`src/` 内 0 处实例化**；4 处全在 `CpgRelationQueryTests.cs:147,192,216,238` |
| (iii) | `NLCPGSliceQuery.LoadFrontierGraphAsync` `:227-286` | `Dictionary<NodeId, NLCPGNode>` `:229`、`HashSet<NLCPGEdge>` `:230` | `:285` 同上（`includedEdges` 已是 `:282-284` 的数组） | 经 `CpgShardQueryResolver` 构造；`src/` 内无实例化，测试/性能测试构造 |

(iii) 与 (i) **语义重合**：(ii)/(iii) 都是「逐分片调
`CpgFrozenShardGraphReader.ReadGraph(shard, stringInterner)`，再跨分片归并」——
而这正是 (i) 的 `ReadGraph(IEnumerable<CpgFrozenShard>)` 已经做的事
（`CpgShardRelationQueryService.cs:163`、`NLCPGSliceQuery.cs` 内层调用）。
⇒ **(ii) 与 (iii) 的归并段落可直接委托给 (i)**，无需保留三份。

### 1.2 累加器路径（属 (i)，但机制不同）

`ReadMutableFacts` `:166-181` → `CreateMutableFactsAccumulator()` `:183-…` →
`MutableFactsAccumulator` `:338-390` 持有 `Dictionary<NodeId, NLCPGNode> _nodes`（`:341`）+
`HashSet<NLCPGEdge> _edges`（`:342`）；`Complete()` `:378-389` 返回

```csharp
new CpgFrozenShardGraphFacts(_nodes.Values.ToArray(), _edges.ToArray(), _stringInterner);
```

⇒ **在仍然存活的字典与哈希集之上，再物化两份完整载荷数组**。这不是"复制一次"，是
**同一批节点同时以两种表示常驻**，直到 `Complete()` 的返回值脱离方法作用域、累加器被回收。

消费端为 `NLCPGGraph.ImportMutableFacts`（`NLCPGGraph.cs:429-477`），逐项 `AddNode`/`AddEdge`。

### 1.3 关键不对称：单分片路径**不**双持

`ReadGraph(CpgFrozenShard shard)` `:189-210` / `internal ReadGraph(CpgFrozenShard, StringInterner)`：

- `:197-200` 建 `NLCPGNode[]`（`.OrderBy(...).Select(...).ToArray()`）
- `:202-208` 建 `NLCPGEdge[]`
- `:209` `CreateFrozen(nodes, edges, stringInterner)`

`NLCPGGraph.CreateFrozen`（`NLCPGGraph.cs:218-258`）内部为
`var frozenNodes = nodes.ToArray();`（`:227`）。但 `NLCPGGraphIndex.Create` 用
`as NLCPGNode[] ?? nodes.ToArray()`（`:418`/`:422`）**接管调用方数组**，故**数组入参不再复制**。
（`:224` 注释即为此而写；[FrozenNodeStorageEquivalenceTests](../../tests/NLISSN.ContractTests/Cpg/FrozenNodeStorageEquivalenceTests.cs)
`:236` `Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace` 与 `:285`
`Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath` 锁定该行为。）

> ⇒ **三份重复实现的问题不是"多了一次 ToArray"，而是它们传的是 `Dictionary.Values` /
> `HashSet` 这类非数组集合，因而走 `?? ToArray()` 兜底分支，同时集合本身仍然存活。**

---

## 2. 改动清单

### 2.1 收敛为一处归并核心

在 `CpgFrozenShardGraphReader` 内新增一个**接受调用方提供容器**的内部重载，
让 (ii)/(iii) 复用同一段逻辑，只保留各自"读哪些分片"的策略差异：

```
internal static NLCPGGraph ReadGraph(
    IEnumerable<CpgFrozenShard> shards,
    Dictionary<NodeId, NLCPGNode> nodes,     // 复用已有容器，避免重建
    HashSet<NLCPGEdge> edges,
    StringInterner stringInterner);
```

(ii) 的 `BuildGraph`（`:155-181`）与 (iii) 的 `LoadFrontierGraphAsync`（`:227-286`）
改为「自建容器 → 调该重载」，删除各自重复的逐分片归并循环。

**不改** (i) 的公开签名（`:125`），它继续作为便利入口。

### 2.2 消除"集合与数组同时存活"

两种可选做法，实施时二选一并记录：

- **做法 A（低风险，推荐先试）**：在归并**结束后**、`CreateFrozen` 之前，把容器内容复制成数组，
  随后**立即 `Clear()` 并解除引用**（`nodes = null; edges = null;` 或限定作用域），
  使载荷只在一段短窗口内双持。收益是**窗口缩短**，不是峰值消除。
- **做法 B（收益完整）**：不建字典/哈希集——改为
  （1）先把各分片节点收集进 `List<NLCPGNode>` 并记录 NodeId 去重位图；
  （2）排序去重后**直接**把数组交给 `CreateFrozen`（走接管分支）。
  这需要处理"后者覆盖前者"的现有语义（`Dictionary` 索引器赋值序），
  并以 `NLCPGEdge` 的 `HashSet` 语义（`:130`）做等价替换。

> ⚠️ 做法 B 触碰去重语义，风险高于 A。**若本轮无法证明去重语义逐元素等价，采用 A 并如实记录
> "峰值未消除、仅窗口缩短"。**

### 2.3 **禁止**的改动

| 诱惑 | 为什么不能做 |
| --- | --- |
| 删掉 `CreateFrozen` 的 `nodes.ToArray()` / `Create` 的 `?? ToArray()` | `Create_CopiesANonArrayInputSoTheCallersCollectionIsNotReordered` `:258` 与 `Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace` `:236` **分别锁定两条分支**。必须改**调用方**，不是兜底 |
| 把 (i)/(ii)/(iii) 的 `Dictionary` 换成"更省的"自建结构 | 会改变重复 NodeId 的**覆盖序**，而 `Export`/恢复的字节序依赖它 |
| 顺手删 `CpgShardRelationQueryService` | 它是 `ICpgRelationQueryService` 的实现之一，是库的**公开可组合面**；`src/` 内无实例化 ≠ 可删 |

---

## 3. 等价性分析

### 3.1 必须保持的语义

1. **重复 NodeId**：现为"后写入者覆盖"（`nodes[id] = …`）。任何重写必须同序覆盖。
2. **边去重**：`HashSet<NLCPGEdge>`（值相等性）。同一条边在不同分片出现必须只算一次。
3. **字符串表**：跨分片共用一个 `StringInterner`，不得每分片新建。
4. **节点序**：交给 `CreateFrozen` 后由 `Array.Sort(..., NodeIdOrderComparer)`（`NLCPGGraphIndex.cs:436`）
   统一决定，故合并阶段的插入顺序**不影响**最终 `OrderedNodes`——但**会**影响
   `ImportMutableFacts` 路径的 `AddNode` 顺序（`NLCPGGraph.cs:429-477`）。两条路径分别验证。

### 3.2 风险点

1. `ReadGraph(IEnumerable<CpgFrozenShard>)` 的返回值按 `GraphSnapshotVersion` 等字段参与
   `Assert.Equal(original.GraphSnapshotVersion, restored.GraphSnapshotVersion)`
   （`CpgShardBuildCoordinatorTests.cs:807`）——收敛实现时不得丢掉任何快照字段。
2. `NLCPGSliceQuery.LoadFrontierGraphAsync` 只加载**可达子图**（`:227-286` 内有访问预算逻辑），
   其"读哪些分片"策略与 (i) 完全不同。收敛的**只是归并段**，不是分片选择策略。
   误把 `LoadFrontierGraphAsync` 整个换成 `ReadGraph(shards)` 会**加载全图**，是性能倒退。
3. `NLCPGNode` 是**公开图契约**，必须保持对象/语义同一性；不得为省内存把节点改成类或改变身份
   （见 [类/结构体实验研究](../research/2026-09-18-csharp-class-struct-experiment-research.md)）。

---

## 4. 验证门禁

### 4.1 既有 oracle（**必须全绿，不得修改期望值**）

| 测试 | 位置 | 锁住什么 |
| --- | --- | --- |
| `BuildFromSource_StreamingPersistence_PublishedShardsRestoreBaseGraphAndBuilderRestoresFullGraph` | `CpgShardBuildCoordinatorTests.cs:754` | **整图往返**：`ReadGraph(publishedShards)` 还原结果与原始图逐项相等（含 `GraphSnapshotVersion`、`:784`/`:807`）——本项**最强** oracle |
| `BuildFromSource_StreamingPersistence_MatchesSerialSnapshotAtConfiguredDop` | `:1112` | 流式 vs 串行快照等价（多 DOP） |
| `BuildFromSource_PersistenceMetrics_ExposeNonStreamingRestoreBreakdown` | `:113` | 恢复计数指标 |
| `QueryBackwardAsync_ShardResolver_TraversesAcrossAdjacentShards` | `NLCPGSliceQueryTests.cs:136` | (iii) 跨分片遍历 |
| `QueryBackwardAsync_ShardResolver_ProjectsOnlyReachableShardRecords` | `:184` | (iii) **只投影可达分片**——防止 §3.2-2 的全图回归 |
| `QueryBackwardAsync_ShardResolver_RespectsVisitedNodeBudgetBeforeExpandingFrontier` | `:222` | 访问预算 |
| `QueryBackwardAsync_ShardResolver_MissingAnchorReportsUnavailable` | `:259` | 缺失锚点 |
| `QueryBackwardAsync_BoundaryManifest_TraversesWithoutDuplicatedEndpointNodes` | `:19` | **边界端点不重复**——直接对应 §3.1-2 去重语义 |
| `QueryBackwardAsync_ShardResolver_LegacyV5AndV6Shards_ReturnSamePaths` | `:71` | 跨 schema 版本一致 |
| `CpgRelationQueryTests`（4 处） | `CpgRelationQueryTests.cs:147,192,216,238` | (ii) 的公开行为 |
| `NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload` | `FrozenNodeStorageEquivalenceTests.cs:320` | **全图可达 `NLCPGNode[]` 计数 = 1**——见 §4.2 冲突说明 |

### 4.2 与既有"单载荷"oracle 的关系（**必须先厘清**）

`FrozenNodeStorageEquivalenceTests.NodeStorageSubsystem_RetainsAtMostOneCompleteNodePayload`
（`:320`，计数助手 `CountNodePayloadArrays` `:738`）断言从 `graph` 出发可达的**非空
`NLCPGNode[]` 恰好 1 个**。它约束的是**冻结图本身**，不涵盖"合并过程中临时字典的
`.Values`"。⇒ 本项的中间容器**可能不在**该 oracle 的遍历根内。

**实施时第一步**：确认该 oracle 的遍历根与递归深度；若中间容器不在其中，
则在**本项**新增测试里单独守住它（§4.3），**不得**修改既有 oracle 的断言值。

### 4.3 拟新增门禁（**新增**，判别力必须可证）

1. **`MergeShards_DoesNotHoldBothDictionaryAndArrayPayload`**（新增）
   —— 以 `Dictionary`/`HashSet` 的 `_buckets`/`_entries` 计数手法
   （仿 `FrozenNodeStorageEquivalenceTests.CountDictionaryBytes` `:726`，
   条目宽 = `12 + valueWidth`）在 `CreateFrozen` 调用点前后各采样一次，
   断言"集合存活期间的节点载荷字节数"不超过改前基线的某个**实测**阈值。
   **阈值必须先量再定**，不得预设。
2. **`BuildGraph_DelegatesToSharedMergeCore`**（新增，结构断言）
   —— 断言 (ii)/(iii) 归并后的节点数/边数/快照字段与直接调用 (i) 的结果**逐项相等**。
   这是"收敛"这个动作本身的 oracle。
3. **变异检验（必做）** —— 故意让归并丢一条边、或让覆盖序反转，确认 §4.1 中至少
   `QueryBackwardAsync_BoundaryManifest_TraversesWithoutDuplicatedEndpointNodes`
   与 `...RestoreBaseGraphAndBuilderRestoresFullGraph` 变红，然后**还原**。

### 4.4 命令

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~NLCPGSliceQueryTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgRelationQueryTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardBuildCoordinatorTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FrozenNodeStorageEquivalenceTests'
```

耗时门禁同①§4.4（1 预热 + ≥3 交错配对，中位数劣化 ≤5%，否则 **Inconclusive**）。

---

## 5. 收益口径（**均为算术推导，未实测**）

采用既有实测宽度：`NLCPGNode` = **104 B**、`NLCPGEdge` = **72 B**、`HashSet` 槽位 = **80 B**、
x64 SZArray 头 = **24 B**、字典条目 = `12 B 头 + key + value`。

| 项 | 结论（【推导】） |
| --- | --- |
| (i)/(ii)/(iii) 传非数组集合 ⇒ `?? ToArray()` 兜底 | 每合并一次多一份 `nodeCount × 104 B`（+ `edgeCount × 72 B`） |
| `MutableFactsAccumulator.Complete()` | 字典+哈希集**仍存活**时再物化两份数组：`nodeCount × 104 B` + `edgeCount × 72 B` |
| 收敛三份实现 | 减少**代码重复**，不直接减少字节；字节收益来自 2.2 的做法 A/B |

> ⚠️ 与①同理：**不得**写成端到端峰值结论。合格表述是"在受控容器存活字节口径下，
> 合并窗口少持有一份节点载荷"。

---

## 6. 回退条件

1. 任一分片的还原结果出现字段差异，或 `QueryBackwardAsync_*` 任一用例行为变化。
2. (iii) 出现"加载全图"迹象（`...ProjectsOnlyReachableShardRecords` 红）。
3. `MutableFactsAccumulator` 的 `AddNode` 顺序变化导致 `ImportMutableFacts` 后图不等价。
4. §4.4 耗时劣化 > 5%（非 Inconclusive）。

回退方式为反向补丁；**不得** `reset --hard` / `clean` / 整文件覆盖。

---

## 7. 本文件不得越读的边界

1. ~~本轮**只写文档**~~ **已过期**：本轮**已改产品代码并跑测试**，见 §8。§4 的门槛**已执行**。
2. §5 全部是**算术推导**，不含本轮实测；且 **§5 第 1 行已被证伪**（见 §8.3）。
3. (ii)/(iii) 在 `src/` 内**无实例化**（静态检索结论）；它们在**测试与性能测试**中被构造。
   「生产可达」仅对 (i) 成立，且 (i) 仅在持久化配置下生效。
4. 静态检索**不能**证明外部程序集不会调用这些 public 类型；本文件不据"无引用"提议删除公开类型。
5. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)。
6. 本项与①共享 `CpgFrozenShardExporter`/`CpgShardBuildCoordinator` 的验证面；
   两者**不应同批合入**，以便红/绿可归因。

---

## 8. 执行结果（本轮实测）

### 8.1 改动

| 文件 | 净变化（`git diff --numstat`） |
| --- | --- |
| `src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs` | +53 / −10（净 +43 行；新增 `AccumulateShardGraphs` / `MergeInto` 两处内部核） |
| `src/NLCPG/Analysis/CpgShardRelationQueryService.cs` | +3 / −9（净 −6 行；`BuildGraph` 改为调共用核） |
| `src/NLCPG/Analysis/NLCPGSliceQuery.cs` | +8 / −8（净 0 行；`LoadFrontierGraphAsync` 归并段就地换为 `MergeInto`） |
| `tests/NLISSN.ContractTests/Cpg/CpgRelationQueryTests.cs` | +54 / −0（收敛等价性守卫） |

三份逐字重复的归并循环收敛为：

- `AccumulateShardGraphs(...)`（`:170`）——「按分片序读图 → 并入容器」，供 (i)/(ii) 复用；
- `MergeInto(...)`（`:191`）——「节点 `TryAdd`、边 `UnionWith`」两行最小核，供 (iii) 复用
  （(iii) 的归并交错在逐跳遍历与访问预算中，无法整段套用按分片循环）。

**边界边仍留在各自调用方**：(i) 抛 `InvalidDataException("A CPG boundary edge references a node
that was not restored.")`，(ii) 静默跳过端点缺失的边界边。两条策略**未被收敛**，这是有意的。

### 8.2 ⚠️ 两条计划前提**已被证伪**（必须如实记录）

#### (1) §2.2 / §5 第 1 行：「传非数组集合 ⇒ 走 `?? ToArray()` 兜底，多一份 `nodeCount × 104 B`」——**错**

`NLCPGGraph.CreateFrozen` **无条件**执行 `var frozenNodes = nodes.ToArray();`（`NLCPGGraph.cs:232`），
并把**那个副本**交给 `NLCPGGraphIndex.Create`（`:261`）。⇒ `Create` 里
`nodes as NLCPGNode[] ?? nodes.ToArray()`（`NLCPGGraphIndex.cs:422`）的 `as` 分支**恒为真**，
`?? ToArray()` 兜底**对 (i)/(ii)/(iii) 三条路径全部不可达**。

独立探针（临时控制台程序，非产品代码）：

```
int[] a = {1,2,3};  ReferenceEquals(a, a.ToArray())  →  False
int[] e = Array.Empty<int>();  ReferenceEquals(e, e.ToArray())  →  True
```

⇒ `Enumerable.ToArray()` 对**非空数组**仍会复制。故「让调用方改传数组以走接管分支」这一
收益路径**在本仓不存在**。仓库自身也早已写明这一点——见
[FrozenNodeStorageEquivalenceTests](../../tests/NLISSN.ContractTests/Cpg/FrozenNodeStorageEquivalenceTests.cs)
`:234`：「因为经由 `CreateFrozen` 时中间还会多一层它自己的 `ToArray`，测不到本方法的接管行为」。

**结论：§5 第 1 行整行作废。** 本项**不产生**该项声称的字节收益。

#### (2) §3.1-1 / §2.3：「重复 NodeId 现为『后写入者覆盖』」——**错**

`git diff` 直接证明：改动前三处（`CpgFrozenShardGraphReader.cs` 原 `:137`、
`CpgShardRelationQueryService.cs` 原 `:163`、`NLCPGSliceQuery.cs` 原 `:256`）**原本就是**
`nodes.TryAdd(...)`（**先到者胜**）与 `edges.UnionWith(...)`，**不存在**索引器赋值
`nodes[id] = …`。全 `src/NLCPG` 检索亦**零**处出现该赋值。

⇒ 做法 B 所述难点「需要处理『后者覆盖前者』的现有语义」**是无的放矢**；
§2.3 第 2 行「换成更省的容器会改变重复 NodeId 的**覆盖序**」的**前提不成立**。
（该禁令的**结论**仍保留——收敛不改去重语义，风险照样不值得冒——但理由须改。）

### 8.3 §2.2 做法 A / B 的裁定：**均不实施**

**做法 A 已实现并试跑，随后回退**。原因是它不但没有缩短峰值，反而**抬高**峰值：

```
改动前峰值 = 字典/哈希集本体（entries ≈ nodeCount × (12+4+104) B）
             + CreateFrozen 内 ToArray 产生的 array1（nodeCount × 104 B）

做法 A 峰值 = array1 = dict.Values.ToArray()（nodeCount × 104 B）
             + 字典本体仍在（Clear() 只清计数，**不释放** entries 容量）
             + CreateFrozen 又对 array1 复制出 array2（nodeCount × 104 B）
```

⇒ 做法 A **多出一整份节点载荷数组**（实测 0 例测试变化，但结构与算术均指向净增）。
它缩短的是字典的**存活时长**（提前 `Clear()`），付出的是**峰值上升**；
在 LOH/GC 压力这个真正关心的口径上，做法 A 是**净损失**。

**做法 B 未实施**：它需要先证明「按 `List` 收集 + NodeId 去重位图 + 排序去重」与
`HashSet`/`Dictionary` 语义**逐元素等价**（尤其 `NLCPGEdge` 的值相等性），
本轮**未能证明**；且依 (2)，其动机之一已消失。

> ⇒ **本项的实际交付 = 三份实现收敛为一份（代码去重）+ 一份顺序不等价守卫。
> 字节收益为零，且 §5 两行中的第 1 行已被证伪。** 这是诚实的结论，不得美化。

### 8.4 §4.3-1 门禁**未按原样**实现（附理由）

`MergeShards_DoesNotHoldBothDictionaryAndArrayPayload` **未写**。它要求"先量再定"一个
「集合存活期间的节点载荷字节」阈值；但依 §8.2 (1) 与 §8.3，该口径下**不存在可下降的载荷**，
任何实测阈值都只会锁住"做法 A 未被实施"这一事实，而**不能**判别去重语义是否等价——
即它**没有判别力**。按 §4.2「指标口径必须能判别」的精神，改以 §4.3-2 的结构等价性守卫替代。

### 8.5 §4.3-2 新增门禁与变异检验（**判别力已证**）

| 新增门禁 | 断言 | 变异 | 结果 |
| --- | --- | --- | --- |
| `BuildGraph_DelegatesToSharedMergeCore` `CpgRelationQueryTests.cs:212` | 同一组分片经 (i) `ReadGraph(shards)` 与 (ii) `BuildGraph` 归并后，**节点集 / 边集 / `GraphSnapshotVersion` 逐项相等**，且节点 3、边 2（分片内 1 + 跨分片边界 1） | 让共用核少并入一条边：`edges.UnionWith(shardEdges.Take(Math.Max(0, shardEdges.Count() - 1)))` | **变红**（本用例 + 6 例）✅ |

§4.3-3 要求的变异，**两个指定 oracle 均如预期变红**：

```
[xUnit FAIL] NLCPGSliceQueryTests.QueryBackwardAsync_BoundaryManifest_TraversesWithoutDuplicatedEndpointNodes  ✅ 指定的 oracle
[xUnit FAIL] CpgShardBuildCoordinatorTests.BuildFromSource_StreamingPersistence_PublishedShardsRestoreBaseGraphAndBuilderRestoresFullGraph  ✅ 指定的 oracle
[xUnit FAIL] NLCPGSliceQueryTests.QueryBackwardAsync_ShardResolver_TraversesAcrossAdjacentShards
[xUnit FAIL] NLCPGSliceQueryTests.QueryBackwardAsync_ShardResolver_ProjectsOnlyReachableShardRecords
[xUnit FAIL] NLCPGSliceQueryTests.QueryBackwardAsync_ShardResolver_RespectsVisitedNodeBudgetBeforeExpandingFrontier
[xUnit FAIL] CpgRelationQueryTests.QueryAsync_FrozenShardAndMemory_ReturnSameStructuralResult
[xUnit FAIL] CpgRelationQueryTests.BuildGraph_DelegatesToSharedMergeCore
→ 失败 7, 通过 64, 总计 71
```

变异随后**已还原**，还原后 138 例全绿。

> 注意 `...ProjectsOnlyReachableShardRecords` 也变红 ⇒ 该 oracle 对"归并段"同样敏感，
> 故 §4.1 用它守「§3.2-2 全图回归」**与**本项的归并语义是**双重有效**的。

### 8.6 编译证据

```
build tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj
  → 0 错误, 21 警告（全部为既有 xUnit 分析器规则，与本项无关）
```

### 8.7 测试证据（定向 ~10% 核心子集）

```
--filter CpgShardContractTests|NLCPGNodeIdContractTests|CpgPersistenceStateTests
       |CpgShardBuildCoordinatorTests|NLCPGSliceQueryTests
       |CpgRelationQueryTests|FrozenNodeStorageEquivalenceTests
  → 已通过! 失败 0, 通过 138, 总计 138, 耗时 1 m 13 s
```

覆盖 §4.1 全部四组 oracle（`NLCPGSliceQueryTests` / `CpgRelationQueryTests` /
`CpgShardBuildCoordinatorTests` / `FrozenNodeStorageEquivalenceTests`）与 §4.4 三条命令，
**期望值未做任何修改**。

### 8.8 边界更新

- §7 第 1 条"本轮只写文档"**已过期**（见上）。
- §7 第 2 条补充：§5 **第 1 行已被证伪**。
- §2.2、§2.3、§3.1-1、§5 第 1 行的**前提**不成立，以 §8.2 / §8.3 为准，
  本节**不改写**前述章节的原文，以免掩盖"计划曾据此判断"这一事实。
- 本项**未**跑全量测试套件（按用户指示只执行 ~10% 核心子集）。

### 8.9 未验证边界

1. **未做任何内存采样**：§5 剩余各行（及 §8.3 的峰值算术）**仍全部是推导**，
   本轮**未**测量分配或峰值。§8.3 对做法 A 的"净损失"判定**是算术论证，不是实测**。
2. **未测耗时**：§4.4 的"中位数劣化 ≤5%"门槛**未执行**。本轮无任何耗时数据可报告。
3. **未跑全量测试**：仅上述 7 个类共 138 例。`HostTests`、`UnitTests`、`PerformanceTests`
   **未运行**。
4. **未做去重语义的逐元素对拍**：§8.5 的等价性守卫比对的是**归并结果**
   （节点集/边集/快照字段），**未**构造"同一 NodeId 在两个分片里取值不同"或
   "同一条边在两个分片重复出现"的对抗输入。⇒ `TryAdd` 先到者胜与 `UnionWith` 值去重
   这两条语义**未被专门锁定**。
5. **未验证** §4.2 的"既有单载荷 oracle 是否涵盖中间容器"这一问：本轮结论是
   **不在其遍历根内**（该 oracle 从 `graph` 与 `index` 出发，而 `BuildGraph` 的局部
   `Dictionary` 在归并函数返回后即不可达），但此判断是**静态阅读**，且——依 §8.3——
   本轮**未**新增门禁去守它。
6. **未验证** (ii)/(iii) 在**真实持久化配置**下的行为：两者在 `src/` 内无实例化，
   本轮全部用例均通过测试内构造的 `ICpgShardStore`/`ICpgShardCatalog` 桩件驱动。

