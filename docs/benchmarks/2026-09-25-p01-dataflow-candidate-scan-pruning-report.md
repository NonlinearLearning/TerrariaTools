# P01 DataFlow 候选桶裁剪执行报告

执行时间：**2026-09-25 06:29–06:52，北京时间**。
范围：[P01 执行计划](../plans/2026-09-25-dataflow-candidate-scan-pruning-execution.md)。
共同门槛：[CPG 性能优化执行索引](../plans/2026-09-25-cpg-performance-optimization-execution-index.md)。

## 1. 结论

- **T2 与 T3 均已交付**：`_byBase` 改为仅在 `usedFact.BaseKey is null` 时查询；`_byBaseAndPath` 桶、其建桶与查询、以及仅为它存在的 `ComposeBaseAndPathKey` 已删除。
- **结构目标达成且与预测一致**：Collision 总 posting/Contains 由 **37,764 降为 22,212**（−15,552，−41.18%），其中 Base **10,368 → 0**、BasePath **5,184 → 0**，BasePath 建桶 **64 → 0**，BasePath 键数 **2 → 0**。四轮 Detailed 全部一致，DOP=2 记录同样一致。
- **等价性逐项成立**：三个夹具、全部 21 条记录的 `GraphSnapshotVersion`、`PublicationHash`、节点数、边数、DataFlow 边数、`RawCandidateCount`、`UniqueCandidateCount`、`ExitReason` 与改前逐项相同；`FactsMatchCalls`/`FactsMatchTrue`/`IndexedReturns`/`IndexedDefinitionLookups`/`ReturnedCandidates` 全部未变（Collision 324/324/614/324/324）。提交序列与完整图逐字节相同。
- **时间不可归因**：两批的校准门槛均为 `Inconclusive`（改前 Coarse→Detailed 增幅中位 286.6%、范围 +7.9%…+426%；改后中位 −2.8%、范围 −91.6%…+243%），且候选循环中位耗时在亚毫秒量级。按共同门槛，**本报告只声明扫描减少，不声明性能提升**。
- Sparse 与 JoinLoop 的 Base/BasePath 本来就不扫描（两桶 posting 均为 0），因此裁剪后结构计数完全不变；这符合计划“其他夹具允许剩余 Base 扫描，不得强行全置零”。
- 本报告只覆盖三个小夹具；**未复验 Terraria.NPC 等生产语料**，未执行全仓 Performance 层。

## 2. 改前/改后证据与身份

两批均由官方宿主 `Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1` 生成，各自 21 次真实 `BuildFromSource` 调用。输出目录均不存在（脚本拒绝覆盖），原始记录不可变。

| 项目 | 改前 | 改后 |
| --- | --- | --- |
| 证据目录 | `Build/DataFlowTailMeasurement/p01-pre-r3` | `Build/DataFlowTailMeasurement/p01-post-t2t3` |
| 样本 / 等价 / 覆盖 / 计时不变式 | 21 / True / True / True | 21 / True / True / True |
| 宿主审计 | Status=Verified, 18 次独立逐字节比较, `FullMetadataAndPublicationOrderEqual=true` | 同左 |
| `CalibrationGate` | Inconclusive | Inconclusive |
| NLCPG.dll SHA-256 | `7AAC55F2C9EC815A7A12434F249966B79E421F00232FBCECD0DC525306DD0EE1` | `B38430ED32DF86A9900336CD349F0713CB81988432F34C4D3D3F0445729165FD` |
| 宿主 DLL SHA-256 | `CD023214F0A5BEF670A7BF457DF9FB41DB9B419D61D4A8C36D5BE7CB61A0BEB3` | 同左（宿主未改） |
| 批次 / 进程耗时 | 6935.9 ms / 7115.5 ms | 9478.7 ms / 9699.3 ms |
| `SourceUnchanged` | true | true |
| 配置 | Release，`DOTNET_TieredCompilation=0`，`DOTNET_ReadyToRun=1` | 同左 |

两批的 `source-state.json` 各记录 **464** 个构建相关源文件的 SHA-256，逐一比对后**只有 1 个文件不同**：

| 文件 | 改前 | 改后 |
| --- | --- | --- |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | `D69448111D9C7B194C5742593BA261E7BECB8D9AA4429A1CFE5CD9F188C85120` | `964B994BF30EFB700ED39DFF27332693EA30E040079FBF12A8372FE66C7BC887` |

即唯一变化就是本补丁自身，其余 463 个文件逐字节相同，**不存在工作树漂移**。两批 `Head` 同为 `925161d2003e626fe00000e799cee2f847025067`，含既有未提交改动；不是干净 commit 基线。（两批之间另有一个不属于本补丁的未跟踪文件 `tests/NLISSN.ContractTests/Cpg/M2TimingProbe.cs` 由并发工作流移除，它不参与构建产物，也未出现在任一 `SourceHashes` 中。）

