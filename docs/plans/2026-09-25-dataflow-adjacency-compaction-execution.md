# M5 DataFlow 方法局部邻接序号化 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 消除每节点前驱/后继 List、ToArray 和节点值字典副本，以连续 int 邻接代替完整节点邻接值。

**Architecture:** 方法计划拥有一次生成的 FlowNodes 和原节点相等语义的 ordinal 映射。前驱、后继分别从各自缓存原序构建 CSR；fixpoint 和候选循环读取序号。图缓存和候选语义不变，计划释放时一起释放这些局部结构。

**Tech Stack:** C# / .NET、int[] offsets/ordinals、现有 DataFlow diagnostics 和 ContractTests。

---

日期：2026-09-25。feature：`dataflow-adjacency-compaction`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置、成本门槛和回退见 [总索引](2026-09-25-memory-optimization-execution-index.md)。本轮只交付执行文档。

> **执行状态（2026-09-25）：已完成并通过。** M7 已收口（`sparse-set-union-allocation` → done），
> 本项以其验收后的工作树为基线实施。执行结果与证据见文末 **§7**。

## 1. 方案选择

| 方案 | 决定 | 理由 |
| --- | --- | --- |
| 方法局部 CSR + 复用既有流节点 ordinal | 采用 | 消除 O(N) 容器和 O(E) 完整节点副本，消费循环还可少一次节点查找 |
| 只让空节点不创建 List | 不作为主方案 | 改动小，但保留有邻居节点的大值复制和两次物化 |
| 由后继反转生成全部前驱 | 不采用 | 反转顺序未必等于当前前驱缓存顺序，影响 worklist 和预算路径 |
| 全局 CFG 格式替换、方法内并行、候选索引重写 | 不采用 | 超出局部存储优化，证明和集成代价过高 |

粗估 2–3 人日。产品集中于 DataFlowPass，最多新增一个内部方法局部邻接类型文件。
已有其它计划若也准备实施“方法局部序号邻接”，由本 M5 单独拥有该子任务，另一计划只引用本项结果，不能各写一套或重复归因。

## 2. 现状与不变量

[DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs) 中：

- BuildCfgSensitivePartitionPlan 两次调用 BuildFlowNeighborsFromCache + SnapshotNeighbors。
- 前者给每个节点先创建 List，后者复制成数组和另一个大节点键字典。
- AnalyzeCfgSensitivePartition 已有 FlowNodes、flowNodeOrdinals、整数 worklist；邻接循环又把节点查回序号。
- 候选生成重扫前驱，顺序与预算是正确性约束；SparseSet 的定义序数与流节点序数是不同空间。

必须保持 FlowNodes 首次加入顺序、同身份节点映射、每个缓存邻居的原枚举顺序及重复项处理。
前驱和后继分别构建，不能排序、额外去重或依赖 HashSet 全局枚举顺序。
原缓存邻居的完整载荷仅用于 membership/ordinal 查找的事实，须在实施前逐个消费点核对；若某处确实读取不同载荷字段，先保留那一处值路径，不用规范节点替代原值。

## 3. 表示、构造和释放

N 个流节点，每个方向拥有：

```text
offsets: int[N + 1]
neighbors: int[E]
nodeOrdinal 的邻接区间 = neighbors[offsets[nodeOrdinal] .. offsets[nodeOrdinal + 1]]
```

空区间由相等 offsets 表示；零节点/零边共享空 neighbors，不逐节点创建数组或包装对象。
完整节点继续由 FlowNodes 持有，ordinal 映射最多一份。

构造步骤：

1. 在原 FlowNodes 集合完成后，只物化一次数组并建立 node→ordinal 映射，沿用原相等比较器；之后分析阶段复用它，删除重复建表。
2. 对前驱缓存按 FlowNodes 序逐节点计数，只保留可在本方法 ordinal 表中命中的邻居，前缀和得到 offsets。
3. 精确分配 neighbors，第二遍沿同一缓存枚举顺序填入 ordinal；每个节点只需局部 cursor，不再建 List。
4. 对后继独立重复上述过程。两遍之间缓存必须稳定；先核对缓存写入边界，不能凭“只读”注释假定。
5. fixpoint 当前已有 nodeOrdinal，直接切区间；候选操作节点用原映射定位当前节点，再读前驱序号。定义 ordinal 转换仍由原 definitionOrdinals 承担。
6. MethodDataFlowPlan.Release 释放两组 CSR、FlowNodes 和映射；不得留下全局缓存或池容量。

