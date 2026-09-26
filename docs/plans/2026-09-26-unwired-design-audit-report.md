# 近期设计文档「已实现但生产未使用」审计报告

日期：2026-09-26。性质：**只读审计**。未修改任何产品代码、未运行任何构建或测试。
范围：2026-09-22 至 2026-09-26 的设计 / 计划 / 执行 / 基准文档（约 47 份 Markdown），
外加 `设计docs/目前设计/性能分析组件.md` 与 `docs/loh-large-object-optimization-guide.md`。

判据：设计文档声称「已实现 / 已落地 / 已完成」的**具名产物**（类型、方法、属性、选项），
其 `src/` 引用是否**全部落在自己的定义文件内**（自引用），或**只有 `tests/` 引用**。
生产入口定义为 `src/NLISSN.Application/**`、`src/NLISSN/**`、`src/NLCPG/Cli/NLCPGCli.cs`、
`ApplicationService.cs`、`DirectoryAnalysisUseCase.cs`、`ProjectJsonExporter.cs`。
`tools/**` 与 `Build/**` 探针**不算**生产入口（它们靠 `InternalsVisibleTo` 读 internal）。

---

## 0. ⚠️ 首要边界：审计期间工作树被并发会话改写

按仓库既有惯例，先声明测不准的部分——**本报告不能当作单一 revision 的快照**：

| 时刻 | 变化 | 影响 |
| --- | --- | --- |
| 17:04 | `ApplicationService.cs` 改写 | — |
| **17:11:56** | `DirectoryAnalysisUseCase.cs` 改写，**移除** `const bool UseCrossFileBatch = false` | **D1 跨文件装箱从「被硬编码关闭」变为「已接通」** |
| 17:07:43 → 17:18 | 出现又消失 `tests/.../ZzTempSingleFileRegistryProbeTests.cs` | 临时探针，非持久产物 |
| **17:21:02** | `2026-09-26-workbatch-shard-packing-execution.md` 补写 **§F（F1 把跨文件装箱接进生产）**，并**自我作废** §E「`BuildMany` 零生产调用者」的表述 | 本报告 §5 据此改述；该文档已成为 D1 的**权威执行记录** |

⇒ 我在 17:0x 观察到的「D1 整条链路被一个 `false` 常量关闭」这一结论**在 17:11 之后已失效**。
本报告对 D1 采用**改写后**的事实（已接通）。此点如实记录，不掩盖前一次观察。

**审计期间捕获的源码哈希（前 16 位，2026-09-26 17:29 复验）**：

```
44A33989A01B618D  src\NLISSN.Application\Analysis\DirectoryAnalysisUseCase.cs
B88ACC88FEFA3DD7  src\NLISSN.Application\Analysis\DocumentShardPlanner.cs
259A3D9246AD75ED  src\NLCPG\Builder\NLCPGBuilder.cs
E89FEDA1855F3E38  src\NLCPG\Builder\Concurrency\CpgWorkBatchBuilder.cs
4DB4270E4D938BC1  src\NLCPG\Builder\Concurrency\CpgWorkBatchExecutor.cs
C20D02BAC3A57B85  src\NLCPG\Builder\Passes\CallGraphPass.cs
788C52E40A9F6D6E  src\NLCPG\Builder\Passes\ControlDependencePass.cs
E859DAB8BC714F62  src\NLCPG\Builder\Passes\PartitionedOperationPass.cs
BE592357855EE45A  src\NLISSN.Infrastructure\Concurrency\IConcurrencyPool.cs
```

> 复验结论（17:29）：§5 的两条判据在**改写后的树上依然成立**——
> 5 个 `new CpgWorkItem(...)` 生产构造点仍**全部**走 7 参重载；
> `CallGraphPass.cs:93` 仍 `foreach (var document in context.Documents)` 跨文件聚合装箱。

---

## 1. 结论提要

**未发现「文档声称实现、而实现被无故拒绝落地」的条目。** 但发现了**四类**共 **24 项**实质问题，
其中 **第 1 类（幽灵类型）与第 3 类（写-only 遥测）最严重**，因为它们使文档的
「可验收性主张」在**非测试**路径上无法成立。

| # | 类别 | 项数 | 严重度 |
| --- | --- | --- | --- |
| 1 | **幽灵类型**：文档点名、且被当作既存事实引用，但全仓不存在 | 5 | **高** |
| 2 | **零调用方**：实现存在，但连自引用之外无任何调用（含死方法/死常量） | 4 | 中 |
| 3 | **仅测试使用**：生产不可达，只有 `tests/` 或探针读得到 | 11 | **高** |
| 4 | **只写不读（写-only 遥测）**：生产写入，无任何读者 | 2 | 中 |