复现入口（从仓库根目录，输出目录必须尚不存在）：

    pwsh -File Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory Build/DataFlowTailMeasurement/replay-p01
    py -3 tools/DataFlowTailMeasurement/summarize.py Build/DataFlowTailMeasurement/replay-p01

### 2.1 关于快照身份的一处更正

本仓库根目录**没有** `rg` 可执行文件，而宿主脚本第 33 行调用 `rg --files`。本次测量通过前置 `Build/tools/rg.cmd` 垫片解决（该目录在 `.gitignore` 的 `Build/` 下，不入库）。

另需注意：`DataFlowGraphSnapshot.Capture` 会序列化**已解析的绝对** `FullName`，因此它的 SHA-256 依赖进程工作目录（宿主从仓库根目录运行，测试宿主从 `Build/test/Debug/net10.0` 运行）。本报告的等价性判据因此使用图中自有、与路径无关的 `GraphSnapshotVersion`，并附节点/边/DataFlow 边计数与 `PublicationHash`；这与宿主 `raw.jsonl` 逐样本记录的 `NormalizedGraphHash` 是两回事，后者用于宿主内部的同批次比较。

## 3. 结构证据

### 3.1 Collision（四轮 Detailed，DOP=1，逐轮相同）

| 指标 | 改前 | 改后 | 变化 |
| --- | --- | --- | --- |
| 总 posting/Contains | 37,764 | 22,212 | −15,552（−41.18%） |
| Location posting | 11,844 | 11,844 | 0 |
| Root posting | 10,368 | 10,368 | 0 |
| Base posting | 10,368 | **0** | −10,368 |
| BasePath posting | 5,184 | **0** | −5,184 |
| BasePath 建桶 posting | 64 | **0** | −64 |
| BasePath 键数 | 2 | **0** | −2 |
| Base 查询数 | 614 | 452 | −162 |
| BasePath 查询数 | 162 | **0** | −162 |
| 返回候选 | 324 | 324 | 0 |

改后 Collision 的 Base 查询 452 次是 `BaseKey is null` 分支的真实工作（container 匹配），并非残留。`BaseContains`/`BaseSeenChecks` 等派生量按 `summarize.py` 的四个恒等式自洽通过。

### 3.2 Sparse 与 JoinLoop（四轮 Detailed，DOP=1，逐轮相同）

| 夹具 | 总 posting 改前 → 改后 | 说明 |
| --- | --- | --- |
| Sparse | 128 → 128 | 两桶 posting 原本为 0；Base 查询 128 → 128（全部走 null 分支） |
| JoinLoop | 4,510 → 4,510 | 两桶 posting 原本为 0；Base 查询 222 → 222 |

### 3.3 计时（不可归因，仅记录）

Collision `MethodTotalTicks` 逐轮抖动远超信号：改前 Detailed r0–r3 = 12.398 / 3.998 / 13.303 / 15.686 ms，改后 = 9.636 / 14.994 / 7.497 / 5.990 ms；Coarse r3 改后出现 70.999 ms 长值。改前 Coarse→Detailed 校准增幅为 +7.9% / +346.5% / +355.8%，改后为 +242.9% / −2.8% / −91.6%。两批 `CalibrationGate` 均为 Inconclusive，函数级纳秒值只保留原始记录，**不取得归因资格**。故本报告不给出“提速 x%”的结论。

## 4. 等价性证明如何落实到代码分支

证明写在 `DataFlowPass.cs` 的两处注释中，紧邻被改代码。

**Base 分支**：`FactRootKey(d) = d.BaseKey ?? d.LocationKey`。对非空查询基址 `b`，`_byBase[b]` 的每个定义都有 `d.BaseKey = b`，故 `FactRootKey(d) = b`，必落在 `_byRoot[b]` 中；`_byRoot[b]` 只是额外多出 `BaseKey` 为 null、`LocationKey = b` 的定义。Root 先扫描、两者共用同一 `reachingSlot`、中间不清 `_seen`，因此后扫 Base 不可能追加候选。`BaseKey` 为 null 时 Root 不执行（`AddReachable` 对空键早返回），而 `_byBase[LocationKey]` 正是 `IsContainerMatch` 要求的“整体使用匹配组成部分”，故保留并改为按 `LocationKey` 查询。

**BasePath 分支**：`ComposeBaseAndPathKey` 用 U+001F 拼接且无转义契约，因此不能仅凭拼接字符串相等就断言基址相同；证明按原 `FactsMatch` 逐分支展开。设使用侧键为 `(L, b, p)`，被 BasePath 命中而 Location/Root 均未命中的定义为 `d`：

