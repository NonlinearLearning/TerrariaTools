# P0 跨调用点复用 `ArgumentToParameter` 计划段执行计划

**Goal:** 把跨过程桥计划的 `ArgumentToParameter` 段从「每个调用点**各分配一份**整桶载体」改为「按目标方法**分配一次**、各调用点复用」，使该段的**计划载体分配总量**从 `c²·p` 条降到 `c·p` 条（省因子 `c`），同时保持 `GraphSnapshotVersion`、边集、冻结边序、**计划条数**、三种桥的插入顺序与全部预算/门控语义**逐位不变**。

> ⚠️ 注意"条数"的两个不同含义（详见 §2.5）：**遍历/发布/建行的计划条数不变**（每组仍 `c·p`），
> 变的是**同时驻留的载体份数**。本项是**纯内存（累计分配）优化**，不改边数也不改行数。

**Architecture:** 组的计划存储由一个 `InterproceduralPlanBuffer` 改为「**共享的 `ArgumentToParameter` 前缀** + **本组私有的尾段**」两段式。共享前缀按 `targetMethodSymbolKey` 缓存在**本 pass 内**，构造一次后只读；尾段（`MethodReturnToCallResult` + 首个调用点的 `ReturnToMethodReturn`）仍逐调用点构造。发布时仍逐调用点传各自的 `callSiteContext`，故 `AddEdge` 调用序列不变。

**Tech Stack:** C#/.NET 10、`NLCPG.Builder`、xUnit（ContractTests）。

日期：2026-09-28（计划）。

> 📌 **2026-09-28 状态更新：本文已按用户指示【执行】。**
> 执行时同样**不做任何前置验证**（无基线、无 A/B、无探针、无变异测试），只做 §6.1 的 **10% 核心验收**。
> 产品代码改动落在 `InterproceduralDataFlowPlanGroup.cs` 与 `NLCPGBuilder.cs`；测试侧只有 §5.1 预告的
> 那 1 处断言（组属性集形状）需要改，另按 §5.3 补了 N1–N5 护栏。
> §6.2 聚焦过滤器实测 **116 总数 / 115 通过 / 1 失败**，唯一失败是**既有、已记载**的调用点 span 漂移
> （`CpgInterproceduralEdgeOrderTests`，期望 `156:167`、实际 `167:178`，见 `Context/progress.md:27`）；
> §5.4 全部冻结 oracle 逐条按名复核通过。**本轮仍未做任何计时与内存测量** ——
> 下面 §2 的收益量依旧全部是**引用既有实测或算术推算**，不是本轮实测。
> §4.4.1 的口径抉择**已在实施时裁决并落地为口径 ②**（保留载体口径），见 §4.4.1 末尾的执行注记。
>
> 📌 **本文初稿性质（保留存档）：只写执行文档。不改产品代码、不跑构建、不跑测试、不做任何前置测试、不新增探针。**
>
> **据以执行的上游文档**：[M8 静态审查](../../Build/MemoryOptimization/M8/STATIC-REVIEW-InterproceduralDataFlowPlan.md) §2.5 与 §6 P0
> （含 2026-09-26 的两次订正），以及 [⑥ 延迟物化执行文档](2026-09-27-interprocedural-plan-late-materialization-execution.md)。

> 📌 **行号基准**：`src/NLCPG/Builder/NLCPGBuilder.cs` 当前 **4,647 行**。
> 该文件正被并发的内存优化会话反复改写（历史 4,497 → 4,503 → 4,514 → 4,647），
> 故下列引用**一律同时给出符号名**，行号漂移时以**符号名**为准。

---

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 复用是否安全（语义）？ | **安全** —— 计划载体不含任何调用点字段，调用点区分发生在**发布时**（`callSiteContext`）；依据是结构恒等式，不依赖语料 |
| 收益多大量级？ | **仅载体存储** `c²p → c·p`：63.2% 语料上 **≈8.61 GiB 累计分配**（既有探针实测，@216 B 口径），复用因子 **454.5** |
| 与已落地的 ⑥（宽度 216→8 B）关系？ | **正交且不可相加**：⑥ 去**宽度**（27×，实测 −14.79%），本项去**份数**（因子 `c`）。单独做本项略大（8.61 vs 8.31 GiB） |
| 收益**不**体现在哪里？ | **行数与边数逐位不变**：每组仍须重放 `c·p` 条计划、发 `c·p` 条边（§2.5）。故 `PeakWindowRows`、单组行数、边数**都不下降** |
| 本项最大风险是什么？ | **不是**边序，而是 ① `PlanIndex` 的组内插入序绑定；② 账本口径重定义后 `PlanInitialCapacity*` 判别力被稀释（§4.4） |
| 能否不改测试就通过？ | **不能**。至少 1 条断言必红（组属性集，§5.1）；账本口径若改为"仅尾段"则另有多条必红或变空（§5.2） |
| 本轮验收门槛？ | 按用户指示：**只做 10% 核心测试，不做任何前置测试**。故正确性只由**仓库既有冻结 oracle** 承担（§6） |

---

## 1. 目标表示

### 1.1 现状：一段可复用、两段不可复用

一组（= 一个调用点）的计划由三段拼接进**同一个** `callSitePlans`（`NLCPGBuilder.cs` 的
`RunInterproceduralDataFlowPassForDocument`）：

| 段 | `BridgeKind` | 产生位置 | 数据来源 | 段内 `TargetNode` | 跨调用点 |
| --- | --- | --- | --- | --- | --- |
| **1** | `ArgumentToParameter` | `:2845-2857` | `argumentsByMethod[targetMethodSymbolKey]` | 目标方法的**形参节点**（全图唯一） | **逐条相同 ⇒ 可复用** |
| 2 | `MethodReturnToCallResult` | `:2859-2876` | `DataFlowForTarget(callSite)` | **本调用点**节点 | 每站点不同 |
| 3 | `ReturnToMethodReturn` | `:2880-2895` | `returnsByMethod[targetMethodSymbolKey]` | 目标方法的**返回节点** | 仅**首个**调用点有 |

