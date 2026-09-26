# 记忆优化 M1–M7 收尾报告（2026-09-25 第四会话核实）

日期：2026-09-25 20:20–20:45。范围：对 [总索引](2026-09-25-memory-optimization-execution-index.md) 七份分册
的**执行状态、未完成项、待落盘更正**做一次只读核实，并落盘已确认的更正。
权威状态仍以 [feature_list.json](../../Context/feature_list.json) 为唯一来源；本报告**不新增**任何收益主张。

---

## 1. 七册状态总览（核实结论）

| 编号 | feature | 状态 | 本轮核实 |
| --- | --- | --- | --- |
| M1 | `interprocedural-plan-carrier-compaction` | **in_progress** | 唯一实质剩项：阶段耗时门槛 Inconclusive；§7 第 4 条子项①无判别力 |
| M2 | `interprocedural-edge-index-compaction` | **done** | 夹具位置偏差（§8.7 第 1 条）核实为**硬性约束**，裁决见 §3 |
| M3 | `workbatch-consume-results` | **done** | 无未决项 |
| M4 | `frozen-node-storage-compaction` | **done** | 无未决项（峰值 RSS 缺口已由第三会话补测） |
| M5 | `dataflow-adjacency-compaction` | **done** | 本轮更正 2 处事实错误 + 删 1 个死成员，见 §2 |
| M6 | `local-fragment-array-ownership` | **done** | 无未决项 |
| M7 | `sparse-set-union-allocation` | **done** | 无未决项 |

**合计**：7 项中 **6 项 done**，**M1 仍 in_progress**。

---

## 2. 本轮落盘的更正（已核实并写入）

### 2.1 M5 执行文档 §7.3 下方口径注记：两处事实错误

原文（更正前）：

```
> 4/9 夹具低于 40%（33.5%–37.0%），它们都是邻接极稀疏而节点多的形态
> （`Sparse64` 67 个节点仅 67 条保留邻接、`WideLocals` 类同理），
```

**错误一：计数自相矛盾。** 同节 §7.3 表内九个数是完整枚举，低于 40% 的恰为
`AllZeroFactMethod` 32.7%、`Sparse64` 33.5%、`ZeroFactOps` 37.0% 共 **3** 个，而非 4 个。
`feature_list.json` 早已更正为 `3 of 9`（第三会话按 `retain-pre.json`/`retain-post.json`
原始字节独立复算），但**执行文档未同步**。

**错误二：夹具名不存在。** `WideLocals` 在全仓（排除 `Build/`）**零命中**——
`grep` 唯一命中该行自身。九个夹具的真实名单在
`tests/NLISSN.ContractTests/Cpg/DataFlowAdjacencyCompactionTests.cs` 的 `AllFixtures`：
`Sparse64`、`Collision32`、`JoinLoop4x4`、`ZeroFactOps`、`ZeroFactChain`、`AllZeroFactMethod`、
`FactWithEmptyIncomingSet`、`FactWithIncomingSet`、`StructureZoo`。

**错误三：`Sparse64` 的节点数不符。** 原写「67 个节点」；按
`docs/benchmarks/dataflow-tail-small-batch-20260925/measurements.csv` 实测列，
`Sparse64` 为 **2218 图节点 / 580 流节点**，每方向**67 条保留邻接**。
「67」是**邻接条数**，被误写成节点数。

⇒ 已改写为 `3/9` + 真实夹具名 + 实测节点/邻接数，并保留更正说明。

### 2.2 `PlanNeighborMaterializationVisits` 死成员删除

**事实**：该枚举成员已是**零引用死成员**——

| 检查 | 结果 |
| --- | --- |
| `src/` 写入点 | **0**（旧链 `BuildFlowNeighborsFromCache`/`SnapshotNeighbors` 已删除） |
| `src/` 读取点 | **0** |
| `tests/` 断言 | **0**（`grep` 全仓仅命中历史 CSV 表头与一处文档句） |

