# PartitionedSyntax / PartitionedOperation 跨文件聚合设计

日期：2026-09-27。状态来源：[`Context/feature_list.json`](../../Context/feature_list.json)。

> ## ⛔ 实施状态：**暂不实施**（2026-09-27 决定）
>
> **本文档保留为设计记录，不作为执行授权。** 决定与理由见 §0。
> 未修改任何产品代码；未运行构建或测试。
>
> 若要重新激活，**唯一前置条件是先测出阶段耗时表**（§0.2）。
> 在该表被读出之前，不得据本文档开工。
>
> ---
>
> 本文档提出一项**调度层**改造：把 `Syntax` 与 `Operation` 两个阶段从「每文件一次
> `ExecuteAsync`」改为「整批一次 `ExecuteAsync` + 按源文件流式物化」，使之与
> `CallGraph`/`ControlFlow`/`MemberAccess`/`Dominance`/`ControlDependence` 的既有形态
> 一致。改造**不改变节点、边或语义**，只改变「谁和谁共享同一个 worker 池」。
>
> 本文档中所有源码引用均于 2026-09-27 实读核对；所有「收益」均为**待测量**，
> 不构成性能承诺。
>
> ⚠️ **行号非稳定锚**：核对期间 `NLCPGBuilder.cs` 正被**另一并发写入者**编辑
> （23:00 采样时 4623 → 4647 行）。下文行号取自 2026-09-27 23:00 的读取，
> **结构性事实（函数名、调用关系）已逐条复核仍成立**；引用时请以符号名为准，
> 行号仅作定位辅助。本仓库已有同类教训（
> [D1 执行计划](2026-09-26-workbatch-shard-packing-execution.md) §1.7）。
>
> 关联：[NLCPG WorkBatch 并发设计](../CodeDesign/目前设计/cpg-workbatch-concurrency.md)、
> [D1 跨文件凑标准大小执行计划](2026-09-26-workbatch-shard-packing-execution.md)、
> [统一 Work Scheduler 执行计划](2026-09-24-unified-work-scheduler-execution.md)（附录 X.6 边界 2）。

## 0. 决定：暂不实施

**结论：本计划优先级下调，暂不实施。** 设计本身自洽（§1～§9），但**收益/成本比不支持开工**。

### 0.1 四条理由

| # | 理由 |
| --- | --- |
| **R1** | **省不掉串行工作。** 本设计（§4.3）的相位 A 仍**逐文档串行**建分区、填分区外事实。真正省掉的只有池的启停——约 2 万次 `Task.Run`，相对 71 s 量级的构建**可忽略**。 |
| **R2** | **被当作卖点的 93.2% 是误读。** 「池在 93.2% 的墙钟时间里不存在」的原因恰恰是**主线程在串行干活**；聚合池**不会**消灭这段时间。§1 原先把这条列为「直接成因」，口径偏乐观，以 R1 为准。 |
| **R3** | **主要成因不在本阶段。** 诊断的头号结论是 `FreezeQueryIndex` 占 **55.1% 墙钟、单线程 0.85 核 / 12**；另有 `InterproceduralDataFlow`、`MethodDecoration` 等串行相位。本改造**一行都碰不到**它们。 |
| **R4** | **有净恶化风险。** 归并后 reducer 成为「995 个文件物化」的单线程串行点，尾延迟可能**更差**；相位 A 的 `partitions` 数组需同时持有全部文件（§7.3 已记账）。 |

### 0.2 重新激活的唯一前置条件

**先读出阶段耗时表**：`NLCPGBuildMetrics.PassElapsedMilliseconds` 或 `runtime.log` 的阶段表。

- 若 `Syntax` + `Operation` 之和**占比不大**（预期如此）⇒ **归档本计划**，把力气投向
  `FreezeQueryIndex` 与串行相位。
- 仅当这两阶段确为墙钟大头时，本计划才值得重新评估。

⚠️ 该表是**唯一能定案的证据**，且在本次设计轮次中**从未被读出**。在它被读出之前，
§1 的「建议做」与 §6.1 的收益预期均**无实测支撑**。



## 1. 范围

