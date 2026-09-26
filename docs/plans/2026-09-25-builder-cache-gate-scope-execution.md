# P05：Builder 缓存锁范围优化执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 只有在真实等待证据成立时，缩短 Builder `_cacheGate` 临界区中可证明独立的 miss 准备工作，同时保持缓存唯一性、图节点合并和现有并发正确性。

**Architecture:** 先给现有单一门加入低开销的等待/持锁观察，再选择最多一到两个 miss 路径做“双重检查—准备—发布”。准备阶段只生成局部 draft 或 Roslyn operation；共享字典、图物化、计数器和 check-then-publish 仍由 `_cacheGate` 保护。禁止去锁、拆成多个门或引入 worker 私有缓存。

**Tech Stack:** C#/.NET 10、`NLCPGBuilder`、Roslyn `SemanticModel`、现有 Dominance/DocumentSharding 并发 ContractTests、WorkBatch telemetry。

日期：2026-09-25。执行切片 `builder-cache-gate-scope` 尚未加入 [feature_list.json](../../Context/feature_list.json)，实施前先登记。当前 `_cacheGate` 是为真实 dominance 竞态修复加入的正确性门；G4a 复验已有 3/3 重复通过和多 worker 护栏。这里没有等待证据，默认不改锁。

## 1. 当前锁覆盖与风险

目标文件：[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs)。当前关键路径：

- `GetOrCreateOperationNode`（约 1659）：命中/未命中、operation kind/name/full/signature/type 解析、`graph.AddNode`、字典写入和计数器都在锁内。
- `GetOperationRoot`（约 1686）：命中/未命中、`SemanticModel.GetOperation`、`RegisterOperationRoot` 都在锁内。
- `GetOrCreateSymbolNode`（约 1906）和 `GetOrCreateTypeDeclNode`（约 1937）：符号字段解析、graph.AddNode、字典/方法表发布在锁内。
- `_declaredTypes` 及 CallGraph 读取、operation root plan check-then-publish 也依赖同门。

锁承担“同一 operation/symbol 只产生一个节点”和跨 worker 可见性；它不是普通性能缓存锁。把 `graph.AddNode` 或共享 map 写入移到锁外会重新引入已修复的竞态。

## 2. 方案取舍

| 方案 | 代价 | 决定 |
| --- | --- | --- |
| 只增加等待/持锁计时 | 小；默认关闭时不记录 | 先采用 |
| Operation/Symbol miss 的纯 draft 解析移出锁，锁内二次检查并发布 | 中；需证明 Roslyn 读取与 graph 发布边界 | 有等待证据才采用 |
| `SemanticModel.GetOperation` 锁外执行后 check-then-publish | 中高；重复计算和 operation identity 风险 | 默认不采用，另立实验 |
| 拆 `_cacheGate`、无锁读、worker 私有缓存/reducer 合并 | 高；锁序/唯一性/快照风险 | 不采用 |

不以“持锁代码行很多”作为采用依据。必须量到等待；没有等待，任何锁范围改动都是风险大于证据。

## 3. 观察指标与不变量

### 指标

在 `lock` 周围用 `Monitor.Enter/Exit` 的等价结构或专用 helper 记录：请求次数、命中/未命中、等待 ticks、持锁 ticks、每个方法的最大/中位等待、`SemanticModel.GetOperation` 与 `graph.AddNode` 在锁内的时间。默认诊断关闭时不分配、不调用 Stopwatch；不要在每次命中无条件创建 record。

测量必须区分 `_cacheGate` 等待与 interner 内部锁等待，锁序仍固定为 `_cacheGate → StringInterner._gate`。

### 不变量