**删除安全性的依据（非推测）**：`DataFlowMethodProbe.CounterSnapshot()` 用
`Enum.GetValues<DataFlowCounter>()` **动态**生成键；全仓**无** `Counters.Count`
或枚举成员数断言 ⇒ 删成员不会让任何既有断言失配。

**为什么必须删而不是留着**：留着会使该计数**恒为 0 却仍出现在诊断输出里**，
与总索引 §2「诊断访问计数记录真实计数，**不能伪装**仍只扫描一遍」冲突。
这比原 §7.6「未补等价计数器」的记录是**更强的处置**：
由「已失效但仍在输出」变为「已退役且不再输出」。

等价工作量仍由 `PlanIncomingEdgesRetained`/`PlanOutgoingEdgesRetained` 覆盖
（每条保留边在 `flat.Add` 处各计一次），而这两项正是
`DataFlowAdjacencyCompactionTests` 实际断言的计数。

**验证**：

| 判据 | 结果 |
| --- | --- |
| `NLCPG.csproj` 构建 | **0 警告 / 0 错误** |
| 定向过滤器（4 个 DataFlow 测试类，**非全量**） | **55/55 通过**（见 §5：首次运行受并发写入干扰报 5 失败，随后同命令同二进制 55/55） |
| 其中 `DataFlowDiagnosticFallbackTests` 对 `CounterSnapshot()` 的 **6** 处具体键断言 | 全部通过 |

### 2.3 `Context/progress.md`：M5 一节的 DOP 自相矛盾

同一文件内：line 72 已写「原 §7.6 承认『DOP 1/2 对照未执行』**已关闭**」，
而 line ~950 的边界段仍写「**DOP>1 等价性未验证**」。

执行文档 §7.7 已记录新增 dop 1/2/4/8 永久护栏并关闭该条 ⇒ 边界段是**过期残留**。
已改写为：缺口已关闭，**但**明确该护栏锁的是真不变量（集合不重不漏 + 跨 DOP 一致），
**不**锁诊断发布顺序——因为后者已证实为进程/线程池状态的函数、不是不变量。

### 2.4 `feature_list.json`：M5 记录同步死成员退役

在 `dataflow-adjacency-compaction` 的 NOT-verified 条目上补记：枚举成员**已整体删除**
（而非留在原处恒报 0），等价工作量由测试实际断言的两个 retained 计数覆盖。

---

## 3. 裁决：M2 夹具位置偏差（§8.7 第 1 条）可接受，**不应搬进 `CpgBuilderSources.cs`**

**你问的是否要把夹具搬进 `CpgBuilderSources.cs`。核实结论：不能，且原因是硬性的。**

`src/NLCPG/Model/NLCPGGraph.cs` 的冻结路径：

```
FreezeQueryIndex()  →  ReclaimConstructionCapacity()   (:478 → :905)
                          →  _pendingEdges.Release()   (:917)
```

`PendingEdgeBuffer.Release()`（:1008）置 `_released = true` 并清空键集合、元数据池与元数据表；
此后任何读取都经 `ThrowIfReleased()`（:1022）抛：

```
InvalidOperationException: The pending-edge buffer was released at freeze time and can no longer be read.
```

而 `InterproceduralEdgeSnapshot.Create` 的入参正是 `IEnumerable<PendingEdge>`，
唯一来源是 `EnumeratePendingEdgesLazily()`（:66）——**构建返回后必然抛异常**。

⇒ **裁决：维持现状。** §8.7 第 1 条的偏离是**被产品语义强制的**，不是偏好或偷懒：

- 「手建可变图 + 调用与生产相同的 `Create`」是**唯一**能覆盖该构造器的形态；
- 走 `BuildFromSource(...)` 的真实绑定夹具在此约束下**不可能**存在；
- §8.7 已如实记录「产品代码从未为测试放宽」，且 M1 的既有夹具
  `InterproceduralPlanCapacityPressure` 仍被正常使用、未被绕开。