段 1 是唯一可复用的：它的循环体（`:2853-2856`）

```csharp
var edge = edgeSnapshot.Edge(ordinal);
callSitePlans.Add(new InterproceduralPlanRef(
  ordinal,
  ParseArgumentOrdinal(edge.TargetNode)));
```

**只读池与池内边**，**完全不读 `callSite`**。段 2/3 分别依赖 `callSite` 与跨迭代门控，**不可复用**。

### 1.2 为什么"重复"不能靠删边解决（必须先讲清，否则会设计错）

`c` 倍重复**在最终图里也存在**，不是可删的冗余。本项**已直接重读源码核实**该链条
（不依赖 M8 文档的行号）：

```csharp
// NLCPGGraph.cs:1177-1179（PendingEdgeBuffer.Add）
var metadataId = InternMetadata(structuredLabel, contextId, callSiteContext);
_keys.Add(new PendingEdgeKey(sourceOrdinal, targetOrdinal, kind, metadataId));

// NLCPGGraph.cs:1331-1335
private readonly record struct PendingEdgeKey(
  int SourceOrdinal, int TargetOrdinal, NLCPGEdgeKind Kind, int MetadataId);

// NLCPGGraph.cs:1229 / 1238（InternMetadata）
var candidate = new EdgeMetadata(structuredLabel, contextId, callSiteContext);
var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
```

`EdgeMetadata` 是 `record class`，`Equals` 按值比较
（`NLCPGGraph.cs:1340-1343`）⇒ **`callSiteContext` 不同 ⇒ `MetadataId` 不同
⇒ 两条边都存活**。冻结 oracle 证实：`Leaf` 的段 1 是 **5 source × 5 span = 25 条边**
（`CpgInterproceduralEdgeOrderTests.cs:67-96`），全部出现在最终边集。

⇒ 可安全复用的是**计划载体**（中间表示），**不是边**。这是本项全部收益的来源，
也是"边集不变"这一验收判据成立的原因。

### 1.3 目标结构

把组头的单缓冲改为**两段视图**：

```csharp
internal readonly struct InterproceduralDataFlowPlanGroup(
  NLCPGNode callSiteNode,
  int stableCallSiteOrder,
  InterproceduralPlanBuffer sharedArgumentPlans,   // 按目标方法共享，只读
  InterproceduralPlanBuffer groupTailPlans)        // 本组私有（段 2 + 段 3）
{
    internal InterproceduralPlanBuffer SharedArgumentPlans { get; } = sharedArgumentPlans;
    internal InterproceduralPlanBuffer GroupTailPlans { get; } = groupTailPlans;
    internal int Count => SharedArgumentPlans.Count + GroupTailPlans.Count;
}
```

**共享前缀的构造时机**：在调用点循环**之前**（与 `:2683-2723` 的排序键预计算并列），
对 `edgeSnapshot` 里每个**有实参桶的方法键**各构造一次。
但**不得**对全部方法键无条件构造——见 §3.3 的懒惰构造约束（否则短命构建会更慢）。

### 1.4 排序键全段共享 ⇒ 可整段缓存（本项最有利的结构事实）

`PlanSortRowComparer.Compare`（`:3309-3340`）的键序为：

```
BridgeKind → ArgumentOrdinal → SourceKey → TargetKey → PlanIndex
```

`PlanSortRow` 由 `BuildAndSortPlanRows`（`:3258-3263`）构造，其**前四个键全部是计划的
内生属性**（由 `PoolOrdinal` 经池导出），**只有末键 `PlanIndex` 是组内下标**。

⇒ 共享前缀的**四个排序键可随段一并缓存**，各组只需重算末键。
这是本项在 ② 段上**唯一**的 CPU 收益：每方法算一次键，而非每调用点算一次。
⚠️ **但行数不变（仍 `c·p`）**，故 ② 的**建行次数**与 ③ `List.Sort` 的**比较次数不变**；
本文**不**声称"②③ 两段工作量一起降下来"（初稿曾如此表述，已订正，见 §2.5）。
相关两段占发布段 **48.4%** 的既有读数（`Context/progress.md` 轮次 30）**不适用于**本项收益推算。

---

## 2. 收益（引用既有实测 + 算术，**本轮不新测**）

### 2.1 已实测的规模量（既有探针，非本轮）

来源：`Build/MemoryOptimization/M8/p1-probe/`，真语料抽样 **63.2%**（612/968 文件），
8 样本 × 80 文件等距旋转，全量 968 个语法树共用一份 compilation。
M8 审查 §6 P1 记载该探针 `GroupIdentityViolations = 0`（同一方法的各调用点重放的
`(实参, 形参)` 对集合 **`SetEquals`**），即 §0 那条**恒等式在真语料上成立**。

| 量 | 实测值 |
| --- | ---: |
| `ArgumentToParameter` 计划（=边） | **42,907,193** |
| 占全部跨过程边 | **99.89%** |
| 去重后桶大小 `Σ(c·p)`（复用后需保留的载体量） | **94,400** |
| **复用因子 `Σ(c²p)/Σ(cp)`** | **454.5** |
| `c` 分布 | 1 … **1262**（长尾）；`c==1` 占 51.8%–69.7% |
| `p` 分布 | 1 … 11（多数 1–2） |
| 单方法最大 `c²p` | **6,370,576**（`Terraria.Item.sellPrice`，c=1262, p=4） |
| 收益集中度 | **Top-10 方法占 84.0%–99.8%** |

单文件交叉校验（`Build/AB-8B-vs-216B/probe-single/summary.json`，`Item.cs`）：
`emitted = 9,679,406`、`reusable = 10,898`、`ReuseFactor = 888.2`、
`emitted = c×bucket = 1262×5048 = 6,370,576` ✓（`sellPrice`）。

### 2.2 载体收益算式

```
可省计划载体 = 42,907,193 − 94,400 = 42,812,793 条
@8 B（⑥ 已落地的载体宽度）= 42,812,793 × 8 B ≈ 343 MB   ← 本项在【当前工作树】上的增量
@216 B（⑥ 之前）           = 42,812,793 × 216 B ≈ 8.61 GiB
```

