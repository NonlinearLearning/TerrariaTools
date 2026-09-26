# 多 DOP 下 CPG 构建的数据竞态：根因与修复方案选型

- 日期：2026-09-24
- 触发：Gate G4（`docs/plans/2026-09-24-unified-work-scheduler-execution.md` Task S4-3）
- 状态：**已复现，未修复**；`src/NLCPG/Builder/Passes/**` 正在被并行优化，本文只做分析与选型，不含补丁。

---

## 1. 现象

在「多方法批 + ≥2 worker」时构建 200 方法以上的文件，抛：

```
System.InvalidOperationException:
Operations that change non-concurrent collections must have exclusive access.
A concurrent update was performed on this collection and corrupted its state.

  at NLCPGBuilder.GetOrCreateOperationNode(IOperation, NLCPGGraph)   NLCPGBuilder.cs:1446
  at NLCPGBuilder.AddMappedOperationNode(IOperation, ISet<NLCPGNode>, NLCPGGraph)
                                                                     DominancePass.cs:529
  at NLCPGBuilder.MapNodesByBlockOrdinal(...)                        DominancePass.cs:493 / :501
  at NLCPGBuilder.AnalyzeDominanceRoot(...)                          DominancePass.cs:402
  at NLCPGBuilder.<>c__DisplayClass278_0.<RunDominancePassWithWorkBatches>b__7
                                                                     DominancePass.cs:363
  at CpgWorkBatchExecutor.WorkerLoopAsync[TResult](...)              CpgWorkBatchExecutor.cs:588
```

**特征**：不确定。同参数重复运行会时通时败（实测 3 次运行 2 次失败，且失败的用例不同）。

---

## 2. 根因

### 2.1 直接原因：无锁「读—改—写」共享字典

`NLCPGBuilder.cs:1444-1466`：

```csharp
private NLCPGNode GetOrCreateOperationNode(IOperation operation, NLCPGGraph graph)
{
    if (_operationNodesByOperation.TryGetValue(operation, out var cachedNode))  // :1446 读
    {
        _operationNodeCacheHitCount += 1;                                       // :1448 非原子自增
        return cachedNode;
    }

    _operationNodeCacheMissCount += 1;                                          // :1452 非原子自增
    var operationNode = graph.AddNode(new NLCPGNodeDraft(...));                 // :1454
    _operationNodesByOperation[operation] = operationNode;                      // :1464 写
    return operationNode;
}
```

`_operationNodesByOperation` 是**普通 `Dictionary`**（`NLCPGBuilder.cs:67`，`ReferenceEqualityComparer`）。
两个 worker 同时进入 `TryGetValue` / 索引器赋值，即破坏其内部状态。
`_operationNodeCacheHitCount` / `MissCount` 是普通 `int` 的 `+= 1`，并发下会丢更新（影响统计，不致命）。

### 2.2 为什么 worker 会走到这里

`RunDominancePassWithWorkBatches` 把 `AnalyzeDominanceRoot` 放在**工作批 worker 回调**里
（`DominancePass.cs:357-371`，`processBatch` 即 `b__1`）：

```csharp
var batchResults = _workBatchExecutor.ExecuteAsync(
  workBatches,
  (batch, _, _) =>                       // ← 这段跑在 worker 线程
  {
      var roots = batch.Items.OrderBy(item => item.StableOrder)
        .Select(item => rootsByOrder.TryGetValue(item.StableOrder, out var rootPlan)
          ? AnalyzeDominanceRoot(context, rootPlan)   // :364
          : null)
        ...
  },
  stageId: CpgWorkBatchPerformanceStageId.Dominance).GetAwaiter().GetResult();
```

调用链：`AnalyzeDominanceRoot`(:402) → `MapNodesByBlockOrdinal` → `AddMappedOperationNode`(:529)
→ `GetOrCreateOperationNode`。

### 2.3 一个关键反差：这个 pass **已经**是「worker 收集 → reducer 提交」的形状

