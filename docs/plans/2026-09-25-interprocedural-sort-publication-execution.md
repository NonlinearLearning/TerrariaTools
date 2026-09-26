# P04：跨过程计划排序与串行发布执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在保留跨过程计划稳定排序和串行 `AddEdge` 语义的前提下，减少重复字符串比较；只有被测量证明值得时才改排序键，不绕过图的节点合并与边发布边界。

**Architecture:** 把已经落地的 `PlanIndex + 4 个排序键` 作为基线，不重做方案 A。先给排序比较和发布物化增加可关闭的局部计数；若字符串比较确实占窗口成本，再构造窗口局部、按 `StringComparer.Ordinal` 排出的整数 rank，比较器只比较 rank 和 PlanIndex。AddEdge 保持串行，标签缓存或已知节点快速路径只作为独立的否决项评估。

**Tech Stack:** C#/.NET 10、`NLCPGBuilder`、`InterproceduralDataFlowPlan`、现有窗口并行构造/排序、ContractTests。

日期：2026-09-25。执行切片 `interprocedural-sort-publication` 尚未加入 [feature_list.json](../../Context/feature_list.json)，实施前先登记。当前工作区已有 PlanSortRow 方案 A：行保存 `PlanIndex`，发布时 `plans[row.PlanIndex]` 回读；这不是本计划的新收益。方案 B/C 尚未实施。

## 1. 当前基线与边界

目标文件：[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs)，约 1071–1258。

- 计划构造和组内排序可并行；`FlushParallelPublishWindow` 的 AddEdge 循环按 slot 升序串行。
- `PlanSortRow` 已是 32 B（当前运行时实测），字段为 PlanIndex、BridgeKind、ArgumentOrdinal、SourceKey、TargetKey。
- `BuildPendingNodeCallSiteContext` 已按组计算；不可再次认领这个收益。
- `PlanIndex` 是当前组原始插入序，也是严格 tie-break；不得把已完成 A 写成待做项。
- `NLCPGGraph.AddEdge` 会通过 `AddNode` 做稳定锚点物化，再向 pending buffer 写入序号；这个边界承担节点合并和 metadata 语义。

历史报告把排序行内嵌计划列为旧问题，但当前源码已不再如此。执行文档只处理“重复字符串比较”和“发布物化”两个剩余机会。

## 2. 取舍

| 方案 | 代价 | 决定 |
| --- | --- | --- |
| 窗口局部字符串 rank，仍保留 PlanIndex | 中等；一次去重/排序与两个 int 字段 | 只有计数门槛通过才采用 |
| 复用同一 BridgeKind 的不可变 label | 小；需验证 record/metadata identity 不影响去重 | 可单独测量，收益不足即不做 |
| 为发布器新增 `AddKnownNodeEdge` 快速路径 | 中高；需要证明节点已物化且不绕过合并 | 默认不采用 |
| 全局稳定字符串排名、改 NodeSortKey、重做窗口分区 | 高；改变生命周期或 tie-break 风险 | 不采用 |
| 把 AddEdge 并行化或按边类型分批提交 | 高；直接改变发布顺序 | 不采用 |

## 3. 语义不变量

1. 比较次序必须仍为 `BridgeKind → ArgumentOrdinal → SourceKey → TargetKey → PlanIndex`；rank 只允许替换字符串比较结果，不能改变 ordinal 或空值处理。
2. rank 只能在一个窗口内使用；不得跨图、跨窗口复用整数，因为字符串键的生命周期和排序集合不同。
3. rank 的建立使用 `StringComparer.Ordinal`，重复字符串只保留一个 rank；为同一输入字符串分配的 rank 由其完整排序位置决定，而不是 Dictionary 插入顺序。
4. slot 顺序、计划列表索引、预算裁剪前缀、`recordedReturnMethods` 生命周期和串行 AddEdge 次序不变。
5. 边 label 的缓存只能复用不可变 `ForInterproceduralBridge(BridgeKind)` 结果；带方法 key、resolution、endpoint 的 summary label 不能混入此缓存。
6. 任何快速发布路径必须仍调用节点身份/边去重所需的同一图逻辑；不能只因节点“通常已存在”就跳过 `AddNode`。