⚠️ **必须写清分母**：8.61 GiB 是 **⑥ 之前**的算术；⑥ 已把宽度降到 8 B，
在**当前工作树**上本项的增量是 **≈343 MB 累计分配**。
**两个数字不可混用**，也不可与 ⑥ 的实测 −14.79%（§2.4）相加。

### 2.3 为什么仍值得做（尽管增量数字变小）

1. **⑥ 的实测收益 −14.79% 是在"份数不变"前提下取得的**，而份数才是 `c²` 项。
   两者相乘，不是相加：`c²p × 216 B → c·p × 8 B` 相对原始是 **≈12,000×** 的载荷缩减
   （⑥ 文档 §6.3 已列该组合，标注为"~12,000×"）。
2. **窗口峰值行数不变**（**本轮订正，撤回初稿判断**）：每组仍须枚举整桶
   （`argumentsByMethod[target]`，大小 `c·p`）并为每条计划建一行、发一条边。
   实测单组最大 **5,050 行**（源码注释 5,878）**改造后不变**，
   行上界冲刷路径**照旧被触发**。详见 §2.5。
3. 段的**排序键可整段缓存**（§1.4），使 ② 段的**键计算**（`BridgeKindOf` + 两次
   `CachedNodeSortKey` 查表）由"逐调用点重算"变为"每方法算一次"。
   ⚠ 但**行的条数不变**（仍 `c·p`），故 ② 的构造次数与 ③ 的 `List.Sort` 比较次数不变（§2.5）。

### 2.4 与 ⑥ 的实测关系（口径纪律）

⑥ 的实测（`Build/AB-8B-vs-216B/RESULT.md`）：单文件 `Item.cs`、DOP=4、
累计分配 **13.607 GB → 11.595 GB = −2.012 GB（−14.79%）**，
与"9,683,025 条 × 208 B"预测吻合 **99.905%**。

⇒ 该实测**已经把"每条计划省 208 B"兑现过一遍**。本项省的是**同时驻留的载体份数**，
故**不能**拿 −14.79% 与本项叠加，也不能拿它预测本项。

### 2.5 ⚠️ 边界：本项**不减少**计划条数、行数、边数（**本轮订正，撤回初稿判断**）

初稿曾断言"复用后单组行数降到 `p + O(1)`，行上界冲刷消失"。**该判断错误**，
现依据源码逐条订正：

| 量 | 改造后 | 依据 |
| --- | --- | --- |
| 每组的计划条数 | **不变**，仍为桶大小 `c·p` | `:2845-2857` 仍逐调用点遍历整桶；共享的是**载体数组**，不是**遍历** |
| `callSitePlans.Count` | **不变** | 发布/窗口都读它 ⇒ `windowRowCount += callSitePlans.Count`（`:2948`）不变 |
| `PeakWindowRows` / 单组行数 | **不变**（实测单组最大仍 **5,050**） | `BuildAndSortPlanRows` 仍按 `callSitePlans.Count` 建行（`:3238`） |
| 边数 / `AddEdge` 序列 / 边序 | **不变**（设计目标） | 发布仍 `foreach (var row in rows)` 逐条发（`:3084-3096`） |
| 每行 `PlanSortRow`（32 B） | **不变** | 行布局冻结（`:3298-3303`） |
| **载体分配总量（累计）** | **`c²p → c·p`（本项唯一收益）** | 原每个组各分配一份 `c·p` 载体数组；改造后按方法共享，只需 `c·p`。**这才是 §2.2 的 343 MB / 8.61 GiB 的口径** |

**为什么条数不能省**：最终图里同一 `ArgumentToParameter` 关系按调用点各留一条边
（`callSiteContext` 不同 ⇒ `MetadataId` 不同 ⇒ 下游不去重，§1.2），
而"每组各发一遍"正是产生这 `c` 份边的机制。省掉遍历就会**少发边**，
`HaveStableTotalCount`（`:165-174`，74 条）会立刻红。
⇒ 复用的对象**只能是中间载体**，这正是 §1.2 的结论。

**对本项收益口径的后果**：
- §2.2 的 343 MB / 8.61 GiB 是**唯一**成立的口径（载体宽度 × 份数），**保持**；
- 任何"行数下降 / 窗口峰值下降 / 排序变快 / 边数下降"的说法**均不成立**，本文已全部删除；
- 这也意味着 **§5.2 初稿列出的"行上界夹具失准"风险不存在**：夹具照旧越界，
  `WindowRowBound_FiresBeforeGroupBound_WhenOneGroupIsWide` 等三条**应原样通过**。
  初稿所称"没有现成答案的替代夹具设计"（§7、§8.7）**随之取消**。

---

## 3. 不变条件（改动不得违反）

以下每一条都必须由 §6 的验收判据覆盖，否则视为未交付。

1. **边集与边序逐位不变**：`AddEdge` 调用序列不变 ⇒
   `CpgInterproceduralEdgeOrderTests.ExpectedInterproceduralOrder`（`:65-141`，**74 条**冻结序）
   与 `GraphSnapshotVersion` 逐字节不变。
2. **段内次序不变**：段 1 的桶遍历序（`argumentsByMethod` 的填充序，即池内序号的插入序）
   必须原样保留；共享前缀**不得重排**。
3. **`PlanIndex` 语义不变**：它必须仍是**该组内**的原始插入序（见 §4.1 的设计约束），
   因为它同时是 `PlanSortRowComparer` 的**末键**（`:3338-3339`），
   而 `List<T>.Sort` 是**不稳定**排序 ⇒ 末键必须构成**严格全序**才能保证组内顺序确定。
4. **`recordedReturnMethods` 门控语义不变**：`recordedReturnMethods.Add(...)`（`:2880`）
   必须仍在**原串行位置**执行，且**不受预算影响**（即溢出时也必须更新）。
   生命周期仍是**整个 pass**，不得移进 worker 或窗口局部。
