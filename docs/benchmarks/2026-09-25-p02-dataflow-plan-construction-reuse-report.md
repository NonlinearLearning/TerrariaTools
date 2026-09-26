# P02 DataFlow 计划构建与重复遍历复用执行报告

执行时间：**2026-09-25 08:28–08:32，北京时间**。
范围：[P02 执行计划](../plans/2026-09-25-dataflow-plan-construction-reuse-execution.md)。
共同门槛：[CPG 性能优化执行索引](../plans/2026-09-25-cpg-performance-optimization-execution-index.md)。

## 1. 结论

- **T2 已交付**：候选循环在 `sets.Clear` **之前**对 `FactCount == 0` 的记录直接 `continue`，并且 `UsedFactsByOperation[...]` 的查找由两次降为一次（局部变量 `usedFactRecord` 同时供 guard 与 `EnumerateFacts()` 使用）；`DefinitionFactForSymbol` 与 `DefinitionFactForParameter` 的 `SymbolId` 各只计算一次。
- **T3 判定为“已存在、不新增”**：计划要求的“方法局部只读投影”在改前就已完整存在于 `MethodDataFlowPlan` 中——`BuildCfgSensitivePartitionPlan` 一次生成 `OperationNodes`（数组，与 `OrderedOperations` 同序）与 `OperationNodesByOperation`（`ReferenceEqualityComparer` 字典），definition setup、候选循环、explicit sources 与 return boundary 四个消费方全部读取它，全文件对全局索引 `NodesByOperation[...]` 的访问**恰好一次**（即构建投影那一次，位于 `BuildCfgSensitivePartitionPlan` 中的 `var operationNode = operationIndex.NodesByOperation[operation];`；其后 `BuildFlowNeighborsFromCache` 读的是它自己新建的 `flowNodeOrdinals` 字典而非全局索引）。四个消费方都经 `plan.OperationNodesByOperation.TryGetValue` 读取该投影，操作访问数等于 `OrderedOperations.Length`（Sparse 577、Collision 779、JoinLoop 324），不存在重复的全局索引查找。按计划第 4 节停止条件“T3 仅减少一次字典查找却增加等量数组/字典常驻，不采用”，**不引入 `OperationNodeProjection` 新类型**。
- **T4 判定为不达标、不采用**：`BuildFlowNeighborsFromCache` + `SnapshotNeighbors` 这一对临时载体在 Collision（最大的夹具）上只占 `BuildFromSource` 总分配的 **446,064 / 14,129,304 ≈ 3.16%**（Sparse 446 KB / 7.49 MB ≈ 5.95%，JoinLoop 259 KB / 5.71 MB ≈ 4.54%）。改写成方法局部 CSR 的收益上界严格小于该占比，远达不到共同门槛要求的“实际分配下降 10%”，因此按计划第 4 节“T4 的两遍邻接构建无法在真实大方法上显示净收益，不采用”停止，**未进入 CSR**。
- **等价性逐项成立**：PRE/POST 两批各 21 条记录，21 条全量 `Equivalent=true`；跨批比对中 `Nodes`、`Edges`、`DataFlowEdges`、`GraphSnapshotVersion`、`NormalizedGraphHash`、`PublicationHash`、`PublicationOrder` 与全部 `Diagnostic` 指标字段**零不匹配**，唯一移动的计数器是本次新增的 `CandidateZeroFactSkips`。
- **时间不可归因**：两批的校准门槛均为 `Inconclusive`（改前 Coarse→Detailed 增幅中位 143.1%、范围 −67.7%…+514.9%；改后中位 274.7%、范围 −89.4%…+1701.5%），且候选循环中位耗时在亚毫秒量级。按共同门槛，**本报告只声明结构计数与候选前驱访问的减少，不声明性能提升**。

## 2. 改前/改后证据与身份

两批均由官方宿主 `Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1` 生成，各自 21 次真实 `BuildFromSource` 调用。输出目录均不存在（脚本拒绝覆盖），原始记录不可变。

