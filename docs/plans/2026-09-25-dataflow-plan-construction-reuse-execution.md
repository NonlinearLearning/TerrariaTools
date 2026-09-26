# P02：DataFlow 计划构建与重复遍历复用执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 消除 DataFlow 计划构建和候选循环中的无效重复访问，同时保持操作顺序、CFG 邻接顺序、预算前缀和完整图输出不变。

**Architecture:** 先做零 UsedFacts 短路和单次 `SymbolId` 计算；再在方法局部建立一次操作到节点的投影和一次 ordinal 映射。只有在这些低风险步骤显示仍有足够成本时，才把节点邻接从 `Dictionary<NLCPGNode,List<NLCPGNode>>` + `ToArray` 改为局部 CSR 序号。所有邻接顺序直接继承已有缓存枚举顺序，禁止借此改成排序或全局图格式。

**Tech Stack:** C#/.NET 10、Roslyn `IOperation`、现有 `DataFlowMethodProbe`、xUnit Contract、DataFlowTailMeasurement。

日期：2026-09-25。范围是用户提出的第二优先项；本轮仅编写执行文档，不修改产品代码。执行切片 `dataflow-plan-construction-reuse` 尚未加入 [feature_list.json](../../Context/feature_list.json)，实施前先登记状态和完成条件；已有的 `dataflow-adjacency-compaction` 只作为可能复用的子项，不能替代本切片的状态。

## 1. 当前热点和取舍

目标文件：[DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs)。当前 Collision 记录的每次 Detailed 计数稳定为：`UsedFacts`、计划 operation→node、计划 operation node 再扫、定义采集各 **779**；return 检测实际访问 **770**；CFG incoming/outgoing 缓存条目 **168/167**。这些是访问次数，不是 CPU 百分比。

相关实现链：

- `AnalyzeUsedFactPartition`（约 454）：倒序遍历操作，计算 `DirectFacts`、子记录和 `FactCount`。
- `BuildCfgSensitivePartitionPlan`（约 624）：建立 `operationNodes`、`operationNodesByOperation`、`flowNodes`，再扫描 return/exit。
- `AnalyzeCfgSensitivePartition`（约 752）：定义采集、序号映射、fixpoint、候选前驱重建和显式 source/return 查找。
- `BuildFlowNeighborsFromCache` / `SnapshotNeighbors`（约 1128）：先建节点键字典和 List，再复制为数组。
- `DefinitionFactForSymbol` / `DefinitionFactForParameter`（约 1965）：同一符号各调用 `SymbolId` 两次。

| 方案 | 代价 | 决定 |
| --- | --- | --- |
| `FactCount == 0` 时跳过候选前驱重建 | 一处顺序保持的早返回 | 先采用 |
| `SymbolId` 局部变量复用 | 两个私有方法的机械改动 | 采用 |
| 方法局部操作/节点序号投影 | 中等；减少重复 Dictionary 查找，但需保留 fallback | 条件采用 |
| 直接序号 CSR 邻接 | 中等；替换两个中间载体，收益明确时采用 | 条件采用 |
| 压平 UsedFactRecord、重写 Roslyn 操作遍历、全局 CFG 存储 | 高；会改变事实展开顺序或生命周期 | 不采用 |

“共享操作元数据”不意味着把 `UsedFactRecord` 改成另一种事实模型。只共享本方法已存在的投影，不移动跨 pass 所有权。

## 2. 必须守住的语义

1. `AnalyzeUsedFactPartition` 的倒序访问和 `UsedFactRecord.EnumerateFacts()` 的 direct-before-child 顺序不变。任何把它改成一次正序遍历的尝试都要单独证明，不在本计划内。
2. `operationNodes` 的顺序仍与 `partition.OrderedOperations` 一一对应；`operationNodesByOperation` 对显式 source、return 和 terminal lookup 仍按 reference equality 工作。
3. flow node 的顺序仍是参数节点先、操作节点按原序、return/exit 按当前门控加入。不可用 `HashSet` 枚举顺序替代该顺序。
4. CFG 邻接的元素顺序继承 `_cfgPredecessorsByNode` / `_cfgSuccessorsByNode` 当前集合枚举顺序；不能用排序、反转后继或从后继反推前驱。
5. 候选阶段的前驱 union 顺序、`SparseSetStore` 升序不变量、`MaxCandidateEdgesPerMethod` 的“去重前计数”保持不变。
6. 空事实短路只允许跳过候选阶段的 `sets.Clear`、前驱 union 和 `TryGetCandidates`；fixpoint、definition setup、explicit sources 和 return boundary 仍执行原逻辑。

## 3. 执行步骤

### T1：冻结基线和计数守卫

文件：新增 `tests/NLISSN.ContractTests/Cpg/DataFlowPlanConstructionReuseTests.cs`，必要时复用 `DataFlowDiagnosticsTests`。