**若将来要消除这一偏离**，前置不是搬夹具，而是**先解决 `Release()` 在 freeze 时释放这一约束**
（例如让池在冻结后仍可读，或为测试暴露一个受控的 pending 边快照入口）——那属于产品改动，
超出 M2 范围，且会改变 M2 赖以成立的「发布阶段只读池、不回读图」前提。**本轮不动。**

---

## 4. 裁决：Host 7 条与 Fast 10 条是否需要另开工作流

**结论：不需要另开工作流处理；它们是既有失败，且已有归因记录。**

| 批次 | 数量 | 归因 | 证据 |
| --- | --- | --- | --- |
| Host | **7** | 全部在**规则/传播层**，与 M2 的跨过程索引构造无关；与仓库既有 Host 基线 **`Compare-Object` 逐条一致**（零新增） | M2 §8.5 |
| Fast | **10** | **9 条** `NLCPGPartitionedBuilderTests` 全为 `OutOfMemoryException`，抛出点在 `NLCPGBuilder.CreateMetadataReferences`（元数据引用加载）；隔离运行该类 **39/39 通过** ⇒ 整档并行时的机器内存压力。**1 条** `LayoutArchitectureTests` 为工程引用图漂移 | M2 §8.5 |

两者**均不属 M2**，按计划 §4「未关联的既有失败保留名称和复现证据，不顺手改期望清单」处理。
**不另开工作流的理由**：① 它们不是 M2 引入的，也不是 M2 能修的（9 条 OOM 在元数据加载路径，
M2 只减少了索引子系统保留字节）；② 已按规则留名留证；③ 为让套件变绿去改不属于本项的文件，
正是该规则明令禁止的。

