# 跨过程 pass 分配削减设计（方案 B + A）

> 目标读者：接手 `RunInterproceduralDataFlowPass` 性能工作的代理。
> 本文只回答"改什么、为什么等价、怎么验证"。执行步骤见 [执行计划](2026-09-23-interprocedural-pass-alloc-reduction-execution.md)。
> 前置：`RunInterproceduralDataFlowPass` 的**全局物化 → 逐调用点流式**重构已于 2026-09-23 落地（见 `Context/progress.md` 顶部条目）。

## 0. 结论先行

两个**低风险、互相独立**的改动，目标是削减**分配总量**（不是降低驻留峰值）。**均已实施并通过验证**：

| 编号 | 改动 | 诊断依据 | 本次实测 | 风险 |
| --- | --- | --- | --- | --- |
| **B** | 消 `ToContextId()` 的**重复插值** | 该调用点 **1,061.5 MiB**（基线 329.6 → **+731.9**，全表**增幅第一**） | 调用 **814 → 444**（每条边少 1 次） | **零语义风险** |
| **A** | 消 `graph.PendingEdges` 的**全量物化** | `PendingEdge` = **272 B**；`Materialize` **641.4 MiB**（基线 0，新路径第一大） | `Materialize` **10 → 5** 次；本 pass 那份 922 元素数组**归零** | 低（同序枚举） |

**收益上界合计约 1.37 GiB**（按各自诊断口径相加，非同一时间点，**不可当作峰值下降量**）。

**必须诚实声明**：
- 这两项削减的是**分配 churn**，**不能**声称降低内存峰值或消除 LOH。
- `PendingEdge[]` 单数组 LOH 阈值 = `ceil(85000/272)` = **313 个元素**即破 ⇒ 改后**仍会进 LOH**，只是不再额外多分配一整份。
- 641.4 MiB ÷ 272 B ≈ **2.47 M 元素**，与"百万级"量级一致。

## 1. 现状：两个分配点

### 1.1 B —— `ToContextId()` 每条边插值两次

`NLCPGCallSiteContext.ToContextId()`（`src/NLCPG/Model/NLCPGCallSiteContext.cs:11-15`）每次调用都新拼一个字符串：

```csharp
return new NLCPGContextId($"callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}");
```

而 `NLCPGEdge` 的构造器（`src/NLCPG/Model/NLCPGEdge.cs:9-26`）**自己也会算一次**，用途只是**校验一致性**：

```csharp
var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
if (callSiteContext.HasValue && contextId.HasValue && contextId.Value != resolvedContextId)
{
    throw new ArgumentException("CallSiteContext must derive the same ContextId when both are provided.");
}
```

于是 `PublishCallSitePlans`（`NLCPGBuilder.cs:942-949`）里：

```csharp
var callSiteContext = BuildPendingNodeCallSiteContext(graph, plan.CallSiteNode);
graph.AddEdge(..., callSiteContext.ToContextId(), callSiteContext);   // 第 1 次插值
//   AddEdge -> _pendingEdges.Add(...)
//   后续 Materialize 时再 new PendingEdge(...)，
//   若走 NLCPGEdge 构造器则第 2 次插值
```

**关键**：`AddEdge` 本身**不**构造 `NLCPGEdge`（`NLCPGGraph.cs:327-344` 只把参数存进 `_pendingEdges`）。所以真正的第二次插值发生在**消费侧**——`Edges` 物化时经 `NLCPGEdge` 构造器（`NLCPGGraph.cs:781-787`）再次调用。

⇒ 同一 `CallSiteContext` 在 **`NLCPGBuilder.cs:948`** 与 **`NLCPGGraph.cs:786` 那条 `new NLCPGEdge(...)`** 各插值一次。

### 1.2 A —— `PendingEdges` 属性每次都物化全量数组

`NLCPGGraph.cs:50`：

```csharp
internal IReadOnlyCollection<PendingEdge> PendingEdges => _pendingEdges.Materialize(_nodesByOrdinal);
```

`Materialize`（`NLCPGGraph.cs:902-922`）**无条件 `new PendingEdge[_keys.Count]`**：

```csharp
var pendingEdges = new PendingEdge[_keys.Count];
foreach (var key in _keys) { ... pendingEdges[index] = new PendingEdge(...); index += 1; }
```

而 `RunInterproceduralDataFlowPass`（`NLCPGBuilder.cs:726`）**只是建 4 个索引**就遍历了这个属性：

```csharp
foreach (var edge in graph.PendingEdges)   // ← 全量物化一份 PendingEdge[]
```

**全局仅此一处使用 `graph.PendingEdges`**（已用 ripgrep 核实：`src` + `tests` 中除 `facts.PendingEdges`（`MutableGraphFacts`，另一条路径）外只有 `NLCPGBuilder.cs:726`）。
`PendingEdge` 实测 **272 B** ⇒ 这一行按实测 641.4 MiB 的量级分配一个临时大数组，**用完即弃**。

