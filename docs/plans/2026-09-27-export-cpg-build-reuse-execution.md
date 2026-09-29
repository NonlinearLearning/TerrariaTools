# 导出阶段复用分析构图（Export CPG Build Reuse）执行方案

**目标：** 消除 `artifacts.projectJson` 导出阶段的重复开销。当前导出对同一批文件**再建一遍 CPG**
（只跳过规则），而分析阶段已经为这些文件建过图并已丢弃。本方案给出三级可独立交付的改动：
先摘掉导出构图里「算了但没人读」的产物，再摘掉第二次整包工作区加载，最后才讨论复用批次图本身。

**思路来源：** 编译器前端的 **IR 复用**——同一次编译里 IR 只构造一次，后端各消费者共享；
只有在「消费者需要的 IR 变体不同」时才重建。本方案先做共享，把变体差异（能力集）作为**前置条件**
显式登记，而不是把差异藏进复用里。

**Tech Stack：** C#/.NET 10、`System.Lazy<T>`/`Interlocked` 惰性记忆化、既有 NLCPG 构图与 Contract 测试。

---

日期：2026-09-27。

> 📌 **本轮性质：只写文档，不改产品代码、不跑测试。**
> 因此本方案**不产出任何导出侧的实测收益数字**。下文所有收益量级都标注了口径：
> 「实测」= 来自 `Build/worker8-utilization/export-cpg/` 的运行证据或源码内既有的实测注释；
> 「推算」= 由实测事实推出的上界。**导出阶段目前没有任何阶段计时**（见 §1.4），
> 这是本方案必须先解决的度量缺口（§6）。

> 📌 **行号基准**：`src/NLCPG/Builder/NLCPGBuilder.cs` 4,623 行；
> `src/NLCPG/Model/NLCPGGraphIndex.cs` 1,284 行；
> `src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs` 1,281 行。
> 下列引用同时给出**符号名**，行号漂移时以符号为准。
>
> ⚠️ `NLCPGBuilder.cs` 在本文写作期间**正被并发编辑**（同一会话内从 4,554 行增至 4,623 行），
> 故该文件的行号**不稳定**；引用它时请以 §3、§5 给出的**符号名**为准。
>
> ⚠️ 当前工作树有大量未提交改动（exporter 已从 930 行增至 1,281 行，新增单文档分片），
> 且运行二进制已不是当次那份：当次为 `Build/src/Debug/net10.0/`（19:42:49），
> 该路径现在是 21:19 之后的构建。**现在重跑不会复现同一二进制。**

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 重复构图值多少？ | 导出重构图 **≲175 CPU-s（推算）**，占导出 ≈404 CPU-s 的 **30–43%**；其余是 9.53 GiB 的物化与序列化 |
| 能否直接复用分析那张图？ | **不能直接复用**，且**不能做到逐字节中性**（§5.2、§5.3） |
| 有没有马上能做、且字节中性的收益？ | **有两处**：冻结期审计产物（§3）与第二次工作区加载（§4） |
| 冻结期审计能省多少？ | `CreateSnapshotVersion` 在源码内有 NPC.cs 的实测注释：缓冲化改造后仍 **7,203 ms CPU**（§3.3）；导出从不读它 |
| 第二次加载能省多少？ | **≤9.9 s 墙钟**（实测：分析完成 → 首份 payload 的间隔） |
| 复用批次图的真实前置条件是什么？ | ①能力集差异；②`NodeId` 依赖 **identity factory 的 interner 顺序** ⇒ 复用会改 `localNodeId` ⇒ 属 **payload 契约变更** |
| 字节中性的判据可用吗？ | **不可用**：现有 `baseline-payload-sha256.txt` 生成于 10:45、966 条、与当前 `out/` **0/966 命中** ⇒ 必须先用 `Export-PayloadSha256.ps1` 重建（§8） |

## 1. 事实基线

### 1.1 导出的两处重复

| # | 重复项 | 证据 |
| --- | --- | --- |
| ① | 同一批文件**再建一遍 CPG** | `ProjectJsonExporter.cs:256` 每次 `new NLCPGBuilder(builderOptions).BuildFromSemanticModel(...)` |
| ② | 同一工程**再加载一次工作区** | `ProjectJsonExporter.cs:144` `new MsBuildWorkspaceInputLoader().LoadAsync(...)`；分析侧已在 `WorkspaceAnalysisService.cs:40-42` 加载过 |

### 1.2 分析不是「建完即弃」

本次运行走的是**批次**路径，不是 `ApplicationService.BuildAnalysisContextCore`
（`ApplicationService.cs:444`，那是单文件入口，本运行未走）：