**但其中一条已变化，值得你决策**：`LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
**仍然失败**，且失败位置是 **`src/NLISSN/NLISSN.csproj`**（不是此前多处记录的 `NLISSN.Application.csproj`）：

- 期望值（`LayoutArchitectureTests.cs:78-90`）不含 ProjectJson；
- 实测第 19 行有 `<ProjectReference Include="..\NLISSN.Infrastructure\ProjectJson\NLCPG.ProjectJson.csproj" />`。
- 该引用由**并发工作流**新引入，`NLCPG.ProjectJson.csproj` 为**未跟踪**新文件。

> **证据**：本轮一次全量 Contract 运行（在收到「不做全量测试」指令**之前**已启动并结束）为
> **792 通过 / 1 失败 / 793**，唯一失败即上述该条；`NLISSN.Application.csproj` 本身已**不含** ProjectJson 引用，
> 与测试期望一致 ⇒ 失败已从 Application **迁移到** `NLISSN.csproj`。此后本轮**未再运行任何全量档**。

⇒ **并发工作流并未修复它**。这不是本任务造成的，本任务**未触碰任何 `.csproj`**。
是否要把它并入某个工作流，取决于你是否接受当前工作树保留一个**已知红灯**。

---

## 5. 本轮观察到的一次并发写入者造成的假失败（诚实记录）

跑 §2.2 的定向过滤器时，**首次**运行报 **5 失败 / 50 通过**，失败面是
`DataFlowPlanConstructionReuseTests` 的冻结图哈希 oracle
（如 `Collision` 期望 `5071C7F2…`、实测 `037952D6…`）。

**真因不是被测代码，也不是我的改动**：
`src/NLCPG/Builder/NLCPGBuilder.cs` 在该运行窗口内被**并发工作流**改写——
SHA256 `B9778EC9E4BB555E…` → `834DA7AB1068E28B…`，
`git diff --stat` 显示 **1425 插入 / 209 删除**，mtime **20:34:16** 落在运行窗口内。

随后**同一命令、同一二进制**：单独运行 `~DataFlowPlanConstructionReuseTests`
连续 **3 次 18/18 通过**，组合过滤器 **55/55 通过**。

⇒ 删除一个**未被引用的枚举成员**不可能改变 `GraphSnapshotVersion`；
该 5 条失败是**采样期源码漂移**的产物，**不归因于本次改动**。

**方法论教训（与本仓已有记录同源，值得复用）**：
在共享工作树里，**一次失败的归因必须先核对失败窗口内的源码哈希**；
「先失败后通过」既**不**等于「已修复」，也**不**等于「失败是幻觉」——
要拿**哈希**证明窗口内确有外部写入。本仓已多次记录同源陷阱
（陈旧制品、`ToArray()` 所有权、把非不变量当基线）。

**写入者唯一性探测（供提交前参考）**：
20:27–20:28 我对 5 个关键文件做 75 s 哈希轮询，**全部 `UNCHANGED`**，看似已静默；
但 **20:34 仍发生**上述外部改写 ⇒ **75 s 静默不足以证明写入者已退出**。
提交前建议：改动**前后各记一次**源码哈希，并确认 `git status` 中本任务涉及文件集合
**仅有本任务的改动**。

---

## 6. M1 的剩项（唯一未 done 项）

| 项 | 状态 | 核实 |
| --- | --- | --- |
| 阶段耗时门槛（§7 第 108 条，唯一未过验收项） | **Inconclusive** | 墙钟在本机不可用：同二进制同输入三次差 **−70.9%/+16.3%/+44.6%**；机器安静后（`dotnet` 10→2）仍差 **8.7%–63.8%**；阶段拉长到 ~30 s 仍差 **15.2%** |
| 低噪声代理 | 已建立 | `GC.GetTotalAllocatedBytes(precise: true)` 复现到 **±0.045%**，测得 **−5.36%/−13.97%**；但**代理不等于耗时**，门槛本身仍未通过 |
| §7 第 4 条子项①「key 引用已清理」 | **✅ 已修复（本轮）** | 原断言只读账本计数器、不读槽内容；**变异实测**：删掉 `rows.Clear();` 整行后该测试类 **39/39 通过**。现产品侧在 `Clear()` 后实测 `rows.Count` 累加为 `SortBufferResidualRows`，新增判据断言其为 0。**变异复验：删除 `Clear()` 后新判据失败、旧计数器判据仍通过（1 失败/39 通过）** ⇒ 缺口关闭，总索引 §4 的「强引用可达性」由注释级承诺变为可观测事实。详见 §10 |

**M1 保持 `in_progress` 是正确的**：唯一未过项仍是**阶段耗时门槛**，不得把「方案 B 完成」写成整个 M1 完成。

---

## 7. 非记忆优化的未完成项（核实）

`feature_list.json` 共 **62** 个 feature；非 `done` 状态 **10** 个，与你的记录**一致**：

| feature | 状态 | 核实要点 |
| --- | --- | --- |
| `interprocedural-plan-carrier-compaction` | in_progress | M1，见 §6 |
| `rule-catalog-source-generator-execution` | in_progress | 生成目录当前只含 Core |
| `minimal-roslyn-cpg-declared-symbol-query-optimization` | in_progress | 0 条 verification |
| `roslyn-deletion-prototype-mark-analysis-snapshot-optimization` | in_progress | 0 条 verification |
| `cpg-minimal-routing-catalog` | in_progress | 0 条 verification |
| `unified-work-scheduler-s2-rule-dag` | in_progress | S2-1/S2-2/S2-4 已迁移；S2-3 与 `ForEachScan` **按设计受阻** |
| `unified-work-scheduler-s2-blocked-sites` | **blocked** | 结构性顺序约束；解锁条件为 S3-1b/S3-1c，**经核查确实未满足** |
| `unified-work-scheduler-gate-g4b-shard-adapter` | todo | 前置类型**确实不存在**：`src/NLISSN.Application/Analysis/DocumentShardPlanner.cs` 与 `DocumentShardPlan.cs` 均无（全仓仅有一个同名**测试**文件） |
| `minimal-roslyn-cpg-next-analysis-gap` | todo | 无证据 |
| `flow-summary-2-execution` | pending | 无证据 |

**顺带核实到一处状态词表不统一**（不影响你的 10 项计数，但影响机读）：
有效值为 `done/todo/in_progress/blocked`，而实际另有
**`completed` 3 个**、**`complete` 2 个**、**`superseded` 2 个**、**`pending` 1 个**。
其中 `complete` 与 `completed` 语义重复、且与 `done` 并存 ⇒ 若下游按状态串过滤会漏项。
**本轮未改**（属状态模型决策，需你裁决），仅记录。

---

## 8. 本轮未验证项（与既有记录一致，不得越读）

- **端到端峰值驻留内存**：未测（M5 的 46.6% 只是**邻接载体**净存活字节，非整次构建峰值）。
- **Performance 档**：未跑。
- **NPC 全量语料**：未跑（M5 夹具均为方法级小样本）。
- **DOP>1 等价性**：M5 已由第三会话补 dop 1/2/4/8 护栏；M1 仍未做 DOP 1/2 对照。
- **T2 的真实效果只在专门构造的 `ZeroFactChain` 上可见**（`CandidatePredecessorVisits` 24 → 0）；
  三个冻结 oracle 夹具上的该计数**一字未动**（那些操作本来就没有计划前驱）——
  报告已如实分开陈述。
- **并发工作流 14:45–14:48 独立落地的 T4 CSR 邻接**：**不是本任务交付**，
  其收益与等价性**未经本任务验证、本任务不予背书**；本任务仅复验 T2 未被破坏。
- **两批 `CalibrationGate` 均为 Inconclusive** ⇒ 只声明**结构计数与前驱访问的减少**，
  **不声明性能提升**。
- **9 个夹具中有 3 个低于计划设定的 ≥40% 门槛**（32.7%–37.0%），
  均为「邻接极稀疏而节点多」形态；合计 **46.6%** 达标，
  但**不声称每个夹具都降 40%**。

---

## 9. 本轮改动清单

| 文件 | 改动 | 性质 |
| --- | --- | --- |
| `docs/plans/2026-09-25-dataflow-adjacency-compaction-execution.md` | §7.3 口径注记更正（3/9、真实夹具名、实测节点数）；§7.6 升级为「已显式退役」；新增 §7.8 收尾核实 | 文档 |
| `src/NLCPG/Builder/DataFlowMethodProbe.cs` | 删除死枚举成员 `PlanNeighborMaterializationVisits` + 退役说明注释 | 产品（1 行 + 注释） |
| `Context/feature_list.json` | M5 记录同步「枚举成员已删除」；M1 新增 CLOSURE 条目（29 → 30 条） | 状态 |
| `Context/progress.md` | M5 边界段消除 DOP 自相矛盾 | 状态 |

**§10 追加的执行改动（第五会话）**：

| 文件 | 改动 | 性质 |
| --- | --- | --- |
| `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlanGroup.cs` | 账本新增 `SortBufferResidualRows`（record + 累加器 + `ToLedger` + `Empty`） | 产品 |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | 发布段 `Clear()` 后累加 `rows.Count` | 产品（1 行 + 注释） |
| `tests/…/Cpg/InterproceduralPlanCompactionTests.cs` | 新增残留行判据 + 旧判据改注 | 测试 |
| `docs/plans/2026-09-25-interprocedural-plan-compaction-execution.md` | §8.1/§8.3/§8.4 更新为「已修复」 | 文档 |

**未改**：任何 `.csproj`、`DataFlowPass.cs`、任何夹具、
M2 的 §8.7 夹具位置（按 §3 裁决维持现状）、状态词表不统一问题（按 §7 维持记录）。

**验证**：`NLCPG.csproj` 构建 0 警告 0 错误；
定向过滤器（4 个 DataFlow 测试类）**55/55 通过**；`feature_list.json` JSON 合法（62 features）；
`git diff --check` 退出 0。**全程未运行任何全量测试档。**

---

## 10. 报告执行记录（第五会话，2026-09-25 20:50–21:15）：M1 key-release 缺口**已关闭**

本节是**执行本报告**的结果。报告 §6 中唯一「已提出但未实施」的落地项——
M1 的 key-release 守卫最小修法——**本轮已实施并验证**。

### 10.1 改动

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/Passes/InterproceduralDataFlowPlanGroup.cs` | 账本 record 与累加器新增 `SortBufferResidualRows`；写入 `ToLedger()` 与 `Empty` |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | 发布段 `rows.Clear();` **之后**累加 `rows.Count`（正常恒 0） |
| `tests/…/Cpg/InterproceduralPlanCompactionTests.cs` | 新增 `PublishedSlots_RetainNoResidualSortRowsAfterClear`；既有 `PublishedSlots_ReleaseSortRowKeyReferences` 保留并改注为「锁清空动作发生过」 |

