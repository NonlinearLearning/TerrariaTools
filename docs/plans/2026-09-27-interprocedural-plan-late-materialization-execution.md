# ⑥ 跨过程计划延迟物化（Late Materialization）执行报告

**目标：** 把跨过程桥计划的**计划载体**从「内嵌两个端点节点值」的 **216 B** 结构，改为只存
**池内序号 + 实参序**的 **8 B** `int` 对。排序键与发布所需的端点值**不在载体里保存**，
而在构造排序行、发布边时按池内序号**现取**。

**思路来源：** Roslyn 的红/绿树（[red/green trees](https://stackoverflow.com/questions/25963328/how-does-the-release-version-of-roslyn-implement-immutable-trees)）
—— 宽的是"绿树"（不可变事实），窄的是"红树"（带父指针的视图）；**中间结果不必物化成宽记录**，
真正需要时才把窄引用展开。本项把这一模式用在**计划数组**上。

**Tech Stack：** C#/.NET 10、`System.Buffers.ArrayPool<T>`、现有 NLCPG 构图与 Contract 测试。

---

日期：2026-09-27。本项是
[M8 静态审查](../../Build/MemoryOptimization/M8/STATIC-REVIEW-InterproceduralDataFlowPlan.md)
§8.3 的执行化，对应 §8.4 对照表的 **⑦**。

> 📌 **本轮性质：只写文档，不改产品代码、不跑测试。**
> 因此本报告**不产出任何实测收益数字**；所有字节数均为**由实测宽度与既有抽样推出的算术**，
> 已在 §6 逐项标注口径。切片尚未在 [feature_list.json](../../Context/feature_list.json) 登记。
>
> ✅ **已执行（2026-09-27，见 §12）**：产品代码与测试同批落地，NLCPG 构建 0 警告 0 错误，
> 10% 核心测试 **97/97 通过**（93 条定向 + 4 条跨 DOP oracle）。§4.4 的二选一**定为选项 2**（退役 ⑤ 池化）。
> 仍然**不产出任何内存收益数字**——本轮只做 10% 核心测试，未做分配量 A/B。

> 📌 **行号基准**：`src/NLCPG/Builder/NLCPGBuilder.cs` 当前 **4,514 行**（本会话曾被并行工作改写，
> 4,497 → 4,503 → 4,514）。下列引用同时给出**符号名**，行号漂移时以符号为准。

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 契约是否允许发布期回读？ | **允许** —— 契约禁的是"回读**图节点**"，不是回读本身；本方案回读的是**池**，而这正是 M2 现行架构（详见 §3） |
| 载体能否压到 8 B？ | **能** —— `(int 池内序号, int 实参序)`；`BridgeKind` 可由池内边的端点种类**导出**（§2.3） |
| 收益多大？ | **载体数组 27×**（累计口径）；**窗口峰值只有 6.2×**（排序行不缩）；**单组退出 LOH**（394 行 → 10,626 行）是相对 P0 的**唯一独有收益** |
| 是否可解释 9,178 MiB 峰值？ | **不能，且不得这样写** —— 窗口有上限，载体直接常驻**有界（≈5 MiB 量级）**；本项打的是 **LOH 的产生/退役（churn 与碎片）** |
| 与 P0、⑤ 的关系 | **P0 在前、本项在其后**；本项**使 ⑤ 的池化路径在实测语料上失效**（§4.4，非显然） |

## 1. 执行范围（本轮，用户指定）

| 项 | 本轮决定 |
| --- | --- |
| 产品代码改动 | **不做**（本报告只给方案与改动清单） |
| 前置测试 / 探针 / A/B | **不做**（契约已由 §8.3 判明，收益为算术推算） |
| 测试修订清单 | **列出并分级**（编译级 vs 断言级，§4.3） |
| 未测量的收益/风险 | **不声称**（§6、§9） |

**本轮放弃的东西，写明而不是默认**：本报告**不回答**"改完省了多少常驻内存"，
也**不产出** `InterproceduralDataFlowPlan[]` 的 LOH 排名复测。它只回答三件事：
**契约是否允许（允许）**、**要改哪些地方（§4 封闭清单）**、**哪些既有结论会因此失效（§4.4、§4.3）**。

## 2. 目标表示

### 2.1 载体类型

新增（或改名沿用）一个内部值类型，**宽度恰为 8 B**：

```csharp
// 跨过程桥计划的【惰性载体】：只记"这条计划用的是池里哪个序号"。
// 端点节点值不在此保存 —— 构造排序行与发布边时按 PoolOrdinal 从池现取。
internal readonly record struct InterproceduralPlanRef(
  int PoolOrdinal,
  int ArgumentOrdinal);
```

`Unsafe.SizeOf<InterproceduralPlanRef>() == 8`（两个 `int`，按 4 B 对齐）。

**为什么必须是"池内序号"而不是别的编号**：池内序号是本 pass 内的**边序号**
（`NLCPGBuilder.cs:2561`，源码明确写了"不是 `NodeId`、图 node ordinal 或 metadata id"），
且池建成后本 pass 只读（`:2352-2353` 的"本段不调用 `AddEdge`/`AddNode`"），
故序号在"创建 → 回读"之间**恒不变**。

### 2.2 8 B 里放哪两个 `int`

| 槽位 | 内容 | 为什么必须存 |
| --- | --- | --- |
| 第 1 个 `int` | **池内序号** | 唯一的端点来源。它就是"不物化"的那个句柄 |
| 第 2 个 `int` | **`ArgumentOrdinal`** | 它是排序的**第 2 键**（`:2930`、`:3240`），且在构造期由 `ParseArgumentOrdinal`（`:3401-3406`）一次性落定；存下来可让 `BuildAndSortPlanRows` **不必**再访问实例字典 `_methodParameterOrdinalsByNode`（该方法是 `static`，`:3150`） |

`ArgumentOrdinal` 在段 2/3 恒为 `-1`（`:2794`、`:2815` 用 3 参构造 ⇒ 取默认值 `-1`），
与现状逐位相同。

### 2.3 `BridgeKind` 由池内边导出（本方案成立的关键）

`ArgumentOrdinal` 存下来了，但**排序第 1 键 `BridgeKind` 没有存**。它不需要存：
三个段由**池内边的端点种类**唯一确定。

三段的产生点与判据（全部为源码事实）：

| 段 | `BridgeKind` | 产生位置 | 该段的边从哪个桶来 | 端点种类特征 |
| --- | --- | --- | --- | --- |
| 1 | `ArgumentToParameter` | `:2771-2775` | `_argumentsByMethod[key]`（`:2403`） | `edge.TargetNode.Kind == MethodParameter` |
| 2 | `MethodReturnToCallResult` | `:2794-2797` | `_dataFlowByTarget[callSite]`（`:2396`） | `edge.TargetNode.Kind == CallSite` 且 `edge.SourceNode.Kind == MethodReturn` |
| 3 | `ReturnToMethodReturn` | `:2815-2818` | `_returnsByMethod[key]`（`:2407`） | `edge.TargetNode.Kind == MethodReturn` |

桶成员集的判据写在第 1 遍计数段与第 2 遍填充段（`:2334-2341`、`:2401-2408`），
二者逐字相同：**`MethodParameter` 只进 `arguments`，`MethodReturn` 只进 `returns`**。

> ⇒ **导出函数是全的且无歧义的**：一个节点只有一种 `Kind`，
> 故 `TargetNode.Kind ∈ {MethodParameter, MethodReturn, CallSite}` 三者互斥，
> 恰好对应段 1 / 段 3 / 段 2。池内一个序号只属于一个段（每条保留边在池里恰好一份，`:2268-2269`）。

```csharp
// 由池内边导出该计划所属的桥种类。判据与 :2401-2408 的入桶条件、:2781-2786 的段 2 过滤同源。
private static NLCPGInterproceduralBridgeKind BridgeKindOf(NLCPGGraph.PendingEdge edge)
{
    return edge.TargetNode.Kind switch
    {
        NLCPGNodeKind.MethodParameter => NLCPGInterproceduralBridgeKind.ArgumentToParameter,
        NLCPGNodeKind.MethodReturn => NLCPGInterproceduralBridgeKind.ReturnToMethodReturn,
        _ => NLCPGInterproceduralBridgeKind.MethodReturnToCallResult,
    };
}
```

**这是本方案唯一的隐式耦合**：它依赖"当前只有这三个产生者"。若将来新增第 4 个产生者
（例如把 `SummaryMapping` 也走计划路径），该 `switch` 的 `_` 分支会**静默给出错误结果**。
⇒ §7 判据 3 专门为此设一条护栏：**断言导出的 `BridgeKind` 与构造期实际使用的一致**。

### 2.4 构造、排序、发布三处的形状

| 环节 | 现状 | 改后 |
| --- | --- | --- |
| 构造（`:2771`/`:2794`/`:2815`） | `Add(new InterproceduralDataFlowPlan(源节点, 目标节点, 桥种类, 实参序))` | `Add(new InterproceduralPlanRef(ordinal, argumentOrdinal))` |
| 构造排序行（`:3164`、`:3180-3185`） | 读 `plan.SourceNode`/`TargetNode`/`BridgeKind`/`ArgumentOrdinal` | `var edge = edgeSnapshot.Edge(refs[i].PoolOrdinal);` 后取 `edge.SourceNode`/`edge.TargetNode`，`BridgeKind` 走 `BridgeKindOf(edge)`，`ArgumentOrdinal` 直读 |
| 发布（`:3011`、`:3013-3016`） | `plans[row.PlanIndex]` 回读整条计划 | `edgeSnapshot.Edge(refs[row.PlanIndex].PoolOrdinal)` 后 `AddEdge(edge.SourceNode, edge.TargetNode, …, ForInterproceduralBridge(BridgeKindOf(edge)), callSiteContext)` |

**连带签名改动**：`FlushParallelPublishWindow`（`:2941`）与 `BuildAndSortPlanRows`（`:3150`）
都是 **`private static`**，当前只拿到 `graph` / 组数组 / 排序行 / `nodeSortKeys`。
回读池需要 `edgeSnapshot`，故二者各加一个 `InterproceduralEdgeSnapshot edgeSnapshot` 形参
（仍是 `static`，只是多一个参数）。这是本项**唯一**的跨方法接线。

## 3. 契约判定（引自 §8.3，不重复论证）

判定的完整论证在
[M8 静态审查 §8.3](../../Build/MemoryOptimization/M8/STATIC-REVIEW-InterproceduralDataFlowPlan.md)。
此处只留结论与它对本项的约束：

- **被禁的是"回读图节点"，不是回读本身。** 契约措辞（`:2236-2237`、`:2563-2564`）为
  "池保存的是扫描当时的完整 `PendingEdge` 值快照 …… 发布阶段【不回读图节点】"。
  它的理由写在同一处：`MergeNode`（`NLCPGGraph.cs:897-911`）会在事后替换节点。
- **"桶存 int、发布期回读池"就是 M2 现行架构**：桶是 `int[]`（`:2248-2252`，形状由
  `InterproceduralEdgeIndexSnapshotTests.cs:75-84` 护住），`Edge(ordinal)` 的实现是
  `return _pool[ordinal];`（`:2275-2278`），发布期本来就在调它（`:2770`/`:2780`/`:2814`）。
- **已有先例**：`PlanSortRow`（`:3218-3223`）就是"只存 `PlanIndex` + 排序键，
  发布期 `plans[row.PlanIndex]` 回读"（`:3011`，设计注释 `:3210-3216`）。
  本项只是**把同一模式再用在池上**。
- **反向红线（不许做）**：**池本身绝不能改成序号**（那才是把"值"换成"句柄"，会触碰契约）。
  本项不动池，只动池的**消费者**。

## 4. 必须同批改动（封闭清单）

### 4.1 产品代码

| # | 位置 | 改动 |
| --- | --- | --- |
| 1 | `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs`（17 行，整文件） | 改为 8 B 的 `InterproceduralPlanRef` |
| 2 | `InterproceduralDataFlowPlanGroup.cs:29`、`:37`、`:75`、`:82-83`、`:89`、`:91`、`:114`、`:173` | 元素类型 `InterproceduralDataFlowPlan` → `InterproceduralPlanRef`；`ArrayPool<…>.Shared` 的元素类型跟随。`clearArray: false` **仍然安全**（新类型只有 `int`，比原来更安全） |
| 3 | `NLCPGBuilder.cs:2771`、`:2794`、`:2815` | 三处 `Add(...)` 改存 `(ordinal, argumentOrdinal)` |
| 4 | `NLCPGBuilder.cs:3164`、`:3180-3185` | 排序行构造改为经池回读 + `BridgeKindOf` |
| 5 | `NLCPGBuilder.cs:3011`、`:3013-3016` | 发布改为经池回读 + `BridgeKindOf` |
| 6 | `NLCPGBuilder.cs:2941`、`:3150` 及 `:2901`、`:2962`、`:2886` 调用点 | 两方法各加 `edgeSnapshot` 形参 |
| 7 | `NLCPGBuilder.cs:2852`（账本 slack 计算） | `Unsafe.SizeOf<InterproceduralDataFlowPlan>()` → 新类型。**语义不变**：slack 字节数自动按 8 B 计 |
| 8 | `NLCPGBuilder.cs` 新增 `BridgeKindOf` | 见 §2.3 |

**改动面是封闭的**：该类型在全仓库的非测试引用只有
`InterproceduralDataFlowPlanGroup.cs` 的 9 处（`:29`、`:37`、`:75`、`:82`、`:83`、`:89`、`:91`、`:114`、`:173`）
+ `NLCPGBuilder.cs` 的构造 3 处（`:2771`、`:2794`、`:2815`）与账本 1 处（`:2852`）
+ 4 处注释（`:2737`、`:2738`、`:3165`、`:3211`），其余引用都在测试内。

### 4.2 常量重算（必须同批，否则会反向劣化）

`InterproceduralPlanBuffer.PoolMinimumCapacity`（`InterproceduralDataFlowPlanGroup.cs:27`）
现为 `(85_000 / 216) + 1 = 394`，其含义是"该元素宽下**进 LOH 的最小条数**"。

| 元素宽 | 进 LOH 的最小条数 | 公式 |
| --- | ---: | --- |
| 216 B（现状） | 394 | `85_000 / 216 + 1` |
| **8 B（本项）** | **10,626** | `85_000 / 8 + 1` |

⇒ 常量必须改为 `(85_000 / 8) + 1`。**不改的后果**：门槛仍是 394 条，
于是"根本没有进 LOH 的数组"也会被拿去池化，这既违背 `:18-23` 记录的
"只池化大数组"契约，又会让 `ArrayPool` 的 2 的幂桶对齐放大 `Capacity`，
从而破坏"前缀收集"那条"容量不得超过预算"的既有护栏
（该护栏之所以至今没被破坏，正是因为门槛 394 让**小数组走精确分配**，见
[P03 执行文档](2026-09-26-interprocedural-plan-arraypool-execution.md) §3 的实施订正）。

### 4.3 测试修订清单（按失效方式分级）

**(a) 编译级 —— 类型被改名/退役，不修就构建不过**

| 文件 | 位置 | 处理 |
| --- | --- | --- |
| `InterproceduralPlanCompactionTests.cs` | `:74-90` | 字段集断言改为 `{ArgumentOrdinal, PoolOrdinal}`；`DoesNotContain("CallSiteNode"/"TargetMethodNode"/"StableCallSiteOrder")` **保留**（仍有效） |
| 同上 | `:93-110` | `Assert.Equal(216, planWidth)` → `8`；`≤428 B 的 60%` 仍通过。注释里的 `2 × NLCPGNode(104) + 2 × int` 要改 |
| 同上 | `:131-139` | 无需改逻辑，仅类型名 |
| 同上 | `:751` | `Unsafe.SizeOf<…>()` 仅需改类型名；**宽度值自动跟随**（§8.3.6 那句"无需改"只对宽度值成立，**类型名仍要改**） |
| `CpgInterproceduralEdgeOrderTests.cs` | `:199`、`:206` | 类型名 |
| `NLCPGNodeIdContractTests.cs` | `:190` | 字符串 `"NLCPG.Builder.Passes.InterproceduralDataFlowPlan"` 改新名 |

**(b) 断言级 —— 能编译，但断言的前提被反转（**最容易漏**）**

| 文件 | 位置 | 为什么会红 | 处理 |
| --- | --- | --- | --- |
| `CpgInterproceduralEdgeOrderTests.cs` | `:208-215` | 该断言是 `rowWidth < planWidth`（32 < 216）与 `Assert.Equal(32, rowWidth)`。载体降到 8 B 后 **32 < 8 为假** —— "排序行必须窄于计划"这条**前提整体反转**（载体比行还窄） | 断言改为"行宽 ≤ 32 且行内不含载体类型"；`Assert.Equal(32, rowWidth)` **保留**（行布局未变） |
| `InterproceduralPlanArrayPoolTests.cs` | `:25-26`、`:53-56`、`:63-66` | 见 §4.4 | 见 §4.4 |

**(c) 无需改动 —— 真正的验收判据**

`DataFlowPlanConstructionReuseTests.cs:41-66` 的三组冻结
`NormalizedGraphHash` / `PublicationHash`（Sparse / Collision / JoinLoop）。
它们锁的是**发布出去的边序列与内容**；只要 §5 的"每个调用点各传自己的 `callSiteContext`"
不被破坏，边集逐位不变，这些哈希**照旧通过**。**这是本项安全的客观判据，不需要新增测量。**

### 4.4 ⑤ 的池化路径会因此失效（非显然，必须先知道）

`InterproceduralPlanArrayPoolTests` 是 P03（⑤ ArrayPool 池化）的**唯一**护栏，
其夹具刻意让"单组计划数 ≥ `PoolMinimumCapacity`"才触发池租借
（`:20-24` 注释：`25 × 20 = 500 > 394`），并在 `:53-56` 断言
"若 `RentCount == 0`，则本断言无判别力"。

载体的 LOH 门槛升到 **10,626** 后：

| 事实 | 数值 | 后果 |
| --- | --- | --- |
| 该夹具的单组行数 | 500 | **远低于 10,626** ⇒ 走精确分配 ⇒ `RentCount == 0` |
| P1 实测的**最大单组行数** | 5,050（源码注记 5,878） | **同样低于 10,626** |

⇒ **在已实测语料上，没有任何一个组会再达到池化门槛**：
池化代码成为**不可达路径**，⑤ 的收益归零（其收益来源"LOH 分配量"本身也被本项消除）。
这不是 bug，而是两项的**内在冲突**：⑤ 的前提是"载体进 LOH"，本项恰好把载体移出 LOH。

**处置（二选一，实施前必须定）**：

1. **保留 ⑤ 作为防御性池化**：把夹具放大到 `c · p ≥ 10,627`
   （组大小定律 ≈ 形参个数 × 指向该方法的调用点数，`:23`），例如 `113 × 95 ≈ 10,735`。
   代价：夹具规模约为原夹具有 21 倍，按 `:24` 记录的"40 × 64 约 9 s"外推，
   该测试可能升到**十秒量级**。
2. **随本项退役 ⑤ 的池化分支**：载体已不落 LOH，池化不再有收益；
   删除 `ArrayPool` 与 `RentCount`/`ReturnCount` 账本，`InterproceduralPlanBuffer`
   退化为一个薄的精确分配包装（或直接被数组替代）。

**推荐 2**，但**必须显式记录**这是"推翻 ⑤"，不能静默删除——⑤ 已落地于工作树
（见 §8.2 的状态订正），其 `ArrayPool` 引入当初还**推翻过** M1 的"不采用全局 ArrayPool"决定
（[P03 执行文档](2026-09-26-interprocedural-plan-arraypool-execution.md) §0）。若选 2，
应在 ⑤ 的执行文档里补一段"被 ⑥ 取代"的记录，而不是删掉它的结论。

## 5. 语义不变量（逐位不许变）

1. **`graph.AddEdge` 的调用序列逐位不变** ⇒ `CpgInterproceduralEdgeOrderTests`
   的冻结期望序必须**原样通过、oracle 文件零改动**（用 `git diff` 核对）。
2. **每个调用点仍各传自己的 `callSiteContext`**（`:3006-3017`
   `BuildPendingNodeCallSiteContext(graph, group.CallSiteNode)`）⇒ 边集与 `ContextId` 不变。
3. **组内有效排序键不变**：`BridgeKind → ArgumentOrdinal → NodeSortKey(Source) → NodeSortKey(Target)`，
   末键 `PlanIndex` 保稳定（`:2930`、`:3240-3259`）。
4. **预算语义不变**：正预算走**前缀收集**（`:2750-2756`、`:2764-2768`）；
   非正预算**保留**原 `RemoveRange` / `plans[0]` 异常契约（`:2858-2869`、`:2999-3004`）。
5. **`recordedReturnMethods` 门控不受影响**（`:2800-2802`）—— 它只依赖
   `targetMethodNode` 的 `FullName`，不经过载体。
6. **窗口双上界与冲刷时机不变**（`:2667-2668`、`:2883-2884`）。

## 6. 收益的正确算式（三处硬约束，引用时不得省略）

### 6.1 载体数组：27×

`216 B → 8 B`。**这只适用于计划载体数组本身**（= 累计分配口径）。

### 6.2 窗口峰值：只有 6.2×（因为排序行不缩）

发布期**排序行与计划载体同时存活**，而 `PlanSortRow` 恒 32 B（`:3218-3223`）：

| 口径 | 每行字节 | 相对 |
| --- | ---: | ---: |
| 现状 | 216（载体）+ 32（排序行）= **248 B** | 1× |
| 本项 | 8 + 32 = **40 B** | **6.2×** |

### 6.3 累计分配（@63.2% 语料，`ArgumentToParameter` 计划 42,907,193 条）

| 方案 | 剩余载体 | 省 | 因子 |
| --- | ---: | ---: | ---: |
| 现状 | 42,907,193 × 216 B = 9.27 GB | — | 1× |
| **仅本项** | 42,907,193 × 8 B = 343 MB | **8.31 GiB** | 27× |
| 仅 §6 P0 | 94,400 × 216 B = 20 MB | **8.61 GiB** | 454.5× |
| **P0 + 本项** | 94,400 × 8 B = 0.75 MB | **8.63 GiB** | ~12,000× |

⚠️ **两者不可相加**：P0 已消掉占绝对多数的 `c² → c`，本项再乘 27×，
**增量仅约 0.02 GiB**。就累计分配而言，**本项单独做略逊于 P0 单独做**。

### 6.4 唯一的独有收益：把载体移出 LOH

| 载体元素宽 | 进 LOH 的最小条数 | 实测最大单组 5,050 行 ⇒ |
| --- | ---: | --- |
| 216 B | 394 | 1,090,800 B ⇒ **必然 LOH** |
| **8 B** | **10,626** | 40,400 B ⇒ **落回 SOH** |

源码注释记录的 5,878 行更大单组：`5,878 × 8 = 47,024 B`，**仍在 SOH**。

**对照**：排序行的 LOH 阈值是 `85,000 / 32 = 2,656` 行，实测单组 5,050 行
⇒ **排序行本身仍在 LOH**。本项**不缩小**它（行数不变），要把它也拉出 LOH 得靠 P0 减少行数。
**这是两条路线的分工，也是"本项是 P0 的乘数而非替代"的结构性原因。**

### 6.5 不得越界的一步

窗口是**受上限约束**的（`:2667-2668`：64 组 / 131,072 行），故载体的**直接常驻是有界的**：

| 时点 | 窗口直接常驻（131,072 行） |
| --- | ---: |
| 现状 | 131,072 × 248 B ≈ **31 MiB** |
| 本项 | 131,072 × 40 B ≈ **5 MiB** |

⇒ **本项不是 9,178 MiB 峰值的解法**，也不得被这样表述。它打的是
**LOH 数组的产生与退役（churn 与碎片）**，即 §8.2 已点名的轴。

## 7. 验收判据（4 条，覆盖 2 个致命失效模式）

| # | 判据 | 文件 | 防的是 |
| --- | --- | --- | --- |
| 1 | 冻结边序 oracle **原样通过、文件零改动** | `CpgInterproceduralEdgeOrderTests`（**已有**） | 顺序/边集被改坏 —— **致命** |
| 2 | 冻结 `NormalizedGraphHash`/`PublicationHash` 不变 | `DataFlowPlanConstructionReuseTests.cs:41-66`（**已有，不改**） | 发布内容被改坏 —— **致命**，且**无需新增测量** |
| 3 | **导出的 `BridgeKind` == 构造期实际使用的 `BridgeKind`** | **新增** 1 个 Fact | §2.3 的隐式耦合被将来的第 4 个产生者打破 —— **本项特有的失败模式** |
| 4 | 非正预算仍抛 `ArgumentOutOfRangeException` | `InterproceduralPlanCompactionTests.cs:297-309`（**已有，不改**） | `:2858-2869`/`:2999-3004` 兼容分支被改写吞掉 |

判据 3 是本项**必须新增的唯一测试**：它把"`BridgeKind` 可以导出"这个**假设**变成可观测事实。
实现方式：在三段各造一条计划，断言 `BridgeKindOf(edgeSnapshot.Edge(plan.PoolOrdinal))`
等于该段应有的 `BridgeKind`（可用一个测试内可见的比较钩子，或断言发布出的边标签
`NLCPGEdgeLabel.ForInterproceduralBridge` 的取值分布）。

**不通过的处置**：任一红 → 只回退本项补丁，不改他人文件、不整文件覆盖。

## 8. 明确不做的事（防止范围蔓延）

- **不改**池本身（`_pool` 与四个 `int[]` 桶）—— 那是契约要求保值的对象，改了就违约（§3 红线）。
- **不改**排序键、比较器、稳定性末键（`:3225-3261`）。
- **不改** `NLCPGNode`（104 B）与节点相等语义。
- **不改**三段顺序、预算判断位置、`recordedReturnMethods` 门控。
- **不引入**第二层数组缓存。
- **不动** GC 设置、DOP、预算（改变它们的运行不能作为单变量 A/B）。
- **不做**前置验证、变异验证、分配量 A/B、全量语料复测（本轮显式裁剪，§1）。

## 9. 残余风险与未验证边界

1. **池不被消除**。本项只缩载体；池仍持有 U 条完整 `PendingEdge`（注记 272 B/条）。
   这是**正确的取舍**，但意味着**"消除物化"名不副实**：
   准确说法是**"消除计划的重复物化，保留池的单份物化"**。**必须用后者表述。**
2. **§2.3 的隐式耦合**：`BridgeKind` 的导出依赖"当前只有三个产生者"。
   判据 3 是本项设的护栏，但它**只能防住已枚举的情形**；新增产生者时该 `switch` 需同步。
   （更保守的替代：第 2 个 `int` 改存**段 tag**、`ArgumentOrdinal` 改为发布期用
   `ParseArgumentOrdinal` 现取。二者同为 8 B。**本报告按用户指定采用 `(ordinal, argumentOrdinal)`**，
   理由是它让 `static` 的 `BuildAndSortPlanRows` 免于访问实例字典 `_methodParameterOrdinalsByNode`。）
3. **单行 CPU 增加**：每行多一次池读（以及一次 `Kind` 分支）。量级远小于
   §8.2 已量化的插值收益，但**未测**。
4. **`c · p > 10,626` 的极端尾部仍会进 LOH**（实测 `c` 最大 1262、`p` 最大 11，
   理论最大 13,882）⇒ 本项不是"永不落 LOH"。
5. **累计分配 ≠ 常驻峰值**：8.31 GiB 是累计口径；对 `wsBytes` 峰值的贡献**只能按
   "单组载体退出 LOH"论证**（§6.5），不能按 8.31 GiB 线性外推。
6. **⑤ 的连带失效未处置**：§4.4 的二选一必须在实施前落定，否则
   `InterproceduralPlanArrayPoolTests` 会变成"夹具越不过门槛"的假绿或直接变红。
7. **墙钟不可用于验收**：仓库 `BASELINE.md §5.4` 已证明本机墙钟无法分辨 5%
   （同二进制同输入两次运行差 16%–71%）；§5.6 证明**累计分配字节可判**（配对差 <0.05%）。
   ⇒ 若将来要主张收益，**必须**用分配量/counters 口径，不能用计时。

## 10. 执行顺序与命令

实施顺序（**P0 在前，本项在其后**，依据 §6.3 与 §6.4）：

```
P0（形参去重，消除份数 c² → c）
  → 本项（载体 216 B → 8 B，把载体移出 LOH）
    → 若仍需压 LOH 且已处置 §4.4，再决定保留或退役 ⑤
```

两者都改生成段与载体类型，**同批改可省一次回归**；但若求稳，**先 P0**（收益更大、
语义护栏更直接），本项作为后续乘数。

```powershell
# 从仓库根执行。注意：不能传 -p:...（PowerShell 会把 p 解析为 -ProgressAction 歧义），
# 统一用 --property:（与 2026-09-26-declared-symbol-test-handoff.md §4 的记录一致）。
& ./Build/Tools/Invoke-SerialDotnet.ps1 build ./src/NLCPG/NLCPG.csproj --no-restore --property:UseSharedCompilation=false
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore --property:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~InterproceduralPlanCompactionTests|FullyQualifiedName~DataFlowPlanConstructionReuseTests'
```

`NLCPG.csproj` 构建必须 0 警告 0 错误；TRX 必须含实际测试，不能 0 tests。
§4.4 若选保留 ⑤，过滤器需再并入 `InterproceduralPlanArrayPoolTests`。

## 11. 与其它切片的关系

| 切片 | 关系 |
| --- | --- |
| **§6 P0**（形参去重） | **正交**：P0 去**份数**（`c² → c`），本项去**宽度**（216 → 8 B）。P0 收益更大（8.61 vs 8.31 GiB），**顺序 P0 在前** |
| **⑤ ArrayPool 池化** | **冲突**：本项把载体移出 LOH，使 ⑤ 的门槛在实测语料上不可达（§4.4）。**必须显式处置，不能静默删除** |
| **M1 方案 A**（`PlanSortRow` 只存 `PlanIndex`） | **同模式先例**：本项是"把方案 A 再用在池上"。方案 A 已经把排序行从 428 B 降到 32 B |
| **M2**（桶只存 `int` 序号） | **本项的前提**：正因为桶里已经是池内序号、`Edge(ordinal)` 已经在发布期被调用，本项才不需要新的契约 |
| **§8.2 ③ LOHThreshold** | **可能互补**：那 1 行 JSON 是把中等数组移出 LOH 的通用手段；本项是**只针对本载体**的结构性手段。两者不宜同批 A/B |

## 12. 执行记录（2026-09-27，已落地）

本节记录实际落地形状与 §4 清单的差异。**按用户指示：不做任何前置测试，只做 10% 核心测试。**

### 12.1 实际改动

| # | 位置 | 实际改动 |
| --- | --- | --- |
| 1 | `src/NLCPG/Builder/Passes/InterproceduralPlanRef.cs`（**新文件**，22 行） | 8 B `readonly record struct InterproceduralPlanRef(int PoolOrdinal, int ArgumentOrdinal = -1)`；**旧 `InterproceduralDataFlowPlan.cs` 已删除**（§4.1 #1 说"整文件改为新类型"，实际按"新文件 + 删旧文件"落地——文件名与类型名一致更便于检索） |
| 2 | `InterproceduralDataFlowPlanGroup.cs` | 元素类型全量跟随为 `InterproceduralPlanRef`；**⑤ 池化分支退役**（见 §4.4 处置） |
| 3 | `NLCPGBuilder.cs` 三处 `Add(...)` | 改存 `new InterproceduralPlanRef(ordinal, ParseArgumentOrdinal(edge.TargetNode))` / `new InterproceduralPlanRef(ordinal)` |
| 4 | `NLCPGBuilder.cs` 排序行构造 | 改为 `edgeSnapshot.Edge(plan.PoolOrdinal)` 后 `BridgeKindOf(edge)` + `plan.ArgumentOrdinal` 直读 |
| 5 | `NLCPGBuilder.cs` 发布循环 | 改为经池回读 + `BridgeKindOf` |
| 6 | `FlushParallelPublishWindow` / `BuildAndSortPlanRows` | 各加 `InterproceduralEdgeSnapshot edgeSnapshot` 形参（**唯一的跨方法接线**）；3 个调用点同步 |
| 7 | `NLCPGBuilder.cs` 账本 slack | `Unsafe.SizeOf<InterproceduralPlanRef>()`（8 B） |
| 8 | `NLCPGBuilder.cs` 新增 `BridgeKindOf` | 见 §2.3；**`internal` 而非 `private`**——§7 判据 3 的护栏需要直接驱动它（沿用 `ReclaimIdleSortBufferCapacity` 的先例） |

### 12.2 §4.4 的处置：**选项 2（退役 ⑤ 池化）**，已按"记录而非删除"落实

用户选择选项 2。实际退役内容与留痕见
[⑤ 执行文档 §9](2026-09-26-interprocedural-plan-arraypool-execution.md)（新增一节，**未删 ⑤ 的任何原结论**）：

- 删除 `PoolMinimumCapacity`、`ArrayPool.Rent/Return` 分支、`RentCount`/`ReturnCount` 与账本的
  `PlanBufferRentCount`/`PlanBufferReturnCount` 两个字段、`Return`/`RecordRent` 及其两处调用点；
- 删除 `tests/…/InterproceduralPlanArrayPoolTests.cs`；
- **保留** ⑤ §7.1 "载体必须是 class 而非 struct" 的论证——它与池化无关，讲的是组头按值复制的所有权问题。

**新增的约束（如实记录）**：`InterproceduralPlanBuffer.Grow` 现在**不再归还旧数组**（无池可还），
`_initialCapacity` 保持构造时刻的请求值不变。正常路径下调用方已按精确上界预分配，`Grow` 只是兜底。

### 12.3 测试修订（按 §4.3 三级清单逐条落实）

**(a) 编译级**

| 文件 | 改动 |
| --- | --- |
| `InterproceduralPlanCompactionTests.cs` | 字段集断言改 `{ArgumentOrdinal, PoolOrdinal}`；宽度断言 `216 → 8`；类型名全部跟随 |
| `CpgInterproceduralEdgeOrderTests.cs` | 类型名跟随；`DoesNotContain(typeof(NLCPGNode))` 一并加上（端点不得回到行里） |
| `NLCPGNodeIdContractTests.cs:190` | 字符串改 `"NLCPG.Builder.Passes.InterproceduralPlanRef"` |

**(b) 断言级（**最容易漏的一类**）**

| 文件 | 原断言 | 处置 |
| --- | --- | --- |
| `CpgInterproceduralEdgeOrderTests.cs:208` | `rowWidth(32) < planWidth(216)` —— **载体降到 8 B 后该前提整体反转** | 改为 `rowWidth <= 32` **且** `planWidth(8) < rowWidth(32)`；`Assert.Equal(32, rowWidth)` 保留，并**新增** `Assert.Equal(8, planWidth)` |
| `InterproceduralPlanArrayPoolTests.cs` | 护"租借 == 归还" | **整文件删除**（见 §12.2），其"夹具越不过门槛"的假绿/变红风险随之消失 |

**(c) 无需改动 —— 真正的验收判据**

`DataFlowPlanConstructionReuseTests.cs:41-66` 的三组冻结哈希**零改动**（§4.3(c) 的预测成立）。

### 12.4 §7 验收判据的实际结果

| # | 判据 | 结果 |
| --- | --- | --- |
| 1 | 冻结边序 oracle 原样通过、文件零改动 | ✅ `CpgInterproceduralEdgeOrderTests` 全绿；`git status` 确认 oracle 文件**未被本会话修改** |
| 2 | 冻结 `NormalizedGraphHash`/`PublicationHash` 不变 | ✅ `DataFlowPlanConstructionReuseTests` 零改动通过 |
| 3 | **新增**：导出的 `BridgeKind` == 构造期实际使用的 | ✅ 新增 Fact `BridgeKindOf_DerivesEveryBridgeKindFromPoolEndpointKind`（`InterproceduralEdgeIndexSnapshotTests`），对三段各断言一次，并断言三段目标种类**两两互斥**（这正是导出无歧义的依据） |
| 4 | 非正预算仍抛 `ArgumentOutOfRangeException` | ✅ `NonPositiveBudget_KeepsFrozenOutOfRangeContract`（3 个 Theory 用例）通过 |

### 12.5 构建与测试命令（与 §10 一致）

```powershell
# 从仓库根执行
& ./Build/Tools/Invoke-SerialDotnet.ps1 build ./src/NLCPG/NLCPG.csproj --no-restore --property:UseSharedCompilation=false
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore --property:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~InterproceduralPlanCompactionTests|FullyQualifiedName~DataFlowPlanConstructionReuseTests|FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests|FullyQualifiedName~NLCPGNodeIdContractTests'
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore --property:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgWorkBatchInterproceduralTests'
```

**实测结果**：`NLCPG.csproj` **0 警告 0 错误**；定向过滤 **93 通过 / 0 失败 / 93 总计**、22 s；
跨 DOP oracle **4 通过 / 0 失败**、2 s。§10 提到的 `InterproceduralPlanArrayPoolTests` 已随 §12.2 退役，
故过滤器**不再并入**它。

### 12.6 本轮**未做**的事（与 §1、§8 一致）

- **未做任何前置测试**（无冻结基线、无变异验证、无 oracle 固化）；
- **未做分配量 A/B、未跑全量语料、未测墙钟** ⇒ **本项仍不产出任何内存收益数字**；
  §6 的 27× / 6.2× / 8.31 GiB 全部**仍是算术推算**，未经实测确认；
- **未做变异证明**：§7 判据 3 的判别力**只由断言构造保证**，未用"把 `BridgeKindOf` 改坏"的变异实测过。
  这是本轮唯一未做的护栏强度验证，按 §1 的裁剪属于预期范围，故在此**显式记录**而非默认。