> **⚠️ 更正（2026-09-26 18:2x）**：第 2 类原报 6 项，其中 `CpgWorkBatchBuilder.cs:5-6`
> 两个默认常量经**变异检验**复核后**不成立**（18 个构造点在编译期引用它们），
> 已从该类移出，故由 6 项更正为 **4 项**。另发现同性质的第 5 项
> `PartitionedOperationPass.CountLineSpan`，已一并处置。详见 §4.1.1。
>
> **清理执行结果**：该类 **5 项已删除**（4 项原报 + 1 项附带），
> 构建 0 警告 0 错误，定向测试 135/135 通过。剩余类别的处置见 §7。

**一句话诊断**：本仓库近期采用的验收模式是「**internal 观测面 + Contract 测试断言**」。
该模式对**正确性**有效，但当**同一个 internal 观测面没有任何生产消费者**时，
文档会把「测试能断言」写成「已可观测 / 已落地」，而**生产的实际行为并未改变**。
第 3、4 类是这一模式的系统性副作用，不是个别笔误。

---

## 2. 【高】第 1 类：幽灵类型——文档当成事实引用，实现不存在

| 产物 | 文档：行 | 全仓 `.cs` 命中 | 判定 |
| --- | --- | --- | --- |
| **`CpgFrozenShardStore`** | `docs/loh-large-object-optimization-guide.md:443`；`docs/plans/2026-09-24-frozen-edge-projection-design.md:54,417`；`...-execution.md:43` | **0**（`src` + `tests`） | 类型**根本不存在** |
| `InterproceduralPublicationProbe` | `docs/plans/2026-09-25-interprocedural-sort-publication-execution.md:48` | **0** | 计划项，未实现 |
| `CacheGateDiagnostics` | `docs/plans/2026-09-25-builder-cache-gate-scope-execution.md:55` | **0** | 计划项，未实现 |
| `WordsPerSetBeforeCompaction` / `FlowNodeWordsPerSet` | `2026-09-22-dataflow-bitset-compaction-design.md:250`；`...-execution.md:150`（措辞「**必须同时**增加」） | **0** | 与「必须」措辞**直接矛盾** |
| `NLCPGNodeStreamKey` / `SegmentCursor` | `2026-09-23-zero-allocation-key-execution.md:16-18,358-360` | **0** | 已按回退策略删除 |

**`CpgFrozenShardStore` 是最严重的一条**：它同时出现在**三份**不同文档中，其中
`frozen-edge-projection-design.md:417` 还把它列进一张「已核实」的证据表：

> `| CpgFrozenShardStore 生产零调用者 | 全 src/ 扫描 |`

该表格**结论方向是对的**（内存有界架构确实从未接线），但**引用的被证对象不存在**——
真实存在的是 `CpgShardStore`（`src/NLCPG/Persistence/CpgShardStore.cs`）与
`CpgFrozenShard`（`CpgShardContracts.cs:212`）。一个**不存在的类型名**被当作「已扫描证实」的证据，
说明该表的「全 `src/` 扫描」**没有真正执行**，或执行后未核对命中。

**影响**：`docs/loh-large-object-optimization-guide.md:442-444` 第 6 条边界
（「不启用内存有界架构」）是**正确的**，但读者若按名字去检索该类型，
会得到 0 命中并**误以为自己的检索或仓库结构有问题**。
同期 `frozen-edge-projection-design.md:416` 称 `StreamingMode` 默认在
`NLCPGBuilderOptions.cs:94`（实际 `:109`）、`:418` 称
`RequiresPreallocatedNodeIds()` 在 `NLCPGBuilder.cs:450-453`（实际 `:1235`）
——同一张表的**三处行号锚点全部失准**，进一步佐证该表未经执行核对。

**建议（不代为执行）**：把 `CpgFrozenShardStore` 改为真实类型名 `CpgShardStore`，
或删除该行；三条行号锚点重定位。**不得**保留「已核实」措辞而锚点失准。

---

## 3. 【高】第 3 类：仅测试使用——生产不可达

### 3.1 内存有界架构（persistence / streaming / frozen shard）整条链路

四份文档自述「从未接线」，**核实成立**：

| 产物 | 定义位置 | 生产可达性 | 判定 |
| --- | --- | --- | --- |
| `NLCPGBuilderOptions.Persistence` | `NLCPGBuilderOptions.cs:32` | `src/` 中**唯一赋值是 `= null`**（`NLCPGBuilder.cs:1245`）⇒ 分支 `:893` 恒不进入 | 生产不可达 |
| `CpgPersistenceOptions.StreamingMode` | `NLCPGBuilderOptions.cs:109` | 读取点 `NLCPGBuilder.cs:994,1237`、`CpgShardBuildCoordinator.cs:71,113,190`，但均以 `Persistence` 非 null 为前提 | 生产不可达 |
| `RequiresPreallocatedNodeIds()` | `NLCPGBuilder.cs:1235` | 3 处调用**全同文件**；守卫因选项恒 false 而不执行 | 恒 false |
| `CpgShardRelationQueryService` / `CpgShardQueryResolver` | `Analysis/CpgShardRelationQueryService.cs:9`；`CpgShardQueryResolver.cs:11` | `src/` 内 `new CpgShardQueryResolver` **0 处** | **仅测试使用** |