```
WorkspaceAnalysisService.AnalyzeAsync        (:24)   每个 project 一次
  → DirectoryAnalysisUseCase.AnalyzeCompiled (:90)
  → AnalyzeCore                              (:135)
  → AnalyzeFiles                             (:233)
      → ApplicationService.BuildGraphBatch   (:254 → ApplicationService.cs:137)
          → BuildGraphBatchCore              (ApplicationService.cs:164) → BuildManyDocuments (:187)
      → AnalyzeFile (闭包)                   (DirectoryAnalysisUseCase.cs:259-268)
```

`graphBatch.Graphs` 被 `AnalyzeFile` 闭包持有，**整个项目分析期间存活**，
随 `AnalyzeCore` 返回（`:183-186`）而失去引用。
`monitor.log` 中 `wsMB 7255 → 1108`（20:05:24）正是这次释放，恰在分析结束边界上。

### 1.3 实测时间线（当次运行）

| 事实 | 值 |
| --- | --- |
| 分析 `evt=completed` | 20:05:24.585（`elapsedMs=577966`） |
| 首份 payload 写出 | 20:05:34.485（**+9.9 s**） |
| 末份 payload | 20:11:02.561 |
| 进程退出 | 20:11:03 |
| 导出墙钟 | **≈338 s** |
| 分析 CPU | 810 CPU-s / 578 s = **1.40 核** |
| 导出 CPU | ≈404 CPU-s / 338 s = **1.19 核**（由 60 s 采样差推出） |
| `op=cpg-worker` busy 之和 | **175.4 CPU-s**（12 个 worker，实测） |

> ⚠️ `cpuSec` 是整进程累计 `Process.TotalProcessorTime`；**导出侧无阶段遥测**
> （`writePerformanceSummary: false`、无 `evt=sampled`）。故 404 CPU-s 是采样外推，不是仪表读数。

### 1.4 度量缺口（本方案的前置）

- `runtime.log` 只有 `evt=started` / 12×`op=worker` / 聚合 / 12×`op=cpg-worker` / `evt=completed`。
- **没有任何**「构图 / 投影 / 序列化 / 落盘」的拆分。
- 因此 §3、§5 的收益都是**推算**，必须先用 §6 的度量方案坐实，再谈优化幅度。

### 1.5 输出规模（当次）

967 份 payload、10,231,985,304 B（**9.53 GiB** / 10.23 GB）、8,166,789 节点 + 19,788,190 边 = **27,954,979 条记录**。
前 4 个文件占 215.0 s（63.6%）/ 6,648.0 MiB；914/967 个文件 <0.2 s。

## 2. 执行范围

| 项 | 本轮 |
| --- | --- |
| 产品代码改动 | **不做**（只给方案与改动清单） |
| 测试 / 探针 / A/B | **不做** |
| 收益数字 | 只给**口径明确的推算**，不声称实测 |
| 契约变更（§5） | **登记前置条件**，不在本方案内执行 |

**本方案不回答**「复用批次图后导出变成多少秒」——那要先解决 §5.2/§5.3 两个契约问题。

## 3. 第一步：冻结期的审计产物（字节中性）

导出只读 `graph.Nodes` / `graph.Edges` / `Resolve*` / `ResolveFilePath`。
以下两项在导出构图里**照样执行、但没有任何读者**。

### 3.1 `CpgBuildInventory.Create`（无条件）

`NLCPGBuilder.cs:1504-1507`：

```csharp
CpgBuildInventoryMetrics? buildInventoryMetrics = null;
MeasureStage("BuildInventoryAudit", () => buildInventoryMetrics = CpgBuildInventory.Create(context).Metrics);
```

`CpgBuildInventory.Create`（`CpgBuildInventory.cs:27-56`）会：

- 遍历全部节点统计 syntax fact（`:32`）；
- 遍历全部边统计 CFG fact（`:33-34`）与 typed-symbol fact（`:38-43`）；
- **物化全部 anchor**（`:44-47`，要求每个节点都有 `StableAnchor`，否则抛异常）；
- 对 anchor 序列做 SHA-256 指纹（`:55` → `Fingerprint`）。

结果进 `LastBuildMetrics.BuildInventoryMetrics`（`NLCPGBuilder.cs:1523`，赋值点）。
**导出侧从不读 `LastBuildMetrics`**（已核对 `ProjectJsonExporter.cs`：
`LastBuildMetrics` / `GraphSnapshotVersion` / `BuildInventory` 三个模式**均 0 处引用**）。

### 3.2 一个必须处理的耦合

`BuildFromSource` / `BuildFromSemanticModel` 在**预分配 NodeId** 路径上读该字段并解引用：