> ⚠️ 本节原为「结论与范围」，其中「**建议做**」的结论**已被 §0 取代**。
> 以下内容仅保留**范围界定**，供将来重新激活时复用。

本改造的对象是：`Syntax` 与 `Operation` 是**唯二**仍按文档循环调用 `AssembleWorkBatches(context)`（单文档版）
的阶段。995 文件构建下，这两个阶段各自产生约 995 个独立的 worker 池，每个池平均存活极短
（既有实测：池均寿命 250.2 ms、`batchesPerPool = 2.45`、池在 93.2% 的墙钟时间里不存在）。
每次 `ExecuteAsync` 都真实创建 `MaxDegreeOfParallelism` 个 `Task.Run` worker，跑完即全部拆除。

⚠️ 上段末句原写「这正是『中间阶段 CPU 与内存双低』的直接成因之一」——**该归因已被 §0 R1/R2 修正**：
池不存在的那段时间正是主线程在串行干活，聚合池不消灭它。

范围**严格限于**：

- `PartitionedSyntaxPass.RunSyntaxPass` 的批次装配与归并窗口；
- `PartitionedOperationPass.RunPartitionedOperationPassWithWorkBatches` 的批次装配与归并窗口；
- 二者所需的 `_partitionedSyntaxFacts` 生命周期调整。

**明确不在范围内**（见 §9）：不改 `CpgWorkBatch`/`CpgWorkItem` 契约，不改
`CpgWorkBatchBuilder` 装箱算法，不改 `ShardOrder` 解析，不改图语义，不把这两个阶段搬进
规划相位（那是 X.6 边界 2 明确划出的另一类改动）。

## 2. 判据：已核实的当前事实

| # | 事实 | 证据（2026-09-27 实读） |
| --- | --- | --- |
| F1 | `Syntax` 阶段在**一个** `MeasureStage` 内 `foreach (var document in documents)`，逐文档调 `RunSyntaxPass` | `NLCPGBuilder.cs:1265-1285` |
| F2 | `Operation` 阶段同样逐文档循环调 `RunPartitionedOperationPass` + `CompleteOperationBackedSyntaxTypes` | `NLCPGBuilder.cs:1310-1323` |
| F3 | Syntax 用**单文档** `AssembleWorkBatches(context)` | `PartitionedSyntaxPass.cs:52` |
| F4 | Operation 用**单文档** `AssembleWorkBatches(context)` | `PartitionedOperationPass.cs:68` |
| F5 | 姊妹阶段全部用 `AssembleWorkBatchesAcrossDocuments(context)` | `ControlFlowPass.cs:77`、`MemberAccessPass.cs:83`、`DominancePass.cs:391` |
| F6 | 跨文件装配能力**已存在且已在生产使用** | `PartitionedOperationPass.cs:344-383` |
| F7 | 按源文件路由的宿主**已存在** | `Concurrency/SourceFilePartition.cs`：`SourceRoutedGroup<T>` + `BySourceFile` |
| F8 | 按项解析 context/图的入口**已存在** | `NLCPGBuildContext.ResolveDocument:101`、`ResolveGraph:121` |
| F9 | 归并回调**合法**且**已经**在图线程上写图 | `CpgWorkBatchExecutor.cs:639-644`（L2 是唯一合法图写入者）；`PartitionedOperationPass.cs:88-133` 已在 reducer 内 `MaterializeOperationPartition` |
| F10 | 归并窗口只开**共享态**窗口，刻意**不**开图侧窗口 | `CpgWorkBatchExecutor.cs:641-644` |
| F11 | `_partitionedSyntaxFacts` 是**构建级**单例字典，逐文件填充后 `Clear()` | `NLCPGBuilder.cs:108`；`PartitionedSyntaxPass.cs:80` |
| F12 | 分区性能事件 ID **不含文件路径** | `NLCPGBuilder.cs:2223-2230`：`$"{stage}:{partitionIndex}:{spanStart}-{spanEnd}"` |

### 2.1 一条与会话历史推理相反的事实（重要）

先前一轮分析（见 §10 附录）曾推断「NodeId 按创建顺序分配，故跨文件物化交错会改变图」。
**该推断不成立**，源码给出的结论恰好相反：

