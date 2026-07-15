# CPG DiffGraph 与 FlatGraph 风格存储实施提案

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 将 `MinimalRoslynCpg` 从“每个节点/边即时托管对象化、冻结时多重复制索引”的构图模型，迁移为“分片 DiffGraph 暂存 + 数值 ID 的紧凑邻接/属性表”，降低高并行 CPG 构图的常驻与冻结峰值，同时保持 DOP 图等价和规则输出不变。

**Architecture:** 本提案借鉴 Joern 的分工，不移植 Scala 的 FlatGraph 库。`RoslynCpgDiffGraph` 是每个 pass 或方法分片私有的增量日志；稳定调用线程按既有顺序提交。`RoslynCpgFlatGraph` 是 .NET 内部的最终只读图：节点用数值 ID 定位，边用连续数组与偏移表保存；节点属性按列/可选表保存。`RoslynCpgGraph` 暂时保留为公开 façade，避免规则层同时大面积改动。

**Tech Stack:** .NET 10、现有 Roslyn、BCL `ArrayPool<T>` / `ReadOnlySpan<T>` / `FrozenDictionary`（仅在测量证明有收益时）；不引入 Joern、Scala、数据库或新的 NuGet 依赖。

---

## 0. 边界、替换对象与不可变契约

### 当前需要替换的机制

| 当前机制 | 问题 | 替换目标 |
| --- | --- | --- |
| `Dictionary<string, RoslynCpgNode>` | 字符串作为主地址；节点对象常驻 | `int nodeId` 主地址 + 仅构建期的 `string -> int` 映射 |
| `HashSet<RoslynCpgEdge>` | 每条边是 record、哈希槽、扩容数组 | `EdgeDraft` 值类型暂存；冻结后连续 edge arrays |
| `RoslynCpgGraphIndex` 的五套边分组数组 | 同一边引用被出/入、按 kind、按 node-kind 多次保留 | `outOffsets/outEdges`、`inOffsets/inEdges`；按 kind 查询采用范围或轻量二级索引 |
| `GraphCache.Edges = graph.Edges.ToList()` | 规则阶段保留整图边副本 | 基于 FlatGraph 入/出邻接做局部 BFS；只缓存查询结果 |
| `OperationPartitionResult.Records` 全量等待后 materialize | 高 DOP 下每个方法保留 operation 记录 | 每个完成分片产生 diff，稳定 window 按顺序 apply 并释放该 diff |

### 必须保持

- 同一源码在 DOP `1/8/12/14/16` 下节点属性、边集合、稳定排序和删除规则决策等价。
- worker 继续只读 Roslyn semantic facts；全局节点编号、去重和最终提交仍在稳定调用线程完成。
- `RoslynCpgGraph` 冻结后不可变；`GetOutgoingEdges`、`GetIncomingEdges`、`GetEdges`、`NodesByKind`、局部视图和 CLI 输出语义不变。
- 不以降低 DOP、丢弃 CFG/DataFlow 边、截断完整图作为优化手段。

### 非目标

- 本阶段不做跨文件持久化、磁盘图数据库、Joern API 兼容层或压缩序列化格式。
- 不把 Roslyn 的 SyntaxTree、SemanticModel、IOperation 生命周期纳入 FlatGraph；它们是构图输入，须由分析上下文独立释放。
- 不承诺固定内存下降百分比；验收以同一机器、同一输入、重复运行的测量结果为准。

## 1. 先锁定现有行为和基线

**Files:**
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Create: `tests/RoslynDeletionPrototype.Tests/RoslynCpgStorageContractTests.cs`
- Create: `scripts/measure-cpg-memory.ps1`