| 项目 | 改前（guard 关闭 + `SymbolId` 双调用） | 改后（T2 落地） |
| --- | --- | --- |
| 证据目录 | `Build/DataFlowTailMeasurement/p02-pre` | `Build/DataFlowTailMeasurement/p02-post` |
| 样本 / 等价 / 覆盖 / 计时不变式 | 21 / true / true / true | 21 / true / true / true |
| 宿主审计 | Status=Verified, 18 次独立逐字节比较, `FullMetadataAndPublicationOrderEqual=true` | 同左 |
| `CalibrationGate` | Inconclusive | Inconclusive |
| NLCPG.dll SHA-256 | `24352B94A26BF5D1398BD386651FF2DB8EB540038C7CB2D4DBF932AE331D1738` | `8011F037FC68CEFD3EE817D6FA0390CB2E88BC6D678830618F4D3FB556FD9168` |
| 宿主 DLL SHA-256 | `060DEA3D90199635CD5AA0E81DC724ED9D95538CC028DB92444C6B7A5C98E776` | 同左（宿主未改） |
| 批次 / 进程耗时 | 5465.6 ms / 5596.9 ms | 5175.0 ms / 5296.6 ms |
| `SourceUnchanged` | true | true |
| 配置 | Release，`DOTNET_TieredCompilation=0`，`DOTNET_ReadyToRun=1` | 同左 |

两批的 `source-state.json` 各记录 **464** 个构建相关源文件的 SHA-256，逐一比对后**只有 1 个文件不同**：

| 文件 | 改前 | 改后 |
| --- | --- | --- |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | `927D5B01A08C222090152D4719CB3F5B48819B6260244E9961939B5524C790FA` | `B6557E917E5A12436D3F5113891376F73B8AA367DC49B87583E496FAA778B630` |

即唯一变化就是本补丁自身，其余 463 个文件逐字节相同，**不存在工作树漂移**。两批 `Head` 同为 `925161d2003e626fe00000e799cee2f847025067`，含既有未提交改动；不是干净 commit 基线。

改前的“PRE”状态由人工把 guard 反转为 `if (false && ...)` 并还原 `SymbolId` 双调用得到，因此该批次的 `DataFlowPass.cs` 哈希是构造值，不是任何交付状态。改后哈希 `B6557E91…` 即当前工作树状态。

复现入口（从仓库根目录，输出目录必须尚不存在）：

    pwsh -File Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory Build/DataFlowTailMeasurement/replay-p02
    py -3 tools/DataFlowTailMeasurement/summarize.py Build/DataFlowTailMeasurement/replay-p02

### 2.1 关于快照身份的一处更正

本仓库根目录**没有** `rg` 可执行文件，而宿主脚本第 33 行调用 `rg --files`。本次测量通过前置 `Build/tools/rg.cmd` 垫片解决（该目录在 `.gitignore` 的 `Build/` 下，不入库）。

另需注意：`DataFlowGraphSnapshot.Capture` 会序列化**已解析的绝对** `FullName`，因此它的 SHA-256 依赖进程工作目录（宿主从仓库根目录运行，测试宿主从 `Build/test/Debug/net10.0` 运行）。本报告的等价性判据因此使用图中自有、与路径无关的 `GraphSnapshotVersion` 与 `NormalizedGraphHash`（后者由宿主逐样本自行计算），并附节点/边/DataFlow 边计数与 `PublicationHash`。

## 3. 结构证据

### 3.1 三个夹具的候选/不动点计数（Detailed，DOP=1，逐轮相同）

| 夹具 | 指标 | 改前 | 改后 | 变化 |
| --- | --- | --- | --- | --- |
| Sparse | `CandidateOperationVisits` | 577 | 577 | 0 |
| Sparse | `CandidatePredecessorVisits` | 65 | 65 | 0 |
| Sparse | `CandidateUnions` | 65 | 65 | 0 |
| Sparse | `CandidateZeroFactSkips` | 0 | **64** | +64 |
| Sparse | `Enqueues` / `Dequeues` | 580 / 580 | 580 / 580 | 0 |
| Sparse | `FixpointPredecessorVisits` | 67 | 67 | 0 |
| Sparse | `DefinitionOperationVisits` | 577 | 577 | 0 |
| Collision | `CandidateOperationVisits` | 779 | 779 | 0 |
| Collision | `CandidatePredecessorVisits` | 163 | 163 | 0 |
| Collision | `CandidateUnions` | 163 | 163 | 0 |
| Collision | `CandidateZeroFactSkips` | 0 | **64** | +64 |
| Collision | `Enqueues` / `Dequeues` | 784 / 784 | 784 / 784 | 0 |
| Collision | `FixpointPredecessorVisits` | 167 | 167 | 0 |
| Collision | `DefinitionOperationVisits` | 779 | 779 | 0 |
| JoinLoop | `CandidateOperationVisits` | 324 | 324 | 0 |
| JoinLoop | `CandidatePredecessorVisits` | 50 | 50 | 0 |
| JoinLoop | `CandidateUnions` | 50 | 50 | 0 |
| JoinLoop | `CandidateZeroFactSkips` | 0 | **36** | +36 |
| JoinLoop | `Enqueues` / `Dequeues` | 333 / 333 | 333 / 333 | 0 |
| JoinLoop | `FixpointPredecessorVisits` | 58 | 58 | 0 |
| JoinLoop | `DefinitionOperationVisits` | 324 | 324 | 0 |

