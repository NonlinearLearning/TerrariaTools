# CPG 查询与图算法性能执行提案 Implementation Plan

> **状态：阶段 1–4 已实施；性能验收未完成。**
>
> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 在保持冻结图、持久化 shard、slice、规则和 rewrite 结果完全等价的前提下，消除已测得的 Dominance、反向 shard 投影与 routing-sidecar 重复读取热点；仅在中型真实样本证明必要时实施 DataFlow bitset 和冻结图邻接重构。

**Architecture:** routing sidecar 保持不可变的构建级文件格式，通过 manifest 身份缓存已验证的内存索引；span/symbol 在读取后使用确定性范围索引。shard v6 增加按目标 local index 分组的 CSR 入边段，旧 shard 继续走 v5 扫描读取。Dominance 的计算内部改为 block ordinal bitset，最终仍物化并排序现有 `Dominates`、`PostDominates` 边。

**Tech Stack:** .NET 10、C#、`FrozenDictionary` / 排序数组、`ulong[]` bitset、SQLite、xUnit、现有 CPG persistence 与 benchmark 工具。

## 执行记录（2026-07-25）

- 已实施：Dominance/PostDominance 位集计算、v6 shard CSR 入边表及 v5 读取兼容、已验证 routing-sidecar single-flight 缓存、span/symbol lower-bound 范围查询，以及 resolver 的 O(1) LRU。
- 已通过：`CpgShardContractTests|NLCPGSliceQueryTests` 为 34/34；`DominancePassContractTests|SqliteCpgShardCatalogTests|CpgBuildRoutingIndexTests|NLCPGPartitionedBuilderTests`（排除两项 interprocedural 测试）为 66/66；`check-harness-consistency.ps1` 通过。
- 未关闭：同一完整筛选中的两项 interprocedural 断言仍失败，尚未归因到本次算法改动；96-method Strict/DOP1 三样本基准在 124 秒上限内未产出 JSON，样本无效。DataFlow bitset 与冻结图 CSR 邻接仍维持遥测准入，未实施。

---

## 1. 依据、范围与顺序

2026-07-25 小样本的当前实现基线如下：

| 路径 | 样本 | p50 | 结论 |
| --- | --- | ---: | --- |
| Dominance 增量 | 64 条件分支单方法 | 2.239 s | 首要计算热点 |
| `ReadIncomingProjection` | 5,000 node / 30,000 edge shard | 0.801 ms | 完整 shard 扫描限制反向 slice |
| verified routing sidecar 读取 | 96 条 route 文件 | 0.193 ms | 每次查询重复 I/O 与 hash 校验 |
| resolver 旧 LRU 淘汰选择 | 1,024 cache entry | 0.182 ms | 缓存规模较大时线性退化 |
| `GetNodesInFileSpan` | 13,936 node 图、结果 1 node | 0.321 ms | 文件内线性过滤 |
| `ExtractLocalView` | 2 hop、878 node / 1,073 edge | 8.785 ms | 全图边扫描限制局部视图 |
| DataFlow fixpoint | 1,291 flow node / 130 definition | 1 ms | 当前小样本没有证明 HashSet 是主热点 |
| freeze index | 1,000 node / 6,000 edge | 21.796 ms | 先以真实 freeze telemetry 判定 |

执行优先顺序固定为：

1. Dominance bitset；
2. shard CSR 入边表；
3. 已验证 routing-sidecar 缓存；
4. resolver O(1) LRU；
5. span/symbol 范围索引；
6. DataFlow bitset 与冻结图 CSR 邻接的遥测准入，不在本提案开始时实现。

本提案不改变默认 DOP、durability、SQLite 单 writer、规则执行顺序、公开 API、NodeId
分配、边排序或 rewrite 行为。

## 2. 全局不变式与验证基线

