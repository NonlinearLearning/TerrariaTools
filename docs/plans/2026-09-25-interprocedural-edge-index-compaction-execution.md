# M2 跨过程边索引单份快照 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 四个跨过程索引共享一份完整边快照，索引项仅保存 int，消除同一边在多个列表中的完整值副本。

**Architecture:** 在原索引构建屏障内完成只读计数和精确填充；只收集 CallTargets/DataFlow 边。池保存扫描时完整 PendingEdge，四个索引保存池内序号。后续目标查找、排序键预计算及计划构造通过池读取，发布仍串行。

**Tech Stack:** C# / .NET、数组、Dictionary、现有 PendingEdge 与跨过程边序契约。

---

日期：2026-09-25。feature：`interprocedural-edge-index-compaction`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置和门槛见 [总索引](2026-09-25-memory-optimization-execution-index.md)。本轮仅交付执行文档。

## 1. 方案取舍

| 方案 | 决定 | 成本/收益 |
| --- | --- | --- |
| 完整快照单池 + 四个 int 索引 | 采用 | 不改变节点载荷与身份，直接消除重复边载荷 |
| 两次只读扫描，池和桶精确分配 | 采用并测耗时 | 避免 List<PendingEdge> 增长及尾部 ToArray 再复制；多一次扫描成本可测 |
| 直接存图节点 ordinal、发布时回读图 | 不采用 | 图节点可被合并替换，破坏原快照语义 |
| 池中继续按端点身份 interning | 不采用 | 完整载荷去重和额外字典的成本/风险高 |
| 四索引全部泛化为通用图查询框架 | 不采用 | 非当前性能问题所需 |

粗估 2–3 人日，仅限 Builder 跨过程局部索引；至多一个内部 helper。图的 PendingEdgeBuffer 表示保持现状。

## 2. 代码依据和所有权

[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs) 的 `RunInterproceduralDataFlowPass` 定义四个值列表；
`AddPendingEdgeIndex` 执行复制。DataFlow 边先加入按目标的索引，部分又进入参数/返回方法索引。
[PendingEdge](../../src/NLCPG/Model/NLCPGGraph.cs) 内嵌两个完整节点；现有惰性枚举仅消除了入口全量物化，并未消除四列表副本。

新池的 ordinal 是本 pass 内的**边序号**，不是 NodeId、图 node ordinal 或 metadata id。
不把值相等/身份相等当作可以合并不同载荷的依据，也不对池额外 Distinct。每条被选中的源边保存一次原值。
两遍扫描之间和填充完成之前不得调用 AddEdge/AddNode 或外部摘要发布；需证明原屏障下没有其他写入者。
发布后只读池，不重新读取图里的节点。最后一个窗口发布结束后池及索引一起离开作用域，不进入 Builder 常驻字段。

## 3. 具体数据流

计数阶段对同一 `EnumeratePendingEdgesLazily()`：

1. 非 CallTargets/DataFlow 跳过。每条保留边使 U 增 1。
2. 按原条件统计四类桶的成员数量，保留键的原比较器。CallTargets 仍只进入第一类桶。
3. 分配 `PendingEdge[U]` 与四类 `Dictionary<TKey, int[]>`。每桶数组为精确 Count，写游标独立。

填充阶段再次枚举同一未改变的边序列，按原顺序给每条保留边编号：

```text
edgePool[edgeOrdinal] = edge                  // 完整快照，仅一次
callTargets/source 或 dataFlow/target += edgeOrdinal
若匹配方法参数/返回边，再向对应方法桶写同一个 edgeOrdinal
edgeOrdinal++
```

校验池填充数与各桶游标等于计数结果；计数/游标字典在构建结束释放。所有数组不再 TrimExcess/ToArray。
原方法查找得到整数桶，循环中读取 `edgePool[id]`；排序键收集也走同一快照池。
原缺失键使用共享空 int 数组。不得为了迁移方便长期保留一份旧 `List<PendingEdge>`。

桶计数、游标和最后的索引短暂共存必须列入峰值；若中间字典抵消收益，不继续叠加通用 CSR 或对象池。

## 4. Task 1：先证明重复率值得做