## 2. 设计

### 2.1 B 的实现选择：让短路生效，而不是删除计算

`NLCPGEdge` 构造器是**唯一**的 `ContextId` 权威来源，且它已有一个 `??` 短路：

```csharp
var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
```

只要**产出侧不再预先传 `contextId`**，这一次插值就只发生在构造器里，**总计 1 次**（当前是 2 次）。

⚠ **不可**删掉构造器里的 `ToContextId()` —— 它是 `contextId: null` 时的**唯一**来源，删掉会让所有只传 `callSiteContext` 的调用点（`CpgFrozenShardGraphReader.cs`、`SkeletonShardPublisher.cs:389-397`、`CpgEdgeCandidate.cs:15-24`、各 `*Pass.cs`）丢失 `ContextId`。

⚠ **不可**改成"先比较再算"来省——校验块 `contextId.Value != resolvedContextId` 本身就要算出 `resolvedContextId`，省不掉。

**落地方式**：把本 pass 的两处产出点（`NLCPGBuilder.cs:948`、`NLCPGBuilder.cs:1099`）从

```csharp
graph.AddEdge(..., callSiteContext.ToContextId(), callSiteContext);
```

改为

```csharp
graph.AddEdge(..., callSiteContext: callSiteContext);
```

即**去掉预计算的 `contextId` 实参，改为具名传 `callSiteContext`**。

**为什么值不变**：`AddEdge` 只把参数存进 `_pendingEdges`（`NLCPGGraph.cs:337-343`），不在那里构造 `NLCPGEdge`。`PendingEdge.ContextId` 变为 `null`，但 `CallSiteContext` 仍在；直到消费侧经 `CpgEdgeCandidate`（`SkeletonShardPublisher.cs:389-398`，`ContextId` 原样带 `null`）进 `NLCPGEdge` 构造器时，由 `callSiteContext?.ToContextId()` **算出同一个值**。⇒ 最终 `NLCPGEdge.ContextId` 逐字节相同。

**副作用（有意）**：产出侧不一致的 `contextId` 不再被提前发现。但本 pass 的调用点传的就是 `callSiteContext.ToContextId()`，**恒等**，故该校验在此路径上从未触发过，删除不损失保护。

### 2.2 A 的实现选择

给 `PendingEdgeBuffer` 加**惰性枚举**，让索引直接读 `PendingEdgeKey`，不构造 `PendingEdge` 数组。

**等价性关键证据（已核实）**：`Materialize` 的枚举顺序就是 `_keys` 的 `HashSet` 枚举顺序（`NLCPGGraph.cs:906-907` 注释亦如此声明）。惰性枚举**同序** ⇒ 4 个索引的构建顺序不变 ⇒ 后续所有排序/去重结果不变。

落地时把 `NLCPGGraph.PendingEdges` 属性**替换**为 `EnumeratePendingEdgesLazily()` 方法：改完唯一调用点后，该属性在全仓已无调用者，且它**每次访问都全量物化**（`NLCPGGraph.cs:50`），留着就是把刚消除的隐患重新挂回门口，故删除。

⚠ **保留** `Materialize` 本身（`SnapshotMutableFacts`、`RemapToNodeIds`、`SkeletonShardPublisher` 仍在用），只让索引构建换用惰性视图。

### 2.3 为什么不动排序键（明确排除）

`NodeSortKey` 零分配化（`docs/plans/2026-09-23-zero-allocation-key-design.md`）虽收益更高（实测 **1,864.4 MiB / 14.16%**），但它要求**逐字符等价**（`'|'`=0x7C 参与比较）且**不能**把 `OrderBy` 换成不稳定的 `Array.Sort`，风险与验证成本显著更高。本次**不做**，留给后续独立批次。

## 3. 语义不变性论证

1. **B**：`ContextId` 的**值**由 `FilePath/SpanStart/SpanEnd/DisplayName` 唯一决定，故"算一次后用同一值"与"算两次"的**结果值相同**。唯一被消除的是重复的**临时字符串**。
2. **A**：索引构建只读 `edge.Kind`/`SourceNode`/`TargetNode`，不依赖 `PendingEdge` 的物化身份；`_keys` 枚举序稳定（注释与实测均如此）⇒ 索引内容与顺序逐项相同。
3. **边序**：`AddEdge` 依旧 append-only；`NLCPGGraphIndex.Create` 仍对全部边做规范排序（`SourceNodeId/Kind/TargetNodeId/StructuredLabel?.StableKey/ContextId?.Value/CallSiteContext?.FilePath/SpanStart/SpanEnd/DisplayName`）⇒ **快照指纹不受影响**。
4. **不变量**：图索引字节喂 SHA256 指纹（`CreateSnapshotVersion`）⇒ 任何**值**变化都被禁止；本设计只删冗余计算，不改值。