**原文引证（文档自己承认）**：

- `docs/loh-large-object-optimization-guide.md:442-444`：「**不启用内存有界架构。** shard / streaming /
  persistence 路径（`NLCPGBuilderOptions.Persistence`、`CpgPersistenceOptions.StreamingMode`、
  `CpgFrozenShardStore`）在 `src/` 中**从未接线**，是**架构级独立立项**。」
- `docs/plans/2026-09-24-frozen-edge-projection-design.md:411`：「仓库里**内存有界的架构已经写好，
  只是从未接线**：」
- `docs/plans/2026-09-24-frozen-edge-projection-execution.md:41-43`：「**硬边界（用户指示）：**
  **不启用内存有界架构。**」

**判定：正当，非缺陷。** 这是**用户明确指示**的硬边界（`-execution.md:41`），
且两份设计文档均**主动标注**了未接线状态。问题仅在于第 2 节的幽灵类型名。

### 3.2 `DocumentShardPlanner` 全套 8 文件（D2 核心）

| 项 | 值 |
| --- | --- |
| 定义 | `src/NLISSN.Application/Analysis/DocumentShardPlanner.cs:23` 等 **8 个文件** |
| 文档声称 | `docs/plans/2026-09-26-workbatch-shard-packing-execution.md:929`：「✅ 已实现、已编译、已测试通过」；21/21 用例通过 |
| `src` 中 `Plan`/`PlanAll`/`Classify` 调用 | **0** |
| `src` 中对 `DocumentShardPlanner` 的 6 处命中 | **全部是 XML 文档注释**（`DocumentShard.cs:13,17,18`、`DocumentShardInputFile.cs:6`、`DocumentShardPlannerOptions.cs:4`）+ 定义本身 |
| 测试引用 | `tests/NLISSN.UnitTests/Analysis/DocumentShardPlannerTests.cs`（21 用例） |

**文档自己也承认**（`:958`）：

> `| 生产接线 | ❌ **未接入任何生产调用路径**（无引用者）——集成留给后续任务 |`

以及 `:645`：「**⚠️ 未接入生产：** 计划器目前**无任何调用者**。」

**判定：文档表述诚实（主动标注未接入），但「✅ 已实现」的勾号有误导性。**
`DocumentShardPlan.StableOrder`（`DocumentShard.cs:17`）被设计为「跨文件全局单调调度序号」，
而它**唯一的可能消费者**是 `CpgWorkItem.ShardOrder` / `CpgWorkBatch.ShardOrder`；
这两个属性在生产中**从未被赋予非默认值**（见 §4.2）。⇒ **D2 的产物与 D1 的消费端之间接口已定义、无人接线。**

**附带风险（子代理主动声明，值得记录）**：`DocumentShardPlan.Shards` 是**集合成员**，
`record` 生成的 `Equals` 只做**引用比较** ⇒ 两份内容相同但分别构造的计划**不相等**。
文档 `:960-963` 已记录该缺口并保留 XML 注释警告。

### 3.3 DataFlow 诊断子系统（M5 全部计数证据的载体）生产不可达

| 产物 | 定义 | 生产写入者 | 判定 |
| --- | --- | --- | --- |
| `NLCPGBuilder.DataFlowDiagnostics` | `src/NLCPG/Builder/DataFlowDiagnostics.cs:24` | **`src/` 内 0 个写入者** | 生产恒 `null` |
| `NLCPGBuilder.LastDataFlowDiagnostics` | `DataFlowDiagnostics.cs:26` | `src/NLISSN.Application/**`、`src/NLCPG/Cli/**` **0 命中** | 仅测试/探针 |
| `DataFlowMethodProbe` / `DataFlowCounter` | `DataFlowMethodProbe.cs:13,52` | 唯一实例化 `DataFlowPass.cs:788`，受上一行门控 | 生产恒 `null` |

读取点 `DataFlowPass.cs:680,787` 本身正确，但**唯一写入者是 `tests/` 与
`tools/DataFlowTailMeasurement/Program.cs:118`**（两者靠 `InternalsVisibleTo`）。
CLI 与 `ApplicationService` **均无诊断开关**。

**文档自己也承认**（`docs/benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md:55`）：

> 「诊断默认关闭，通过内部只读附加结果暴露；**未扩展公开 CLI 参数或公开 BuildMetrics 协议**。」

**影响**：M5 的 §7.2 主张「邻接访问计数两侧逐项相同」，
`docs/plans/2026-09-25-dataflow-adjacency-compaction-execution.md:136-137` 声称已落地。
该主张在**测试与探针内**成立，但**生产路径上无法被开启**，故
「M5 的收益在生产中可观测」这一隐含主张**不成立**。