**Files:**
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchInterproceduralTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/InterproceduralEdgeIndexSnapshotTests.cs`。
- Fixtures: `tests/NLISSN.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`。

1. 在小型真实绑定夹具和多调用点压力夹具统计 U、四索引成员总数 L、旧 Capacity 总和及当前 PendingEdge 宽度 W。
2. 预先计算：旧主载荷约 `W × Σ旧容量`，新主载荷约 `W × U + 4 × L`；再加入键字典、数组头、计数器和峰值重叠。
3. 记录完全无共享边的对照夹具。若 L=U，单池多了一层 int，不能宣称天然节省。
4. 若代表性夹具的索引子系统净保留预计不足 15% 降幅，停止此项，记录不采用；15% 是拟定成本收益门槛。

## 5. Task 2：边序与快照护栏

1. 冻结四桶逻辑成员顺序、三种 bridge 的最终提交序和完整节点/边元数据。
2. 新增同一身份但节点字段逐步补全的构图夹具，使 AddEdge 合并前后载荷确有差异。
3. 在受控结构诊断中：建池后改变图内节点，再读取池，必须仍得到原字段；不只比较 StableAnchor/Equals。
4. 覆盖无边、无相关边、只有 CallTargets、DataFlow 双索引命中、缺失方法归属、预算截断和窗口跨越。
5. 旧代码语义测试应通过；“每边仅保存一次/索引为 int”的结构门槛在旧实现失败。

## 6. Task 3：迁移与单变量验证

1. 加入计数、精确分配和填充，先对照所有桶内容，不改变下游读取。此双存只允许短期诊断，不进入最终产品。
2. 迁移目标选择、节点排序键收集、argument/return 三类计划构造，一次移除旧值列表及诊断双存。
3. 保留 nodeSortKeys 缓存、比较器、外部摘要调用位置和 return-method 全 pass 去重。
4. 单独运行本项测试和内存账本；M1 若已实施，以 M1 验收后的工作树为本项前基线，避免叠加认领收益。

## 7. 命令、验收与退出

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~CpgWorkBatchInterproceduralTests|FullyQualifiedName~InterproceduralEdgeIndexSnapshotTests|FullyQualifiedName~PendingEdgeOrdinalizationDeterminismTests'
```

在总索引构建后运行，检查新增类实际执行。完成条件：

- 池元素数恰为 U，每个桶完整有序成员与前基线相同；不存在四类 PendingEdge 值列表。
- 完整载荷快照测试、原边序、预算与 DOP 1/2 均通过。
- 代表性夹具索引子系统净保留字节至少下降 15%，计数阶段峰值不得高于旧阶段峰值；全部表、数组头和 Capacity 计入。
- 无共享边对照公开记录额外开销，不能隐藏；若代表性收益仅靠合成高重复度成立，则不采用。
- 两遍扫描后的相关阶段耗时满足共同 ≤5% 劣化门槛；否则回退本项，不通过修改预算/DOP 获益。

本方案不是全图边序号化的再次实施，也不承诺进程峰值按相同比例下降。

---

## 8. 实施记录与实测证据（2026-09-25 完成）

权威状态见 [feature_list.json](../../Context/feature_list.json) 的
`interprocedural-edge-index-compaction`（**done**）。证据目录 `Build/MemoryOptimization/M2/20260925-m2-r1/`
（`Build/` 已被 gitignore，故关键结论在此复述）。

**改动（产品代码 1 文件 + 新增测试 1 文件）**

- `src/NLCPG/Builder/NLCPGBuilder.cs`：`RunInterproceduralDataFlowPass` 里四个
  `Dictionary<TKey, List<PendingEdge>>` 换成**一个**嵌套
  `internal sealed class InterproceduralEdgeSnapshot`：一份 `PendingEdge[] _pool`
  + 四个只存池内序号的 `Dictionary<TKey, int[]>`。`Create` 走 §3 规定的**两遍只读扫描**
  （第一遍只计数得精确 U 与各桶 Count；第二遍分配并一次填满）。
- 新增 `tests/NLISSN.ContractTests/Cpg/InterproceduralEdgeIndexSnapshotTests.cs`（13 例）。

**完成条件逐条对照**

