# P01：DataFlow 冗余候选桶裁剪执行计划

> 执行时使用 executing-plans 的逐任务方式；先阅读[共同门槛](2026-09-25-cpg-performance-optimization-execution-index.md)。本文件是执行文档，编写本轮未实施算法。

**Goal:** 消除可证明不会贡献 DataFlow 提交的 Base/BasePath 扫描及无用建桶成本。

**Architecture:** 保持 Location → Root → 必要的 Base 查询顺序、现有 reaching 集合和按定义序数去重。先做条件性 Base 裁剪，再单独证明并删除 BasePath；不更换集合表示或候选预算算法。

**Tech Stack:** C#/.NET 10、NLCPG、现有 DataFlow diagnostics、xUnit Contract、DataFlowTailMeasurement。

状态登记：`dataflow-candidate-scan-pruning` 尚未加入 [feature_list.json](../../Context/feature_list.json)。实施前先登记 feature、完成条件和验收证据；本轮不擅自改变状态。

## 1. 当前证据与方案选择

锚点：[DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs) 的 DefinitionFactIndex 构造、TryGetCandidates、AddReachable、FactRootKey、FactsMatch。历史行号分别约 230、273、335、1683、1609；执行时以符号定位。

Collision 四次 Detailed 记录（预热加三轮）一致：37,764 次 posting/Contains；324 次返回候选。Base 10,368 次、BasePath 5,184 次，均零追加；合计扫描占比 41.18%，不是 CPU 收益。见[测量报告](../benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md)和[原始 CSV](../benchmarks/dataflow-tail-small-batch-20260925/measurements.csv)。

| 方案 | 力度/代价 | 决定 |
| --- | --- | --- |
| 条件裁剪 Base + 删除无贡献 BasePath | 删除整桶查询和一个索引的构建；局部改动 | 采用，分两步验收 |
| 按 posting 与 reaching 大小动态选择求交方向 | 可能减少剩余不可达检查，但需要维护桶内顺序、附加标记或恢复次序 | 本轮不采用 |
| 并行生成候选或微调 FactsMatch | 顺序/预算证明成本高，或当前热点证据不足 | 不采用 |

## 2. 等价性证明必须落实到分支

### Base 分支

FactRootKey(d) = d.BaseKey ?? d.LocationKey。对非空查询基址 b，每个 Base[b] 的定义都有 d.BaseKey=b，因此必在 Root[b] 中。Root 先扫描，reachingSlot 相同，中间不清理 _seen；后扫 Base 不会追加候选。

- usedFact.BaseKey 非空：跳过 Base。
- usedFact.BaseKey 为 null：Root 不执行，Base[usedFact.LocationKey] 仍可能贡献“整体使用匹配组成部分”，必须保留。
- usedFact.BaseKey 为空串：旧 Root/Base 均因空键不扫描；仍保持这一行为。
- LocationKey 空：仍返回 false，走原回退，不清理或重定义该早返回契约。

查询骨架（替换原 Base 调用；不是新增算法 API）：

~~~csharp
AddReachable(_byLocation, usedFact.LocationKey, reachingDefinitions, reachingSlot,
    DataFlowCounter.LocationQueries);
AddReachable(_byRoot, usedFact.BaseKey, reachingDefinitions, reachingSlot,
    DataFlowCounter.RootQueries);
if (usedFact.BaseKey is null)
{
    AddReachable(_byBase, usedFact.LocationKey, reachingDefinitions, reachingSlot,
        DataFlowCounter.BaseQueries);
}
~~~

### BasePath 分支

正常组合键匹配表示基址/路径相同，其定义已在 Root[b]。但 ComposeBaseAndPathKey 用 U+001F 拼接，没有通用转义契约，不能仅凭拼接字符串相等就声称基址总相等。

完整证明按原 FactsMatch 展开：BasePath 命中的定义若基址相同，已由 Root 处理；若因为分隔符歧义而基址不同，Container/Part/Alias 分支都不能贡献新匹配。它只有 Location 相同才可能匹配，而这种情况已由最先的 Location 桶处理。因此 BasePath 不会新增通过 FactsMatch 的提交。

验收契约是完整有序图、collector 原始提交序列、RawCandidateCount 与预算行为。分隔符歧义输入下，索引内部未通过 FactsMatch 的多余候选可能减少；不得同时声称这种内部候选清单逐项不变。诊断的 posting、ReturnedCandidates、FactsMatchCalls 允许反映减少后的真实事件。