- DOP 1、8、12、14、16 的节点、边、NodeId、slice path/truncation、Mark、Decision、rewrite 和 diff 必须等价。
- `Complete` build 的可见性、routing manifest hash/length/version 校验、legacy build fallback 与 corruption failure 语义不变。
- `Dominates`、`PostDominates`、`ControlDependence` 的公开返回边集合、稳定排序和 capability dependency 不变。
- 新 shard 格式必须能读取既有 v5 shard；v6 reader 只在通过完整结构校验后使用 CSR。
- 所有性能结论使用同一二进制、固定输入 manifest、一次预热和至少三次测量的中位数；缺少完整结束事件、超时或资源耗尽均为无效样本。

在每个生产改动前，先为当前行为增加测试并观察其失败。每个阶段结束后运行其 owning ContractTests、`Run-TestTiers.ps1 -Fast`、harness consistency 和 `git diff --check`。实施提交遵守仓库 Lore trailer 协议。

## 阶段 0：补齐查询与阶段遥测，锁定语义基线

**文件：**

- 修改：`src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- 修改：`tools/CpgPersistenceBenchmark/Program.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**步骤：**

1. 写失败测试，要求 node/span/symbol lookup telemetry 分别记录 manifest 查询、sidecar read、hash/结构校验、缓存命中、缓存失效、legacy fallback、shard read 与总耗时。
2. 写失败测试，锁定 `ReadIncomingProjection` 对相同 target、edge-kind、maxEdges 的 nodes、edges、顺序和 maxEdges 截断结果。
3. 为 `NLCPGDataFlowPassTelemetry` 和 `NLCPGFreezeTelemetry` 的已有子项写 benchmark 投影测试；测试只验证字段可用与语义，不能用机器时间阈值断言。
4. 实现只追加诊断字段的最小改动；日志与 benchmark JSON 报告冷/热 lookup 的 p50/p95、CSR/legacy 读取分支和 freeze 子阶段。
5. 运行 focused ContractTests，确认未更改查询结果、图快照或默认开关。

**准入门：** 后续阶段必须能分别解释 route catalog、sidecar validation、shard payload、incoming projection、DataFlow substep 和 freeze substep；缺少任一项不得用端到端时间替代。

## 阶段 1：Dominance / PostDominance 位集化

**文件：**