| §7 完成条件 | 结果 |
| --- | --- |
| 池元素数恰为 U；不存在四类 PendingEdge 值列表 | 段 8.1 结构门槛：`PendingEdge[]` 字段恰 1 个、`List<PendingEdge>` 字段 0 个、四个 `Dictionary<,>` 值类型均为 `int[]` |
| 完整载荷快照、原边序、预算与 DOP 1/2 均通过 | 段 8.2：**66 通过 / 0 失败**（多类逐项自 TRX 核对） |
| 净保留字节 ≥15%、计数峰值不高于旧峰值 | 段 8.3：**67.24%** 降幅、峰值 **−41.83%** |
| 无共享边对照公开记录额外开销 | 段 8.4：`L == U` 时按精确容量口径**净增 0–20%**，已进入仓库内常驻测试 |
| 两遍扫描后相关阶段耗时 ≤5% 劣化 | 段 8.3：**−40.29%**（更快） |

### 8.1 结构门槛及其反向验证

对构建产物反射：`PendingEdge[]` 字段恰好 1 个（`_pool`）、含 `List<PendingEdge>` 的字段
恰好 0 个、`Dictionary<,>` 字段恰好 4 个且值类型均为 `int[]`。

**门槛非恒真**：注入旧载体 `private readonly List<NLCPGGraph.PendingEdge> _legacyPayload = new();`
后，`EdgeSnapshot_StoresOneFullPayloadPerRetainedEdge` **失败**（1 失败 / 11 通过，其余 11 条仍过）；
回滚采用行级编辑（非整文件覆盖），回滚后 `NLCPGBuilder.cs` 的 SHA256 与验证时逐字节相同：
`602EECAEE1577BC15919D48A5BA826DDCF0E2B19377CE172EA4F13F840533DF9`。

### 8.2 计划 §7 验收命令与结果

**最终验收在主测试工程上、按 §7 原文命令执行**：四类过滤器 **32 通过 / 0 失败**；
纳入承载预算/窗口门槛的 `InterproceduralPlanCompactionTests` 后 **66 通过 / 0 失败**：

| 测试类 | 条数 |
| --- | --- |
| `InterproceduralEdgeIndexSnapshotTests`（本项新增） | 13 |
| `InterproceduralPlanCompactionTests`（预算/窗口，M1） | 34 |
| `CpgInterproceduralEdgeOrderTests`（原边序） | 5 |
| `CpgWorkBatchInterproceduralTests`（DOP 1/2 等价） | 4 |
| `PendingEdgeOrdinalizationDeterminismTests` | 10 |

日志 `task3-main-s7-literal.log`、`task3-main-all.log`，TRX `trx-main-s7/`、`trx-main-all/`
（均在 `Build/MemoryOptimization/M2/20260925-m2-r1/`）。两轮运行时产品文件哈希均为
`602EECAE…3DF9`，与 §8.1 记录一致。

**等价性是字节级的**：`GraphSnapshotVersion = 9B59A3…E896` 迁移前后**逐字节相同**；
新实现复算的 `Σ旧容量 = 160,744` 与旧实现实测值完全一致。
完整载荷快照用例**逐字段**比较——`NLCPGNode.Equals`/`GetHashCode` 在两端都有 `StableAnchor` 时
**只比较锚点**，用 `Equals` 断言会退化成恒真。

> **执行载体说明（历史，不掩盖问题）**：上述主工程验收**一度无法执行**——本轮共享工作树里
> 另一个工作流的两个**未追踪**测试文件带有真实的缺 using 错误（`CS0103`），令主测试工程
> 连续 40 分钟以上不可编译。当时改用一个位于 gitignored `Build/` 下的隔离工程取证
> （只编入上表测试文件，引用**同一个** `src/NLCPG/NLCPG.csproj`，
> 四类 **31/31**、含预算类 **66/66**；当时新增类为 12 条）。
> **未编辑、未删除、未排除对方任何文件。** 对方随后修好该问题，主工程随即按原文命令重跑通过，
> 两条路线在同一产品哈希上互为交叉验证；隔离工程不再是验收依据。
> 详见 `Build/MemoryOptimization/M2/20260925-m2-r1/TASK3-隔离复验说明.md`。

### 8.3 量化门槛（同进程交错 A/B）

真实语料**前后各跑一次不可用**：测量期间并发工作流在改同一工作树，**未被本项触碰**的
`Operation` stage 从 8,628 ms 变到 15,757 ms（**+82%**），单次前后读数无法支撑 5% 结论。
故改用同进程交错 A/B（同一份边序列、奇偶对交换次序、取成对中位数比值、
计时/保留/峰值三阶段隔离），规模匹配夹具 `U = 49,335`：