```csharp
// NLCPGBuilder.cs:846-851（BuildFromSource）、:885-890（BuildFromSemanticModel）
BuildInventoryMetrics = LastBuildMetrics.BuildInventoryMetrics! with
{
    PreallocatedAnchorDiff = CpgBuildInventory.CompareAnchors(collector.Anchors, graph.Nodes),
},
```

`!` 使「关掉审计」与「预分配路径」直接冲突。导出虽不触发该路径
（`CreateDefault()` ⇒ `UsePreallocatedNodeIds = false`、无 `Persistence`），
但开关必须**保守**：仅在调用方显式关闭**且** `RequiresPreallocatedNodeIds() == false`
（`:1540-1543`）时跳过，否则保持现行行为。

### 3.3 `CreateSnapshotVersion`（无条件）——建议改为**惰性**

`NLCPGGraphIndex.cs:534`（在 `Create` 内，`:416`）：

```csharp
var snapshotVersion = CreateSnapshotVersion(orderedNodes, store);
```

`CreateSnapshotVersion`（`:1129-1167`）对**每个节点写 8 个字段**、**每条边写 9 个字段**，
其中 `StableKeyOf` 是插值属性，故按标签实例做了记忆化（`:1145-1149`）。
导出既不读 `GraphSnapshotVersion`（`NLCPGGraph.cs:220`）也不读 `LastBuildMetrics`。

源码内已有该函数的**实测锚点**（`NLCPGGraphIndex.cs:1175-1182`）：

> NPC.cs 实测 113,636,842 次 `AppendData` 调用、每次均摊 108 ns；SHA-256 对 1,481.5 MiB 本身仅约 1.3 s。
> 改造前 CPU 13,016–13,375 ms → 改造后 **7,203 ms**（净省约 6.0 s CPU，1.83×）。

⇒ 即使经过缓冲化，NPC.cs 单文件的快照哈希**仍耗约 7.2 s CPU**。
当次导出在前 4 个文件上花了 215 s 墙钟，其中包含这个纯审计开销。

**推荐做法：惰性记忆化，而不是删字段。**
`SnapshotVersion` 改为首次访问时计算（`Lazy<string>` /
`LazyThreadSafetyMode.ExecutionAndPublication`，或 `Interlocked` + 局部变量）。
可行性依据：`CreateSnapshotVersion` 只依赖 `OrderedNodes` 与 `EdgeStore`，
二者都是索引的**常驻字段**（`:353`、`:368`），冻结后不释放；`_pendingEdges.Release()`
（`NLCPGGraph.cs:1052`）作用于图而非索引，不影响该计算。

这样：

- 读者（`CpgExecutionSnapshotComparer`、`CpgShardBuildCoordinatorTests`、
  `DataFlowPlanConstructionReuseTests` 等）拿到**逐字节相同**的字符串；
- 不读它的调用方（导出）**永不付出**该成本；
- 无需新增开关，也就没有「开关忘了传」的风险。

### 3.4 改动清单

| 文件 | 改动 | 风险 |
| --- | --- | --- |
| `NLCPG/Model/NLCPGGraphIndex.cs` | `SnapshotVersion` 改惰性记忆化；`Create` 不再预先调用 | 低（返回值不变） |
| `NLCPG/Builder/NLCPGBuilderOptions.cs` | 新增 `bool ComputeBuildInventory = true` | 低（默认保持现状） |
| `NLCPG/Builder/NLCPGBuilder.cs` | `BuildInventoryAudit` 按开关执行；预分配路径整体受开关保护 | 中（`!` 解引用，见 §3.2） |
| `NLCPG/Model/NLCPGGraph.cs` | `FreezeQueryIndex` 若需传开关则一并透传 | 低 |
| `NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs` | `builderOptions with { ComputeBuildInventory = false }` | 低 |

**默认值必须是「算」**：全部既有测试与 NLISSN 分析路径依赖 `LastBuildMetrics.BuildInventoryMetrics`
非空（如 `NLCPGPartitionedBuilderTests.cs:241-259`），默认 `true` 使这些用例零改动。

## 4. 第二步：去掉第二次工作区加载（字节中性）

### 4.1 现状

| 侧 | 加载点 | 选项 |
| --- | --- | --- |
| 分析 | `WorkspaceAnalysisService.cs:17`（`_loader`）+ `:40-42` | `YamlConfigurationLoader.cs:569-578` 构造，`RequireCleanCompilation` 默认 **true**（`WorkspaceInputOptions.cs:30`），`:578` 设 `TargetDocumentPath`，`:576` 设 `GeneratedSourceMode` |
| 导出 | `ProjectJsonExporter.cs:144-154` | 就地构造，`Path = options.FullProjectPath`、`ProjectPath = null`、`RequireCleanCompilation: false` |