- `NodeId` 由 `DeterministicNodeIdTable.Create` 按
  `(Kind, FilePathId, SpanStart, SpanEnd, Role, Ordinal, ExtraKeyId)` **排序后连续分配**
  （`DeterministicNodeIdTable.cs:16-37`）——是**稳定锚点的纯函数**，与创建顺序无关。
- 边在 `AssignDeterministicNodeIds` 中经 **4 趟稳定基数排序**
  （`metadataRank → target → kind → source`）落到 canonical 序
  （`NLCPGGraphIndex.cs:498-514`），再以 `MetadataId` 值去重（`NLCPGGraph.cs:1179`）。
  排序键**不含插入序**，故同一条边无论何时加入都落到同一位置。
- `graph.Nodes` 暴露的是**输入序**视图，但该序只影响枚举顺序，而 `GraphSnapshotVersion`
  是对 **canonical（NodeId 升序）节点序 + canonical 边序**求哈希
  （`NLCPGGraphIndex.cs:1147-1185`），故对输入序免疫。

⇒ **图的可观测产物（`GraphSnapshotVersion`、NodeId、边集）对「文件物化交错顺序」不敏感。**
这把设计从「必须逐字节复刻原物化顺序」降级为「只需保证**同一文件内**的分区/方法次序不变」，
是本设计能成立的关键前提。

⚠️ **边界**：上述结论仅覆盖 `GraphSnapshotVersion`、`NodeId` 与边集合。**不覆盖**性能事件序列、
`_partitionedSyntaxFacts` 的填充时序，以及任何按「插入序」遍历 `graph.Nodes` 且**顺序敏感**的
下游消费者（见 §7.2 待验证项 V3）。

## 3. 不变条件

改动的安全前提是以下不变量**逐条保持**。任何一条被破坏，失效形态都是**静默**的。

1. **单文件构建行为逐字不变。** `context.DocumentSet is null` 时，两条路径必须回到原实现。
   这是仓库对 D1 系列改造的一贯要求（`AssembleWorkBatchesAcrossDocuments:348-352` 即此先例）。
2. **同一文件内的分区次序不变。** 归并时对每个文件分组，组内必须按 `StableOrder` 升序处理，
   与改造前 `batch.Items.OrderBy(item => item.StableOrder)`（`PartitionedSyntaxPass.cs:128`）
   语义一致。
3. **`_partitionedSyntaxFacts` 必须按文件消费、用完即清。** 这是用户明确选择的峰值内存策略
   （见 §10.2）。不得改为「保留全部文件事实到物化结束」。
4. **worker 仍然只读。** worker 不得写图、不得写 builder 共享态。跨文件分组后，
   worker 必须用 `item.SourceFilePath` 解析**该项自己**的文档与语义模型，
   不得沿用外层 `context`（`SourceFilePartition.cs:7-17` 记录的正是这个失效形态）。
5. **归并回调是唯一写图者，且只在共享态窗口内。** 不得为物化而开图侧窗口（F10）。
6. **`ShardOrder` 唯一性不受影响。** 装配必须继续走 `BuildWorkBatches`（`NLCPGBuilder.cs:589`），
   它是 `ShardOrder` 的**唯一解析点**。
7. **性能事件 ID 必须补上文件身份**（F12）。否则跨文件聚合后，不同文件的同名分区会产出
   **相同的事件 ID**，使诊断数据互相覆盖。
8. **`LastSyntaxPassTelemetry` 的语义必须显式定义。** 改造前它是「最后一个文件的 metrics」
   （逐文件覆盖）；聚合后「最后」不再等于文档序最后一项（见 §7.1）。

## 4. 设计方案

### 4.1 共同形态（与姊妹阶段同构）

```text
规划/装配（串行，一次）
  └─ AssembleWorkBatchesAcrossDocuments(context)   → 一个跨文件批次列表（ShardOrder 全局唯一）

执行（一次 ExecuteAndConsumeAsync）
  ├─ worker：对批次内每个「文件分组」分别产出独立结果
  └─ reducer：按 ShardOrder 升序，逐分组 fold → 物化 → 清该文件事实
```

这与 `ControlFlowPass.CollectControlFlowFragments`（`ControlFlowPass.cs:106-117`）的形态同构：
**一个批次仍只产出一个结果对象**（执行器的归并/度量契约不变），但**结果内部已按文件分离**。