- 修改：`src/NLCPG/Builder/Passes/DominancePass.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- 新建：`tests/Roslyn Prototype.ContractTests/Cpg/DominancePassContractTests.cs`

**步骤：**

1. 先写失败测试：对直线、分支、循环、不可达 block 和多出口方法，锁定完整 `Dominates` 与 `PostDominates` 边键集合和排序；同时锁定 `ControlDependence` 输出。
2. 运行该测试并确认它因缺少 bitset 等价测试夹具而失败，不改生产算法。
3. 建立内部 `BlockBitSet`：block ordinal 是位号，使用定长 `ulong[]`；提供 clone、AND、set、equals、枚举置位 ordinal，禁止暴露可变数组。
4. 将 successor、predecessor、可达集合和迭代中的 dominance 集从 `SortedSet<int>` 替换为 bitset。每一轮只比较位字；最终在边物化前按 ordinal 升序枚举。
5. 保持现有 immediate/完整 dominance 输出策略和 `DominancePass` capability contract；不引入“只存支配树”模式。
6. 运行新旧夹具图、DOP matrix 和 ControlDependence 测试；记录同一中型分支方法的 Dominance p50/p95、分配量与 edge count。

**准入门：** 全部 edge set 和排序相同，ControlDependence 未变化；中型样本的 Dominance 阶段中位数下降且没有分配或 p95 回归。若语义有任何差异，恢复 `SortedSet` 实现并保留失败样本。

## 阶段 2：v6 shard CSR 入边表

**文件：**

- 修改：`src/NLCPG/Persistence/CpgShardContracts.cs`
- 修改：`src/NLCPG/Persistence/CpgShardStore.cs`
- 修改：`src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs`
- 修改：`src/NLCPG/Persistence/CpgFrozenShardExporter.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgShardContractTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`

**步骤：**

1. 写失败测试，给出同一 shard 的 legacy scan 与 CSR projection：断言 incoming nodes、edge keys、CallSiteContext、maxEdges 截断和 deterministic order 全等。
2. 为 v5 fixture 写兼容测试：读取旧格式后只能使用 legacy scan，不能假设 CSR 存在。
3. 将 shard 格式版本从 v5 升为 v6；写入按 `TargetLocalIndex` 分组的 `offsets[localNodeCount + 1]` 与 edge-index 数组。原始 edge 记录顺序保持既有稳定顺序。
4. 在 Strict 结构校验中验证 offsets 单调、首尾边界、每个 edge 恰好覆盖一次、edge index 范围、target 与所属 CSR 段一致，以及 CSR 段内的稳定顺序。
5. `ReadIncomingProjection` 对 v6 通过 `offsets[target]..offsets[target + 1]` 读取；对 v5 保留现有 local-index dictionary + edge scan。
6. 运行 shard persistence、slice、DOP 和 corruption suite；在 5k/30k 与中型真实 shard 上记录 projection p50/p95、读取字节与 GC。

**准入门：** v5/v6 projection、完整恢复图和 slice 输出相等；v6 Strict corruption 测试覆盖 offset/edge index 破坏；反向 projection p95 不高于 legacy，且只读取目标入边段。

## 阶段 3：已验证 routing-sidecar 缓存

**文件：**

- 修改：`src/NLCPG/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/NLCPG/Persistence/CpgBuildRoutingIndexReader.cs`
- 修改：`src/NLCPG/Persistence/CpgShardContracts.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgBuildRoutingIndexTests.cs`

**步骤：**

1. 先写失败测试：同一 complete build 的第二次 node/span/symbol lookup 不重读 sidecar、不重新算 SHA-256；首次 lookup 仍验证完整文件。
2. 写失效测试：manifest 的 `(buildId, payloadHash, byteLength, formatVersion, relativePath)` 任一项改变、文件缺失、长度不符、hash 不符或 reader error 时，缓存条目不能使用。
3. 在 catalog 内引入以该 manifest tuple 为键的 `VerifiedRoutingIndex` 缓存。为并发相同键的首次读取使用单 flight gate；缓存值仅在 reader 已完成版本、长度和 hash 验证后发布。
4. `ReadRoutingIndexCandidatesAsync` 仍从 SQLite 获得 completed build manifest；命中相同 key 时直接复用内存 `CpgBuildRoutingIndex`，不读文件和不重新 hash。
5. 保持 newest-completed build 选择、旧 build fallback 和 “损坏 sidecar 不回退到另一 build” 行为；缓存不能越过 build status 或 manifest 校验。
6. 以 telemetry 验证冷命中与热命中的 read/hash bytes、cache hit/miss、p50/p95；运行 catalog/slice recovery suite。

**准入门：** 热查询的 sidecar read/hash 次数为零，结果集合与冷查询和 legacy build 相同；任何 manifest 或 sidecar 异常都不能返回过期索引。

## 阶段 4：span / symbol 范围索引与 resolver O(1) LRU

**文件：**

- 修改：`src/NLCPG/Persistence/CpgBuildRoutingIndex.cs`
- 修改：`src/NLCPG/Persistence/CpgBuildRoutingIndexReader.cs`
- 修改：`src/NLCPG/Analysis/CpgShardQueryResolver.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgBuildRoutingIndexTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`

**步骤：**

1. 写失败测试，覆盖多 build、多 file identity、重复 span、重复 symbol、空结果和按 shardId 的确定性排序。
2. 保持 sidecar writer 的 span 复合排序键 `(projectId, relativePath, sourceHash, spanStart, spanLength, shardId)` 与 symbol 排序键 `(symbolKey, shardId, localOffset)`。
3. reader 读取后为 span、symbol 构建不可变 key-to-range 目录，或以同一排序数组做 lower-bound / upper-bound；`FindBySpan` 和 `FindBySymbol` 不能再调用全数组 `Where(...).ToArray()`。
4. 在 resolver 中将 `_entries.Remove` 加 `OrderBy(...).First` 替换为 `Dictionary<string, LinkedListNode<CacheEntry>>` 与 recency `LinkedList`。命中移动到 tail；淘汰 head；缓存容量为零和 oversized shard 保持现有语义。
5. 写访问序列测试：命中提升、容量边界、连续淘汰、同 shard 重读、零缓存和 oversized shard；使用公开 `FindBy*Async` 与 telemetry 验证 open/hit/miss/eviction。
6. 在 96、1k、10k route synthetic sidecar 以及真实完成 build 上记录 lookup p50/p95；在 64、256、1,024 cache entry 上记录 eviction 时延与 cache correctness。

**准入门：** 查询结果、去重和排序全等；route 基数上升时 span/symbol 查询不再线性增长；大缓存 LRU 驱逐不再扫描全部 entry。

## 阶段 5：DataFlow bitset 与冻结邻接的遥测准入

**文件：**

- 修改：`tools/CpgPersistenceBenchmark/Program.cs`
- 修改：`tests/Roslyn Prototype.PerformanceTests/` 下对应 CPG 性能测试
- 修改：`docs/plans/2026-07-25-cpg-query-algorithms-execution-proposal.md`

**步骤：**

1. 建立小、中、真实源三档固定输入，记录 `DefinitionFactCount`、`FlowNodeCount`、fixpoint、candidate generation/commit、reaching-definition edge、freeze 各子阶段、managed allocations 和 peak memory。
2. 每档使用一次预热和至少三次样本；同一进程内的微基准仅作诊断，不作为默认值变更依据。
3. DataFlow 仅在 `CfgSensitiveElapsedMilliseconds` 或 `FixpointElapsedMilliseconds` 在中型真实样本中持续占比显著时，另开提案实施 definition ordinal + pooled `ulong[]` bitset。
4. 冻结邻接仅在 `FreezeQueryIndexElapsedMilliseconds` 或重复索引引用的内存占比被确认后，另开提案评估节点 CSR + edge-kind range；该方案不得同时更改公开 query 语义。

**准入门：** 形成可重复、带 input manifest 的证据。未达到门槛时，本阶段正确结果是“不实施”，不能以小样本推断改造收益。

## 验证顺序

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path

dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~DominancePassContractTests|FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgShardContractTests|FullyQualifiedName~NLCPGSliceQueryTests|FullyQualifiedName~SqliteCpgShardCatalogTests|FullyQualifiedName~CpgBuildRoutingIndexTests"

dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false

pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

真实源执行按同一 binary、input manifest、DOP、durability、shard export/file-write concurrency
运行。只在图、slice、Mark、Decision、rewrite 与 diff 等价后比较性能。

## 风险与回滚

| 风险 | 控制措施 | 回滚 |
| --- | --- | --- |
| bitset 改变 dominance 闭包或排序 | 先锁定 edge set / order / ControlDependence | 恢复 `SortedSet` 路径 |
| CSR 偏移损坏或漏边 | v6 严格结构校验与 v5 fallback | 拒绝 v6 shard，保留 v5 reader |
| 缓存返回过期或未验证 sidecar | manifest tuple 失效键、single flight、只缓存验证成功值 | 清空内存缓存，重新验证 |
| LRU 改变缓存可见性 | 以公开 resolver 行为和 telemetry 测试访问序列 | 恢复现有 cache entry 路径 |
| 误把端到端波动当算法收益 | 分段 telemetry、固定输入、多次中位数 | 保持默认值与实现不变 |

## 完成条件

- Dominance、CSR、sidecar cache、span/symbol 索引和 LRU 的语义回归全部通过。
- 中型和真实源测量证明每个已实施阶段的目标阶段 p50 或 p95 改善，并且无 graph/slice/rule/rewrite 回归。
- DataFlow 和 freeze 仅在达到阶段 5 的明确定量门槛后进入独立实施提案。
- `feature_list.json` 只在上述完成条件和验证证据满足后变更状态；`progress.md` 仅记录当前阶段、阻塞和下一步。
