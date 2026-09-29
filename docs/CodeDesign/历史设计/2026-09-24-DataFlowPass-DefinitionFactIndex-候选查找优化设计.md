# DataFlowPass DefinitionFactIndex 候选查找优化设计

> AI 阅读入口：任务涉及 `DataFlowPass`、`DefinitionFactIndex`、`TryGetCandidates`、`AddReachable`、数据流候选边生成、`SparseSetStore` 去重或 `NLCPGNode` 哈希开销时，先读本文。

## 1. 目标与边界

目标：在不改变任何图输出（节点、边、候选集内容与顺序）的前提下，消除 `DefinitionFactIndex` 候选查找中**可证明为无效**的每元素开销。

**不做**：

- 不改变候选集语义、去重结果或 `matches` 的元素顺序；
- 不改变 `SparseSetStore` 的表示、位序或"严格升序且无重复"不变量；
- 不改变预算判定（`MaxCandidateEdgesPerMethod`、`MaxDefinitionsPerMethod`、`MaxFlowNodesPerMethod`）与溢出原因；
- 不改 `CpgWorkBatchExecutor` 的调度、通道容量或 reducer 顺序；
- 不做并行化（属 N1/N2，另行设计）。

## 2. 当前事实（全部为实测，非推断）

### 2.1 热点规模（NPC.cs，batchSize=8，MDP=16）

在 `DefinitionFactIndex` 内临时加入 5 个计数器后实测：

| 观测 | 值 |
| --- | ---: |
| `TryGetCandidates` 调用次数 | 26,123 |
| `AddReachable` 内层迭代总数 | **92,284,215** |
| 平均每次调用迭代 | 3,533 |
| ① `flowNodeOrdinals` 查不到（`Dictionary<NLCPGNode,int>`） | **0**（0.0%） |
| ② `definitionOrdinals[..] < 0`（非定义节点） | **0**（0.0%） |
| ③ 位不在 reaching 集（`Contains` 失败） | 92,279,325 |
| ④ 通过全部条件并 `seen.Add` 成功 | **4,516**（0.005%） |

### 2.2 ①② 恒为 0 是**结构性**的，不是数据巧合

`definitionOrdinals` 与 `flowNodeOrdinals` 都由 `definitionFactsByNode` 生成（`DataFlowPass.cs:602-618`、`:662-664`）：

```csharp
for (var ordinal = 0; ordinal < flowNodes.Length; ordinal += 1)
{
    if (definitionFactsByNode.TryGetValue(flowNodes[ordinal], out var definitionFact))
    {
        definitionOrdinals[ordinal] = definitionNodes.Count;
        ...
    }
    else { definitionOrdinals[ordinal] = -1; }
}
```

而旧 `DefinitionFactIndex` 的四个索引**恰好也是**用同一个 `definitionFactsByNode` 填充的：

```csharp
foreach (var pair in factsByNode)   // 与生成 definitionOrdinals 的是同一个字典
{
    Add(_byLocation, fact.LocationKey, pair.Key);
    ...
}
```

因此索引里每个 `NLCPGNode` **必然**满足：在 `flowNodeOrdinals` 中（映射到 `flowNodes` 的某个下标），且 `definitionOrdinals[flowOrdinal] >= 0`。①② 是恒真式，**永远不可能过滤掉任何元素**。

代价却是每元素一次 `Dictionary<NLCPGNode,int>` 查找，而 `NLCPGNode.GetHashCode` 实测 **70.4 ns**（带 anchor），是 `int` 的 **112.3 倍**。

> 92.28M × 70.4 ns ≈ **6.5 s**；按 150 ns/迭代（字典查找 + HashSet Add）估算上界 ≈ **13.8 s**。

### 2.3 CPU 采样交叉验证

`dotnet-trace` CPU 采样（`Inclusive`）：

| 函数 | Inclusive | 占真实工作（÷13.61%） |
| --- | ---: | ---: |
| `DefinitionFactIndex.AddReachable` | 4.07% | 29.9% |
| `DefinitionFactIndex.TryGetCandidates` | 4.07% | 29.9% |
| `AnalyzeCfgSensitivePartition` | 4.54% | 33.4% |
| `SparseSetStore.Contains` | 0.41% | 3.0% |

`AddReachable` **自身** Exclusive 仅 0.38%，说明它的时间几乎全在**被调用**的字典/HashSet 上（`FindValue` 1.67%、`NLCPGNode.GetHashCode` 0.23%、`StableNodeAnchor.GetHashCode` 0.55%、`NLCPGNode.Equals` 0.47%）。与 §2.1 的归因一致。

### 2.4 每调用分配

旧实现每次调用 `new List<NLCPGNode>()` + `new HashSet<NLCPGNode>()` 两个容器（26,123 次）。`HashSet<NLCPGNode>` 还使用 `NLCPGNode`（含 `StableAnchor`）作为键。

## 3. 设计

### 3.1 核心思路：把恒真判定前移到建索引期

既然"该节点在流节点表内且是定义节点"在**建索引时**就已经确定，就把**紧凑定义序数**（`int`）存进索引，而不是 `NLCPGNode`。

于是扫描期每个元素只剩 **1 次 `SparseSetStore.Contains`（数组操作） + 1 次 `bool[]` 标记读写**，彻底消除：

- 每元素一次 `Dictionary<NLCPGNode,int>` 查找（≈70 ns）；
- 每元素一次 `HashSet<NLCPGNode>.Add` 的哈希与相等比较；
- 每次调用的两个容器分配。

### 3.2 去重语义的等价保持

旧实现用 `HashSet<NLCPGNode> seen` 在**单次调用内**去重，跨调用重建。新实现用 `bool[] _seen`（长度 = 定义数）+ `List<int> _touched` 记录本轮真正置位的序数，`TryGetCandidates` 返回前**只回滚 `_touched` 中的条目**。