**必须如实说明**：这三个夹具里被跳过的操作本来**就没有计划前驱**，所以 `CandidatePredecessorVisits` 一字未动——T2 在这三个夹具上省下的是 `sets.Clear`、空的前驱循环与 `sets.IsEmpty` 判断，**不是 union 代价**。真正体现“无事实操作不再访问候选前驱”的是下面专门构造的夹具。

### 3.2 针对性夹具的前驱访问（T2 的真实效果）

夹具 `ZeroFactChain`（24 层嵌套块，每层 `{ int yN = N; }`，全部操作无使用事实且都有真实 CFG 前驱）：

| 指标 | 改前 | 改后 | 变化 |
| --- | --- | --- | --- |
| `CandidateOperationVisits` | 147 | 147 | 0 |
| `CandidatePredecessorVisits` | **24** | **0** | −24（−100%） |
| `CandidateUnions` | **24** | **0** | −24（−100%） |
| `CandidateZeroFactSkips` | 0 | **147** | +147 |
| `Enqueues` / `Dequeues` | 149 / 149 | 149 / 149 | 0 |
| `SetComparisons` | 149 | 149 | 0 |
| `FixpointPredecessorVisits` | 26 | 26 | 0 |
| `DefinitionOperationVisits` | 147 | 147 | 0 |
| `FlowNodeCount` / `DefinitionCount` | 149 / 24 | 149 / 24 | 0 |
| `RawCandidateCount` / `UniqueCandidateCount` | 28 / 28 | 28 / 28 | 0 |
| DataFlow 边 | 28 | 28 | 0 |
| `ExitReason` | Complete | Complete | 0 |

`AllZeroFactMethod`（混合夹具，9 个无事实操作 + 7 个有事实操作）：`CandidatePredecessorVisits` **3 → 2**、`CandidateZeroFactSkips` 0 → 9，其余全部不变。

### 3.3 图与提交序列逐字段等价

跨批比对 `Build/P02Probe/compare_batches.py`（逐条记录、逐字段）：

- 顶层身份 `Fixture`/`Mode`/`Round`/`Dop`/`Equivalent`/`CoveragePassed`/`TimingValid`/`Nodes`/`Edges`/`DataFlowEdges`/`GraphSnapshotVersion`/`NormalizedGraphHash`/`PublicationHash`：**21 条全部相同，0 不匹配**。
- `Diagnostic` 指标字段（`FlowNodeCount`/`WordsPerSet`/`DefinitionCount`/`WorklistIterations`/`RawCandidateCount`/`UniqueCandidateCount`/`OverflowReason`/`SparseOverflowNodeCount`/`ExitReason`/`MethodSignature`/`SpanStart`/`SpanEnd`/`StableOrder`）与完整 `PublicationOrder`：**0 不匹配**。
- `Counters` 字典：**只有 1 个键移动**，即新增的 `CandidateZeroFactSkips`（13 条 Detailed 记录）；其余全部计数器逐值相同。

### 3.4 候选预算前缀

预算取 1 / 2 / 7 / 8 / 27 / 28 / 29 / 262 / 263 共 9 个边界点 × 4 个夹具 = 36 组，逐一比对改前/改后：**36 组差异全部且仅出现在 `skips` 计数上**，`RawCandidateCount`、`UniqueCandidateCount`、`published`（提交条数）、`OverflowReason`、`ExitReason`、已提交 DataFlow 边数**完全一致**。这正是计划第 2 节第 5 条要守住的“去重前计数”前缀不变。

## 4. T2/T3/T4 分项账目