### 4.2 Operation 阶段

Operation 是两者中**风险更低**的一个，理由：它的物化**本来就在 reducer 内逐批流式进行**
（`PartitionedOperationPass.cs:88-133`），改动只是把「单文件批次」放宽为「跨文件批次」。

**改动点：**

1. `AssembleWorkBatches(context)` → `AssembleWorkBatchesAcrossDocuments(context)`（`PartitionedOperationPass.cs:68`）。
2. worker 内按 `SourceFilePartition.BySourceFile(batch.Items)` 分组，每组用
   `context.ResolveDocument(group.SourceFilePath)` 取该文件的 `SemanticModel`，
   并按该文件的 `operationRootsByOrder` 解析 root。
   `operationRootsByOrder` 需从**单一扁平表**改为**逐文件表**
   （`Dictionary<string, Dictionary<int, OperationRootPlan>>`），形态照抄
   `DominancePass.cs:402-407`。
3. `OperationPartitionResult` **新增 `SourceFilePath`** 字段，使 reducer 能把结果路由到
   `context.ResolveGraph(sourceFilePath)`。
4. reducer 内用 `context.ResolveGraph(partition.SourceFilePath)` 替代写死的 `context.Graph`。
5. `context.AddOperationInventoryEntry(...)` 需确认落到**该文件**的 context
   （`OperationInventory` 是 per-context 集合，见 `NLCPGBuildContext.cs:134-156`）。

⚠️ **第 5 点是本阶段最容易出错的地方。** `AddOperationInventoryEntry` 同时填充
`_operationInventory` 与 `_invocationOperations`/`_propertyReferenceOperations`/`_fieldReferenceOperations`
四个集合，而这些集合被 `CallGraph`（`CallGraphPass.cs:89-117`）与 `DataFlow`
（`NLCPGBuilder.cs:1965`）的**规划相位**消费。多文件下这些集合**按 context 分离**，
故 reducer 必须写**该项所属文件**的 context，而非驱动 context。

### 4.3 Syntax 阶段（较难，需拆两相位）

Syntax 与 Operation 的差别在于：**物化（`RunPartitionedSyntaxPass`）是当前在主线程串行完成的**，
且分区外语法事实由主线程预填。故需要拆成两个相位。

**相位 A（串行，逐文件准备）：**

对每个文档：

- `BuildSyntaxPartitions(operationRoots)`（`PartitionedSyntaxPass.cs:84-114`，已提取）得到
  `partitionRoots` / `partitions` / `partitionsByOrder`；
- 把这三个数组**按文件**存起来（`Dictionary<string, …>`）；
- 保留「分区外节点预填 `_partitionedSyntaxFacts`」这一步。

```csharp
// 逐文件准备（串行，与改造前同序）
var perFile = new Dictionary<string, SyntaxFilePlan>(StringComparer.Ordinal);
foreach (var document in context.Documents)
{
    var (roots, partitions, byOrder) = BuildSyntaxPartitions(GetOperationRoots(document));
    perFile[document.FilePath] = new SyntaxFilePlan(roots, partitions, byOrder);
    // 分区外节点仍在主线程补齐缓存（不变）
    foreach (var syntax in OutsidePartitions(document, partitions))
    {
        _partitionedSyntaxFacts[syntax] = AnalyzeSyntaxFacts(syntax, document.SemanticModel, buildPlan);
    }
}
```

**相位 B（一次执行 + 按文件流式物化）：**