两次加载**选项并不相同**，因此不能假设快照可直接互换。
导出只在 `ConfigurationRunHost.RunAsync` 内、分析**成功之后**执行
（`ConfigurationRunHost.cs:13-14`；独立 `NLCPG.ProjectExport` 入口已删除），
故「分析成功 ⇒ compilation 干净」成立，`RequireCleanCompilation` 的差异在该路径上不产生分歧。

### 4.2 改法

在 `ConfigurationRunHost.RunAsync` 内把分析那份 `WorkspaceSolutionSnapshot`
（`WorkspaceSolutionSnapshot.cs`，含 `Projects[].Compilation` / `Documents`）
经 `ProjectJsonExportService.ExportAsync` 传入新增的 `ProjectJsonExporter` 重载；
仅在**未传入**时回退到自加载。

`CommandHost.AnalyzeCoreAsync` 目前只返回 `AnalysisRunOutcome`（业务结果 + 性能报告），
**不含工作区快照**。故这是唯一需要新增的管道：把快照或 `loadResult` 带回调用方。

### 4.3 必须核对的三处差异（否则是行为变更，不是优化）

1. **生成源**：导出用 `options.IncludeGenerated` 决定 `GeneratedSourceMode`，忽略 yaml 的 `generators`；两侧文档集可能不同。
2. **`TargetDocumentPath`**：分析侧为「单源文件 + 项目」输入设置；导出侧不设置。需确认导出遍历的 `project.Documents` 与快照一致。
3. **多项目 / `.sln`**：导出用 `CreateUniqueProjectDirectoryName` 为每个项目建子目录；复用快照不改变这一点，但项目集合必须逐一对应。

### 4.4 收益

**≤9.9 s 墙钟**（实测：20:05:24.585 → 20:05:34.485）。
该间隔含第二次加载**加上最早一批文件的构建与写出**。最早写出的是
`BCrypt.cs.json`（753,403 B）/ `CallTracker.cs.json`（413,821 B）/
`nativefiledialog.cs.json`（47,403 B），三份**同一秒**（20:05:34）落盘——
即导出是并发的，9.9 s 里包含的不止一个文件。这三份都远小于 9.53 GiB 总量
（最大者 `NPC.cs.json` 为 2,288,097,179 B），故加载是主要成分。
**真实加载耗时 = 9.9 s 减去这批首批文件的构图与写出**，必须用 §6 度量拆分。

## 5. 第三步：复用分析的批次图（**不是字节中性**）

### 5.1 为什么值得做

导出的重构图与分析构图是同一批文件、同一 compilation，导出只少了规则。
分析侧 `op=cpg-worker` busy 合计 **175.4 CPU-s**（实测），这是该重复项的**上界**
（导出不跑 interprocedural pass，`NLCPGBuilder.cs:1448`，故实际低于此值）。

### 5.2 阻断一：能力集不同 ⇒ 复用会**多出边**

| 侧 | `RequestedCapabilities` | 位置 |
| --- | --- | --- |
| 分析 | `_pipeline.GetRequiredCapabilities()` | `ApplicationService.cs:494` |
| 导出 | 未传 ⇒ `null` ⇒ `NLCPGCapability.Default` | `ProjectJsonExporter.cs:251-255` 的 `CreateDefault()` |

全仓只有一个规则覆盖 `RequiredCapabilities`：
`ExternalSummaryFlowPropagationRule.cs:42-43` 声明 `InterproceduralDataFlow`，
且它是 `Core` feature（`RulePipelineComposer.cs:18` 恒启用）⇒ **分析侧默认就要这一类边**。

而导出侧**永远产不出**这类边：`AddExternalSummaryMappings` 在无 resolver 时直接
`RecordCut("MissingResolver")` 返回（`NLCPGBuilder.cs:3327-3332`），
导出也没有注入 resolver。

实测：导出的 payload 中 `InterproceduralDataFlow` **0 条**（对
`Collision.cs.json`、`CultistRitual.cs.json` 等全篇正则计数）。
⇒ 直接复用会把分析图里多出来的这类边写进 payload，**输出必然改变**。

处理方式（三选一，均需登记）：
① 让导出也请求同一能力集（会改变 payload，等于承认契约变更）；
② 在导出侧**按可判定标记**剔除额外能力产生的边（不能靠猜 `Kind` 字符串）；
③ 给分析侧的导出任务单独构造能力集为 `Default` 的图。

### 5.3 阻断二：`NodeId` 依赖 interner 顺序 ⇒ `localNodeId` 会变