| 步骤 | 决定 | 依据 |
| --- | --- | --- |
| T2 零事实短路 | **采用** | 落点严格在 `sets.Clear` 之前；`ZeroFactChain` 上候选前驱访问 24 → 0；`FactCount = DirectFacts.Length + 子记录 FactCount 之和`，故 `FactCount == 0` 蕴含 `EnumerateFacts()` 无产出，`TryGetCandidates` 不可能被驱动 |
| T2 `SymbolId` 单次计算 | **采用** | 两个字段必须使用同一字符串；改前各调用两次，等于把 `GetDocumentationCommentId` + `ToDisplayString` + `Locations` 付两遍 |
| T3 方法局部操作→节点投影 | **不新增（已存在）** | 改前 `MethodDataFlowPlan` 已持有 `OperationNodes` + `OperationNodesByOperation`，四个消费方均已复用，全局 `NodesByOperation[...]` 全文件仅 1 处；四阶段访问数 = `OrderedOperations.Length`，无重复查找 |
| T4 ordinal CSR 邻接 | **不采用** | 邻接临时载体仅占总分配 3.16%（Collision）/ 5.95%（Sparse）/ 4.54%（JoinLoop），收益上界达不到 10% 门槛 |

**不得混淆的既有收益**：本报告的任何数字都不包含稀疏 bitset（`SparseSetStore` 内联槽位）或 `DefinitionFactIndex` 紧凑序数的既有收益——这两项在改前/改后两批中完全相同，因此被本轮的相等性比对自然抵消。

## 5. 测试证据

新增 `tests/NLISSN.ContractTests/Cpg/DataFlowPlanConstructionReuseTests.cs`（**18 个用例**），分三层：

- **冻结 oracle**：`BuildFromSource` 对 Sparse / Collision / JoinLoop 比对 `GraphSnapshotVersion`、**路径无关归一化图哈希**、`PublicationHash`、**方法标量材料哈希**（`Document`/`MethodSignature`/`SpanStart`/`SpanEnd`/`StableOrder`/`Metrics`/`ExitReason`）、节点/边/DataFlow 边计数、Raw/Unique 候选数与 `PublicationOrder.Count`。
- **T2 结构闸门**：`ZeroFactChain` 必须 147/147 全部跳过、候选前驱访问与 union 归零，同时 `Enqueues`/`Dequeues`/`SetComparisons`/`FixpointPredecessorVisits`/`DefinitionOperationVisits`/`ExplicitOperationVisits` 与 28 条候选结果全部不变。两个对照夹具分辨“无事实”与“有事实但 in 集为空”：`FactWithEmptyIncomingSet`（`DirectUsedFacts=1` 但 `CandidateZeroFactSkips=0`，走原有 `IsEmpty` 早退路径）与 `FactWithIncomingSet`（`TryGetCandidatesCalls=1`、`ReturnedCandidates=1`）；`AllZeroFactMethod` 验证跳过是选择性的（16 个操作中恰好 9 个跳过、前驱访问 3 → 2）。
- **T5 预算与 DOP**：9 个预算边界的 `[Theory]`（1/2/7/8/27/28/29/262/263，含 `CandidateEdgeLimitExceeded` 与 `Complete` 两侧，断言去重前计数 = 预算+1、提交为空），以及 Collision 在 DOP=2 与 DOP=1 的等价性（提交顺序哈希、快照版本、归一化图哈希与计数）。
- **计数作用域**：`AllZeroFactMethod` 复制成两个方法的文档，断言两方法计数逐值相等且**不累加**（否则 `CandidateZeroFactSkips` 会变 18、`CandidatePredecessorVisits` 会变 4）；再用同一 builder 连续两次 `BuildFromSource` 断言第二次计数不累加。

改前红/改后绿证据：

| 阶段 | `DataFlowPlanConstructionReuseTests` | 说明 |
| --- | --- | --- |
| T2 未打补丁（guard 反转） | **12 失败 / 6 通过** | 3 个结构闸门（`FactFreeOperations_KeepFixpointButSkipCandidatePredecessors`、`MixedFactOperations_SkipOnlyTheFactFreeOnes`、`TwoMethodsAndRepeatedBuilds_KeepCountersMethodLocal`）+ 全部 9 个预算边界 `[Theory]` 用例（它们断言 `CandidateZeroFactSkips` 恒为 147）；三个冻结 oracle、两个 in 集对照夹具与 DOP 等价用例按预期仍为绿，因为 T2 不改变它们的可观测输出 |
| T2 已打补丁 | **18/18 通过** | |