| 门槛 | 实测 | 判定 |
| --- | --- | --- |
| 净保留字节 ≥15% 降幅 | 66,761,368 → 21,871,696 B = **67.24%** | 通过 |
| 计数阶段峰值不高于旧峰值 | **−41.83%** | 通过 |
| 相关阶段耗时 ≤5% 劣化 | **−40.29%**（更快） | 通过 |

仪器本身出过两个错，都在采信任何数字之前修掉：1 ms 采样线程与计时混跑读出**假的 +6.94% 劣化**；
两个对象共用一条基线测量会让第二次读数包含第一个对象、凭空放大收益。被丢弃的读数不作为结果报告。

**两个字节数字的口径必须分清**（§7 要求"全部表、数组头和 Capacity 计入"）：

- 上表的 **67.24%** 是**实测存活字节**：从空基线起算、构造后持有对象读取，
  因而**天然包含** `Dictionary` 桶数组、`int[]` 对象头、`List` 容量空槽等全部开销
  ⇒ 满足 §7 该条的"全部计入"。
- 仓库内常驻测试 `EdgeSnapshot_NetRetainedBytes_DropAtLeastFifteenPercent` 用的是**解析式**口径：
  `W×Σ旧容量` vs `W×U + 4×L + 24 B×桶数`，即**主载荷 + int 槽 + `int[]` 头**，
  **不含**两侧 `Dictionary` 本身的桶数组与对象头。它是一条**常驻回归护栏**，
  不是上表那个数字的复算，两者**不得混引**。

### 8.4 无共享边对照（公开记录，不隐藏）

真实 `NPC.cs` 实测 `W=272 B、U=46,251、L=54,691、Σ旧容量=160,744`：旧 43,722,368 B →
新 13,612,420 B（**68.87%**）。**但其中共享单独只贡献约 13.96%，低于 15% 门槛。**
`L == U` 时按精确容量口径，本项**净增 0–20%**（多出 `L × 4 B` 序号槽与每桶 24 B `int[]` 对象头）。

⇒ 本条收益**由消除容量 slack 主导，不是共享主导**，不得把 68.87% 归因给共享。
该结论已固化为仓库内常驻测试 `EdgeSnapshot_NoSharingControl_DisclosesIntLayerOverhead`
（同时断言"精确容量口径净增"与"List 扩容口径降幅 >50%"），以免日后被静默重新归因。

### 8.5 分档测试状态与归因

**Host 档**（`Run-TestTiers.ps1 -Host`）：**674 通过 / 7 失败 / 681**。
7 条失败经 `Compare-Object` 与仓库既有的 Host 基线**逐条一致**（零新增失败），
全部落在**规则/传播层**，与本项改动的跨过程索引构造无关：

| 失败用例 | 条数 |
| --- | --- |
| `PipelineComponentTests.AnalyzeFromArgs_ForDirectoryDeclaration_*` | 2 |
| `TestCodeSetCoverageTests.Analyze_AllTestCodeSetSources_*` | 3 |
| `PropagationRuleExpansionTests.Analyze_LogicalAndChainRightTarget_*` | 1 |
| `RuleStructureContractTests.StructuralKind_ContainsOnlyApprovedStructureConclusions` | 1 |

日志 `task6-host-tier.log`。该 7 条在多个历史轮次中均被记录为既有失败
（见 `Context/progress.md` 相关条目），本项**未修改任何规则/传播层文件**。

**Fast 档**（`Run-TestTiers.ps1 -Fast`）：

| 档 | 结果 |
| --- | --- |
| UnitTests | **117 / 117** |
| ContractTests | **724 通过 / 10 失败 / 734** |

10 条失败中没有一条属于 M2，逐条归因如下（未"顺手"修改任何一处）：

- **9 条 `NLCPGPartitionedBuilderTests`** — 错误消息全部是
  `System.OutOfMemoryException : Insufficient memory to continue the execution of the program.`，
  抛出点在 `NLCPGBuilder.CreateMetadataReferences`（`NLCPGBuilder.cs:2962`），
  即**加载元数据引用**处，与本项改动的跨过程索引构造无关。
  已在**隔离运行**下复核：`--filter 'FullyQualifiedName~NLCPGPartitionedBuilderTests'` 得
  **39 通过 / 0 失败**（`task5-partition-isolated.log`）⇒ 该批失败是**整档并行运行时的机器内存压力**
  （本轮机器 13.86 GB、测试期间常驻 27 个 `dotnet`/`testhost`、最大单进程 836 MB），
  不是代码缺陷。