两遍扫描可能多访问 CFG 缓存，这是明确的成本。诊断访问计数记录真实计数，不能伪装仍只扫描一遍；
保留候选原始提交序、预算事件、fixpoint 迭代等业务可观察结果。新/旧表示转换仅允许在短期对照探针中共存，最终产品直接构建 CSR。

## 4. Task 1：基线、顺序与路径覆盖

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowSparseSetTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/DataFlowDiagnosticsTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/DataFlowAdjacencyCompactionTests.cs`。
- Fixtures: `tests/NLISSN.Testing/TestCodeSet/Cpg/DataFlowMeasurementSources.cs`。

1. 冻结 Sparse/Collision/JoinLoop 的完整图、候选原始提交序、退出原因与预算结果。
2. 增加孤立节点、链、汇合、回边、自环、同节点多路径、跨方法缓存邻居被过滤的场景。
3. 受控结构检查逐节点对照两个方向的原顺序，故意打乱邻接的负对照必须能让顺序敏感测试失败。
4. 新表示的“无逐节点 List/邻接数组”和净字节门槛在旧实现失败；语义基线在旧实现通过。
5. 确认所有读取 Predecessors/Successors 的位置，记录每处只需要身份还是读取载荷。

## 5. Task 2：构造和消费迁移

1. 把原分析阶段的流节点 ordinal 建表移至计划持有，参数、operation、return/exit 的加入次序不变。
2. 实现局部 CSR 计数/填充与空表示，不引入泛型图框架。
3. 依次迁移 fixpoint 前驱、fixpoint 后继、candidate 前驱三个消费点；每次验证相同循环访问顺序。
4. 删除 BuildFlowNeighborsFromCache/SnapshotNeighbors 旧物化链及旧计划字段，不保留两份邻接。
5. Release 覆盖新载体，取消/异常原 finally 不变。同步更新实际计数口径对应的诊断断言，禁止改候选输出 oracle。

## 6. Task 3：验证与成本裁决

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~DataFlowAdjacencyCompactionTests|FullyQualifiedName~CpgWorkBatchDataFlowTests|FullyQualifiedName~NLCPGDataFlowSparseSetTests|FullyQualifiedName~NLCPGDataFlowBitsetCompactionTests|FullyQualifiedName~DataFlowDiagnosticsTests'
$runId = Get-Date -Format yyyyMMdd-HHmmss-fff
pwsh -File ./Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory "Build/DataFlowTailMeasurement/m5-$runId"
python tools/DataFlowTailMeasurement/summarize.py "Build/DataFlowTailMeasurement/m5-$runId"
```

先执行总索引构建。该宿主比较本版诊断模式，另须对照改前产物验证算法前后等价；完整批次仍遵守脚本自己的 5 秒/45 秒限制。

拟定采纳门槛：

- CSR 逐节点序列与旧前驱/后继相等，候选提交序、完整图、预算结果及 DOP 1/2 等价。
- 不再存在 O(N) 个局部邻接 List/数组；最终邻接为每方向两块数组，node→ordinal 不重复构建。
- 在含孤立节点和非空邻接的代表性小夹具上，邻接及其必需映射的总保留字节下降至少 40%；新增映射若非复用须完整计入。40% 是拟定工程决策线。
- 计数/填充两遍的构造成本和分析读取收益一起测量，PlanBuild+Analyze 的中位耗时满足共同门槛；不只测热循环。
- 诊断开启/关闭均正确；扫描计数可因真实两遍访问改变，输出等价标准不降低。

若缓存不稳定需要快照一份完整邻居才能做两遍，或必须重写全局 CFG 才能落地，停止本项。
不以逐节点池化 List 作为自动替代；保留负结论，由用户决定是否接受另一个低收益方案。

## 7. 执行结果（2026-09-25）

结论：**通过**。邻接改为每方向两块 `int[]`（offsets/ordinals），旧「每节点 List + 字典 + 整份复制」
链条已删除，图/候选提交序/邻接访问计数全部等价，保留字节下降 **46.6%**。
证据目录 `Build/MemoryOptimization/M5/run-20260925-143817/` 与 `Build/MemoryOptimization/M5/ab/`。

### 7.1 逐节点表示等价（最强的正确性判据）

因为结果必须可独立复核，本项在**隔离镜像** `ab/diff` 中同时构造新旧两种表示并逐节点比对序号序列
（产品镜像不含该插桩；`pre`/`post` 为仅差本项补丁的对照镜像）。

| 判据 | 结果 |
| --- | --- |
| 比对节点数 | **4,316**（9 个夹具，含结构夹具） |
| 比对边（邻接项）数 | **792** |
| 不一致数 | **0** |
| 负对照（故意打乱首两项） | 抓出 **60** 处不一致 ⇒ 该插桩**非空转** |