1. `LocationKey` 相等 → 已由最先的 `_byLocation[L]` 命中，矛盾。
2. `IsContainerMatch` 需 `FactRootKey(d) = b`。若 `d.BaseKey = b` 则已在 `_byRoot[b]` 命中；否则 `d.BaseKey ≠ b`，与 BasePath 键相等给出的 `d.BaseKey = b` 矛盾。
3. `IsPartMatch` 需 `d.BaseKey = b ?? L`。`b` 非空时同上已在 `_byRoot[b]` 命中；`b` 为 null/空时 BasePath 查询的前置条件（非空 `BaseKey` 与非空 `PathKey`）不成立，该桶根本不会被查询。
4. `IsAliasMatch` 与末尾 `BaseKey`+`Location` 分支同样要求 `d.BaseKey = b`，归入情形 2。

因此 BasePath 的每个命中都已被 Location 或 Root 先行处理；又因 Root 与 BasePath 共享 `reachingSlot` 且中间不清 `_seen`，后扫的 BasePath 不可能追加候选。删除后唯一可观测差异是**索引内部未通过 `FactsMatch` 的候选清单可能变短**——计划第 2 节明确允许这一点，同时要求图与提交顺序不变。

## 5. 测试证据

新增 `tests/NLISSN.ContractTests/Cpg/DataFlowCandidateScanPruningTests.cs`（11 个用例），分层覆盖：

- **真实路径 oracle**：`BuildFromSource` 对三个夹具比对冻结的 `GraphSnapshotVersion`、`PublicationHash`、节点/边/DataFlow 边计数、Raw/Unique 候选数与 `ExitReason`。
- **T2 结构闸门**：非空 `BaseKey` 时 `BaseQueries`/`BasePostings` 必须为 0。
- **T3 结构闸门**：`BasePathQueries`/`BasePathPostings`/`BasePathBuildPostings`/`BasePathKeys` 必须为 0；同基址不同路径仍由 Root 返回两条。
- **边界**：`BaseKey` 为 null 时仍按 `LocationKey` 查 Base 并产生提交；`BaseKey` 为空串时两桶都不查；`LocationKey` 为空时保持早返回契约；同一索引跨调用不泄漏 `_seen` 标记；U+001F 分隔符歧义下两条不同基址的定义合成同一复合键，改后只返回 Root 可达的那一条（2 → 1），这正是计划允许的内部候选缩减。
- 合成事实沿用既有 `DefinitionFact` 反射缝隙（与 `DataFlowDiagnosticFallbackTests` 相同），**未在测试中复制索引或 `FactsMatch` 算法，也未为测试新增公开入口**。

改前红/改后绿证据：

| 阶段 | `DataFlowCandidateScanPruningTests` | 说明 |
| --- | --- | --- |
| T2 未打补丁 | 3 失败（非空基址闸门、跨调用闸门、BasePath 闸门） | 非空基址结构目标确实为红 |
| T2 已打、T3 未打 | 2 失败（两个 T3 闸门） | T2 独立转绿，T3 闸门仍红 |
| T2+T3 已打 | **11/11 通过** | |

定向宽过滤 `FullyQualifiedName~DataFlow|~SparseSet|~Bitset`：**85/85 通过**。
`Run-TestTiers.ps1 -Fast` 的 Unit 层 **117/117 通过**；Contract 层在本轮执行时被另一个并发工作流的 `vstest` 会话（`testhost` PID 29968 / vstest PID 32204）持续锁定 `RoslynDeletionPrototype.ContractTests.dll`，`MSB3027/MSB3021` 复制重试耗尽——这是进程占用，不是本补丁的编译或断言失败。同一 ContractTests 程序集经上述定向过滤（85 项）已在本补丁后通过。

## 6. 未验证边界与停止条件

- **未验证**：生产语料（Terraria.NPC.SetDefaults / AI）、全量 NPC、trace、dump、全仓 Performance 层，以及 DOP>2 的并发行为。三个小夹具的计时不足以支撑任何吞吐结论。
- **未采纳**：按 posting 与 reaching 大小动态选择求交方向；本批不为追逐剩余 98.4% 不可达拒绝引入新索引体系（计划第 4 节停止条件）。
- **回退点**：T2 与 T3 是两个独立的小补丁区域（T2 为 `TryGetCandidates` 中的 Base 调用；T3 为 `DefinitionFactIndex` 字段/建桶/查询与 `ComposeBaseAndPathKey`），任一步出现语义不等价可单独回退。
- 本报告未覆盖、也未改写[原测量报告](2026-09-25-dataflow-tail-small-batch-measurement-report.md)的历史数据；该报告描述的 37,764 posting 基线与本轮改前记录一致，可相互印证。