`AnalyzeDominanceRoot` 本身为每个方法建**局部图**，只把 `LocalCpgFragment` 交出去：

```csharp
var localGraph = new NLCPGGraph(...);                       // :399-401
var nodesByBlockOrdinal = MapNodesByBlockOrdinal(controlFlowGraph, methodSymbol, localGraph);
...
localGraph.FreezeQueryIndex();                             // :432
var descriptors = localGraph.Nodes.Select(CpgNodeDescriptor.FromNode).ToArray();
var edges = localGraph.Edges.Select(... CpgEdgeCandidate ...).ToArray();
return new DominanceRootResult(rootPlan.Order, overlay, fragment);   // :468
```

归并在**单线程** reducer 上完成（`DominancePass.cs:377-379`）：

```csharp
new CpgFragmentReducer().ReduceInto(context.Graph, roots.Select(result => result.Fragment));
```

而 `CpgWorkBatchExecutor` 的骨架正是「N 个 worker 跑 `processBatch` + **1 个** reducer 任务跑
`reduceResult`」：worker 在 `CpgWorkBatchExecutor.cs:437-450` 用 `Task.Run` 起，
reducer 在 `:430-436` 单起一个任务，`ReduceFragmentsAsync`(:629) 在 `:648` **串行**调用 `reduceResult`。

⇒ **局部图 + fragment 归并的设计是对的；漏的是那一处共享缓存写。**

### 2.4 同类调用点：正确的写法长什么样

同一套框架里，其他 pass 把节点物化放在 **reduce 回调**（单线程）里，是正确的：

| Pass | 是否在 worker 里写共享状态 | 证据 |
| --- | --- | --- |
| `PartitionedOperationPass` | ❌ 否（正确） | `processBatch` 只 `AnalyzeOperationPartition`（读 SemanticModel）；`GetOrCreateOperationNode` 在 reduce 回调 `:177-191` |
| `CallGraphPass` | ❌ 否（正确） | `processBatch` = `CollectCallGraphWorkBatch`（只收集 fact）；`PublishCallGraphWorkBatch` 在 reduce 回调 `:84`，内部 `:143/:149` 才建节点 |
| `MemberAccessPass` | ❌ 否（正确） | `processBatch` 只收集 fact `:80-83`；reduce 回调 `:84-96` 内 `:88` 建节点 |
| `ControlDependencePass` | ❌ 否（正确） | `CollectControlDependenceFragment` 是 **`static`**（`:72`），只建局部图，**不调用** `GetOrCreateOperationNode` |
| **`DominancePass`** | ✅ **是（缺陷）** | 见 2.2 |
| **`ControlFlowPass`** | ✅ **是（同类，见下）** | `processBatch` `:48` → `CollectControlFlowFragment` `:54` → `:63` `AddMethodLevelControlFlow(context, localGraph, order)` → `:169/:190/:201` 调 `GetOrCreate*Node(..., graph)` |

`ControlFlowPass` 这条**是同一个缺陷类**（我按调用路径读出来的，**未观测到它实际崩溃**——
三次复现的堆栈都在 dominance；这很可能只是采样/时序，**不能当成它安全**）：

`AddMethodLevelControlFlow(context, localGraph, selectedRootOrder)`（`ControlFlowPass.cs:148-153`）
在 worker 里执行，而它会写一批 **builder 级**共享字典：

- `_operationNodesByOperation`、`_operationNodeCacheHitCount` / `MissCount`（经 `:190/:201`）
- `_methodEntryNodes` / `_methodExitNodes` / `_methodParameterNodes` / `_methodReturnNodes`
  （`MethodDecorationPass.cs:162/109/138` 等）
- `_methodOwnerSymbolKeysByBoundaryNode`、`_methodParameterOrdinalsByNode`
  （`MethodDecorationPass.cs:128-129`）