### 7.2 真实夹具等价性（同二进制交替 A/B，2 轮 × 2 次重复）

9/9 夹具 `GraphHashEq = True` 且 `PubOrderEq = True`（完整图快照 + 候选发布顺序逐字节相同）。
邻接访问计数 `PlanIncoming/OutgoingNodeVisits` 两侧**逐项相同**（如 `Collision32` 均为 1568，
`Sparse64` 均为 1160）——区间长度若漏边/重复/误纳方法外邻居，这些计数会立刻改变。

PlanBuild 相位耗时（每轮取最小值，同机有十余个并发 `dotnet` 进程，**只作参考不作判据**）：
9/9 夹具 POST 均不高于 PRE，例如 `Collision32` 5.62 → 3.21 ms、`Sparse64` 4.48 → 2.41 ms。

### 7.3 保留字节（受控，`GC.GetTotalMemory(forceFullCollection: true)` 差值）

方法：抓住邻接载体强制 GC 测得 M1，释放载体再强制 GC 测得 M2，`M1 − M2` 即载体自身存活字节
（图在两次测量中都存活，故被消掉）。两侧注入**同一个**观察器，代码路径对称。

| 夹具 | PRE 字节 | POST 字节 | 降幅 |
| --- | --- | --- | --- |
| Collision32 | 269,368 | 118,144 | 56.1% |
| FactWithIncomingSet | 2,768 | 1,168 | 57.8% |
| FactWithEmptyIncomingSet | 2,512 | 1,136 | 54.8% |
| StructureZoo | 55,424 | 27,232 | 50.9% |
| ZeroFactChain | 47,344 | 25,280 | 46.6% |
| JoinLoop4x4 | 101,056 | 55,024 | 45.6% |
| ZeroFactOps | 39,920 | 25,152 | 37.0% |
| Sparse64 | 173,904 | 115,712 | 33.5% |
| AllZeroFactMethod | 7,248 | 4,880 | 32.7% |
| **合计** | **699,544** | **373,728** | **46.6%** |

结构证据（载体形状，独立于 GC 差值）：PRE 为 `Dictionary[N]+Dictionary[N]`，
POST 为 `Dictionary[N]+int[N+1]+int[N+1]+int[E]+int[E]`，即**每方向两块数组**。
`Dictionary[N]` 是复用的 node→ordinal 映射（原先在分析期重建第二份，现只建一次）。

> ⚠️ **门槛口径**：计划 §6 的「≥40%」是**含孤立节点和非空邻接的代表性夹具**口径。
> **3/9** 夹具低于 40%（`AllZeroFactMethod` 32.7%、`Sparse64` 33.5%、`ZeroFactOps` 37.0%），
> 它们都是**邻接极稀疏而节点多**的形态：`Sparse64` 有 **2218 个图节点 / 580 个流节点**，
> 而每方向**仅 67 条保留邻接**，
> 此时 CSR 的 `int[N+1]` offsets 与映射的固定成本占比高。
> （本节原写「4/9」并列出一个**全仓不存在**的夹具名 `WideLocals`；已按 7.3 表中九个数与
> `retain-pre.json`/`retain-post.json` 原始字节更正为 **3/9**，与 `feature_list.json` 一致。）
> 合计降幅 46.6% 达标，但**不能声称每个夹具都下降 40%**。

### 7.4 与计划的差异（必须记录）

计划 §3 步骤 2–4 设计为「先计数、再填充」的**两遍**构造，并要求先核对缓存两遍之间的稳定性。
实施改为**单遍**：按 flow 节点序追加，`offsets[i + 1]` 恒等于追加后的总数，
末位补 `offsets[N] = flat.Count`。因此**两遍稳定性前置条件不再存在**，
计划中「若缓存不稳定需快照完整邻居就停止本项」的回退条件未被触发。
节点→序号映射（计划步骤 1）从分析期上移到计划构建期，只建一次。

### 7.5 测试与并发

- 新增 `tests/NLISSN.ContractTests/Cpg/DataFlowAdjacencyCompactionTests.cs`：**20/20 通过**。
  含 9 个夹具的冻结图/边数、邻接访问计数与确定性、旧物化链的源码护栏、默认模式无诊断。
- M7 的定向 4 类过滤器（含本项新类）**41/41 通过**。
- `NLCPG.csproj` 构建 **0 警告 0 错误**。
- 本轮共享工作树仍被其他工作流并发编辑（`NLCPGBuilder.cs` 等）；
  全部测量在 `Build/` 下隔离镜像完成，`pre`/`post` 只差本项补丁（指纹见 `ab/fingerprints.json`）。