### 3.4 其余「仅测试使用」项

| 产物 | 定义位置 | 生产消费者 | 判定 |
| --- | --- | --- | --- |
| `CpgGraphValidator`（整套验证器） | `src/NLCPG/Validation/CpgGraphValidator.cs:30` | **`src/` 内 0 处实例化**；5 处全在 `CpgGraphValidatorTests.cs` | 仅测试使用 |
| `InterproceduralEdgeSnapshot.PoolCount` | `NLCPGBuilder.cs:2001` | `src` 内唯一命中是同文件注释 `:2218` | 仅测试使用 |
| `InterproceduralDataFlowPlanGroup.StableCallSiteOrder` | `InterproceduralDataFlowPlanGroup.cs:28` | 属性自身**零读取**（仅构造传参 + 反射断言属性名） | 零调用方 |
| `InterproceduralPlanCapacityLedger` 全部字段 | `InterproceduralDataFlowPlanGroup.cs:50` | 生产只写不读（回收决策用局部 `retainedBudgetBytes`） | 仅测试使用 |
| `InterproceduralPlanCapacityLedgerBuilder.SortBufferResidualRows` | `InterproceduralDataFlowPlanGroup.cs:58,100,170` | 唯一断言在 `InterproceduralPlanCompactionTests.cs:554` | 仅测试使用 |
| `ConcurrencyWindowOptions.EstimatedRetainedBytesPerItem` | `ConcurrencyWindowOptions.cs:16` | 生产构造点 `PartitionedOperationPass.cs:68` **不传该参数** ⇒ 恒 0 | 仅测试使用 |
| `NLCPGBuilderOptions.UsePreallocatedNodeIds` | `NLCPGBuilderOptions.cs:33` | 3 处调用全同文件，守卫分支恒不执行 | 生产不可达 |

**特别严重的一条**：`SortBufferResidualRows` 被
`docs/plans/2026-09-25-memory-optimization-closing-report.md:265,288` 称为
「**唯一能观测强引用可达性的量**」，并被表述为「由注释级承诺变为**可观测事实**」。
但该「可观测」的**唯一读者是测试断言**。若其目的是「在真实运行中观测」，
则当前实现**未达成**该目的。

---

## 4. 【中】第 2 类：零调用方 / 只写不读

### 4.1 确认的死方法与死常量

> **✅ 处置状态（2026-09-26 18:2x 执行）：下表 6 项中，前 4 项已删除，第 5/6 项经复核
> **不属于本类**、已保留。详见 §4.1.1 的更正记录——本报告原先对第 5/6 项的判定**有误**。

| 产物 | 定义位置 | 全仓命中 | 备注 | 处置 |
| --- | --- | --- | --- | --- |
| `SyntaxId(SyntaxNode, string)` | `NLCPGBuilder.cs:3817` | **1**（=定义） | 文档 `zero-allocation-key-design.md:287` 已标为死代码，**核实成立** | ✅ **已删** |
| `TokenId(SyntaxToken, string)` | `NLCPGBuilder.cs:3822` | **1**（=定义） | 同上 | ✅ **已删** |
| `RunPartitionedOperationPassOrderedCompatibility` | `PartitionedOperationPass.cs:62` | **1**（=定义） | 注释自称「保留逐项路径作为迁移期间的语义对照实现」 | ✅ **已删** |
| `ShouldUsePartitionedOperationBuild` | `PartitionedOperationPass.cs:526` | **1**（=定义） | 方法体恒 `return true`（`:528`） | ✅ **已删** |
| `CpgWorkBatchBuilder.DefaultMaxMethodsPerBatch` | `CpgWorkBatchBuilder.cs:5` | ⚠️ **非零**（18 个构造点引用） | **原判有误，见 §4.1.1** | ⛔ **保留** |
| `CpgWorkBatchBuilder.DefaultMaxEstimatedBytesPerBatch` | `CpgWorkBatchBuilder.cs:6` | 同上 | 与 `NLCPGBuilderOptions.cs:40-41` 重复定义 | ⛔ **保留** |

`RunPartitionedOperationPassOrderedCompatibility` 的自述理由（「迁移期间的语义对照」）
在 `ShouldUsePartitionedOperationBuild` 恒真的前提下**已失效**——
两个方法构成一对互相支撑的死重载。设计文档 §8 删除清单对其有**明确授权**：
「`RunPartitionedOperationPassOrderedCompatibility` …… **零引用，可直接删**」、
「`ShouldUsePartitionedOperationBuild`（恒 true）…… **直接内联**」。

**附带删除（本类同性质，审计时未列出）**：`PartitionedOperationPass.CountLineSpan`
（原 `:458`，1 处命中 = 定义），等价逻辑已内联于 `:359`/`:420`。一并删除。

**验证**：`dotnet build src/NLCPG/NLCPG.csproj` → **0 警告 0 错误**；
`--filter "CpgWorkBatch|Partitioned|OperationPass"` → **135/135 通过**。