```csharp
_workBatchExecutor.ExecuteAndConsumeAsync<SyntaxWorkBatchResult>(
  AssembleWorkBatchesAcrossDocuments(context),
  (batch, _, _) => CollectSyntaxFactsAcrossFiles(context, batch, perFile, buildPlan),
  batchResult =>
  {
      foreach (var group in batchResult.Groups)              // 文件分组
      {
          var document = context.ResolveDocument(group.SourceFilePath);
          var plan = perFile[group.SourceFilePath];

          // ① fold 该文件事实并记录性能事件
          foreach (var result in group.Items.OrderBy(r => r.Order))
          {
              foreach (var entry in result.Facts) { _partitionedSyntaxFacts[entry.Key] = entry.Value; }
              RecordPartitionPerformanceEvent(/* 补 SourceFilePath，见 §7.1 */);
          }

          // ② 物化该文件的图（reducer 是唯一写图者，合法）
          RunPartitionedSyntaxPass(document, plan.PartitionRoots, plan.Partitions, buildPlan);

          // ③ 立刻清掉该文件事实（峰值内存纪律）
          ClearSyntaxFactsFor(plan);
      }
  },
  stageId: CpgWorkBatchPerformanceStageId.Syntax).GetAwaiter().GetResult();
```

**关键取舍（如实记录）：**

- **为什么不复用 `ExecuteAsync` 的 `reduceResult`：** `ExecuteAsync` 会累计保留全部结果
  （`collectResults: true`，`CpgWorkBatchExecutor.cs:972`），跨文件聚合后这等于把**全部文件**的
  `SyntaxSemanticFacts` 同时钉在内存里，正好抵消本改造要守住的峰值边界。
  必须用 `ExecuteAndConsumeAsync`（`collectResults: false`）。
- **为什么相位 A 仍持有全部文件的 `partitions` 数组：** 这是本方案的**残余内存代价**，
  必须如实记账（§7.3）。`partitions` 是 `DescendantNodesAndSelf().ToArray()`，纯引用数组，
  但 995 文件下总量可观。彻底消除需要把相位 A 也移进 reducer，但那会与「worker 需要全部
  分区可用」冲突，属**另一个设计**，本方案不声称解决。

### 4.4 `_partitionedSyntaxFacts` 的按文件清理

`_partitionedSyntaxFacts` 是**单一共享字典**（F11），故「按文件清理」需要知道哪些键属于该文件。
两个可选形态：

| 形态 | 做法 | 代价 |
| --- | --- | --- |
| **A：记录该文件的语法节点集合** | 相位 A 时把每个文件加入字典的 `SyntaxNode` 收集成 `List<SyntaxNode>`，物化后逐个 `Remove` | 每文件一份节点引用列表（4~8 B/项） |
| **B：分区内 + 分区外分区键** | 字典值改为 `(SyntaxSemanticFacts Facts, string FilePath)`，清理时扫描 | 全字典扫描 O(N)，且增大每项载荷 |

**推荐 A**：额外内存与收益同阶，且清理是精确的 `Remove` 而非扫描。

⚠️ **正确性兜底（重要）**：`CreateSyntaxNode` 在缓存未命中时会回落到直接查询
（`SyntaxPass.cs:320-330`：`cachedFacts is null && CanDeclareSymbol(syntax)` → 查 `GetDeclaredSymbol`）。
故**即使清理时机写错、事实被提前清空，图内容仍然正确**，只是查询计数上升。
这意味着清理时机**在行为层不可证伪**——判据必须落在**计数层**
（`DeclaredSymbolQueryCount`、`SyntaxPassTelemetry`），与
[统一 Work Scheduler 执行计划](2026-09-24-unified-work-scheduler-execution.md) X.5/Y.4/AA 的同源教训一致。

## 5. 分步实施

> ⛔ **本节的 S1～S8 均未执行，且暂不计划执行（§0）。** 保留作为将来重新激活时的拆分参考。

每步都必须**独立可编译、独立可回归**。

| 步 | 内容 | 依赖 | 风险 |
| --- | --- | --- | --- |
| **S1** | `OperationPartitionResult` 加 `SourceFilePath`；`operationRootsByOrder` 逐文件化；reducer 用 `ResolveGraph` 路由 | 无 | 低 |
| **S2** | Operation 批次装配改 `AcrossDocuments` | S1 | 低 |
| **S3** | 性能事件 ID 补文件身份（Syntax + Operation，§7.1） | 无（可**先**做） | 低 |
| **S4** | `LastSyntaxPassTelemetry` 语义显式化（§7.1） | 无（可**先**做） | 低 |
| **S5** | Syntax 相位 A：抽出 `SyntaxFilePlan` 逐文件准备结构 | 无 | 中 |
| **S6** | Syntax worker：按文件分组采集事实 | S5 | 中 |
| **S7** | Syntax reducer：按文件 fold → 物化 → 清理 | S6 | **高** |
| **S8** | Syntax 批次装配改 `AcrossDocuments` | S7 | 中 |