## 4. 不变量（实施期必须保持）

- `NLCPGEdge.ContextId` 在 `callSiteContext` 存在时**仍等于** `callSiteContext.ToContextId()`。
- `NLCPGEdge` 构造器的**不一致抛错**行为保留（`ArgumentException`）。
- `PendingEdgeBuffer.Materialize` 的**语义与顺序**不变。
- `graph.PendingEdges` 属性本身**不删**（`MutableGraphFacts` 与 `RemapToNodeIds` 仍依赖 `Materialize`）。

## 5. 已知风险

| 风险 | 说明 | 缓解 |
| --- | --- | --- |
| 惰性枚举跨 `AddEdge` 失效 | `_keys` 是 `HashSet`，若枚举期间被修改会抛 `InvalidOperationException` | `RunInterproceduralDataFlowPass` 在建索引阶段**不**调 `AddEdge`（`_interproceduralBarrierCompleted = true` 在循环前设置，索引循环在发布之前）⇒ 需在实现后确认 |
| 缓存 `ContextId` 引入陈旧值 | `NLCPGCallSiteContext` 是 `readonly record struct`，值相等则 `ContextId` 必相等 | 用 `record struct` 的值语义，无陈旧风险 |
| 漏改消费侧 | 只改产生侧则收益减半 | 用分配探针复测 `ToContextId` 调用次数（见执行计划 §5） |

## 6. 明确不做的事

- 不改 `NodeSortKey`（见 §2.3）。
- 不启用 `DataFlowOptions` 预算：`NLCPGBuilder.cs:664-666` 把 `MaxDefinitionsPerMethod` 等写进 `fingerprintInput`，**会改持久化指纹**，属产品决策。
- 不改 `NLCPGBuilder.cs:949` 之外的摘要路径（`AddExternalSummaryMappings` 的 `context.ToContextId()`，`NLCPGBuilder.cs:1099`）——除非探针显示它同量级。

## 7. 验证边界（诚实声明）

- **已实施并通过验证**（2026-09-23）。执行细节与**实测调用次数**见[执行计划](2026-09-23-interprocedural-pass-alloc-reduction-execution.md)。
- ✅ **已实测（计数，线性代理指标）**：B 令 `ToContextId` 调用 **814 → 444**（差值 **370 = 恰好每条边 1 次**）；A 令 `Materialize` 调用 **10 → 5**、本 pass 那份 922 元素全量数组**归零**。
- ⚠ **未实测**：**真实 71 s 快照上的字节收益**。本文的 MiB 数字（B 1,061.5 MiB、A 641.4 MiB）来自**既有诊断运行**，是**收益上界**而非本次复现值；本次只有**调用次数**这一线性代理指标。§0 的"合计约 1.37 GiB"亦按此口径理解，**不可当作峰值下降量**。
- ⚠ **不声称**降低峰值或消除 LOH：`PendingEdge[]` 单数组 LOH 阈值 = `ceil(85000/272)` = **313 元素**即破，改后**仍然进 LOH**；A 消除的是"额外多分配一整份"，B 消除的是重复临时字符串。
- ⚠ **A 的运行时前提未被强制**：`EnumerateLazily` 要求"枚举期间不改图"。已按代码路径确认为真（索引循环在发布之前），但**没有运行时断言**；若将来有人在索引循环中插入 `AddEdge`，会抛 `InvalidOperationException` 而非静默出错（可接受，但需知悉）。
- ⚠ **A1 的惰性收益在单文件规模下不可见**：本仓测试语料每次构图仅 ~900 条 pending 边，故 A 的实测收益体现为**调用次数减半**而非可观字节；其字节量级只在生产大工程（967 个 `.cs` 文件、DOP 12）才显现。

## 8. 参考

- `Context/progress.md` —— 本 pass 的根因与分配归因（`ToContextId` +731.9 MiB、`PendingEdgeBuffer.Materialize` +641.4 MiB、`NodeSortKey` 1,864.4 MiB）。
- `docs/plans/2026-09-23-zero-allocation-key-design.md` —— `NodeSortKey` 零分配方案（本次不做）。
- `docs/plans/2026-09-23-edge-payload-ordinalization-design.md` —— 边载荷序数化（`PendingEdgeBuffer` 的来源）。
- `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs` —— **边序 oracle**，冻结原始插入序，是本改动的守门测试。
- .NET GC 设计（LOH 85,000 B 阈值）：`raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/garbage-collection.md`（Maoni Stephens）—— 原文 "Based on the size, the GC divides objects into 2 categories: small objects (< 85,000 bytes) and large objects (>= 85,000 bytes)."