5. **预算口径不变**：`MaxBoundaryEdgesPerMethod`（默认 10000）仍截取**排序前**的原始计划前缀；
   保留三种桥的插入顺序与 `BoundaryEdgeBudget` 截断事件。
   非正预算的既有异常契约**逐位保留**（见 §3.2）。
6. **`BridgeKind` 仍由池内边导出**（`BridgeKindOf`，`:3499-3509`），
   不得塞回载体。载体字段集仍恰为 `{PoolOrdinal, ArgumentOrdinal}`（护住 §5.1 的形状断言）。
7. **不引入第二份真相**：复用缓存必须是**纯函数结果**（只依赖与本 pass 内只读的池），
   不得依赖"当前已合并了多少批"这类读取时刻状态。
   （这是 CallGraph 非确定性事故的同型风险，见 `Context/progress.md` 顶部条目。）

### 3.1 逐文档作用域（易错点）

`RunInterproceduralDataFlowPass`（`:2614-2622`）**逐文档**执行，
而 `edgeSnapshot`、`nodeSortKeys`、`recordedReturnMethods`、`capacityLedger`
**全部在 `...ForDocument` 内新建**（`:2648-2763`）。

⇒ 共享前缀缓存**必须建在 `...ForDocument` 内**（与 `edgeSnapshot` 同生命周期）。
若提到 builder 字段，会跨文档复用**属于别张图**的池内序号 ⇒ **静默错边**。
这是本项最严重的一类错误，且不会抛异常（序号在池内仍合法，只是指向另一张图的边）。

### 3.2 非正预算兼容分支必须逐位复刻

实测契约（`Build/MemoryOptimization/M1/run-20260925-01/probe.txt` 冻结，
由 `InterproceduralPlanCompactionTests.NonPositiveBudget_KeepsFrozenOutOfRangeContract:302-314` 断言）：

```
int.MinValue / -1 → ArgumentOutOfRangeException（RemoveRange 取负长度）
0                 → ArgumentOutOfRangeException（发布器读 plans[0]）
```

`ParamName == "index"`。现值由**两处**产生：
- `RemoveRange(index, count)`（`:2935-2937`）在 `InterproceduralPlanBuffer.RemoveRange`（`:81-105`）内抛；
- 预算 0 时**整组被删空** ⇒ 空组进入窗口 ⇒ 发布器 `:3076-3081` 显式抛。

⚠️ **两段式改造会直接威胁第二条路径**：若共享前缀与私有尾段分开判空，
"段 1 非空但尾段为空"或反之的组可能**不再产生空组**，使预算 0 从"抛异常"
**静默变成"成功且少发边"**——那是无人察觉的契约变更。
⇒ `Count == 0` 的判空（`:2897`）必须对**两段之和**判定，且 `:3076` 的抛出行必须保留。

### 3.3 懒惰构造（否则短命构建会变慢）

若在循环前**无条件**为所有方法键构造共享前缀，则 `c == 1` 的方法（占 **51.8%–69.7%**）
会白付一次构造 + 一次缓存查找，而**零收益**（`c=1` 时 `c²p = c·p`）。
⇒ 共享前缀必须**在首次被某调用点需要时**构造（记忆化），或按 `c ≥ 2` 预筛。
这一点是"收益为正"的必要条件，须在实现中显式体现并有测试护住（§5.3）。

---

## 4. 改动清单（封闭）

### 4.1 `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlanGroup.cs`（两段式组头）

| 项 | 改动 |
| --- | --- |
| `InterproceduralDataFlowPlanGroup` | 由 3 参数 `(callSiteNode, stableCallSiteOrder, plans)` 改为 4 参数 `(..., sharedArgumentPlans, groupTailPlans)`；`Count` 改为两段之和；新增 `SharedArgumentPlans` / `GroupTailPlans` |
| `InterproceduralPlanBuffer` | **不改**。复用现有的 `Create`/`Add`/`this[]`/`Count`/`Capacity`/`InitialCapacity`/`RemoveRange`/`TrimExcess` |

⚠️ **形状断言会红**：`InterproceduralPlanCompactionTests.cs:129-131` 断言属性集恰为
`{CallSiteNode, Count, Plans, StableCallSiteOrder}`。本改动会**新增两个属性、删掉 `Plans`**
⇒ 该断言必红，属**计划内**修订（§5.1 第 2 条）。

### 4.2 `src/NLCPG/Builder/NLCPGBuilder.cs`（构造 + 发布）

| 位置（符号） | 改动 |
| --- | --- |
| `RunInterproceduralDataFlowPassForDocument` 循环前（与 `:2683-2723` 排序键预计算并列） | 新增共享前缀的**记忆化容器**（如 `Dictionary<string, SharedArgumentSegment>`），键 = `targetMethodSymbolKey` |
| `:2845-2857`（段 1） | 改为**取用**共享前缀；不再逐调用点重放 |
| `:2859-2895`（段 2/3） | **不改**（仍写私有尾段缓冲） |
| `:2829-2843`（上界/`Create`/`InitialCapacity`） | 上界改为 `sharedCount + returnToCall.Length + returns.Length`；`sharedCount` 含预算截断（见 §4.3） |
| `:2897`（空组判空） | 对**两段之和**判定 |
| `:2905-2940`（账本） | 口径需重新定义（§4.4） |
| `:2944-2948`（组头构造） | 传入两段 |
| `:3076-3081`（空组抛出） | **保留原样** |
| `:3089`（`plans[row.PlanIndex]`） | 改为两段分派：`PlanIndex < sharedCount ? shared[idx] : tail[idx - sharedCount]` |
| `:3225-3270`（`BuildAndSortPlanRows`） | 两段拼接成行；共享段的四个键可**预计算缓存** |
| `:3298-3303`（`PlanSortRow`） | **不改**（行布局冻结 32 B） |

### 4.3 预算与共享前缀的交互（必须设计，不能默认）

`MaxBoundaryEdgesPerMethod` 截取的是**排序前**的原始计划前缀，且**三段共用同一个
`callSitePlans.Count >= boundaryEdgeBudget` 判据**（`:2847`/`:2869`/`:2886`）。