### 7.6 未验证边界

- **未测端到端峰值驻留内存**：7.3 是**邻接载体**的净存活字节，不等于整次构建的峰值下降。
- **未跑 Performance 档、未跑 NPC 全量语料**；夹具均为方法级小样本。
- `PlanNeighborMaterializationVisits` 已随旧物化链（`BuildFlowNeighborsFromCache`/
  `SnapshotNeighbors`）一并**显式退役**：新构造路径 `BuildFlowNeighborCsr` 没有等价写入点，
  故该枚举成员已从 `DataFlowCounter` 删除，**不再以恒 0 的形态出现在诊断输出里**
  （保留它会把「已失效」伪装成「仍在计数」，违反总索引 §2）。
  等价工作量已由 `PlanIncomingEdgesRetained`/`PlanOutgoingEdgesRetained` 覆盖
  （每条保留边在 `flat.Add` 处各计一次），二者为测试实际断言的计数。
  历史 CSV `docs/benchmarks/dataflow-tail-small-batch-20260925/measurements.csv`
  的该列表头保留原样，以便与旧运行对读；新运行不再产生该列。

### 7.7 第三会话独立复验补充（2026-09-25 15:20–17:20）

复验记录：`Build/MemoryOptimization/M5/indep-verify/INDEPENDENT-VERIFICATION.md`。
本会话未修改任何产品代码，`DataFlowPass.cs` SHA256 复验前后一致
（`0B728A65FFFBCD6F…`）；实现者测量所用 `ab/post` 镜像与其仅差 1 行插桩 + 1 行注释。

**✅ 已关闭原 7.6 中「计划 §6 要求的 DOP 1/2 对照未执行」一项**

新增永久护栏
`DataFlowAdjacencyCompactionTests.BuildFromSource_CandidatePublicationOrder_IsIndependentOfDegreeOfParallelism`
（dop 1/2/4/8 共 4 个用例）。原 7.6 的该条**不再成立**，故从边界清单移除。

- 既有的 `CpgWorkBatchDataFlowTests` 跨 DOP 测试**不足以**覆盖这一条：其 `DescribeGraph`
  对边序列做了 `OrderBy(SourceNodeId).ThenBy(Kind).ThenBy(TargetNodeId)` 排序，
  **锁不住提交顺序**——而提交顺序正是本项 §2 要求保持的不变量。故该缺口真实存在。
- 测试类现为 **24/24 通过**；§6 原文过滤器 **52/52 通过**（原 48 + 新增 4）。
- 源码护栏经变异验证**承重**：把 `BuildFlowNeighborCsr(` 改名为 `SnapshotNeighbors(` 后，
  **只有**该护栏失败（1 失败 / 24 通过），其余 23 条不变。

**⚠️ 复验发现：诊断发布顺序不是稳定量（先于本项存在，非本项回归）**

我在复验中一度把实测到的「每批 33 个方法、批内降序」当作可冻结基线写进测试
（n=8 → `7..0`；n=40 → `32..0` 然后 `39..33`），**这是错的，已撤回**。
后续证据推翻它：同一份夹具在本类**单独运行**时是降序，与 `DocumentShardingEquivalenceTests`
**同进程运行**时变成**升序**（`0..32`）。即该顺序取决于**进程/线程池状态**，并非不变量。
把非不变量冻结为基线会产生**非确定性断言**（同一提交在不同测试组合下结论相反），
这正是自守卫类断言最危险的失败模式。

**已改为只断言真正的不变量**：方法集合不重不漏（数量 + 去重计数）、
以及跨 DOP 的列表顺序与逐方法提交序一致。

改前快照与当前源码在两个排序处仍**逐行相同**
（PRE L568 / CUR L584、PRE L617 / CUR L633），故顺序形态**先于本项**存在，
**不记为 M5 的回归或缺陷**；本轮**不**对该顺序是否合理下结论。

**方法学教训（写入边界，供后续复验复用）**：

1. 跨 DOP 对照**无法**发现系统性顺序变化——顺序若被整体反转，`dop=1` 与 `dop=N`
   会同时改变、两侧仍相等，断言恒真。本轮两次顺序变异（删除 / 反转 L633 排序）
   **均未被跨 DOP 断言抓住**。
2. 「内禀 oracle」只在它**真的是**内禀时才有效。我最初把它建立在
   一次观测到的批次形态上，而该形态其实是运行环境的函数 ⇒ 造出了假 oracle。
   确立内禀性需要**跨上下文复现**（至少：单独运行 + 与邻近测试同进程运行），
   本轮正是靠这一步才发现错误。