1. 新增图快照 helper：稳定导出节点 `(Id, Kind, 所有公开属性)` 和边 `(SourceId, TargetId, Kind, Label)`，按现有比较排序。
2. 为小源码、CFG 源码和 DataFlow 源码写回归：构建、冻结、再读取全部公开查询 API；断言快照相等且冻结后 mutation 抛异常。
3. 为 `RoslynCpgStructureViewBuilder` 写多片段最短连接路径回归，锁定当前“无向路径 + 片段内边”的结果，防止后续移除 `GraphCache` 时改变规则语义。
4. 增加内部 telemetry 断言入口，仅报告：node count、edge count、构图期 edge draft 数、冻结后 edge slot 数、索引字节估算值；不在业务输出中伪造 GC 指标。
5. 编写测量脚本：每个 DOP 独立进程运行三次，记录 wall-clock、`GC.GetTotalAllocatedBytes`、峰值 `WorkingSet64`、`GC.GetGCMemoryInfo().HeapSizeBytes`、节点数、边数和提交次数到 JSONL。输入沿用已有 Terraria dry-run 路径；缺失时脚本明确失败，不改用小样本冒充完整源集。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore --filter "FullyQualifiedName~RoslynCpgStorageContractTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
pwsh -File .\scripts\measure-cpg-memory.ps1 -InputPath <Terraria-source> -DopValues 1,8,12,14,16 -Iterations 3
```

记录基线 JSONL 后才进入下一任务；基线是判定替换是否真实降低峰值的唯一依据。

## 2. 引入 DiffGraph，但先不替换公开图

**Files:**
- Create: `src/MinimalRoslynCpg/Model/RoslynCpgDiffGraph.cs`
- Create: `src/MinimalRoslynCpg/Model/RoslynCpgDiffEdge.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 定义内部 `RoslynCpgDiffGraph`：包含有序 node upsert 和 `readonly record struct RoslynCpgDiffEdge(string SourceId, string TargetId, RoslynCpgEdgeKind Kind, string? Label)`；它只能由单个 worker 或单个串行 pass 持有。
2. `RoslynCpgGraph.Apply(diff)` 在调用线程执行：节点按 ID 去重，边按现有集合语义去重；在这一阶段保留现有 `RoslynCpgNode` / `RoslynCpgEdge` 存储，目的只是先验证提交边界和等价性。
3. 将 `PartitionedOperationPass` 的结果从 `OperationPartitionRecord` 后的直接写图，改为“分析 facts → 分片 diff → 按 `Order` apply → 立即清空该 diff”。禁止把全部分片 diff 收集完后一次提交。
4. 按相同规则迁移 Syntax 和 DataFlow 的已并行候选路径；串行 pass 可直接创建短生命周期 diff 并立即 apply，避免双实现。
5. 所有 diff 的节点/边顺序必须由源码位置、现有 sequence 和 edge kind 决定；`Apply` 不依赖任务完成顺序。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore
```

新增断言：DOP `1/8/12/14/16` 快照相等、每个分片只提交一次、提交后 diff 不再持有 nodes/edges。

## 3. 以数值 ID 和连续边记录替换最终 Edge 对象图

**Files:**
- Create: `src/MinimalRoslynCpg/Model/RoslynCpgFlatGraph.cs`
- Create: `src/MinimalRoslynCpg/Model/RoslynCpgNodeTable.cs`
- Create: `src/MinimalRoslynCpg/Model/RoslynCpgEdgeTable.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Replace: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgStorageContractTests.cs`

1. 定义最终边记录为紧凑值类型：`int SourceNodeId`、`int TargetNodeId`、`ushort Kind`、`int LabelId`；`LabelId = -1` 表示 null。节点 ID 和 label 通过每图字符串表去重。
2. 构建期继续使用 `string -> int` 节点映射和 edge-dedup hash；冻结时将去重后的 edge records 按 `(Source, Kind, Target, Label)` 排序并一次性写入 `RoslynCpgEdgeTable`。
3. `RoslynCpgEdgeTable` 至少产生：`outOffsets + outEdgeIndices`、`inOffsets + inEdgeIndices`。按 kind 的查询在邻接范围内过滤；只有基准证明热点时，才增加 `(nodeId, kind) -> range` 的紧凑索引。不得恢复当前每个 group 一个 `RoslynCpgEdge[]` 的设计。
4. `RoslynCpgNodeTable` 使用列存字段：kind、span、implicit flag 为值数组；ID、DisplayKind、Name、FullName、Signature、DispatchKind、TypeFullName、FilePath、Text 保存为字符串表 ID 或 `-1`。可选属性不创建 per-node dictionary。
5. `RoslynCpgGraph` 保持现有 public 查询签名，但内部通过 FlatGraph 返回轻量投影。`Nodes` / `Edges` 的全表枚举只供 CLI 和测试使用，禁止生产规则热路径调用；用 analyzer-style test 或 telemetry 检查关键规则路径不触发全表边 materialization。
6. `FreezeQueryIndex()` 改为 `Freeze()` 内部构造 FlatGraph，随后释放构建期 edge-dedup hash、draft 列表和临时排序数组，使它们不与后续规则执行重叠。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore --filter "FullyQualifiedName~RoslynCpgStorageContractTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests"
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore
```