⇒ 共享前缀的**有效长度依赖预算**：预算 `B` 下，段 1 至多取 `min(桶大小, B)` 条。

**结论：共享前缀必须按 `(targetMethodSymbolKey, effectiveBudget)` 记忆化**，
而 `effectiveBudget` 是**全局**配置（`options.MaxBoundaryEdgesPerMethod`，一次构建内恒定）
⇒ 键实际只需 `targetMethodSymbolKey`，但实现必须**显式声明**这一依赖，
不得依赖"预算在构建内不变"这一隐式事实而不加注释。

⚠️ 另需注意：预算**按组**计，而共享前缀是**跨组**的。预算 `B` 下：
- 段 1 取 `min(桶大小, B)` 条；
- 段 2/3 随后**共用剩余额度** `B − 段1条数`。

当前代码这个顺序是**隐式**的（三段依次检查 `Count >= B`）。两段式下必须**逐位复刻**
同一顺序：先算段 1 的截断数，再把"剩余额度"交给尾段。否则 `planOverflowed`
与 `BoundaryEdgeBudget` 计数会与冻结基线不一致（§5.1 第 1 条会红）。

### 4.4 容量账本的口径重定义（可观测性不得退化）

现有账本把「组」当作单一 `InterproceduralPlanBuffer` 观测（`:2907-2910` `ObserveGroup`）。
两段式后必须明确：

| 字段 | 新口径（建议） |
| --- | --- |
| `PlanCountTotal` / `MaxPlanCountPerGroup` | ⚠ **必做取舍，见 §4.4.1**。建议改为"**保留的计划载体份数**"（共享段按方法只计一次） |
| `PlanCapacityTotal` / `MaxPlanCapacityPerGroup` | 同上取舍；与 `PlanCountTotal` **口径必须一致**，否则既有不等式会红 |
| `PlanInitialCapacityTotal` / `MaxPlanInitialCapacityPerGroup` | **仅私有尾段**（`min(上界, B)` 的判别量）；共享段**另行**记账，否则"前缀收集"的判别力被稀释 |
| 共享段（新增字段） | 按方法计一次：条数与容量各一；供 §5.3 N1/N3 断言 |

#### 4.4.1 ⚠️ 必须裁决：`PlanCountTotal` 的两种口径**互斥**

这是一个**被既有冻结断言逼出来的**设计决策，不能靠默认取值：

- **口径 ①（按组消耗）**：每组"消费"了整桶 ⇒ 每组 `c·p`，总和 `c²·p`（与改造前**相同**）。
- **口径 ②（按实际分配/保留的载体）**：共享段**按方法一份** ⇒ 总和 `≈ c·p`（体现本项收益）。

`InterproceduralPlanCompactionTests` 冻结了 `PlanCapacityTotal >= PlanCountTotal`
（`:166`、`:291`），且 `:769-770` 断言 `capacityOverCount = PlanCapacityTotal / PlanCountTotal >= 1.0`。

⇒ **两种口径不能混用**：

| 方案 | 后果 |
| --- | --- |
| `PlanCountTotal`=①（`c²p`）+ `PlanCapacityTotal`=②（`c·p`） | `PlanCapacityTotal >= PlanCountTotal` **必红**（`c·p < c²·p`） |
| 两者都=① | 不等式绿，但容量被**重复计数 `c` 倍** ⇒ 收益在账本上**完全不可观测**，且账本是**假的** |
| **两者都=②（推荐）** | 不等式仍绿（容量含 slack ⇒ 比值 `≈1.0 ≥ 1.0`）；`PlanCountTotal > 0`、预算单调性（`:220-222`）、`MaxPlanCountPerGroup <= budget` 全部仍成立；且 `PlanCountTotal` 本身**成为 N1 的收益判别量** |

**推荐口径 ②**，并要求：改口径时同步改字段注释（"每组保留计划数" → "保留载体份数"），
且**不得**为了让旧断言变绿而在 `PlanCountTotal` 里保留 `c²p`。

> ✅ **执行注记（2026-09-28）：已裁决并落地为口径 ②。**
> 一处**必须显式写出的补充**（实施中实测到的坑）：口径 ② 下共享段**仍需计入**
> `PlanCountTotal`/`PlanCapacityTotal`，不能只记进 4 个共享段专用字段。理由有二：
> ① 否则"每方法只计一次"这个本项的**全部收益**只体现在专用字段上，既有字段读起来与改造前无异；
> ② 更直接地，会读错：某个组完全可能"共享段非空 / 尾段为空"（被调方无返回类桥），
> 此时若 `PlanCountTotal` 只累尾段，这些组记 0，`PlanCountTotal > 0` 的哨兵与
> `PlanCapacityTotal >= PlanCountTotal` 的语义都被破坏。
> 落地形态：`ObserveSharedArgumentSegment(count, capacity, initialCapacity)` 内
> `PlanCountTotal += count; PlanCapacityTotal += capacity;`，**同时**累 4 个专用字段。
> 首次聚焦运行即因此暴露：`PositiveBudget_OverflowingGroups_RetainNoMoreThanBudget`
> 与 `PositiveBudget_InitialCapacityNeverExceedsBudget` 报"夹具没有产生任何计划"（`:165`），
> 补上这两行后转为全绿。

⚠️ 口径 ② 下 `MaxPlanCountPerGroup` 仍受预算约束：截断在段 1 即按 `B` 生效，
故每组保留数 `≤ B`，`:161-162` 继续通过。

⚠️ `PlanInitialCapacity*` 是「前缀收集 vs 先全建再截断」的**唯一判别量**
（`InterproceduralDataFlowPlanGroup.cs:233-236` 就地写明）。
若把共享段并入该字段，`PositiveBudget_InitialCapacityNeverExceedsBudget:182-193`
的判别力会被共享段的份额**冲淡**，甚至变成恒真。
⇒ **共享段必须单独记账**（新增字段），且 §5.3 要求新增断言护住它。