**S3/S4 的地位（与 §0 的关系，需注意）**：它们是**独立的既有缺陷**——跨文件聚合后
性能事件 ID 碰撞（`CreatePartitionPerformanceId` 不含文件路径）与 `LastSyntaxPassTelemetry`
语义漂移。二者**只在多文件聚合真正启用后才成立**（S3 的碰撞需要「同一批次含多文件」，
S4 的漂移需要「跨文件归并顺序」）；既然聚合暂不实施，**它们当前不是活跃缺陷**，
不构成独立开工理由。仅当 §0.2 的条件被满足、聚合重新激活时，S3/S4 才应先行落地。

## 6. 验证

> ⛔ 本节为**将来重新激活时**的验证口径，未执行。

按用户此前裁定的验证口径（**执行 10% 核心测试**，见 §10.2）执行。核心切片命令见
[D1 执行计划 §7](2026-09-26-workbatch-shard-packing-execution.md)。至少须覆盖：

1. **单文件逐字等价**：`CpgWorkBatchSyntaxTests`、`CpgWorkBatchOperationTests` 全绿。
2. **DOP 等价**：`BatchSyntaxBuild_PreservesGraphSignatureAcrossDegreesOfParallelism`（DOP 1/2/16）。
3. **跨文件等价**：`CrossFileBatchEquivalenceTests` 四条全绿——尤其
   `BuildManyDocuments_DeclaredTypesStayInTheirOwnGraph`（路由写错即红）。
4. **遥测计数等价**：`SyntaxPassTelemetryContractTests`——判据是 legacy 与 partitioned
   **查询数必须一致**，这正是 §4.4 那条「行为层不可证伪」性质的判别面。
5. **分片序号契约**：`CpgWorkBatchShardOrderContractTests`。
6. **`GraphSnapshotVersion` 逐字节一致**：改造前后同一输入的快照哈希必须相同。
   这是**最强**的一条判据，也是 §2.1 结论的直接检验。

**必须新增的用例**（现有用例**不覆盖**新失效形态）：

- 跨文件聚合下**多文件** Syntax 阶段的 `GraphSnapshotVersion` 与逐文件构建一致；
- 性能事件 ID 在不同文件同名分区下**不碰撞**；
- `Operation` 的 `OperationInventory` 在跨文件批次下落到**正确文件**的 context。

### 6.1 需实测的量（不得预先断言）

> ⚠️ 本节列出的量**全部未测**。按 §0，**第一优先要测的不是这些，而是阶段耗时表**（§0.2）——
> 若 `Syntax`+`Operation` 占比不大，下面这些量就都不必测了。

- **阶段耗时表**（§0.2，**决定性**）：`NLCPGBuildMetrics.PassElapsedMilliseconds` / `runtime.log`；
- 池启动次数（995 文件下 Syntax+Operation 预期从 ~1990 降到 2 —— ⚠️ 按 §0 R1，
  该数字的**下降本身不构成收益证据**，须与串行相位耗时分开计）;
- `ReducerWaitElapsedMilliseconds` 与 `peakCompletedNotReducedCount`；
- 峰值工作集（重点看 §7.3 的 `partitions` 残余代价）；
- worker `IdleTime` / `ExecutedBatchCount` 分布。

⚠️ **不得**以「池数量下降」直接声称吞吐改善。依据
[cpg-workbatch-concurrency.md](../CodeDesign/目前设计/cpg-workbatch-concurrency.md) §14：
验收以**同一输入的对比**为准，且 reducer 单线程写图可能成为新瓶颈——聚合后 reducer
要连续物化 995 个文件的图，其尾延迟**可能变差**。这是本改造的**首要风险**。

## 7. 风险与边界

> ⛔ 以下风险分析**在聚合暂不实施（§0）的前提下不构成活跃风险**，保留供重新激活时复用。
> ⚠️ 但 §7.1 两项**在概念上仍是既有缺陷**：若将来有任何改动让 Syntax/Operation 批次跨文件，
> 它们立即变成真缺陷。