- 无需整表清零（否则每次 O(定义数)，反而比哈希更贵）；
- 回滚后 `_seen` 全 `false`，与"每次新建 `HashSet`"语义**逐位等价**。

### 3.3 元素顺序的等价保持

四个索引的遍历顺序不变；`_touched`/`_matches` 按追加顺序记录。因此 `matches` 的**元素顺序与旧实现完全一致**（旧实现也是先 `_byLocation`、再 `_byRoot`、`_byBase`、`_byBaseAndPath`，各自按 `List` 的插入序）。

`_matches` 由每次调用新建改为复用同一缓冲；调用方 `AnalyzeCfgSensitivePartition` 在**同一次调用返回后立即消费完毕**才发起下一次调用（`DataFlowPass.cs:749-758`），因此复用安全。

### 3.4 改动清单

`DataFlowPass.cs`：

1. 四个索引 `Dictionary<string, List<NLCPGNode>>` → `Dictionary<string, List<int>>`（存定义序数）。
2. `DefinitionFactIndex` 构造函数增加 `flowNodeOrdinals` / `definitionOrdinals` / `definitionNodes` 三个参数，在建索引时完成 ①② 判定并结晶为 `int`。
3. `TryGetCandidates` 去掉 `flowNodeOrdinals` / `definitionOrdinals` 两个参数（索引已内化），改用复用的 `_matches` / `_seen` / `_touched`。
4. `AddReachable` 由 `static` 改为实例方法，遍历 `int` 序数。
5. 调用点（`:745` 构造、`:749-758` 调用）同步更新。

### 3.5 为什么这是"消除无效开销"而非"语义变更"

新实现在建索引期 `continue` 掉索引项，其条件与旧实现扫描期的 ①②**字面同源**（同一 `flowNodeOrdinals` / `definitionOrdinals`）。区别仅是**执行时机**：从"每次扫描每元素"提前到"建索引每元素一次"。索引项来自 `factsByNode`，而 `factsByNode` 在构造索引前**不再变动**（`:588-618` 已填充完毕），故提前判定安全。

## 4. 验证证据

### 4.1 构建

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj      # → 0 个错误
```

### 4.2 输出等价（改造前/后实测对照）

基线 = `Build/verify-pass-optimization/defindex-breakdown-npc.log`（改造前）；
对照 = `Build/verify-pass-optimization/n0-after-npc.log`（改造后）。同一探针、同一参数（`nodom 8 16`）。

| 观测量 | 改造前 | 改造后 | 判定 |
| --- | ---: | ---: | --- |
| 节点数 | 1,599,506 | 1,599,506 | 一致 |
| 数据流方法数 | 373 | 373 | 一致 |
| `FlowNodeCount` 合计 | 391,840 | 391,840 | 一致 |
| `WorklistIterations` 合计 | 391,840 | 391,840 | 一致 |
| `RawCandidateCount` 合计 | 40,573 | 40,573 | 一致 |
| `UniqueCandidateCount` 合计 | 32,622 | 32,622 | 一致 |
| `DefinitionCount` 合计 | 27,510 | 27,510 | 一致 |
| 溢出方法数 | 0 | 0 | 一致 |

其中 `RawCandidateCount` / `UniqueCandidateCount` **正是本改动所影响子系统（候选生成）的输出**，逐项相等是本改动"未改变候选集语义"的直接证据；`WorklistIterations` 相等进一步说明 fixpoint 未受影响。

### 4.2.1 图指纹（**仅改造后测得，作为后续回归基线**）

改造后新增了与顺序无关的图指纹（对节点与边的 `NodeId`/`Kind`/`SourceNodeId`/`TargetNodeId` 做异或哈希）：

| 观测量 | 改造后实测 |
| --- | ---: |
| 边数 | 7,756,984 |
| 图指纹（顺序无关） | `65F33302D3D8F6C3` |

> **诚实边界**：改造前的边数与图指纹**未采集**（探针在该轮才加入该输出，对照运行因故中断）。因此上表**不构成改造前/后对照**，只作为后续回归比对的基线。改造前的等价性证据以 §4.2 的 DataFlow 指标为准。

### 4.3 核心测试

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~DataFlow|FullyQualifiedName~SparseSet|FullyQualifiedName~Bitset"
# → 已通过! - 失败: 0，通过: 32，已跳过: 0，总计: 32
```

### 4.4 性能

| 观测 | 改造前 | 改造后 | 加速比 |
| --- | ---: | ---: | ---: |
| `DataFlowPass` stage 墙钟 | 43,156 ms | 7,242 ms | **5.96×** |
| DataFlow workBatch `Processing` 合计（与机器负载无关） | 60,059 ms | 16,758 ms | **3.58×** |

注：`FreezeQueryIndex` 同期由 89,843 → 68,937 ms，**非本改动所致**（`NLCPGGraphIndex.cs` 由另一工作流维护，改动前 50 分钟仍被写入）。本改动只触及 `DataFlowPass.cs`。

## 5. 风险与未验证边界

- 未在**真实 967 文件语料**上复测（只测 NPC.cs 单文件与 Mount.cs 对照）。
- `FreezeQueryIndex` 的并行化（N3）可能改变 stage 间相对占比，使本项收益被稀释（但绝对值不变）。
- 未验证多 DOP 下的一致性（`ContractTests` 已含 `AcrossDegreesOfParallelism` 用例并通过）。
- 长尾方法（`WorklistIterations` 最大 122,536）未单独剖析（属 N5）。

## 6. 相关

- 总体方案与十轮记录：`Build/10pass-optimization-plan.md`
- 执行计划：`Build/N0-执行计划.md`
- 性能分析组件：`目前设计/性能分析组件.md`