- **1 条 `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`** —
  `Assert.Equal() Failure: Collections differ`：期望的 `NLISSN.Application` 引用表与
  工作树新增的 `..\NLISSN.Infrastructure\ProjectJson\NLCPG.ProjectJson.csproj` 引用不一致，
  属并发工作流新增工程引用所致。

M2 的 `git status` 范围始终只有 `src/NLCPG/Builder/NLCPGBuilder.cs`（已修改）
+ `InterproceduralEdgeIndexSnapshotTests.cs`（新增）。

### 8.6 未验证边界（不得越读）

- **未跑 Performance 档**；未宣称进程峰值内存按同比例下降。
- §8.3 的三条门槛是在**规模匹配合成图**上测的**索引构造本身**增量，**不是** `NPC.cs` 本体的整 pass 墙钟；
  真实语料 68.87% 与同进程 67.24% **口径不同、不得混引**。
- 峰值读数含 1 ms 采样粒度（结论量级远大于该误差）。
- §8.5 的 9 条 OOM 只在**并行整档**下复现，无法排除"在内存更紧张的机器上真实语料构建也会 OOM"；
  本项**不**声称已消除该风险（本项只减少索引子系统保留字节，未改元数据引用加载路径）。
- §8.5 的 Host 7 条与 Fast 10 条失败均**未修复**：它们不属本项范围，
  按计划 §4「未关联的既有失败保留名称和复现证据，不顺手改期望清单」处理。

### 8.7 与计划的偏差（如实记录）

1. **夹具位置**：§4 把 `CpgBuilderSources.cs` 列为 Fixtures，但新增的
   `InterproceduralEdgeIndexSnapshotTests.cs` **没有**用它，而是在**手建可变图**
   （`new NLCPGGraph()` + `AddEdge`）上直接构造与真实 pass 同形的边集，
   再调用**与生产完全相同**的 `InterproceduralEdgeSnapshot.Create`。
   **原因是硬性的、非偏好**：`PendingEdgeBuffer.Release()` 在 `FreezeQueryIndex()` 内
   经 `ReclaimConstructionCapacity()` 执行，故任何走 `BuildFromSource(...)` 的
   真实绑定夹具在构建返回后调用 `EnumeratePendingEdgesLazily()` 都会抛
   `InvalidOperationException: The pending-edge buffer was released at freeze time and can no longer be read.`
   `Create` 的入参正是 `IEnumerable<PendingEdge>`，故**只有可变图**能喂给它。
   （早期 4 条测试正是这样失败的，随后改写；**产品代码从未为测试放宽**。）
   `CpgBuilderSources.cs` 中本项相关的既有夹具
   （`InterproceduralPlanCapacityPressure`）仍被 M1 的预算类使用，未被绕开。
2. **定量测量的夹具与上述不同**：§4 第 1 条要的"小型真实绑定夹具 + 多调用点压力夹具"
   统计，是由 **Task 1 的只读探针**在**真实绑定输入**上完成的
   （Terraria `NPC.cs` 2,147,280 字符，走真实 `BuildFromSource`；对照
   `control-NoSharing.cs`），**不是**合成图。故"真实夹具统计 U/L/ΣCap/W"这条**已满足**，
   只是执行载体是 `Build/MemoryOptimization/` 下的诊断探针而非常驻测试。
3. **§5 第 4 条的覆盖面**：无边、无相关边、只有 CallTargets、DataFlow 双索引命中、
   缺失方法归属由本项新增测试覆盖；**预算截断与窗口跨越**由 M1 的
   `InterproceduralPlanCompactionTests`（34 条）覆盖，本项**不重复实现**，
   而是在 §8.2 与它**一起**纳入验收命令。
4. **§4 第 2 条的 `PendingEdge` 宽度**：报告用的 `W = 272 B` 是**当前运行时**
   `Unsafe.SizeOf<PendingEdge>()` 实测，不是总索引 §4 提到的 428/216/272 历史值照抄。