- `_symbolNodes`、`_symbolKeysByNode`（经 `GetOrCreateSymbolNode` `NLCPGBuilder.cs:1697-1698`）
- `_methodSymbolsByFullName`、`_methodSymbolsByNameAndSignature`（经 `RegisterMethodSymbol`）

**这就是为什么「只给那一个字典加锁」不够**：worker 可达的共享写点有十几个。
`NLCPGBuilder.cs:48-59` 全是普通 `Dictionary`。

### 2.5 为什么归属是「未提交改动引入」

`git show HEAD:src/NLCPG/Builder/Passes/DominancePass.cs` **不含任何 `WorkBatch` 字样**
（HEAD 版 614 行，工作树版含 8 处 `WorkBatch`）；
`HEAD:src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs` **不存在**。
⇒ 并行 work-batch 路径属未提交新增。

⚠️ 但 `_operationNodesByOperation` 在 **HEAD 就已**是普通字典，且 HEAD 上也有
`_concurrencyPool.ForEachAsync`（`CpgShardBuildCoordinator.cs:98`）。
**HEAD 上该字典的调用者是否全部串行，我没有核查**，故**不声称 HEAD 无同类问题**。

---

## 3. 复现条件（实测）

必须**两个条件同时成立**：

1. **多个方法批**（>1 个 work-batch）
2. **≥2 个 worker 同时进入该 stage**

| 配置 | 结果 |
| --- | --- |
| 多批 + DOP=1 | 4/4 通过（竞态不可达） |
| 单批(`int.MaxValue`/批) + DOP=2/4/8 | 10/10 通过（每 stage 仅 1 批 ⇒ 仅 1 worker） |
| 多批 + DOP=4/8 | 失败（不稳定） |
| 生产默认 `WorkBatchMaxMethodsPerBatch=64`，DOP=8/200 方法 | **失败** |
| 同上，DOP=16/200、DOP=16/400 | **失败** |
| 同上，DOP=8/70 方法 | 未复现（批太少，重叠窗口不足） |

**生产可达性**：默认每批 64 个方法，故**任何 >64 方法的文件都会产生多批**，
无需调阈值即触发。真实代码库（如 `NPC.cs`）远超此规模。

诊断证据（用 `ICpgWorkBatchPerformanceEventSink` 记录 `WorkerIndex` / `PeakActiveWorkerCount`）：

```
dop=8  stage=CPG.WorkBatch.Dominance        batches=1 distinctWorkers=[0]   maxPeakActive=1
dop=8  stage=CPG.WorkBatch.ControlDependence batches=2 distinctWorkers=[0,1] maxPeakActive=2
```

⇒ 「DOP 调大」并不等于每个 stage 都并行：**并行度由批数决定**。

---

## 4. 修复方案

共同前提（决定了哪些方案可行）：

- ✅ `CpgFragmentReducer.ReduceInto` **已按 `StableNodeAnchor` 去重**
  （`CpgFragmentReducer.cs:21,45`：`materializedDescriptors.TryAdd(descriptor.Anchor, descriptor)`），
  且在**单线程** reducer 上跑。所以「两个 worker 各自造出同锚节点」在归并后仍收敛为同一节点。
- ✅ 局部图与主图**共享** `IdentityFactory` / `StringTable`（`DominancePass.cs:399-401`），
  故同一 operation 在局部图与主图算出的稳定锚一致。
- ⚠️ worker 内若对**主图**调用 `graph.AddNode`，同样会破坏 `NLCPGGraph`
  （`_nodesByOrdinal` / `_mutableNodesByAnchor`，`NLCPGGraph.cs:139-147`）。
  当前 dominance/controlflow 传的是**局部图**，所以这一条暂未爆；
  但任何「让 worker 直接写主图」的方案都不成立。

### 方案 A：在 Builder 侧给共享缓存加锁（最小改动、可只改一个文件）