### 7.1 遥测与诊断（确定性会被破坏的地方）

| 项 | 改造前 | 改造后 | 处置 |
| --- | --- | --- | --- |
| 分区性能事件 ID | `syntax:{order}:{start}-{end}`，order 是**文件内局部**序号 | 跨文件下不同文件的同名 order **碰撞** ⇒ 事件互相覆盖 | **必须**补 `SourceFilePath`（S3） |
| `LastSyntaxPassTelemetry` | 最后一个**文档**的 metrics | 「最后被归并的文件」的 metrics，不再等于文档序最后 | **必须**显式定义（S4） |
| `RecordPartitionPerformanceEvent` 调用时机 | 全部批次归并完后、物化前 | 逐文件、物化前 | 保持在物化前以维持语义 |

### 7.2 待验证项（不得当作已解决）

- **V1**：是否存在按**插入序**遍历 `graph.Nodes` 且顺序敏感的消费者？§2.1 只证明了
  `GraphSnapshotVersion`/NodeId/边集不受影响。**需在实施前 grep 全部 `graph.Nodes` 消费点并逐个定性。**
- **V2**：`_syntaxNodes` 是构建级字典且**不按文件清理**（`NLCPGBuilder.cs:1145` 只在构建开始时 `Clear`）。
  跨文件聚合下它会同时持有全部文件的语法节点。这是**改造前就存在**的事实，本设计不改变它，
  但应在峰值内存记账中如实列出。
- **V3**：`SkeletonShardPublisher`（流式分片）与多文件互斥，已由
  `NLCPGBuilder.cs:1252` fail-closed 拒绝，故本改造不与其交互。
- **V4**：`Operation` 的 `StreamingPublisher` 路径（`PartitionedOperationPass.cs:111-116`）
  在跨文件下是否仍成立？多文件已被 fail-closed 拒绝，**但需确认单文件路径逐字不变**。

### 7.3 残余内存代价（必须如实记账）

| 结构 | 改造前峰值 | 改造后峰值 | 说明 |
| --- | --- | --- | --- |
| `SyntaxSemanticFacts` | 单文件（最大文件） | 在途批次（有界） | **改善**：这正是用户选定的目标 |
| 分区 `partitions` 数组 | 单文件 | **全部文件** | **恶化**：相位 A 的残余代价 |
| `_syntaxNodes` | 全构建 | 全构建 | 不变（V2） |
| `_partitionedSyntaxFacts` | 单文件 | 在途批次 | **改善**（按文件清理） |

⇒ **净效果必须实测**。相位 A 的 `partitions` 代价可能吃掉 `SyntaxSemanticFacts` 的收益，
这在 995 文件下是**真实的可能**。

## 8. 不采用的方案

### 8.1 简单聚合（保留全部事实到物化结束）

用户已在决策中**明确排除**（§10.2 选项二）。理由：`_partitionedSyntaxFacts` 峰值随构建文件数
线性增长，而该字典的载荷是 `SyntaxSemanticFacts`（含 `ISymbol` 引用），是最重的中间态。

### 8.2 只改 Operation 阶段

用户选项三，**未选**。仍作为**降级路径**保留：若 S7（Syntax reducer 物化）证明风险不可接受，
S1/S2 可独立交付并获得大部分收益（Operation 的物化本就在 reducer 内，增量内存明显更小）。

### 8.3 把 Syntax/Operation 搬进规划相位

**明确排除。** 依据
[统一 Work Scheduler 执行计划](2026-09-24-unified-work-scheduler-execution.md) X.6 边界 2：
这两者在**规划相位开启之前**就已执行完，其配额成因是 `ExecutedBeforePlanningPhase` 而非
`DeferredPlanning`；把它们「已前移」需要**重排执行顺序**，属另一类改动。
⚠️ 引用该边界时勿读成「将来会前移」。

### 8.4 在 worker 内写图

**排除。** 违反 L1 分层（`CpgWorkBatchExecutor.cs:641-644`）。本设计把全部物化留在 reducer。

## 9. 本轮边界（不得越读）