**为什么必须在产品侧记账**（这是原复验者无法在测试侧补齐的原因）：
窗口槽 `windowRows` 是 `FlushParallelPublishWindow` 的**形参**，构建结束后测试无法从任何 API
观察槽内容。原复验者的 `WeakReference` 尝试正因「不经过被测产品代码」而撤回。

### 10.2 判别力：变异实测（本次的关键证据，不是断言）

变异 = **删掉 `rows.Clear();` 整行、保留计数器**：

| 判据 | 变异前 | 变异后 |
| --- | --- | --- |
| `PublishedSlots_RetainNoResidualSortRowsAfterClear`（**新增**） | 通过 | **Failed** ✅ |
| `PublishedSlots_ReleaseSortRowKeyReferences`（**原有**） | 通过 | **Passed** ← 直接证实旧判据确实无判别力 |
| 该类合计 | 40/40 | **1 失败 / 39 通过** |

⇒ 这条变异同时证明了两件事：**新判据承重**，且**旧判据确实只锁计数器**
（与 §6 记录的判断逐字吻合）。报告 §6 原先「无判别力」的表述**不再成立**。

**还原**：行级编辑还原后 `NLCPGBuilder.cs` SHA256 **逐字节相同**（`70DF54D0…`），
`MUTATION-CHECK` 残留 **0** 处，临时备份已删除。