#### 4.1.1 ⚠️ 自我更正：第 5/6 项被误判为「零调用方」

原报告把 `CpgWorkBatchBuilder.cs:5-6` 两个私有默认常量列入「零调用方」，
依据是「生产唯一构造点显式传三实参 ⇒ 默认值分支不可达」。
**该依据只对生产成立，对测试不成立**——实测全仓 **20 个构造点中有 18 个省略第 2/3 实参**
（`CpgWorkBatchBuilderTests`、`CpgWorkBatchCrossFilePackingTests`、
`CpgWorkBatchIsolationThresholdTests`、`CpgWorkBatchShardOrderContractTests`、
`CpgFragmentByteCalibrationTests`），这些点**在编译期引用该常量**。

**变异检验（决定性证据）**：把两个常量改为哨兵值（`64 → 999999`、
`1MB → 999999999`）后重新编译并运行 WorkBatch 测试 ⇒ **95/95 仍全部通过**。
⇒ 两个事实同时成立：① 常量**被引用**（非零调用方，不该删）；
② 其**取值不被任何测试区分**（测试对该上限无判别力）。
原报告的判定**混淆了「被引用」与「被区分」**，属误分类，已更正。

⇒ 若将来要清理它们，正确理由是「与 `NLCPGBuilderOptions.cs:40-41` 重复定义、
且测试对取值无判别力」，**不能**说成「零调用方」。

### 4.2 只写不读（写-only 遥测）——系统性

**这是本报告认为最值得决策者注意的结构性发现**：
下列 `internal` 观测面在生产中**只被赋值，无任何读者**；
`src/NLISSN.Application/**`、`src/NLISSN/**`、`src/NLCPG/Cli/**` 对它们的命中数**全部为 0**
（实测验证：一条组合检索返回空）。

| 观测面 | 定义位置 | 生产读取者 |
| --- | --- | --- |
| `SyntaxPassTelemetry.LastSyntaxPassTelemetry` | `SyntaxPass.cs:87` | **0**（仅 `SyntaxPassTelemetryContractTests.cs`） |
| `NLCPGBuilder.LastStageWorkResults` | `NLCPGBuilder.cs:1370` | **0**（仅 `StageWorkResultAccountingTests.cs`） |
| `NLCPGBuilder.LastStageQuotaAllocation` | `NLCPGBuilder.cs:1419` | **0**（仅 `StageQuotaScopeContractTests.cs` 等） |
| `NLCPGBuilder.LastInterproceduralPlanCapacity` | `NLCPGBuilder.cs:470` | **0**（仅 `InterproceduralPlanCompactionTests.cs`） |
| `NLCPGBuilder.LastDataFlowDiagnostics` | `DataFlowDiagnostics.cs:26` | **0**（见 §3.3） |
| `NLCPGBuildMetrics.SparseOverflowNodeCount` | `NLCPGBuildMetrics.cs:133` | **0**（全仓 4 处命中**无一为读取**，见下） |
| `WorkSubmission.MaxInFlightBytes` | `WorkSubmission.cs:22` | **0 赋值方**（3 个生产构造点均不设；`WorkScheduler.cs:456` 是内部转发） |

**两条特别说明**：

1. `SparseOverflowNodeCount` 的注释（`NLCPGBuildMetrics.cs:120-123`）自称「仅诊断/测试用」，
   但全仓 **4 处命中无一为读取**：`NLCPGBuildMetrics.cs:120`（XML 注释）、
   `:133`（record 参数声明）、`DataFlowPass.cs:1360`（私有方法形参）、`:1371`（构造传参）。
   ⇒ 它是**只写一次的丢弃值**，注释**失实**（`tests/` 中亦无任何断言）。
2. `MaxInFlightBytes` 在 `docs/plans/2026-09-24-unified-work-scheduler-execution.md:775`
   已被**主动记录**：「**`MaxInFlightBytes` 仍无生产调用方**……赋值来源只有测试。
   故本轮的修复**当前不影响任何生产路径**。」⇒ 文档诚实，但该项**仍未接线**。

### 4.3 `IConcurrencyPool` 死成员（设计文档点名的「事实 C」）

设计 `2026-09-24-unified-work-scheduler-design.md:74` 声称「8 个成员中 4 个生产死代码」。
**核实：该判断至今成立，且部分成员情况已变化**：