⚠️ 新增字段会使 `InterproceduralPlanCapacityLedger` 的**位置构造**与
`Empty`（`:182-183`，17 个实参）**逐参数失配** ⇒ 编译错误，
须同步更新 `Empty` 与 `ToLedger()`（`:275-292`）。

#### 4.4.2 ⚠️ 共享段的**容量**不得按组累加（重复计数陷阱）

`PlanCapacityTotal` 是**累加**量（每组各累一次）。共享段**只有一份**：
若让 `c` 个组各把同一份共享 buffer 的 `Capacity` 累加进去，
就会把它**虚增 `c` 倍**，使：

- `PlanCapacityTotal` / `PlanCountTotal`（`:769` 的 `capacityOverCount`）
  **失真**，`ControlledWindowPeakLedger` 的账目被污染；
- 更糟的是**收益被账本掩盖**：真值本应 `c·p`，账本却记 `c²·p`，
  与改造前读数**无差别** ⇒ 本项收益在账本上**不可观测**。

⇒ 共享段的容量必须**按"每个方法一份"累加**（每个共享前缀只入账一次），
或在新增字段中**按方法去重后**记账。两种口径都要在 §5.3 的 N1/N3 断言中**显式钉住**：
"共享段账本字段 ≈ `c·p`，而非 `c²·p`"。

📌 同理适用于 `PlanInitialCapacity*`：共享段的初始容量是**每方法一次**，
不是每组一次。这是它必须**独立成字段**的第二个理由（第一个是判别力稀释）。

---

## 5. 测试修订清单（分级）

### 5.1 **必红的断言**（计划内修订）

| # | 文件:行 | 逐字内容 | 级别 |
| --- | --- | --- | --- |
| 1 | `tests/NLISSN.ContractTests/Cpg/InterproceduralPlanCompactionTests.cs:129-131` | `Assert.Equal(`<br>`  new[] { "CallSiteNode", "Count", "Plans", "StableCallSiteOrder" },`<br>`  properties);` | **断言级红**（属性集改变）。这是**唯一确定必红**的一条 |

### 5.2 **会因账本口径而变红或变空**（取决于 §4.4 的取舍）

⚠️ **本轮订正**：初稿把 `CpgBuilderSources.cs:582` 的"组行数 ≈ 形参 × 调用点"定律
与三条行上界夹具列为**必红/必空**。该判断已撤回——按 §2.5，**行数不变**，
故 `InterproceduralPlanRowThresholdPressure` 的定律**仍然成立**，
`:360-381` / `:386-398` / `:401-418` 三条夹具**照旧越界，应原样通过**。

真正的风险面收窄为**容量账本口径**（§4.4）：

| # | 文件:构造/断言 | 风险 |
| --- | --- | --- |
| 2 | `InterproceduralPlanCompactionTests.cs:182-193` `PositiveBudget_InitialCapacityNeverExceedsBudget`（`PositiveBudget_OverflowingGroups_RetainNoMoreThanBudget:149-168` 同） | `PlanInitialCapacity*` 是「前缀收集 vs 先全建再截断」的**唯一判别量**。若把共享段并入该字段，判别力被**稀释甚至恒真**（§4.4）。⇒ 共享段必须**单独记账** |
| 3 | `InterproceduralPlanCompactionTests.cs:196-207` `SameSource_WithLargeBudget_HasLargerInitialCapacity` | 同因：若该字段混入共享段份额，"大预算上界明显更大"可能不再成立 |
| 4 | `InterproceduralPlanCompactionTests.cs:159, 186-188, 289, 764` | `PlanCountTotal > 0` / `MaxPlanCountPerGroup > 0` 哨兵：按 §4.4.1 任一自洽口径都仍为正，**预期继续通过**；但若实现改为"只观测尾段"，小预算下尾段可合法为 0 ⇒ 误红 |
| 4b | `InterproceduralPlanCompactionTests.cs:166, 291`（`PlanCapacityTotal >= PlanCountTotal`）与 `:769-770`（`capacityOverCount >= 1.0`） | ⚠ **§4.4.1 的取舍直接决定它是否红**：`PlanCountTotal` 用口径①而 `PlanCapacityTotal` 用口径②时**必红**。必须两字段同口径 |
| 5 | `InterproceduralPlanCompactionTests.cs:749-822` `ControlledWindowPeakLedger_...` | 若组头由 1 个 buffer 引用变 2 个 ⇒ `Unsafe.SizeOf<InterproceduralDataFlowPlanGroup>()`（`:757`）**变大**，账本数字随之移动。`PeakWindowRows` 本身**不变**（§2.5），故 `oldPeakBytes > newPeakBytes` 仍成立 |
| 6 | `InterproceduralPlanCompactionTests.cs:430` | `Assert.Equal(first, second)` 是**整个 17 字段 record struct** 的相等。**新增字段必须跨重复构建确定**，否则 DOP 1/2 下会红 |

> ✅ **初稿的"核心难点"不存在**：`CpgBuilderSources.cs:564-582` 明确记载
> "形参 × 调用点才是低成本构造方式"——该方式**继续有效**，因为组行数并未下降。
> 无需设计替代夹具，也无需降级该覆盖。

### 5.2b **必须主动检查但不预期改动**（`PlanCountTotal` 语义）

现有账本字段的新口径（§4.4）必须**保持**以下既有不变量继续成立，
它们是"口径没有退化"的证据，**不得**为了让新实现通过而放宽：

- `PlanCapacityTotal >= PlanCountTotal`（`:166`、`:291`）—— **§4.4.1 口径 ② 下成立，①+② 混用则必红**；
- 预算递增 ⇒ `PlanCountTotal` 单调不减（`:220-222`）；
- `SortBufferReclaimedSlots <= SortBufferClearedSlots`（`:542`）；
- `SortBufferRetainedBytes >= 0`（`:436`、`:167`）。

### 5.3 **必须新增的护栏**