1. ⛔ **本计划暂不实施（§0）**。本文档是**设计记录**，不是执行授权；S1～S8 均未执行。
2. **本文档只交付设计**：未修改任何产品代码，未运行构建或测试。所有「改善/恶化」判断
   均为**源码推导**，不是实测结论。
3. **不声称性能收益**：§6.1 列出的量全部待测。池数量下降**不等于**吞吐提升，
   且按 §0 R1，池启停本身**大概率可忽略**。
4. **重新激活的唯一前置条件是阶段耗时表（§0.2）**，该表**从未被读出**。
5. **`GraphSnapshotVersion` 免疫性（§2.1）是推导结论**，其依据是
   `DeterministicNodeIdTable` 的排序键与边的基数排序键；**须由实测哈希复核**。
6. **V1/V2 未验证**，不得据此认为「顺序完全不可观测」。
7. **不改变** `CpgWorkBatch`/`CpgWorkItem` 契约、装箱算法、`ShardOrder` 语义、图语义、
   阶段顺序与 capability 判定。
8. **不触碰** `Miscellaneous/`、`tests/` 既有夹具、`设计docs/` 既有主题页。

## 10. 附录：决策来源与历史推理

### 10.1 来源会话

本设计的动机来自一次静态诊断会话（`session-03984125`，2026-09-27），其结论为
「中间阶段 CPU 与内存双低**不是**内存背压造成」：

- 字节背压**未接线**：`CpgWorkBatchExecutorOptions` 的
  `EffectiveMaxQueuedEstimatedCost/Bytes` 默认 `int.MaxValue`/`long.MaxValue`
  （`CpgWorkBatchExecutor.cs:100-102`），而 `NLCPGBuilder.cs:782-788` 构造执行器时
  **不传**这四个参数 ⇒ `CpgWorkBatchQueueBudget.AcquireAsync` 的 `fits` 分支恒真。
- 归并器**构造上几乎不产生背压**：`pending.Add` 无条件收下后按 `ShardOrder` 回调
  （`CpgWorkBatchExecutor.cs:978-986`），归并线程持续排空结果通道。
- 主因是**结构性**的：池按**阶段**启停（§2 F1/F2），加上若干纯串行相位。

### 10.2 用户裁定

会话中用户对两个问题作出选择（原文）：

| 问题 | 用户选择 |
| --- | --- |
| 峰值内存策略 | 「**分文件流式物化（推荐）：聚合批次，但按源文件消费、物化完即清**」 |
| 验证许可 | **执行 10% 核心测试**（自定义作答） |

⇒ §4.3 的按文件流式物化、§6 的验证口径均直接源自此裁定。

### 10.3 需撤回的历史推理

来源会话在 `max-tokens` 截断前产出的设计草稿中，有两处推断**与源码不符**，本文档予以更正：

1. **「NodeId 按创建顺序分配 ⇒ 跨文件物化交错会改变图」** —— 错误。见 §2.1：
   NodeId 是稳定锚点的纯函数，边经基数排序规范化。
2. **「reducer 内可以在 `RunPartitionedSyntaxPass` 之外自行拼装物化」** —— 该草稿未解决
   `RunPartitionedSyntaxPass` 末尾 `PublishSyntaxPassTelemetry` 的逐文件覆盖问题，
   本文档在 §7.1 将其升级为**必须显式处置**的项（S4）。

会话最终因连续三轮输出被 `max-tokens` 截断而停在半句设计推理上，**未落地任何本改造代码**；
唯一落地的 `BuildSyntaxPartitions` 方法提取（`PartitionedSyntaxPass.cs:84-114`）是**与此改造无关的
纯重构**，且已自洽。

### 10.4 决定记录：暂不实施（2026-09-27）

本设计交付后**同日即决定暂不实施**，理由见 §0.1（R1～R4）。要点是：本改造**省不掉串行工作**
（R1）、被当作卖点的「池 93.2% 时间不存在」实为**主线程在串行干活**的另一种表述（R2）、
而诊断出的**主要成因**（`FreezeQueryIndex` 55.1% 墙钟等）本改造**完全碰不到**（R3）。

⚠️ **该决定取代了本文档 §1 原先「建议做」的结论**；§1 已据此改写为纯「范围」节。
本文档自此作为**设计记录**保留，不作为执行授权。