**做法**：`NLCPGBuilder` 内新增一个 `_cacheGate`，把 `GetOrCreateOperationNode` 及
其他 `GetOrCreate*` / `Register*` 的「读—改—写」整段放进 `lock`；计数器改
`Interlocked.Increment` 或一并放进锁内。

**优点**
- 改动集中在 `NLCPGBuilder.cs`，**不需要碰正在被优化的 `Passes/**`**。
- 语义不变：同一 operation 仍返回同一节点实例（这是跨 pass 图一致性的前提）。
- 一次覆盖所有同类调用点（含 2.4 列出的那十几个字典）。

**代价 / 风险**
- 全局串行点。锁内包含 `graph.AddNode`（字符串 interning 另有自己的锁 ⇒ 锁嵌套，
  但顺序固定为 builder→interner，`StringInterner`（`:7-8`）不回调用 builder，**无死锁**），
  以及 `ResolveOperationName/FullName/Signature`（遍历语法/操作树，可能不便宜）。
  **高 DOP 下的吞吐损失未实测**，需补测。
- 锁的覆盖面必须完整：漏一个共享写点就仍有洞（见 2.4 的清单）。
  这类「靠人记忆」的完备性本身就是风险。

### 方案 B：worker 私有缓存 + reducer 合并（贴合既有架构）

**做法**：每个 worker（或每个 batch / 每个 root）持有自己的 `Dictionary<IOperation, NLCPGNode>`；
worker 只写本地字典，把「operation → 节点」随结果返回；reducer（单线程）合并进 builder 缓存。

**优点**
- 无锁竞争，可随 DOP 扩展。
- 与 `PartitionedOperationPass` / `CallGraphPass` / `MemberAccessPass` 已有的
  「worker 收集 → reducer 提交」形状一致，是**这些 pass 本来就在用的模式**。
- 由 4 节前提：同一 operation 归并后身份一致，故不改变图语义。

**代价 / 风险**
- 改动面较大：per-worker 缓存要穿透到各 pass 的 processBatch。
- 需要论证「同一 operation 不会跨 worker 出现」。按「operation 归属唯一方法树」通常成立，
  但**需验证**（局部函数、循环条件在多个 CFG block 中出现等）。
- 仍需处理 worker 内 `graph.AddNode`：只允许写局部图。

### 方案 C：让 dominance 改用 pass 级本地缓存（针对本处的最小正确改法）

**做法**：`AnalyzeDominanceRoot` 已经是「局部图 → fragment → 按锚归并」。
把 `MapNodesByBlockOrdinal` 用到的缓存从 builder 级换成 **per-root 本地字典**
（或干脆不缓存），worker 不碰任何共享状态。

**优点**
- 改动局限在 dominance 一个 pass，无需锁。
- 与 `ControlDependencePass.CollectControlDependenceFragment`（`static`、只建局部图）的
  正确写法对齐。

**代价 / 风险**
- **只修 dominance**：`ControlFlowPass`（2.4）的同类问题仍在。
- 作为最终方案不完整，只能算局部分步。

### 方案 D：受影响 stage 退回有序串行路径（临时围堵）

**事实**：代码里**已有**语义对照实现——
`RunDominancePassOrderedCompatibility`(`DominancePass.cs:295`)、
`RunPartitionedOperationPassOrderedCompatibility`(`PartitionedOperationPass.cs:62`)。

**做法**：当 `workerCount > 1`（或该 stage 批数 > 1）时走 ordered 路径；
或把这两个 stage 的有效并发设为 1。

**优点**
- 立刻安全，零锁开销，复用已有代码。
- 不需要在 pass 优化期间制造冲突（可只在 Builder/executor 侧决定）。

**代价 / 风险**
- **是压制不是修复**：放弃这些 stage 的并行收益（占比**未知**）。
- 「worker 不得写共享状态」这条不变式继续被隐藏，S3-1 迁移时会再踩一次。

### 方案 E：把不变式显式化并可在测试期主动发现（架构目标）