| # | 建议名称 | 判别内容 |
| --- | --- | --- |
| N1 | 共享前缀**载体份数**断言 | 对 `(参数 p, 调用点数 c)` 夹具断言：**保留的计划载体份数 ≈ c·p + O(c)**，而非 `c²·p`。按 §4.4.1 推荐口径 ②，该量即 `PlanCountTotal`。⚠ **不得**用 `PeakWindowRows`：它按 §2.5 **不变**。这是本项**唯一的收益判别量** |
| N2 | **懒惰构造**断言 | `c == 1` 的方法**不产生**共享前缀条目（护住 §3.3；否则短命构建净变慢） |
| N3 | 共享段**独立账本**断言 | 护住 §4.4 新增字段：非零、且**按方法计一次**（≈ `c·p` 量级，**不是** `c²·p`）。这是 §4.4.2 重复计数陷阱的守门断言 |
| N4 | 逐文档作用域断言 | 多文档构建下，共享前缀缓存**不跨文档**（护住 §3.1；这是最严重错误类型且不抛异常） |
| N5 | 非正预算**空组**契约 | 强于现有第 302-314 行：显式覆盖"段 1 非空 / 尾段为空"与"段 1 为空 / 尾段非空"两种组，确认仍抛 `ArgumentOutOfRangeException("index")`（护住 §3.2） |

### 5.4 **必须保持不变且继续通过**（冻结 oracle，**禁止修改**）

| 文件 | 内容 | 级别 |
| --- | --- | --- |
| `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs:65-141` | `ExpectedInterproceduralOrder`（**74 条**冻结序：`ArgumentToParameter` 61 + `MethodReturnToCallResult` 11 + `ReturnToMethodReturn` 2） | **冻结 oracle**：定义等价性的**主判据** |
| 同上 `:143-153` | `BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder` | 同上 |
| 同上 `:155-163` | `..._AreStableAcrossRepeatedBuilds` | 同上 |
| 同上 `:165-174` | `..._HaveStableTotalCount`（`Assert.Equal(ExpectedInterproceduralOrder.Length, ...)`） | **边数不变**的直接判据 |
| 同上 `:234-254` | `..._DeriveContextIdFromCallSiteContext` | 护住 `callSiteContext` 仍然**逐调用点**传入（本项设计的核心前提） |
| `tests/NLISSN.ContractTests/Cpg/DataFlowPlanConstructionReuseTests.cs:43-63` | `Sparse`/`Collision`/`JoinLoop` 的 `NormalizedGraphHash` 与 `PublicationHash` | **冻结哈希** |
| `tests/NLISSN.ContractTests/Cpg/DataFlowCandidateScanPruningTests.cs:46-54` | 同上三个 PublicationHash | **冻结哈希** |
| `tests/NLISSN.ContractTests/Cpg/InterproceduralEdgeIndexSnapshotTests.cs:590` | `BridgeKindOf_DerivesEveryBridgeKindFromPoolEndpointKind` | 护住 §3.6 |
| `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs:190` | `InterproceduralPlanRef` 必须是非公开值类型 | 护住"按值搬运"前提 |
| `InterproceduralPlanCompactionTests.cs:74-93` | `InterproceduralPlanRef_KeepsOnlyLazyHandlePayload`（字段集恰为 `{ArgumentOrdinal, PoolOrdinal}`） | 护住 §3.6（载体不得变宽） |
| `InterproceduralPlanCompactionTests.cs:98-115` | 宽度 ≤ 428×60%，且 `== 8` | 护住 ⑥ 的成果 |
| `CpgInterproceduralEdgeOrderTests.cs:183-227` | `PlanSortRow` 字段集与宽度 ≤ 32 B | 护住方案 A 的行布局 |
| `InterproceduralPlanCompactionTests.cs:302-314` | `NonPositiveBudget_KeepsFrozenOutOfRangeContract` | **冻结异常契约**（§3.2） |
| `InterproceduralPlanCompactionTests.cs:249-269` | `BudgetOverflow_DoesNotDuplicateReturnToMethodReturnBridges` | **冻结门控契约**（§3.4） |
| `InterproceduralPlanCompactionTests.cs:227-236` | `BudgetOverflow_StillRecordsBoundaryEdgeBudgetCut` | 冻结截断事件 |
| `CpgWorkBatchInterproceduralTests.cs:68-96` | 跨 DOP oracle | 等价性辅助判据 |
| `InterproceduralPlanCompactionTests.cs:442-517` | `SelectSortSlotsToReclaim_*`（4 条纯函数直测） | 与两段式**无关**，应原样通过 |
| `InterproceduralPlanCompactionTests.cs:519-560` | `PublishedSlots_ReleaseSortRowKeyReferences` / `..._RetainNoResidualSortRowsAfterClear` | 发布段 `rows.Clear()` 语义不变 |
| `InterproceduralPlanCompactionTests.cs:567-734` | `ReclaimIdleSortBufferCapacity_*` / 轮流大槽压力（**合成槽数组**，不依赖 `c²p` 定律） | **不受影响**，应原样通过 |

---

## 6. 验收（本轮按用户指示裁剪）

### 6.1 本轮门槛

**只做 10% 核心测试。不做任何前置测试**（无冻结基线复采、无 A/B、无探针、无变异验证）。

| 判据 | 内容 |
| --- | --- |
| 构建 | `NLCPG.csproj` **0 警告 0 错误** |
| **主判据（等价性）** | §5.4 的全部**冻结 oracle** 逐字节通过：74 条边序、三个 `NormalizedGraphHash`/`PublicationHash`、`GraphSnapshotVersion`、节点/边数 |
| 核心定向 | §5.1 + §5.2 + §5.3 的修订与新增全部通过 |
| 未验证边界 | §8 逐条如实登记 |

⚠️ **本轮不产出任何耗时或倍数结论**。理由不是"没空做"，而是**本机墙钟不具备该判别力**：
`BASELINE.md` §5.4 实测同二进制同输入三次运行差 **16%–71%**；DoD#4 的阳性对照
（注入已知 +3.5% 忙等）CI **跨 0**，即**判不出** 3%；
反推分辨 3% 需约 **41 对**、1% 需约 **361 对**（`Context/progress.md`）。
⇒ 若将来要**主张**收益，必须用 `allocBytes` **配对口径**（配对差 <0.05%），
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