- [ ] 保存 Sparse、Collision、JoinLoop 的完整规范化图、PublicationOrder、Raw/UniqueCandidateCount、OverflowReason。
- [ ] 为一个没有 used fact 的操作增加源码夹具，确认它仍保留 CFG/fixpoint 结果，但候选阶段不会报告 `CandidatePredecessorVisits`。
- [ ] 为一个有事实但前驱集合为空的操作保留原“清空后发现为空”的行为；不能把两类情况混为一类。
- [ ] 用两个方法和重复构建确认计数不跨方法、不跨 BuildFromSource 累加。
- [ ] 在改实现前运行测试；新增计数断言应先失败，证明测试确实识别目标行为。

### T2：落地低风险短路和 SymbolId 复用

修改 `DataFlowPass.cs`：

~~~csharp
var usedFactRecord = plan.UsedFactsByOperation[operationNodePair.First];
if (usedFactRecord.FactCount == 0)
{
    continue;
}

sets.Clear(incomingSlot);
// 原有 predecessor union 和 EnumerateFacts 保持不变。
~~~

这段必须放在候选阶段的 predecessor 清空前，而不是放在 fixpoint 之前。`DefinitionFactForSymbol` 与 `DefinitionFactForParameter` 各保存一次 `SymbolId` 结果，构造的四个字段继续使用同一字符串。

定向验证：`DataFlowDiagnosticsTests`、`DataFlowDiagnosticFallbackTests`、SparseSet/Bitset 契约。输出必须逐字段等价；计数应只减少无事实分支的无用前驱访问。

### T3：建立一次操作节点投影

只在 T2 通过后执行：

- [ ] 设计内部只读投影（例如 `OperationNodeProjection`）保存原 operation 数组、node 数组和 reference-equality lookup；不新增公开类型。
- [ ] `BuildCfgSensitivePartitionPlan` 生成一次，后续 definition setup、candidate loop、explicit sources、return boundary 读取它。
- [ ] 对仅需要按序 zip 的循环使用数组；对 source operation 的任意查找保留字典，不能以数组索引假定 Roslyn 子节点一定连续。
- [ ] 记录 `PlanOperationVisits`、`PlanOperationNodeVisits`、`DefinitionOperationVisits` 和显式 source visits 的前后计数，区分真实工作减少和计数器移动。

若投影需要保存第二份完整 operation/node 数组，或计数不下降而只增加生命周期，停止 T3，不进入 CSR。

### T4：方法局部 ordinal 邻接（条件步骤）

目标是替换 `BuildFlowNeighborsFromCache` 的临时节点值容器，不改变缓存本身。执行顺序：

1. 在 flow node 数组建立一次 `Dictionary<NLCPGNode,int>`，使用当前节点相等语义。
2. 对每个 flow node 读取缓存邻居，第一遍统计每个节点 bucket 长度，第二遍按缓存枚举顺序写 `offsets` 和 `ordinals`。
3. 计划保留 incoming/outgoing 两套只读 CSR；fixpoint 和 candidate loop 通过 ordinal 遍历，再从 `flowNodes[ordinal]` 取节点供 FactsMatch/collector 使用。
4. Release 同时清空 offsets/ordinals；不让大方法的一个邻接数组留在窗口缓存中。

推荐复用已有 `dataflow-adjacency-compaction` 文档的测试矩阵，但本计划的前置是 T2/T3 已通过；两份文档若同时执行，只保留一个实现，不得各自引入一套 ordinal 表。

CSR 的两遍构建是有意的：若只为消除 List 分配而多次扫描缓存，必须用计数证明新增扫描低于删除的 List/ToArray 成本；否则保留现有实现。不得把 `HashSet<NLCPGNode>` 的当前枚举改为排序。

### T5：候选与预算回归

- [ ] 用候选预算为 1、恰好边界、越界三组输入比较 `RawCandidateCount`、`UniqueCandidateCount`、`ExitReason`、空 publication。
- [ ] 用 Sparse、Collision、JoinLoop 比较完整有序图和候选提交序列；不只比较边数或快照版本。
- [ ] DOP=1 做正式计时；DOP=2 只做等价检查。方法内不加并行。

## 4. 验收与停止条件

低成本 T2 的结构验收是：无事实操作不再访问候选前驱，SymbolId 每个方法只计算一次，所有图和预算输出等价。T3/T4 还必须满足共同门槛的阶段中位下降 10% 或实际分配下降 10%；总耗时中位不得回退超过 5%。

停止条件：

- 任一顺序、预算、fallback 或 DOP 等价失败，回退最近一个小步骤。
- 需要改变 `UsedFactRecord` 公开可见性、Roslyn 操作身份或全局 CFG 缓存时，标记成本过高并停止。
- T3 仅减少一次字典查找却增加等量数组/字典常驻，不采用。
- T4 的两遍邻接构建无法在真实大方法上显示净收益，不采用；不以小夹具的微秒波动硬保留。

验证命令（执行时使用唯一输出目录并保留原始日志）：

~~~powershell
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DataFlow|FullyQualifiedName~SparseSet|FullyQualifiedName~Bitset"
pwsh -File ./Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory "Build/DataFlowTailMeasurement/p02-<run-id>"
python tools/DataFlowTailMeasurement/summarize.py "Build/DataFlowTailMeasurement/p02-<run-id>"
~~~

报告必须把 T2、T3、T4 分开写；不能把已有稀疏 bitset 或 DefinitionFactIndex 紧凑序数的收益归入本项。