**不变式**：**`processBatch`（worker 回调）只读共享状态；一切节点/边/图的变更加在
reducer（单线程）或并入 fragment 返回。**

**做法（择一或并用）**
1. 收窄 `processBatch` 的类型：只允许返回纯数据（fact/fragment），
   从签名上让 worker 拿不到 `NLCPGGraph` 与 builder 的 `GetOrCreate*`。
2. 运行期护栏：`NLCPGGraph.AddNode` / `AddEdge` 记录 owner 线程；
   worker 阶段（或非 owner 线程）写入即抛明确异常，而不是等字典内部状态崩掉。
   这能把「下一次同类回归」从「不确定崩溃」变成「确定性失败」。

**优点**
- 修**一类**而非一处；S3-1（8 个 pass 迁移）与 pass 优化都能直接受益。
- 让缺失的不变式变成可执行断言。

**代价 / 风险**
- 改动最大，需与该 pass 优化工作协调排期。
- 需要确认现有合法路径（如 reducer 线程写主图、worker 写局部图）不被误伤。

### 方案取舍

| 方案 | 改动面 | 立即安全 | 修一类问题 | 吞吐代价 | 推荐定位 |
| --- | --- | --- | --- | --- | --- |
| A 加锁 | 小（Builder 单文件） | ✅ | 部分（依赖人工列全） | 未实测，可能显著 | **短期围堵**（不碰 Passes） |
| B 私有缓存 + 归并 | 中 | ✅ | ✅ | 低 | **目标落地方式**，随 S3-1 |
| C dominance 本地缓存 | 小 | ✅ | ❌（留 ControlFlow 洞） | 低 | 只作分步之一 |
| D 退回串行 | 极小 | ✅ | ❌ | 放弃并行收益 | **临时围堵**（最保守） |
| E 显式不变式 | 大 | —（防回归） | ✅ | 无 | **架构目标**，与 A/B 并用 |

**建议路径**

1. **现在**：`Passes/**` 正在优化 ⇒ 优先 **D**（最保守，改动最小且可只落在 executor/builder 侧），
   或 **A**（若接受 Builder 侧加锁）。二者都**不需要改 pass 文件**。
2. **短期**：补 **A 的吞吐实测**（高 DOP 下 dominance/controlflow 的墙钟时间与并行度），
   否则「加锁会不会把并行收益吃掉」没有依据。
3. **目标**：按 **E** 立不变式，用 **B** 落地；与 S3-1 同一批完成，避免二次迁移。

**明确不推荐**：把 **C** 当作最终方案——它只修 dominance，`ControlFlowPass` 同类洞仍在，
而该洞是否可实际触发**尚未验证**，风险留在地下。

---

## 5. 已落地的判据（防回归）

- `tests/NLISSN.ContractTests/Cpg/DocumentShardingEquivalenceTests.cs`
  —— Gate G4 的边序敏感 oracle。**5 通过 / 2 Skip**。
  被竞态阻塞的用例带 `Skip` 原因字符串，**修复后须去掉 Skip**。
- `tests/NLISSN.ContractTests/Cpg/DominanceRaceReachabilityProbe.cs`
  —— 生产默认配置下的可达性复现（4 组参数 `Skip`）。这是本缺陷目前唯一的可达复现，
  修复者应改用它验证。
- **不得**为了变绿而：删除断言、把边序比较改成集合比较、或移除 `Skip` 而不修代码。

## 6. 未验证边界

- `ControlFlowPass` 的同类共享写**未观测到实际崩溃**，只有调用路径证据。
- 加锁方案（A）的吞吐代价**未实测**。
- 各 stage 在总耗时中的占比**未知**，故「退回串行损失多少」无法量化。
- HEAD 上 `_operationNodesByOperation` 的调用者是否全串行**未核查**。
- 「同一 operation 不跨 worker」**未验证**（方案 B 的前提）。
- 首次崩溃位置是否总在 dominance（三次复现均是）**样本太小**，不代表其他写点不会先崩。