### 10.3 回归与验证

| 判据 | 结果 |
| --- | --- |
| §7 原文四类过滤器 | **65 通过 / 0 失败**（原 64 + 新增 1，**零回归**） |
| `InterproceduralPlanCompactionTests` 单类 | **40/40 通过**（原 39 + 1） |
| `NLCPG.csproj` 构建 | **0 警告 / 0 错误** |
| `feature_list.json` | JSON 合法（62 features）；M1 verification **29 → 30 条** |
| 全量测试档 | **未运行**（遵本轮指令） |

### 10.4 一处操作事故（如实记录）

向 `feature_list.json` 追加条目时，我的插入脚本**漏掉了前一条目末尾的逗号**，
导致 JSON 一度解析失败（`features[1].verification[28]`）。
**已即时定位并修复**，修复后：62 features 合法、状态分布与本轮开始前**逐项一致**
（done 45 / in_progress 6 / completed 3 / complete 2 / superseded 2 / todo 2 / blocked 1 / pending 1）、
M1 30 条与 M5 9 条记录均完好、无空条目。
**教训**：用字符串替换编辑 JSON 时，必须把**前一条目的闭合符**纳入匹配上下文，
并在写入后立即 `ConvertFrom-Json` 验证——本轮正是后者立刻抓到了错误。
（这与本仓反复记录的「变异后必须复核、构建成功不等于产物正确」同源。）

### 10.5 M1 状态未变，仍 `in_progress`

关闭的是**证据轴缺口**，不是**验收门槛**。
M1 唯一未过项仍是 §7 第 108 条的**共同耗时门槛（Inconclusive）**，
故 M1 **保持 `in_progress`**，不得因本节而写成 M1 完成。