1. 同一 operation/symbol/type 返回同一已发布节点；不能返回准备阶段未发布或被竞态丢弃的节点。
2. 所有共享字典、缓存计数器、`_declaredTypes`、方法查找表和 operation-root plan 的 check-then-publish 仍由同一门保护。
3. `graph.AddNode` 的 stable anchor 合并和补全字段语义不变；不以 anchor 相等推断载荷完全相同。
4. `SemanticModel.GetOperation` 的结果若在锁外计算，发布前必须再次查缓存；输掉竞态的一方返回锁内已发布值，而不是自己的未注册 operation。
5. 异常、取消、重复构建和 builder 复用路径都释放计时状态，不留下半发布缓存。

## 4. 执行步骤

### T1：基线观察，不改同步

文件：可新增 `src/NLCPG/Builder/CacheGateDiagnostics.cs` 和最小 Contract/Host 测试；若现有 telemetry 能承载这些字段，优先复用，不新增公开 API。

- [ ] DOP=1、DOP=2、生产默认 DOP 和高 DOP 各跑三次；夹具覆盖 operation/symbol/root miss 与 hit。
- [ ] 用 `DominanceRaceReachabilityProbe` 和 `DocumentShardingEquivalenceTests` 记录多 worker、快照、节点/边等价。
- [ ] 记录 DataFlow、Dominance、CallGraph 阶段墙钟，等待占比和持锁占比；CPU 受扰时只用结构/锁指标做门控。
- [ ] 若 p95 等待低于目标阶段墙钟的 1% 且持锁总量不占目标阶段 5%，直接结束本项，保留现有锁。

上述阈值是本执行计划的工程停手线，不是对当前环境的预测。测量不具资格时，不得改锁来“试试看”。

### T2：选择一个 miss 路径做双重检查

只有 T1 证明等待显著才执行，优先 `GetOrCreateOperationNode` 或 `GetOrCreateSymbolNode` 的纯字段准备：

1. 锁内先查缓存，命中立即返回并计数。
2. 锁外计算 `NLCPGNodeDraft` 所需的只读 Roslyn 字段；不访问共享 builder 字典、图或 interner。
3. 重新进入同一 `_cacheGate`，再次查缓存；若已有值，丢弃 draft 并返回已发布节点。
4. 仍未命中时在锁内执行 `graph.AddNode`、所有字典/计数器写入和方法表注册。

不能把 symbol key 的计算重复放入锁外/锁内而改变字符串或 comparer；可以在进入锁前计算一次 immutable key，但发布时必须使用同一 key。

### T3：OperationRoot 路径单独判定

默认不移出 `SemanticModel.GetOperation`。若 T1 明确证明它是锁等待主因，建立独立探针和测试：锁外计算后锁内二次查找，输掉竞态返回已注册 root；验证 `OperationInventory`、reference equality 和所有依赖 root 的 pass。Roslyn API 若不能提供足够的并发安全证据，停止 T3。

### T4：并发回归与性能门

- [ ] 变异检查：临时移除第二次缓存检查，测试必须失败；恢复后再跑。
- [ ] DOP=1/2/16 重复构建，快照、完整节点载荷、边序/边集合和 worker 并行护栏逐项相同。
- [ ] 异常/取消路径确认临界区不泄漏，builder reused build 的 hit/miss 计数仍符合既有契约。
- [ ] 锁等待 p95、持锁总时间和目标阶段墙钟都有三次原始样本。

只有等待指标实际下降且总阶段中位下降至少 10%、总构建中位回退不超过 5% 时采用 T2/T3。仅“锁持有时间缩短”但等待和总阶段不变，不采用。

## 5. 验证命令和停止条件

~~~powershell
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DominanceRaceReachabilityProbe|FullyQualifiedName~DocumentShardingEquivalenceTests|FullyQualifiedName~CpgPartitionedBuilder"
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.HostTests/RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponent|FullyQualifiedName~Workspace"
pwsh -File ./Miscellaneous/scripts/Run-TestTiers.ps1 -Fast
~~~

任一并发竞态复现、节点载荷丢失、快照变化、锁测量不确定或准备阶段需要新共享缓存时，立即回退并标记不采用。不要把 `_cacheGate` 方案替换成更复杂的锁层；当前正确性修复优先于无法证明的并行收益。