定向宽过滤（计划第 4 节指定命令）`FullyQualifiedName~DataFlow|~SparseSet|~Bitset`：**93/93 通过**。
`Run-TestTiers.ps1 -Fast`：Unit 层 **117/117 通过**；Contract 层 718/719 通过，唯一失败是 `NLISSN.Tests.Architecture.LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`——它断言 `src/NLISSN.Application/NLISSN.Application.csproj` 的 ProjectReference 列表，而该列表被**另一个并发工作流**新增了 `..\NLISSN.Infrastructure\ProjectJson\NLCPG.ProjectJson.csproj`（`src/NLISSN/NLISSN.csproj` 同时 +1 行，`NLCPG.ProjectExport.csproj` 为未跟踪新文件，`LayoutArchitectureTests.cs` 自 2026-09-12 起未改、其中不含 `ProjectJson`）。该失败与本补丁无关，本补丁未触碰任何 `.csproj`。

## 6. 未验证边界与停止条件

- **未验证**：生产语料（Terraria.NPC.SetDefaults / AI）、全量 NPC、trace、dump、全仓 Performance 层，以及 DOP>2 的并发行为。三个小夹具的计时不足以支撑任何吞吐结论。
- **未采纳**：T3 的新投影类型（已存在，新增只会增加常驻）；T4 的 CSR 邻接（收益上界 < 10% 门槛）；把 `UsedFactRecord` 压平、重写 Roslyn 操作遍历或改全局 CFG 存储（计划第 4 节停止条件，会改变事实展开顺序或生命周期）。
- **回退点**：T2 有三处独立小改动（候选 guard、`usedFactRecord` 局部变量、两个 `SymbolId` 局部变量），任一步出现语义不等价可单独回退；`CandidateZeroFactSkips` 计数器可随之删除。
- 本报告未覆盖、也未改写[P01 报告](2026-09-25-p01-dataflow-candidate-scan-pruning-report.md)与[原测量报告](2026-09-25-dataflow-tail-small-batch-measurement-report.md)的历史数据。

## 7. 并发工作流的后续改动：T4 的 CSR 邻接**已由他人落地**（2026-09-25 14:45–14:48）

本会话完成验证后，**另一个并发工作流**把邻接投影改写成了方法局部 CSR：`MethodDataFlowPlan` 现在接收
`predecessorOffsets`/`predecessorOrdinals`/`successorOffsets`/`successorOrdinals` 四个数组，
原先的 `Predecessors`/`Successors` 字典属性已删除，读取点改为按偏移切片。

**必须分清归属**：

- 该 CSR 改动**不是本任务交付的**，也**没有**按本报告第 4 节的门槛被验证过——本任务对 T4 的结论
  （收益上界 3.16%/5.95%/4.54%，低于 10% 分配门槛，故不采用）是在**改前**的树形量化出来的，
  当时 `BuildFlowNeighborsFromCache` + `SnapshotNeighbors` 的两遍载体确实存在。并发工作流在**没有**
  本报告这组分配数据的情况下独立推进了它，其收益**尚未由本任务测量**。
- 因此第 1 节与第 3 节的所有数字都属于**该 CSR 落地之前**的树，不能归因于、也不能用来评价该 CSR。

**本任务在 CSR 落地后的复验（T2 未被破坏）**：

| 检查 | 结果 |
| --- | --- |
| T2 标记存活 | `usedFactRecord.FactCount == 0` guard、`CandidateZeroFactSkips`、两处 `SymbolId` 局部变量全部保留 |
| `NLCPG.csproj` 构建 | **0 警告 / 0 错误** |
| 本任务两个测试套件 | `DataFlowCandidateScanPruningTests`（11）+ `DataFlowPlanConstructionReuseTests`（18）= **29/29 通过** |

即 T2 与 T4-CSR 在语义上正交：T2 跳过的是“无使用事实的操作不做候选前驱并集”，CSR 改的是“邻接怎么存”，
两者不共享代码路径，合并后套件仍全绿。**但本报告不为该 CSR 的收益或等价性背书**——那属于另一个工作流的
验证责任。

**留待他人确认的风险**：并发工作流同时改写了 `DataFlowPass.cs` 的邻接投影与读取点，其上未见等价性
A/B 证据。若该 CSR 后续被回退，本任务 T2 的三处改动与之无耦合，可独立保留。