### 7.8 第四会话收尾核实（2026-09-25 20:20–20:45，只读 + 一处死成员删除）

**① 本文档 §7.3 下方口径注记的两处事实错误已更正**（见其上）：

- 原写「**4/9** 夹具低于 40%」与本节 7.3 表内九个数**自相矛盾**（低于 40% 的恰为
  `AllZeroFactMethod` 32.7%、`Sparse64` 33.5%、`ZeroFactOps` 37.0% 共 **3** 个）。
  `feature_list.json` 早已更正为 `3 of 9`，本文档此前未同步 ⇒ 现更正为 **3/9**。
- 原列夹具名 **`WideLocals` 在全仓不存在**（`grep` 仅命中该行自身），且
  「`Sparse64` 67 个节点」与实测不符：`Sparse64` 为 **2218 图节点 / 580 流节点**，
  每方向**67 条保留邻接**。已按 `docs/benchmarks/dataflow-tail-small-batch-20260925/measurements.csv`
  的 `FlowNodeCount` / `PlanIncoming|OutgoingEdgesRetained` 实测列改写。

**② `PlanNeighborMaterializationVisits` 死成员已删除**（产品代码 1 行 + 3 行说明注释）：

- 它已是**零引用死成员**：`src/` 中无写入点（旧链 `BuildFlowNeighborsFromCache`/
  `SnapshotNeighbors` 已删）、无读取点，`tests/` 中也**无任何断言**。
- 删除安全性的依据（不是推测）：`CounterSnapshot()` 由
  `Enum.GetValues<DataFlowCounter>()` **动态**生成键，全仓**无** `Counters.Count`
  或枚举成员数断言 ⇒ 删成员不会让任何既有断言失配。
- 保留它会使该计数**恒为 0 却仍出现在诊断输出里**，与总索引 §2「诊断访问计数记录真实计数，
  **不能伪装**仍只扫描一遍」冲突。这与 7.6 原「未补等价计数器」的记录相比是**更强的处置**：
  由「已失效但仍在输出」变为「已退役且不再输出」。
- 等价工作量仍被 `PlanIncomingEdgesRetained`/`PlanOutgoingEdgesRetained` 覆盖，
  且这两项正是 `DataFlowAdjacencyCompactionTests` 实际断言的计数（不依赖被删成员）。
- **验证**：`NLCPG.csproj` 构建 **0 警告 0 错误**；定向过滤器
  `~DataFlowAdjacencyCompactionTests|~DataFlowCandidateScanPruningTests|~DataFlowDiagnosticFallbackTests|~DataFlowPlanConstructionReuseTests`
  连续运行 **55/55 通过**（含 `DataFlowDiagnosticFallbackTests` 对
  `CounterSnapshot()` 的 8 处具体键断言）。

**③ 一次并发写入者造成的假失败（记录，不归因于任何补丁）**：

上述定向过滤器的**首次**运行报 **5 失败 / 50 通过**，失败面是 `DataFlowPlanConstructionReuseTests`
的冻结图哈希 oracle（如 `Collision` 期望 `5071C7F2…`、实测 `037952D6…`）。
**真因不是被测代码**：`src/NLCPG/Builder/NLCPGBuilder.cs` 在该运行窗口内被**并发工作流**
改写（SHA256 `B9778EC9E4BB555E…` → `834DA7AB1068E28B…`，`git diff --stat` 显示
**1425 插入 / 209 删除**，mtime 20:34:16 落在运行窗口内）。
随后**同一命令、同一二进制**连续 3 次单独运行 `~DataFlowPlanConstructionReuseTests`
均为 **18/18 通过**，组合过滤器亦 **55/55 通过**。
⇒ 该 5 条失败是**采样期源码漂移**的产物，与本次死成员删除**无关**
（删除一个未被引用的枚举成员不可能改变 `GraphSnapshotVersion`）。
**教训（与本仓已有记录同源）**：在共享工作树里，**一次失败的归因必须先核对失败窗口内的源码哈希**；
「先失败后通过」不等于「已修复」，也不等于「失败是幻觉」——要拿哈希证明窗口内有外部写入。

**④ 写入者唯一性（本轮探测结果，供提交前参考）**：
20:27–20:28 对 5 个关键文件做 75 s 哈希轮询，全部 `UNCHANGED`，看似已静默；
但 20:34 仍发生上述外部改写 ⇒ **75 s 静默不足以证明写入者已退出**。
提交前建议按本仓既有做法：改动前后各记一次源码哈希，并确认 `git status` 中
本任务涉及文件集合**仅有本任务的改动**。