## 3. 文件与小步骤

修改：DataFlowPass.cs。按需要更新 [DataFlowDiagnosticsTests.cs](../../tests/NLISSN.ContractTests/Cpg/DataFlowDiagnosticsTests.cs)、[DataFlowDiagnosticFallbackTests.cs](../../tests/NLISSN.ContractTests/Cpg/DataFlowDiagnosticFallbackTests.cs)。输入资产放 [DataFlowMeasurementSources.cs](../../tests/NLISSN.Testing/TestCodeSet/Cpg/DataFlowMeasurementSources.cs)。新增行为契约文件拟为 tests/NLISSN.ContractTests/Cpg/DataFlowCandidateScanPruningTests.cs。

### T1：冻结改前 oracle，补能抓住回归的测试

- [ ] 保存三个原有夹具的完整图和 PublicationOrder；另留预算 1、恰好边界、越界的退出原因和原始候选数。
- [ ] 新增非空 BaseKey 的结构测试：旧版确实执行 Base posting，新版要求零；该测试在改算法前必须因计数目标失败。
- [ ] 新增 BaseKey=null、空串、LocationKey 空、跨调用缓冲复用、同基址不同路径及复合键分隔符边界。
- [ ] null/空键若没有可靠源码生成路径，沿用已有内部 fallback 契约作为补充，并明确标注；不得把它冒充真实源码完整覆盖。
- [ ] 图与提交顺序通过真实 BuildFromSource 验证；不在测试里复制 FactsMatch/索引算法。生产不为测试新增公开入口。

### T2：只裁剪 Base，再验证

- [ ] 按上述条件替换 Base 调用，不删除 _byBase 建桶。
- [ ] 保留 matches/touched 的清理位置、DefinitionFact 的遍历顺序和所有预算判断。
- [ ] 运行定向测试，检查 null 基址用例仍有预期提交；保存第一步计数与原输出等价证据。
- [ ] 单独形成可回退的小补丁；不混入排序、邻接或 SymbolId 改动。

### T3：删除 BasePath，单独验证

- [ ] 先用 T1 的边界和完整匹配链验证第二节证明；若现有 oracle 要求更强内部契约且无法保持，保留 BasePath，只交付 T2。
- [ ] 删除 _byBaseAndPath 字段、建桶、查询及仅为其存在的 ComposeBaseAndPathKey。
- [ ] 诊断枚举/输出兼容保留原字段时，其值自然为 0；报告说明算法不再执行这类索引，不伪造命中。
- [ ] 核对宿主覆盖自检与诊断测试是否仍硬要求四桶命中。只修改已经被本补丁合法删除的覆盖要求，保留原始候选、预算和输出 oracle。
- [ ] 清理临时 A/B 接线后再验证最终生产路径。

### T4：测量与收口

~~~powershell
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DataFlow|FullyQualifiedName~SparseSet|FullyQualifiedName~Bitset"
$runId = Get-Date -Format yyyyMMdd-HHmmss-fff
pwsh -File ./Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory "Build/DataFlowTailMeasurement/p01-$runId"
python tools/DataFlowTailMeasurement/summarize.py "Build/DataFlowTailMeasurement/p01-$runId"
~~~

以上宿主只证明本版批次内部一致；另按共同门槛比较改前/改后落盘文件。一次基线测量后先 T2 再 T3，三者分别保留证据，防止收益或错误无法归因。

## 4. 验收与停止条件

- Collision 若仍是同一固定输入，T2 的预期结构目标为 Base posting=0；T3 后 BasePath posting/build posting=0，总 posting 从 37,764 降为 22,212。数值是待验证预测，不是本轮实测。
- 上述预测成立时，Location/Root 顺序与真实匹配结果保持，Raw/UniqueCandidates、退出原因、完整图、提交序列逐项相同。其他夹具允许剩余 Base 扫描，不得强行全置零。
- 语义不等价立即回退最小步骤。只有通过机制与共同性能门槛才能称性能优化已验收；时间仍不确定时只报告扫描减少。
- 不为追逐剩余 98% 不可达拒绝而继续引入新的索引体系；求交方向另立实测问题，不滚入本批。
- 报告放 docs/benchmarks/ 下的新日期文件；更新本 feature 的证据，不能覆盖原测量报告或改写旧数据。