## 4. 执行步骤

### T1：建立比较和发布计数

文件：可新增内部 `InterproceduralPublicationProbe`，或复用现有 `PartitionPerformanceEvent`，不扩大公开配置。记录：比较器调用次数、Source/Target 字符串比较次数、相等次数、窗口 unique key 数、BuildAndSortPlanRows 墙钟、串行 AddEdge 墙钟和 label 创建次数。

- [ ] DOP=1 和生产默认 publish DOP 各跑一次预热、三次正式样本。
- [ ] 使用已有跨过程源码夹具，另加多重复键和长字符串键输入。
- [ ] 保存原始边序、`CpgInterproceduralEdgeOrderTests` oracle、GraphSnapshotVersion、计划行数。
- [ ] 探针默认关闭；若校准不确定，不用细项纳秒证明收益。

没有证据显示字符串比较或 label 创建达到窗口成本 10% 以上时，停止本项；PlanIndex 基线保留即可。

### T2：窗口局部 rank（条件步骤）

修改 `BuildAndSortPlanRows` 及其窗口调用，不改 `InterproceduralDataFlowPlan`：

1. 扫描当前窗口的 plans，收集 SourceKey/TargetKey 的引用/值；按 `StringComparer.Ordinal` 排序并去重。
2. 为每个完整字符串建立 rank，生成带 `SourceRank`/`TargetRank` 的临时排序行，PlanIndex 仍是最终 tie-break。
3. `List.Sort` 使用整数 CompareTo；排序结束后释放 rank map、临时键列表和临时行引用。
4. 同一窗口基线和候选各比较一次原始排序行序；不能只比较最终图，因为图冻结会隐藏插入顺序差异。

如果唯一 key 接近行数，rank map 和排序成本可能超过字符串比较；这种输入直接拒绝 T2。若窗口被刷新时每个组独立排序，不能把 rank 表跨窗口保留。

### T3：label 创建微优化（独立可选）

只对 `NLCPGEdgeLabel.ForInterproceduralBridge` 做按 enum 的小缓存，缓存存储必须是只读且不含 call-site context。对每种 bridge kind 比较 label 字段、StableKey、快照和 pending edge 去重结果。不要缓存 `ForFlowSummaryBridge` 结果，不把 label 的引用相等当作语义条件。

若分配下降无法达到共同门槛，回退 T3；不要为了保留它改变 label 类型或全局静态状态。

### T4：发布路径明确否决

可以在探针中测量 `AddNode` 查找占比，但本执行计划不默认添加 `AddKnownNodeEdge`。只有出现稳定的高占比证据、已有图内部方法可安全复用、且新增 Contract 能覆盖 anchor 合并/补全字段时，才另开执行计划。否则记录“发布边界代价已测但不采纳”。

## 5. 验收与命令

- [ ] `CpgInterproceduralEdgeOrderTests` 全部通过，尤其是原始插入序 oracle，不排序比较。
- [ ] 多次构建、DOP=1/2/16 的节点/边/metadata/GraphSnapshotVersion 逐字节等价。
- [ ] 计划 budget 截断、空组、单组超窗口、多个窗口和 `recordedReturnMethods` 门控等价。
- [ ] T2 只有在排序段中位下降至少 10% 或临时分配下降至少 10%，且总发布中位回退不超过 5% 时采用；T3 同理。

~~~powershell
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgInterproceduralEdgeOrder|FullyQualifiedName~CpgWorkBatchInterprocedural|FullyQualifiedName~PlanSortRow"
pwsh -File ./Miscellaneous/scripts/Run-TestTiers.ps1 -Performance
~~~

性能测试必须固定外部 Terraria 输入为 opt-in；常规测试使用仓库夹具。任何 rank 或 label 方案不满足证据门槛都不采用，不为追求第二个收益点扩大到图 API。