payload 每个节点都带 `localNodeId`（`FileNodeEntry.LocalNodeId`；实测 `CultistRitual.cs.json` 中 2,486 处）。
它等于 `node.NodeId.Value`，由 `DeterministicNodeIdTable.Create`（`DeterministicNodeIdTable.cs:20-34`）
按 `(Kind, FilePathId, SpanStart, SpanEnd, Role, Ordinal, ExtraKeyId)` 排序后 `+1` 分配。

关键在于 **`FilePathId` 是 interner 序号**，而它来自 **`StableNodeIdentityFactory` 自己的 interner**
（`StableNodeIdentityFactory.cs:8` 的 `_interner`，`:34` `_interner.Intern(filePath)`），
**不是**图的 `StringInterner`：

| 构建方式 | identity factory | `FilePathId` 序 |
| --- | --- | --- |
| 分析（批次） | 967 张图**共用一个** | 全局首次 intern 序 |
| 导出（逐文件） | 每图 `new`（`NLCPGBuildContext.Create` 传 `identityFactory: null` ⇒ `NLCPGGraph.cs:152` 新建） | **本文件**首次 intern 序 |

同一文件引用的路径集合 `P_F` 在两个构建里**相对顺序可能不同**：
若路径 `B` 已被更早的文件 intern，则批次下 `B < A`，而单文件构建下 `A < B`。
⇒ 同 `Kind` 内跨路径的排序不同 ⇒ `NodeId` 不同 ⇒ `localNodeId` 不同 ⇒ **payload 字节改变**。

> ⚠️ 注意：稳定 `id`（内容派生 SHA-256，`CreateStableId` 见 exporter `:944`）**不受影响**；
> 变的是 `localNodeId`。二者都在 payload 里，故仍属字节变更。

### 5.4 复用不能靠「共享 identity factory」绕开

「让批次构建为每个文件各用一个 factory」不可行：
`BuildManyDocuments` 的文档注释（`NLCPGBuilder.cs:910-915`，
实现处 `:1015-1017`）明确要求全部兄弟图共用同一个 `StringInterner` 与
`StableNodeIdentityFactory`，并声明**这是硬性要求而非优化**——
`NLCPGGraph` 默认各自 `new StringInterner()`（`NLCPGGraph.cs:154`），
而 `StableNodeAnchor` 以 `FilePathId`（字符串表内的整数）参与相等性，
各持一份会使同名文件在不同图里得到不同 `FilePathId`，
锚点不可跨图比较、fragment 无法安全路由。

### 5.5 中间形态：复用图结构、只重排 `NodeId` 表

**做法**：保留批次图，导出时对每个文件收集其节点的 `StableAnchor`，
调 `DeterministicNodeIdTable.Create` 重新分配 `NodeId`。

**收益**：比重构图便宜一个数量级（只需 anchor 排序 + 一次 `AddNode` 合并，不需要 Roslyn 语义遍历、CFG、DataFlow）。

**但它不解决问题**：重排后的 `NodeId` 仍与今日不同——除非把 `FilePathId` 换成与 intern 顺序无关的
**路径内容哈希**。而那一改会改变**所有**既有 `NodeId`，同样是 payload 契约变更。

⇒ 结论：**第三步无论是整体复用还是中间形态，都必须先接受一次 `NodeId` 契约变更**
（新增 payload schema 版本位 + 用 `Export-PayloadSha256.ps1` 重建基线；现有那份已于
§8 证明失效）。

### 5.6 内存

分析期间实测峰值 `wsMB=7432`（20:02:24），分析结束时降到 `wsMB=1108`（20:05:24）。
导出当前峰值 `wsMB=2121`（20:08:25）。
若把批次图保留到导出，需再叠加导出自身的图与 payload（`Terraria\NPC.cs.json` 单文件
2,288,097,179 B ≈ 2.13 GiB，是当次最大 payload）——
本机物理内存实测 **13.86 GiB**（14,877,257,728 B），余量很小。**替代路线**：用既有持久化层
（`CpgShardStore` / `CpgFrozenShardExporter` / `CpgShardReader`）把冻结图落盘，导出改读分片；
代价是新增磁盘往返，且持久化路径自身复杂度高。

## 6. 度量方案（收益判定的前置）

在导出槽点加阶段计时，把 §3/§4/§5 的推算变成读数：

| 桩点 | 位置 | 产出 |
| --- | --- | --- |
| 工作区加载 | `ProjectJsonExporter.cs:144` 前后 | 第二次加载的真实墙钟 |
| 单文件构图 | `:256` 前后 | **每文件**构建耗时 |
| 投影 | `PrepareDocumentProjection`（`:694`）+ `MaterializeShardEdgeInputs`（`:769`，调用点 `:628`）前后 | 身份/边序化耗时 |
| 序列化 + 落盘 | `WriteJsonAtomicallyAsync` 前后 | I/O 与 JSON 耗时 |
| 审计产物 | `MeasureStage("BuildInventoryAudit")` 已有；另加快照哈希 | §3 的真实收益 |