| 成员 | 定义 | `src` 生产调用点 | 判定 |
| --- | --- | --- | --- |
| `SelectOrderedAsync<TSource,TResult>` | `IConcurrencyPool.cs:55` | `DirectoryAnalysisUseCase.cs:284` | 活 |
| `ForEachAsync` | `:132` | `CpgShardBuildCoordinator.cs:98`、`ParameterShrinkAnalyzer.cs:1357` | 活 |
| `ExecuteWithAdmissionAsync` | `:19` | `CpgWorkBatchExecutor.cs:542` | 活（默认实现） |
| `SelectOrderedAsync<TResult>`（按索引） | `:39` | 0 | **死** |
| **`SelectCpuBoundOrdered`** | `:71` | **0**（仅测试与测试假实现） | **死** |
| **`CommitTwoStageOrdered`** | `:112` | **0**（仅测试） | **死** |
| **`RunDependencyGraphAsync`** | `:148` | **0** | **死**（已由 `WorkScheduler` 取代） |
| `CommitOrdered` | `:88` | `PartitionedOperationPass.cs:66` | **准死**——唯一调用点在 §4.1 的零引用方法内 |

⇒ 设计文档的「4 个」**仍准确**；其中 `RunDependencyGraphAsync` 的迁移
（`RuleGraphExecutor.cs:220-280` 已改用 `WorkScheduler`）**已完成但接口未删**，
符合设计 `:671` 记录的「S2-1/S2-2 完成、S2-3/S2-4 未做」。

### 4.4 字节预算是死的（设计文档点名的「事实 B」）

设计 `:62-72` 声称「**所有字节预算参数都是死的**」。**核实：成立**。

关键链条：生产四处 `new ConcurrencyAdmissionRequest(...)`（`BoundedConcurrencyPool.cs:126,250,684,793`）
**全部使用默认 `ReservedByteCount = 0`**；唯一显式传字节的
`CpgWorkBatchExecutor.cs:546` 传的是 `_options.AdmissionReservedByteCount`，
而该参数默认 0 且 `NLCPGBuilder.cs:507-512` **不传它**。
⇒ `ConcurrencyAdmissionController.cs:187` 的 `MaxReservedByteCount`（128 MB，
`ExecutionRuntime.cs:323`）**永不起约束作用**。

同一链条的队列预算（`CpgWorkBatchExecutorOptions` 的 `QueueCapacity` /
`FragmentSinkCapacity` / `MaxQueuedEstimatedCost` / `MaxQueuedEstimatedBytes`）
默认全为 `null`，`NLCPGBuilder.cs` 构造实参**一个都不传** ⇒
`Effective*` 全部走 `?? int.MaxValue` / `?? long.MaxValue` 兜底，
**队列预算在生产中不生效**（与设计 `:71-72` 一致）。

---

## 5. 【记录】D1 的接线状态（因并发改写，单列）

本节对照 `docs/plans/2026-09-26-workbatch-shard-packing-execution.md` 的 T3/T4/T5 记录，
在 §0 所述**改写后**的树上复核。**该文档已于 17:21 补写 §F**，成为 D1 的权威执行记录：

| 项 | 文档声称 | 实测（17:29 复验） |
| --- | --- | --- |
| **T3** 放宽 `CpgWorkBatchBuilder` 单文件断言 | 单子代理「运行中」（`:730`） | ✅ **已落地**：`NormalizeItems` 显式 `_ = sourceFilePath`，去重/排序改用每 item 的 `SourceFilePath` |
| **T4** reducer 按项路由 | ✅ 已完成（§E） | ✅ `AssembleWorkBatchesAcrossDocuments` 被 3 个 pass 调用；`ResolveGraph`/`ResolveDocument` 已路由 |
| **T4 入口 `BuildMany`** | §E `:1000` 称新增；**该表述已被 §F `:1049` 自我作废** | ✅ `BuildMany` 转为薄包装；生产改走 `BuildManyDocuments`；`src` 中 `.BuildMany(` 调用 = 0（仅 8 个契约用例） |
| **D1 生产链路** | §E「未提及」→ **17:21 §F 补写** | ✅ **已接通**：`DirectoryAnalysisUseCase.cs:254` → `ApplicationService.cs:180` → `BuildManyDocuments`。§F 自述 735/735 + 4/4 通过 |
| **T5** `DocumentShardPlanner` | ✅ 已完成，未接入 | ❌ **计划器已存在但 D2 能力零交付**（§3.2）：`src` 中 `Plan`/`PlanAll`/`Classify` 调用 = **0**。且 Gate **G4b = `todo`** 明确 *"enabling it in production is not [permitted]"* ⇒ 接线**受 Gate 管控**，非普通收尾 |

**⚠️ 一处需要决策者注意的语义缺口**：`CpgWorkBatchBuilder.AddBatch`
（`CpgWorkBatchBuilder.cs:224-227`）的 `ShardOrder` 取 **`items.Min(item => item.ShardOrder)`**。
而 5 个 `new CpgWorkItem(...)` 生产构造点**全部使用 7 参重载**，
即 `shardOrder` **默认等于 `stableOrder`**（`CpgWorkItem.cs:28`）——
而 `stableOrder` 是**文件内局部下标**。⇒ 跨文件批次内若两个文件各有 `StableOrder = 0` 的项，
`min` 会给出**相同的 ShardOrder**，进而触发
`CpgWorkBatchExecutor.cs:578-581` 的
`"WorkBatch shard orders must be unique."` 异常。