### 6.3 可选的后续收益实测（**本轮不做**）

若日后要主张本项的收益，唯一有判别力的口径是**配对累计分配 A/B**：
用 ⑥ 已建立的手法（单变量变异：把共享段改回逐调用点重放）在**同一份单文件语料**上跑配对，
读 `RuntimeLog/runtime.log` 的最后一个 `allocBytes=`。
预期量级可**先由算式给出**：`Δ ≈ (c²p − cp) × 8 B`；
`Item.cs` 上即 `(9,679,406 − 10,898) × 8 B ≈ 77.3 MB`（**算术，未实测**）。

> ⚠️ 该预测只看**载体**。⑥ 已证明单变量变异法有效（`RESULT.md` §1.1：
> 用仓库既有宽度护栏验证变异真的生效，实测 `Expected: 8, Actual: 216`）。
> 但本项的变异更难写"单变量"，因为份数变化会牵动 §4.3 预算与 §4.4 账本 ⇒ 风险更高。

---

## 7. 实施顺序（建议）

1. **先改 `InterproceduralDataFlowPlanGroup.cs`**（两段式组头 + `Count` 求和）。
   此时 `NLCPGBuilder.cs` 会编译失败 ⇒ 同一批改完。
2. **改构造段**（`:2829-2950`），保持段 2/3 与门控字面不变。
3. **改发布段**（`:3056-3110`、`:3225-3270`）的分派与行构造。
4. **改账本**（新增共享段字段 + `Empty`/`ToLedger` 同步）。
5. **同步 §5.1/§5.2 的测试修订，补 §5.3 的新增护栏。**
6. 跑 §6.2 聚焦过滤；确认 §5.4 冻结 oracle 全绿。

**唯一必须先裁决的是 §4.4.1 的账本口径**（`PlanCountTotal` 按组消费还是按实际保留载体）：
它决定 §5.2 的 4b 是否红、以及 N1 用哪个字段当判别量。
建议口径 ②（按保留载体），此时 N1 直接读 `PlanCountTotal`。其余均为机械同步。
按 §2.5，**初稿预期的"夹具替代方案"已不存在**。

---

## 8. 未验证边界与风险（不得越读）

1. **本文初稿零执行**；**2026-09-28 已执行**（见文首状态更新），但执行同样**跳过全部前置验证**，
   只做 §6.1 的 10% 核心验收。⇒ 本文所有**收益量**仍为**源码阅读 + 引用既有实测**，
   **本轮未做任何计时与内存测量**；§2 的 8.61 GiB / 343 MB / 454.5 / 888.2 全部来自**既有**探针与算术。
2. **8.61 GiB 是累计分配，不是常驻峰值**（M8 §5 口径纪律）。
   省累计分配**不保证** `wsBytes` 峰值同幅下降。⑥ 的 −14.79% 同样只是累计分配。
3. **43M 条是抽样值（63.2% 文件）**，且语料**尾部极重**
   （单样本最大贡献文件占 80.5%–99.8%）⇒ 全语料真值**可能显著更高**，
   **不宜线性外推**（M8 §6 P1 已就地警告）。
4. **收益高度集中**：Top-10 方法占 84%–99.8%。
   ⇒ 本项是**通用**方案，但**"只治巨型方法"可能拿到大部分收益且风险更小**。
   M8 §6 P1 明确指出该取舍**未被裁决**。本项选择通用方案，须意识到这一点。
5. **`PlanIndex` 与排序稳定性**：改造后末键仍必须构成严格全序（§3.3）。
   若实现中让共享段与尾段产生**相同 `PlanIndex`**（例如两段各自从 0 开始编号），
   则 `List.Sort` 的不稳定性会**泄漏进边序** ⇒ 冻结 oracle 会红。
   本文要求"两段统一编号"（§4.2 `:3089` 的分派公式即按统一编号写）。
   **实施已按统一编号落地**（`PlanAt` 在 `[0, sharedCount)` 派发给共享段、其余派发给尾段），
   并由 §5.3 的 N5 护栏直接断言"共享段空 / 尾段非空"与反向形态的编号连续性兜住。
6. **并发会话**：`NLCPGBuilder.cs` 正被其他会话改写。实施前须**确认文件哈希**
   与本文行号基准一致；不一致时**以符号名为准**并重读。
   **实施记录**：行号基准已漂移（`4,647` → 实施时 `4,759`），实际改动**全部按符号名定位**，
   未使用任何旧行号。`NLCPGBuilder.cs` 的既有并发改动未被本次触碰。
7. **初稿对"行数下降"的判断已被本轮订正**（§2.5）：本项**不减少**计划条数、行数、
   `PeakWindowRows`、边数与 `List.Sort` 比较次数，**唯一收益是同时驻留的载体份数**。
   ⇒ 初稿据此提出的"行上界夹具失准 / 需设计替代夹具"**已撤回**。
   若实施中观测到行数或边数发生变化，说明**实现有误**（多省或少发了计划），须停下重查。
8. **未做**：多文件/全语料、多 DOP、跨 Build 复用、常驻峰值、GC 次数、墙钟。
   **已裁决（2026-09-28 实施时）**：§4.4.1 的 `PlanCountTotal` 口径取 **口径 ②（按保留载体）**，
   且**共享段确实计入** `PlanCountTotal`/`PlanCapacityTotal`（不只进 4 个专用字段）——
   否则"每方法一次"只体现在专用字段上，且"共享段非空 / 尾段为空"的组会读成 0。
   已核实 `PlanCapacityTotal >= PlanCountTotal` 与 `capacityOverCount >= 1.0` 两项均保持通过。
9. **§1.2 的 `c` 倍重复链条已由本轮直接重读源码核实**（`NLCPGGraph.cs:1177-1179`、
   `:1229`、`:1238`、`:1331-1335`、`:1340-1343`），**不是**转述 M8 文档。
   但它仍是**静态阅读**，本轮未运行；实施后必须由 §5.4 的**边序 oracle 与边数断言**
   实际兜住——若该链条有误，边数断言（`:165-174`）会立刻反映出来。