**最小可用版本**：只加「每文件构图耗时」与「每文件写出耗时」两个数，
即可判定导出 338 s 中构图占多少，从而验证「30–43%」这个推算。

可复用既有的 `NLCPGBuilderOptions.PerformanceDiagnostics`
（`NLCPGPerformanceDiagnosticsMode`，`NLCPGBuilderOptions.cs:37`，默认 `Disabled`）
同型的开关承载，默认关闭以保持既有开销。

## 7. 测试修订清单

| 类别 | 用例 / 位置 | 处理 |
| --- | --- | --- |
| 编译级 | `NLCPGBuilderOptions` 新增具名参数 | 既有构造点用 `with`，多数无需改 |
| 断言级 | `NLCPGPartitionedBuilderTests.cs:241-259`（用例 `BuildFromSource_BuildInventory_ReportsFactsAndPreallocatedAnchorEquivalence`）断言 `BuildInventoryMetrics` 非空、`BuildInventoryAudit` 在阶段键中 | 默认 `true` ⇒ **不改**；新增一条「关闭时不产出且不抛」 |
| 断言级 | 同上 `:232-236` 走 `UsePreallocatedNodeIds = true`，`:245-254` 断言 `PreallocatedAnchorDiff` 等价 | 必须新增「关闭 + 预分配」组合的显式用例（覆盖 §3.2 的 `!`） |
| 断言级 | `GraphSnapshotVersion` 出现于 **15 个文件**（13 个测试 + 2 个测试基础设施 `tests/NLISSN.Testing/TestInfrastructure/CpgExecutionSnapshot.cs` 与 `CpgExecutionSnapshotComparer.cs`），主要分布在 `tests/NLISSN.ContractTests/Cpg/`（`CpgShardBuildCoordinatorTests` 9 处、`DataFlowPlanConstructionReuseTests` 7 处、`NLCPGGraphIndexConstructionReuseTests` 5 处、`DocumentShardingEquivalenceTests` 4 处、`DataFlowCandidateScanPruningTests` 4 处、`NLCPGSliceQueryTests` 4 处、`FrozenEdgeProjectionEquivalenceTests` 3 处，以及 `CpgExecutionMatrixTests`、`CpgPropertyEquivalenceTests`、`CpgRelationQueryTests`、`FrozenNodeStorageEquivalenceTests`、`DominanceRaceReachabilityProbe`）与 `tests/NLISSN.PerformanceTests/Performance/NLCPGNodeStoragePerformanceTests.cs` | 惰性化后返回值不变 ⇒ **应全部保持绿**；这是惰性化正确性的主证据 |
| 新增 | 惰性 `GraphSnapshotVersion` 的多线程首次访问 | 锁语义回归 |

## 8. 验证矩阵

按改动级别取最小集合（对齐 [Harness 验证矩阵](../harness-verification-matrix.md)）：

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj
dotnet build .\src\NLISSN\NLISSN.csproj
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
```

**字节中性的判据，以及一个必须先修的缺口。**

⚠️ `Build/worker8-utilization/baseline-payload-sha256.txt` **不能用作基线**。实测核对：

| 项 | 实测值 |
| --- | --- |
| 文件生成时间 | 2026-09-27 **10:45:58** —— 早于 export-cpg 那次运行（20:00–20:11） |
| 条目数 | **966**（非 967） |
| 与当前 `out/` 逐文件比对 | **0/966 命中**（966 个全部不匹配） |
| 缺失文件 | **`Terraria\WorldGen.cs.json`**（当次第 2 大 payload，2,041,898,950 B） |
| git 跟踪状态 | **未跟踪**（`git ls-files` 无此文件） |
| 文件大小 | 113,534 B |

即它记录的是**更早一次**运行的产物。已排除「只是编码差异」这一解释：对该目录下
`Terraria\WaterfallID.cs.json` 分别试算了「原始字节 / 去 BOM / 去 BOM+LF / 缩进 2 / 压缩」
五种形态的 SHA-256，**均与基线值不符**，故差异是**实质性**的，不是 BOM 或换行造成的。

⇒ 直接拿它当判据会把任何改动都判成「破坏中性」（0/966 必然不等）。
**必须先用 `Build/worker8-utilization/Export-PayloadSha256.ps1` 重建基线**，
再开始改 A–D：

```powershell
pwsh -File .\Build\worker8-utilization\Export-PayloadSha256.ps1 `
  -OutputRoot D:\TRbackup\Version4-cpg-export\out `
  -OutFile .\Build\worker8-utilization\export-cpg\payload-sha256-baseline.txt