两个重载的元数**不同**，勿混：`CpgWorkItem` 是 **7 参（默认 `shardOrder = stableOrder`）／8 参**，
`CpgWorkBatch` 是 **8 参（同样默认）／9 参**（`CpgWorkBatch.cs:30`、`CpgWorkItem.cs:28`）。

生产侧只有**一个**批次绕过了 `AddBatch` 的 `min` 推导、直接显式传全局序号：
`ControlDependencePass.cs:87-106` 用 **9 参** 构造 `CpgWorkBatch`，
`shardOrder: workBatches.Count`（全局计数）。其注释 `:83-86` **恰好预言了该缺陷**：

> 「⚠ 必须用 9 参重载：8 参重载把 ShardOrder 默认成 stableOrder(=index)，
> 跨文件时各文件的 index 都从 0 起 ⇒ ShardOrder 重复 ⇒ 执行器抛
> "WorkBatch shard orders must be unique."。」

⇒ **该修复只落在 8 个 pass 中的 1 个**。其余经 `_workBatchBuilder.Build` 装箱的 4 个 pass
（`CallGraphPass.cs:172`、`DataFlowPass.cs:741`、`PartitionedOperationPass.cs:453,513`）
其 `new CpgWorkItem(...)` **全部走 7 参重载** ⇒ item 的 `ShardOrder` 恒等于**文件内局部**的
`StableOrder`，于是 `AddBatch`（`CpgWorkBatchBuilder.cs:227`）的
`items.Min(item => item.ShardOrder)` 在跨文件批次里会给出**重复值**。

**跨文件批次确实会发生（非臆测）**：`CallGraphPass.cs:93-115` 显式
`foreach (var document in context.Documents)` 把**全部文档**的工作项汇进**同一次**
`_workBatchBuilder.Build(...)`。注释 `:112` 自述「D1：跨文件聚合装箱」。
`ControlFlowPass` / `DominancePass` / `MemberAccessPass` 同样调用
`AssembleWorkBatchesAcrossDocuments`（§5 T4 行）。⇒ 该缺口并非理论情形。

这正是执行文档 T2 要求「引入全局单调 `ShardOrder`」的**未完成部分**。
本报告**只陈述静态事实**，未运行构建或测试验证其是否在真实多文件语料上触发。

**为何 `min` 不是合法的排序键（本条的技术要点）：**
`AddBatch` 的注释自述「跨文件装箱时它给出该批在**全局调度序上的下界**」——
它**确实**是下界，但**下界在批间可以相同**。跨文件列表按
`(SourceFilePath, StableOrder)` 排序（`CpgWorkBatchBuilder.cs:58-66`），
故 `ShardOrder` 在列表内**不是单调的**，而是在**每个文件边界处归零**。
于是「某批恰好终止于文件边界」时，下一批的首项就是**下一个文件的 `StableOrder = 0`**
⇒ 两批的 `min` 同为 `0`。

**两条具体可达构造（静态推导，未执行）：**

1. **文件边界对齐**：文件 A 的方法数恰好把首批装满——总数达到
   `WorkBatchMaxMethodsPerBatch = 64`（`NLCPGBuilderOptions.cs:40`）
   或累计 cost 达到 `TargetBatchCost = 1500`（`CpgWorkBatchCostModel.cs:91-97`）
   ⇒ 批次在 A 的末尾 flush；下一个批次以 `B` 的 `StableOrder = 0` 起头 ⇒ 与首批同为 `0`。
2. **同下标超大方法**：成本 `> IsolationCostThreshold = 1500` 的方法**独占一批**
   （`CpgWorkBatchBuilder.cs:126-133`）。若文件 A 与文件 B **各有一个**位于相同
   `StableOrder` 的超大方法，则两批的 `min` **相等**。
   语料中确有 34 个超大方法（执行文档 §1.3），此法无需刻意构造边界即可命中。

⇒ 命中后，生产默认**异步**路径在 `CpgWorkBatchExecutor.cs:578-581` 抛
`ArgumentException("WorkBatch shard orders must be unique.")`。

**⚠️ 与并发会话 §F 的关系（必须如实记录）**：
执行文档已于 **17:21** 补写 **§F「F1 —— 把跨文件装箱接进生产」**，
自述 `~Cpg|~Stage|...|~Shard` **735/735 通过**、新 `CrossFileBatchEquivalenceTests` **4/4 通过**。
⇒ 即：**该缺口已进入生产路径，且现有测试未使其变红**。
这不构成对上述推导的反证——§F 的等价性夹具规模小到方法可落入**单一批次**，
而本条要求的是**批次之间**的 `min` 碰撞。**本报告不声称这些测试无效**，
只指出它们覆盖不到该维度。是否真的在真实语料上触发，**需实测裁决**，
本报告不代为下结论。

---

## 6. 附带发现