新增断言：null label、同端点不同 kind、同端点同 kind 不同 label、重复边、孤立节点、空图、冻结后二次调用和所有公开邻接查询都与基线一致。

## 4. 移除规则阶段的整图副本

**Files:**
- Modify: `src/RoslynPrototype/Analysis/View/RoslynCpgStructureViewBuilder.cs`
- Modify: `src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`

1. 删除 `GraphCache.Edges = graph.Edges.ToList()` 和 `BuildUndirectedAdjacency(Edges)`。
2. 为 FlatGraph 添加内部无向邻居枚举：同一 node 的 out/in 邻接按需合并；BFS 前驱仅保存被访问局部路径，不缓存整图双向邻接。
3. `NodesByFilePath` 只保存排序后的数值 node ID 范围或 file-path string-table ID 到 node ID 列表；构建结构视图时再投影所需节点。
4. `MarkAnalysisSnapshot`、`RuleContext` 直接使用 `NodesByKind` 和边范围 API，消除 `graph.Nodes.FirstOrDefault` 等全表扫描热点。
5. 保留 run-scoped 局部 view 缓存，但增加数量和估算字节 telemetry，避免大量规则片段把局部视图累积成第二个图。

**Verification:**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore --filter "FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~PipelineComponentTests"
```

新增断言：多片段连接路径、节点排序、边排序、缓存命中和 serial/group-parallel 删除决策均保持现有输出。

## 5. 峰值内存验收与发布门槛

**Files:**
- Modify: `scripts/measure-cpg-memory.ps1`
- Modify: `docs/quick-start.md`
- Modify: `docs/developer-guide.md`
- Modify: `progress.md`
- Modify: `feature_list.json`

1. 对同一 Terraria 目录，以 DOP `1/8/12/14/16` 各启动三个独立进程；记录中位数和最大值，分离 wall-clock 与所有逐文件累计时间。
2. 通过 ETW / `dotnet-counters` 或 EventPipe 采集 `GC Heap Size`、`Allocation Rate`、Gen2、LOH、Working Set；脚本采样值只能标记为近似峰值，ETW trace 才可作为精确归因。
3. 通过门槛：每个 DOP 的图快照和删除结果与基线一致；DOP 16 的进程峰值托管堆与工作集均低于基线中位数；wall-clock 不得恶化超过 10%。若任一项不满足，保留旧实现开关并用测量数据定位是 diff 积压、字符串表、边排序还是规则视图缓存造成。
4. 文档说明默认存储、兼容诊断枚举的成本、测量方法及“不能以 DOP 限流作为本提案验收替代”。更新 `progress.md` 与 `feature_list.json` 记录实际数值和未完成验证。

**Final verification:**

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore
pwsh -File .\scripts\check-harness-consistency.ps1
pwsh -File .\scripts\measure-cpg-memory.ps1 -InputPath <Terraria-source> -DopValues 1,8,12,14,16 -Iterations 3
```

若 harness 脚本仍受已知 `NewJoern/Program.cs` 硬编码问题阻断，报告该独立缺陷；构建、测试与测量结果不得借它宣称通过。

## 实施顺序与风险控制

1. 任务 1 先产出基线和契约；无基线不合并后续存储替换。
2. 任务 2 只引入 DiffGraph 提交边界，保持旧存储，是并行确定性风险最低的切入点。
3. 任务 3 替换最终存储，是内存收益主体，必须独立提交并完成全量等价测试。
4. 任务 4 清理结构视图缓存，是避免“主图已压缩、规则层又复制全图”的必要闭环。
5. 任务 5 仅根据实测决定是否默认启用；不得根据小样本或单次 GC 数字决定。

**主要风险：** `Edges` / `Nodes` 是大量测试、CLI 和规则代码的公开枚举面；一次删除会造成语义回归。迁移期必须将其保留为诊断兼容层，并逐个把热路径迁移到 node-ID/adjacency API。DiffGraph 也不能无限积压；提交窗口必须与现有 `BoundedPartitionWorkWindow` 的活跃 worker 数绑定。