```

重建后，判据是：对同一输入重跑导出，与该清单逐文件比对，要求 **967/967 全部相同**。

比对口径有三点必须遵守：

1. **排除 `manifest.json`**：`out/` 下共 968 个 `.json`，其中 967 个是 payload，
   第 968 个是 1,416,167 B 的清单文件（脚本已排除）。
2. **按原始字节哈希**：payload 带 `EF BB BF` BOM（`WriteJsonAsync:1144-1155` 有意写入
   `Encoding.UTF8.GetPreamble`），不得先解码为文本。脚本用 `Get-FileHash -Algorithm SHA256`。
3. **先确认二进制未变**：`Build/src/Debug/net10.0/` 的构建时间与基线生成时一致，
   否则比对的是两个不同的程序（见 §9 第 7 条）。

## 9. 未验证边界与风险

1. **导出侧无阶段遥测** ⇒ §3、§4、§5 的收益均为推算；`7,203 ms` 是**源码注释里的实测值**，
   不是本次导出环境下的复测。
2. **§3.3 的惰性化未验证并发路径**：是否有调用方在冻结后、发布前从多线程同时读 `GraphSnapshotVersion`，
   需以用例锁定；`Lazy<T>` 的 `ExecutionAndPublication` 是保守选择。
3. **§3.2 的 `!` 解引用**：`LastBuildMetrics.BuildInventoryMetrics!` 位于
   `BuildFromSource`（`:846`）与 `BuildFromSemanticModel`（`:885`）内，
   **两处都在 `if (!RequiresPreallocatedNodeIds())` 提前返回之后**（`:813`、`:859`），
   故「不做审计」只在预分配路径上才会与 `!` 相遇。导出不触发该路径，
   但开关必须保守（见 §3.2），且需一条显式用例覆盖「关闭 + 预分配」组合。
4. **§4 的两侧加载选项不等价**（生成源、`TargetDocumentPath`、`RequireCleanCompilation`）。
   复用快照前必须逐项证明对当次输入无分歧；`.sln` 多项目输入的对应关系**未验证**。
5. **§5 的三步全部落回契约变更**：`NodeId` 与 capability 集都会改变既有 payload 字节。
   本方案**不建议**在未登记 schema 版本位、未重建基线的情况下执行 §5。
6. **内存**：保留批次图到导出会与导出自身峰值叠加；本机 13.86 GiB 物理内存下余量未实测。
7. **运行二进制已变**：`Build/src/Debug/net10.0/` 现为 21:19 之后的构建，
   与当次运行的 19:42:49 不同；任何复测都必须重新记录二进制时间戳。
8. **基线失效**（新增，见 §8）：`baseline-payload-sha256.txt` 生成于 10:45、
   966 条、与当前 `out/` **0/966 命中**、且缺 `Terraria\WorldGen.cs.json`。
   它不可用作判据；A–D 任何一步开始前都要先用 `Export-PayloadSha256.ps1` 重建。
9. 工作树有大量未提交改动（含单文档分片功能），**未验证**其与导出基线的交互。

## 10. 推荐执行顺序

| 阶段 | 内容 | 字节风险 | 前置 |
| --- | --- | --- | --- |
| **A0** | §8 用 `Export-PayloadSha256.ps1` 重建字节中性基线 | 无 | 无 |
| **A** | §6 度量桩点（每文件构图 / 写出） | 无 | A0（否则无法区分「基线本身已失效」与「改动破坏了中性」） |
| **B** | §3.3 `GraphSnapshotVersion` 惰性化 | 无 | A0 |
| **C** | §3.1 `BuildInventoryAudit` 开关（默认算） | 无 | A0 |
| **D** | §4 复用工作区快照 | 无（需核对 §4.3） | A 的加载计时 |
| **E** | §5 复用批次图 | **有**（契约变更） | B/C/D 完成 + schema 版本位 + 基线重签 |

**A0 必须先做**：现有基线 0/966 失效，不先重建就无法判定 A–D 中的任何一步是否保持字节中性。
A0→A→B→C→D 可独立提交、独立验证；E 是独立立项，不应与前四步混在一次改动里。

## 11. 执行记录（A0–D 已完成；E 按范围排除）

本次执行范围经确认取 **A + B + C + D（含 §6 导出器度量埋点）**，**不含 E**
（§5 复用批次图需先接受 `NodeId` 契约变更，见 §5.3、§5.5、第 5 条风险）。

### 11.1 落点

| 阶段 | 改动位置 | 要点 |
| --- | --- | --- |
| A0 | `Build/worker8-utilization/Export-PayloadSha256.ps1`（新增，`Build/` 不入版本控制） | 按原始字节哈希、排除 `manifest.json`；重建得 **967** 条基线，自洽比对 967/967 |
| A | `ProjectJsonExporter.cs`、`ProjectExportMetrics.cs`（新增）、`ProjectExportOptions/Result`、`ProjectExportSettings`、`YamlConfigurationLoader`、schema | `performanceDiagnostics` 开关**默认 false**；开启后统计 工作区加载 / 单文件构图 / 投影 / 写盘 四段 |
| B | `NLCPGGraphIndex.cs` | `SnapshotVersion` 由即时计算改为 `Lazy<string>`（`ExecutionAndPublication`） |
| C | `NLCPGBuilderOptions.cs`、`NLCPGBuilder.cs` | 新增 `ComputeBuildInventory`（默认 `true`）；跳过条件为 `!开关 && !RequiresPreallocatedNodeIds()`，保住预分配路径的阶段键 |
| D | `ProjectJsonExporter`/`ProjectJsonExportService` 重载、`WorkspaceAnalysisService`、`CommandHost`、`ConfigurationRunHost` | 分析侧留证 `LoadedSnapshot` → `CommandHost.LastWorkspaceSnapshot` → 导出复用；`IsSnapshotReusable` 限定 `GeneratorMode == Disabled` 才复用，否则退回自加载 |

### 11.2 验证证据

- **编译**：`dotnet build src/NLISSN/NLISSN.csproj` → 0 error / 0 warning。
- **定向测试（用户指定的 10% 核心面，非 `-Fast/-Host/-Performance` 全矩阵）**：
  - `ContractTests`：24/24 通过。
  - `HostTests`：22/22 通过。
- **新增用例**（对应 §7 清单）：
  - B：`SnapshotVersion_ConcurrentFirstRead_ComputesOnceAndStaysStable` —— 断言构造后**尚未求值**、并发首读后才求值，且 32 线程取值一致。
  - C：`BuildFromSource_BuildInventoryDisabled_SkipsAuditWithoutChangingGraph`（关闭后无审计产物、图逐字段等价）；
    `BuildFromSource_BuildInventoryDisabledWithPreallocatedIds_StillAuditsForAnchorDiscovery`（覆盖 §3.2 的「关闭 + 预分配」组合）。
  - D：`ExportAsync_WithSnapshot_DoesNotLoadWorkspaceItself`、`ExportAsync_WithoutSnapshot_StillLoadsWorkspace`（一对判别器：
    项目路径**不存在**时，复用则成功、自加载则失败）、
    `ExportAsync_ReusedSnapshot_ProducesByteIdenticalPayloadsToSelfLoad`。
- **D 的字节中性是实测的，不是推理的**：最后一条在真实 `WorkspaceFixture.sln` 上跑「复用快照」与「自加载」两条路径，
  逐文件比对 payload 原始字节，并单独比对 `manifest.json` 的**诊断集合**（该集合来自快照的 `Compilation.GetDiagnostics()`，
  真实输入上有 1317 条）。三者全部相同。§4.3 的三处差异在该输入上无分歧。
  该用例已通过「把跳过守卫替换为抛异常后仍通过」的方式反证**确实执行了比对**，而非静默跳过。

### 11.3 与本文档原判断的偏差

1. **§9 第 4 条「两侧加载选项不等价」在本配置下不成立**：导出按 `GeneratedSourceMode` 推出 `IncludeGenerated`
   （同一来源），`TargetDocumentPath` 仅用于加载器**校验**（`TryValidateTargetDocument`）而**不裁剪**快照文档集，
   故 §4.3 三项对 `.csproj` 单项目输入无分歧；`.sln` 多项目输入已由 11.2 的真实夹具覆盖。
   即便如此，复用仍**限定**在 `GeneratorMode == Disabled` 时启用（generators 会改变语法树集合，属实质分歧）。
2. **§9 第 1 条（导出侧无阶段遥测）已消除**：A 补上了加载/构图/投影/写盘四段读数；度量开关默认关闭，不改任何输出。
3. **§9 第 7 条（二进制时间戳）仍需每次复测重新记录**：本次未重跑 9.53 GiB 全量导出
   （用户指定只做 10% 核心测试，且执行期间另有会话在跑性能测量、可用内存仅 ~5 GiB），
   故 §8「967/967」的全量判据**未在本轮执行**；D 的等价性改由 11.2 的真实夹具 A/B 逐字节比对承担。
4. **既有缺陷（非本次改动引入）**：`LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
   在 HEAD 上即失败 —— `src/NLISSN/NLISSN.csproj` 已引用 `NLCPG.ProjectJson.csproj`，
   但该测试的期望表未收录；两个文件均未被本次改动触及，故未代改。