### 6.1 未跟踪的 `.bak` 生产目录残留

`git status --short` 显示两个**未跟踪且未被 `.gitignore` 覆盖**的文件：

```
?? src/NLCPG/Model/CanonicalEdgeStore.T1T2-GOOD.bak          (7,502 B)
?? src/NLCPG/Model/NLCPGGraphIndex.T1T2-GOOD.bak             (42,381 B)
```

- SDK 默认 glob 只收 `**/*.cs`，故**不参与编译**（已核实 `NLCPG.csproj` 无显式 `Compile` 项）。
- 但两者含 `CanonicalEdgeStore` / `NLCPGGraphIndex` 的**旧副本定义**，
  会**污染任何基于文本的引用计数审计**（本审计即需刻意排除）。
- 名字 `T1T2-GOOD` 暗示是某次优化的「已知良好」快照，属**临时产物未清理**。

### 6.2 文档状态过期（实际已实施，文档仍称未实施）

两处**方向相反**的偏差（文档比现实**落后**）：

| 文档 | 文档声称 | 实际 |
| --- | --- | --- |
| `2026-09-25-dataflow-candidate-scan-pruning-execution.md:11` | 「**本轮不实施**」 | P01 已实施（`DataFlowPass.cs:237` 注释「已删除」、守卫 `:325-337`） |
| `2026-09-25-dataflow-plan-construction-reuse-execution.md:11` | 「本轮仅编写执行文档，不修改产品代码」 | P02 T2 已实施（`DataFlowPass.cs:1215,2438,2444`） |

⇒ 与第 1 类（幽灵类型，文档比现实**超前**）**方向相反**。
两者共同说明：**文档与源码之间缺少机械核对**。

### 6.3 行号漂移已影响结论可信度

`2026-09-24-frozen-edge-projection-design.md:416,418` 两个锚点失准（见 §2）。
同一现象在 `Context/progress.md` 中已有多次记录（如 `WorkScheduler.cs:623→811`）。
本报告所有行号均为**审计当时**实测值，且 §0 已声明并发改写风险。

---

## 7. 判定为正当的条目（覆盖清单）

以下**不构成缺陷**，经核实理由充分：

- **内存有界架构（shard / streaming / persistence）从未接线**：`frozen-edge-projection-execution.md:41`
  记载为**用户明确指示**的硬边界；两份设计文档**主动标注**未接线。⇒ 正当。
- **`Dictionary` 边内存优化尚未实施**：`2026-09-23-dictionary-edge-memory-optimization-execution.md:5,246-247,267`
  三处自述「尚未开始实施」「`src/` 未被修改」「生产代码改动 ❌ 未开始」。⇒ 文档诚实。
- **`zero-allocation-key` 整体回退**：`...-execution.md:674-676` 明示「目标未达成，
  按回退策略整体回退……收益判定为不成立」。⇒ 正当，且**主动暴露失败**。
- **P03 / P04 / P05 未实施**：`cpg-performance-optimization-execution-index.md:3`
  自述「本轮编写文档，不实施产品优化」；`interprocedural-sort-publication-execution.md:11`、
  `builder-cache-gate-scope-execution.md:11` 均标注切片「尚未加入 `feature_list.json`」。⇒ 正当。
- **`S3`/`S4-1`/`S5` 主体未登记**：已由 `2026-09-26-rejection-audit-report.md` §2 记录为**高严重度的登记缺失**，
  本报告不重复，仅确认其结论。
- **`MaxInFlightBytes` 无生产调用方**：执行文档 `:775` **主动记录**并正确定性为
  「内核契约正确性」而非「生产内存行为改变」。⇒ 诚实边界。

---

## 8. 本报告不得越读的边界

1. 本次**只读**：未修改任何产品代码、文档或状态文件；**未运行任何构建、测试或性能采样**。
2. 判定依据是**静态阅读 + 代码检索**。**「零调用方」只证明静态不可达**，
   不排除通过反射、`InternalsVisibleTo`、源生成器或配置文件间接访问。
   已单独核对的两例（`DataFlowDiagnostics`、`LastSyntaxPassTelemetry`）
   确认其唯一访问者确在 `tests/` 与 `tools/`。
3. §5 的 `ShardOrder` 语义缺口是**静态推导**，**未**在多文件语料上实测触发。
4. §0 已声明：审计期间工作树被**并发会话改写**（17:11 接通 D1）。
   本报告所有结论以**改写后**的树为准，并附源码哈希。
5. 「几天内」取 **2026-09-22 ~ 2026-09-26**；更早文档与全部 `设计docs/历史设计/`
   未逐一审计（除 `性能分析组件.md` 与 `loh-large-object-optimization-guide.md`）。
6. 本报告**不构成**对任何实测数字的独立复现；文档引用的 `Build/` 证据路径**未逐一核验存在性**
   （该维度已由 `2026-09-26-rejection-audit-report.md` §8 覆盖）。
