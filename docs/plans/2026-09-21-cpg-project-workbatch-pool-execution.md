# CPG Project WorkBatch Pool Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 `NLCPG.ProjectExport` 从逐文档 builder 调度改为一个跨全项目的固定 12-worker WorkBatch 池，在有足够 CPU batch 时保持 12 个 worker 可运行，同时限制 fragment、catalog 和 Roslyn 对象的内存生命周期。

**Architecture:** 保留现有单文档 `CpgWorkBatch`、成本模型、`LocalCpgFragment`、稳定 reducer 和 DOP1 ordered path 作为局部语义基础。新增 project-level envelope、稳定 planner、一个有界 producer/worker/result sink 和单写入 catalog reducer；项目 worker 内的 NLCPG builder 固定 DOP=1，禁止嵌套并发。`ProjectJsonExporter` 只在全部 payload 和 catalog 完成后原子发布 manifest。

**Tech Stack:** .NET 10、C# preview、Roslyn 4.14、`System.Threading.Channels`、现有 `IConcurrencyPool`/`BoundedConcurrencyPool`、`NLCPG` stable anchors/fragments、SQLite resume catalog、xUnit Contract/Performance tests 和 `Build/Tools/Invoke-SerialDotnet.ps1`。

---

## 适用范围和前置条件

- 工作目录：`D:\ProjectItem\SourceCode\Net\NL`。
- 先阅读 [项目级 WorkBatch 池设计](../CodeDesign/目前设计/cpg-project-workbatch-pool.md)、[单文档 WorkBatch 设计](../CodeDesign/目前设计/cpg-workbatch-concurrency.md) 和 [Version4 基线](../benchmarks/cpg-workbatch-version4.md)。
- 现有单文档 WorkBatch Task 1-13 已有实现和 focused 证据；本计划不重新实现其成本模型、fragment 合同、局部 pass 或 reducer。
- 当前 `ProjectJsonExporter` 的内存释放和 manifest/catalog 修复是工作树中的已有变更，必须在其基础上接入，不得回滚。
- 保留工作树中的其他用户改动。每次改动前检查目标文件的当前内容；只修改本任务相关文件。
- 修改 C# 或测试前，继续遵守根级 `AGENTS.md`、[C# 约束](../../Context/约束/Google-CSharp-Style-Guide-约束.md)、[测试教程](../../Context/约束/测试代码编写教程.md)、[测试目录入口](../../tests/AGENTS.md) 和 [Harness Runtime](../harness-runtime.md)。
- 编译或测试前检查活动的 `dotnet.exe`、`csc.exe`；不要结束 Version4 导出或不明归属的进程。
- 所有 compile-capable 命令必须通过串行 wrapper；不得并行启动 build/test。

## 统一验证命令

每个 focused Contract/Performance 命令都先执行以下只读检查：

```powershell
Get-Process dotnet,csc -ErrorAction SilentlyContinue |
  Select-Object Id,ProcessName,StartTime,Path
```

所有临时文件使用 D 盘目录，避免把构建临时数据写入系统盘：

```powershell
$taskTemp = 'D:\TRbackup\Version4\Build\NL-work-temp'
$env:TEMP = $taskTemp
$env:TMP = $taskTemp
```

Contract focused test 的完整形式如下，后续任务只替换 `--filter`：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~CpgProjectWorkBatchPoolContractTests `
  '-m:1' '-nr:false' `
  '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' `
  '-p:BuildInParallel=false'
```

预期失败测试必须记录为 FAIL，预期通过测试必须记录测试数量、退出码和 warning/error；不能把未执行的 tier 写成通过。

---

### Task 1: 锁定项目级调度合同和嵌套 DOP 不变量

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchPoolContractTests.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectExportOptions.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`

**Step 1: Write the failing tests**

覆盖以下合同：

- 一个 project run 的生产 worker 数为 12；worker 数不随文档数、方法数或 batch 数增长。
- worker 内部传给 `NLCPGBuilder` 的 DOP 始终为 1。
- 12 个文档不会启动 12 个独立 executor；全局 task 数只包含 project pool 的固定 worker 加 reducer/producer 必要任务。
- 当至少 12 个 CPU batch 已准入且 result sink 不背压时，telemetry 能观察到 `ActiveWorkerCount=12`。
- 可运行 batch 少于 12、等待 fragment sink、等待 catalog、Roslyn barrier、GC/runtime 和 cancellation 都能使用不同 `IdleReason`。
- `ProjectExportOptions` 的配置能区分 project worker count 和 builder local DOP，不接受 0 或负数。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~CpgProjectWorkBatchPoolContractTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，因为 project-level envelope、worker-count contract 和 idle telemetry 尚未存在。

**Step 2: Implement the minimal contract**

增加 project-level options/telemetry contract。生产 profile 的 `ProjectWorkerCount` 固定为 12；保留测试用 DOP1/2/12/16 参数，但 16 不能绕过 project admission。将单文档 builder 的 local DOP 明确设置为 1，并把 `MaxDegreeOfParallelism` 的含义从“当前文档内部并发”迁移为“项目级 worker 数”时同步检查 CLI/测试调用者。

不要在此步骤接入 exporter；先让合同测试只验证配置、计数和不变量。

**Step 3: Run the focused test**

运行同一 filter。Expected: PASS；若已有单文档 WorkBatch 测试受影响，先恢复兼容适配，再继续，不修改其语义 oracle。

**Step 4: Commit**

```powershell
git add src/NLCPG.ProjectExport/ProjectExportOptions.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchPoolContractTests.cs
git commit -m "test: lock project work batch concurrency contract"
```

### Task 2: 增加全项目稳定 planner

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatch.cs`
- Create: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchPlanner.cs`
- Modify: `src/NLCPG/Builder/Concurrency/CpgWorkBatchBuilder.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectJsonExporter.cs`
- Create: `tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchPlannerTests.cs`

**Step 1: Write the failing tests**

使用真实 Roslyn fixture 覆盖：

- 全部 project 文档按 normalized project-relative path 生成稳定 `DocumentStableOrder`。
- 每个待计算文档都有 file-prelude work；outermost method 才进入 method work，local function/lambda 不重复排队。
- 每个文档的现有 local `CpgWorkBatch` 顺序保持稳定，project envelope 的 `GlobalStableOrder` 在改变发现完成顺序后仍相同。
- 有效 resume payload 不进入 CPU 计算队列；缺失、损坏或源指纹不匹配的 payload 进入计算队列并记录原因。
- 小方法按现有 target/max cost 合并，大/超大方法不跨文档强行合并；文档边界不会被一个 local batch 跨越。
- 空文档、生成文件过滤、重复路径和没有方法体的文件都有稳定结果。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~CpgProjectWorkBatchPlannerTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，因为当前 exporter 只按文档立即构建，没有 project-level stable plan。

**Step 2: Implement the minimal planner**

planner 接收 `WorkspaceProjectSnapshot`、project `Compilation`、resume state 和 options，先做文档排序和 resume 分类，再调用已有 `CpgWorkBatchBuilder` 为每个待计算文档生成 local batches，最后包成 project envelope。不要把全部 `SyntaxNode`、`SemanticModel` 或 graph 放进 planner 的长期集合；planner 只保留工作身份、成本和可重建的只读上下文。

把 project batch id 定义为稳定的 run-local ordinal，telemetry 另存 project run id；不要用 GUID 或完成时间排序。

**Step 3: Run the focused test**

运行同一 filter。Expected: PASS，并且现有 `CpgWorkBatchBuilderTests` 全部保持 PASS。

**Step 4: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatch.cs src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchPlanner.cs src/NLCPG/Builder/Concurrency/CpgWorkBatchBuilder.cs src/NLCPG.ProjectExport/ProjectJsonExporter.cs tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchPlannerTests.cs
git commit -m "feat: plan project work batches in stable order"
```

### Task 3: 实现全项目 bounded producer/worker/result sink

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs`
- Create: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchTelemetry.cs`
- Modify: `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs`
- Modify: `src/NLISSN.Infrastructure/Concurrency/IConcurrencyPool.cs`
- Modify: `src/NLISSN.Infrastructure/Concurrency/BoundedConcurrencyPool.cs`
- Create: `tests/NLISSN.PerformanceTests/Concurrency/ProjectCpgWorkBatchExecutorContractTests.cs`

**Step 1: Write the failing tests**

覆盖：

- 100 个跨文档 batch 只启动固定 12 个长期 worker；每个 worker 能处理多个 batch。
- 队列容量、estimated cost、estimated bytes 和 fragment sink 容量任一达到上限时，producer 异步背压。
- reducer 以 global stable order 消费结果，与 worker 完成顺序无关。
- producer、worker、reducer 任一失败都能让整个 run 失败，不返回 partial success。
- cancellation 在 enqueue 前、处理期间、sink 等待期间都能停止接收新 batch。
- 不创建 task-per-batch 或 task-per-method；telemetry 的 `WorkerTaskCount` 不随输入量增长。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~ProjectCpgWorkBatchExecutorContractTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，因为现有 executor 的输入是单文档 local batch 集合，尚无 project envelope 和 project-level admission。

**Step 2: Implement the minimal executor**

创建一个 project `Channel<ProjectCpgWorkBatch>`、一个 bounded result channel 和一个单一 stable reducer loop。启动 12 个长期 worker；worker 只调用注入的 `ProcessBatchAsync`，不在内部调用 `Task.Run`。复用现有 `CpgWorkBatchExecutor` 的 cancellation、first failure、queue budget 和 fail-open telemetry 语义，但不得在每个文档内再创建它。

把 `IdleReason` 记录在 worker 状态转移点：等待输入、等待 result sink、等待 reducer/catalog、barrier 和 cancellation。不要通过忙等维持 active 状态。

**Step 3: Run the focused test**

运行同一 filter。Expected: PASS，且现有单文档 executor contract 仍 PASS。

**Step 4: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchTelemetry.cs src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs src/NLISSN.Infrastructure/Concurrency/IConcurrencyPool.cs src/NLISSN.Infrastructure/Concurrency/BoundedConcurrencyPool.cs tests/NLISSN.PerformanceTests/Concurrency/ProjectCpgWorkBatchExecutorContractTests.cs
git commit -m "feat: add bounded project work batch pool"
```

### Task 4: 增加 DOP1 的 NLCPG project worker adapter

**Files:**
- Create: `src/NLCPG/Builder/Concurrency/ProjectCpgBatchWorker.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilder.cs`
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs`
- Modify: `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- Modify: `src/NLCPG/Builder/Streaming/StreamingFragmentCommitter.cs`
- Create: `tests/NLISSN.ContractTests/Cpg/CpgProjectBatchWorkerTests.cs`

**Step 1: Write the failing tests**

比较单文档 ordered path 与 project worker adapter 的：

- syntax/operation/CFG/member/data-flow fragment 内容和 stable anchor；
- `BuildFromSemanticModel` 的节点、边、diagnostic 和 capability；
- worker 传入的 local builder options 确实为 DOP1；
- worker 不修改共享 `NLCPGGraph`，不保留 Roslyn 对象到返回结果；
- 同一个 project compilation 被多个文档 worker 只读使用时，符号解析和 cross-file endpoint 不变化。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~CpgProjectBatchWorkerTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，因为当前 builder API 会在一个完整文档调用内部自行完成调度，不能消费 project envelope 的单个 local batch。

**Step 2: Implement the minimal adapter**

增加一个只处理 project envelope 的 worker adapter。它从预先准备的 project read-only context 取得 document/syntax/semantic inputs，运行已有局部 pass，返回 `LocalCpgFragment` 或 `FileExportProjection`。把内部 builder DOP 固定为 1，并明确禁止 adapter 创建另一个 `CpgWorkBatchExecutor`。

保留现有 ordered path 作为 DOP1 oracle；先通过 adapter parity，再删除或绕过旧的 per-document scheduling，不要一开始删除旧路径。

**Step 3: Run the focused test**

运行同一 filter，再运行已有 `CpgWorkBatch*` Contract 选择。Expected: 新 adapter parity PASS，旧 WorkBatch 合同不回归。

**Step 4: Commit**

```powershell
git add src/NLCPG/Builder/Concurrency/ProjectCpgBatchWorker.cs src/NLCPG/Builder/NLCPGBuilder.cs src/NLCPG/Builder/NLCPGBuilderOptions.cs src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs src/NLCPG/Builder/Passes/PartitionedOperationPass.cs src/NLCPG/Builder/Streaming/StreamingFragmentCommitter.cs tests/NLISSN.ContractTests/Cpg/CpgProjectBatchWorkerTests.cs
git commit -m "refactor: run project CPG batches with local DOP one"
```

### Task 5: 接入 `ProjectJsonExporter` 和 resume 流程

**Files:**
- Modify: `src/NLCPG.ProjectExport/ProjectJsonExporter.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectExportOptions.cs`
- Modify: `src/NLCPG.ProjectExport/Program.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectExportResult.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs`
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportConcurrencyTests.cs`

**Step 1: Write the failing tests**

增加导出合同：

- 多文档 fixture 只创建一个 project pool，所有缺失文档通过同一队列处理。
- `ResumeExportAsync` 不再对每个文档创建 DOP12 builder；有效 payload 走 import，缺失 payload 走计算。
- 输出中的每个 payload、节点端点、边文件列表和 manifest endpoint 与 DOP1 oracle 相同。
- 中途取消或一个文档失败时，返回失败结果，不生成新的成功 `manifest.json`。
- 重复运行 resume 只重建缺失/无效文件，已完成 payload 不重复计算。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~NLCPGProjectExportConcurrencyTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，现有 exporter 仍是逐文档完整调用 builder。

**Step 2: Implement exporter integration**

把当前 `ResumeExportAsync` 拆成四个明确阶段：project snapshot/resume scan、project planner、project executor、catalog/manifest finalize。将 `ProjectExportOptions.MaxDegreeOfParallelism` 的生产语义改为 project worker count，并在默认 production profile 使用 12；测试可显式指定 1/2/12/16，但不能绕过 nested-DOP contract。

保留当前的引用释放边界：在 catalog/manifest 流式阶段清除 `workspaceResult`、Roslyn compilation、project 和 document snapshot 的不必要引用；catalog 在 manifest 原子替换完成前保持有效；manifest 失败时不发布成功状态。

**Step 3: Run the focused tests**

运行 `NLCPGProjectExportConcurrencyTests` 和已有 `NLCPGProjectExportTests`。Expected: PASS，原有 3 个 exporter contract 不回归。

**Step 4: Commit**

```powershell
git add src/NLCPG.ProjectExport/ProjectJsonExporter.cs src/NLCPG.ProjectExport/ProjectExportOptions.cs src/NLCPG.ProjectExport/Program.cs src/NLCPG.ProjectExport/ProjectExportResult.cs tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportConcurrencyTests.cs
git commit -m "feat: export project documents through one work batch pool"
```

### Task 6: 固化 catalog、fragment sink 和内存背压

**Files:**
- Modify: `src/NLCPG.ProjectExport/ProjectJsonExporter.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectExportOptions.cs`
- Modify: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs`
- Modify: `src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs`
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportPersistenceTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

**Step 1: Write the failing tests**

覆盖：

- 慢 catalog writer 会阻塞 bounded result sink，且不会无限制保留 completed fragments。
- queued estimated bytes、active reservations 和 completed-not-reduced bytes 超限时 producer 停止扩张。
- catalog 只有一个 writer，edge key/node id 去重和输出顺序稳定。
- manifest 查询期间 catalog 仍可用，manifest 原子替换后才释放；manifest 写失败不会留下可误判为成功的文件。
- 取消后 worker、channel、catalog connection 和 temp writer 最终关闭；重复 cleanup 不抛二次异常。
- 已完成 payload 可被下一次 resume 重新验证，不依赖上次 run 的内存对象。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~NLCPGProjectExportPersistenceTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，当前 exporter 的 catalog 路径还没有 project-level result admission 和 writer/backpressure 合同。

**Step 2: Implement bounded persistence**

用单一 catalog writer 消费 project result sink；计算 worker 只产生 data-only projection。将 `MaxQueuedEstimatedCost`、`MaxQueuedEstimatedBytes`、fragment sink capacity、project memory reservation 和实际 fragment bytes 接入统一 admission。不要把 SQLite 写入放进 worker，也不要在最后一步收集全部节点/边到 `List`。

恢复当前 catalog 的主键顺序流式读取和 manifest 临时文件原子替换；对中断留下的临时文件只做受控、可诊断的清理，不擅自删除用户已有的中间产物。

**Step 3: Run the focused tests**

运行同一 filter、已有 `NLCPGProjectExportTests` 和 `SqliteCpgShardCatalogTests`。Expected: 全部 PASS，内存水位和 writer count 有可断言的 telemetry。

**Step 4: Commit**

```powershell
git add src/NLCPG.ProjectExport/ProjectJsonExporter.cs src/NLCPG.ProjectExport/ProjectExportOptions.cs src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs src/NLCPG/Builder/Concurrency/CpgWorkBatchExecutor.cs tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportPersistenceTests.cs tests/NLISSN.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs
git commit -m "feat: bound project export results and catalog writes"
```

### Task 7: 增加 project telemetry 和 idle diagnosis

**Files:**
- Modify: `src/NLCPG/Builder/PartitionPerformanceEvent.cs`
- Modify: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchTelemetry.cs`
- Modify: `src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs`
- Modify: `src/NLCPG.ProjectExport/ProjectJsonExporter.cs`
- Create: `tests/NLISSN.PerformanceTests/Cpg/ProjectCpgWorkBatchTelemetryTests.cs`
- Create: `docs/benchmarks/cpg-project-workbatch-baseline.md`

**Step 1: Write the failing tests**

断言：

- `WorkerCount=12`、`WorkerTaskCount=12`，并且不随输入文档数增长；
- `ActiveWorkerCount`、`RunnableBatchCount`、`WorkerActiveRatio`、queue high-water、completed-not-reduced high-water 和 reducer wait 都会输出；
- producer 背压、catalog 慢写、Roslyn barrier 和输入不足分别输出不同 `IdleReason`；
- telemetry sink 失败不会改变导出成功/失败语义；
- `RunId`、stage id 和 project batch id 能关联一个文档的完整生命周期。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~ProjectCpgWorkBatchTelemetryTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，现有单文档 WorkBatch event 不足以区分 project pool 的 worker、reducer、catalog 和 manifest 阶段。

**Step 2: Implement telemetry**

扩展现有 performance sink，而不是新增无法关联的日志通道。每个 project run 使用一个稳定 `RunId`；每个阶段使用稳定 stage id。记录 worker active 时间和等待原因，不能用“worker 没有抛异常”推断 CPU 正在计算。

在 benchmark 文档中区分：CPU active、queue wait、sink wait、catalog write、manifest finalize、GC/runtime 和错误清理；不要把 CPU 使用率单项作为通过条件。

**Step 3: Run the focused test**

运行同一 filter。Expected: PASS，并生成 small/mixed/large fixture 的 DOP1/2/12 telemetry 样本；16 只作为上限压力测试，不得产生超过配置上限的 worker。

**Step 4: Commit**

```powershell
git add src/NLCPG/Builder/PartitionPerformanceEvent.cs src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchTelemetry.cs src/NLCPG/Builder/Concurrency/ProjectCpgWorkBatchExecutor.cs src/NLCPG.ProjectExport/ProjectJsonExporter.cs tests/NLISSN.PerformanceTests/Cpg/ProjectCpgWorkBatchTelemetryTests.cs docs/benchmarks/cpg-project-workbatch-baseline.md
git commit -m "perf: measure project work batch utilization and idle reasons"
```

### Task 8: 完成跨 DOP 语义等价和失败矩阵

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchDeterminismTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgExecutionSnapshotComparerTests.cs`
- Modify: `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs`
- Modify: `tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs`
- Modify: `docs/benchmarks/cpg-project-workbatch-baseline.md`

**Step 1: Write the failing tests**

对同一个 project/source snapshot 运行 DOP1、DOP2、DOP12，必要时运行 DOP16 stress，比较：

- graph signature、stable anchor、node kind、source span、edge kind/label；
- CPG query、slice、call graph、data-flow、capability 和 diagnostic；
- per-file payload、catalog node/edge projection、manifest 文件和 endpoint 顺序；
- rule query、rewrite plan、rewritten source 和 per-file diff；
- valid/missing/invalid resume 混合场景；
- cancellation、worker failure、reducer failure、catalog failure 和 manifest failure。

运行：

```powershell
pwsh -NoProfile -File .\Build\Tools\Invoke-SerialDotnet.ps1 `
  test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore `
  --filter FullyQualifiedName~CpgProjectWorkBatchDeterminismTests `
  '-m:1' '-nr:false' '-p:UseSharedCompilation=false' `
  '-p:MSBuildNodeReuse=false' '-p:BuildInParallel=false'
```

Expected: FAIL，直到所有 project reducer、catalog 和 manifest 输入都不依赖 worker 完成顺序。

**Step 2: Implement only the smallest determinism fix**

按失败样本逐一修复稳定排序、project batch id、edge dedup、跨文档 endpoint、resume import 顺序或 reducer barrier。不能只在最后对 JSON 文本排序掩盖 graph/query 层的不确定性，也不能通过锁把并发路径强制退化成逐文档串行。

**Step 3: Run the focused test**

运行同一 filter，并运行已有 WorkBatch determinism、shard contract 和 exporter tests。Expected: DOP1/2/12 语义等价，失败矩阵不发布半成品 manifest。

**Step 4: Commit**

```powershell
git add tests/NLISSN.ContractTests/Cpg/CpgProjectWorkBatchDeterminismTests.cs tests/NLISSN.ContractTests/Cpg/NLCPGProjectExportTests.cs tests/NLISSN.ContractTests/Cpg/CpgExecutionSnapshotComparerTests.cs tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs docs/benchmarks/cpg-project-workbatch-baseline.md
git commit -m "test: prove project work batch determinism"
```

### Task 9: Version4 DOP12 acceptance 和文档收口

**Files:**
- Create or modify: `docs/benchmarks/cpg-project-workbatch-version4.md`
- Modify: `设计docs/目前设计/cpg-project-workbatch-pool.md`
- Modify: `设计docs/目前设计/cpg-workbatch-concurrency.md`
- Modify: `设计docs/README.md`
- Modify: `Context/progress.md` only with current verified facts
- Modify: `Context/feature_list.json` only if对应 feature 已登记

**Step 1: Run focused project export tests**

设置 D 盘 TEMP/TMP，检查活动 compile process，先运行所有 affected Contract tests，再运行 `NLCPGProjectExportTests`、project concurrency、persistence、determinism 和 existing WorkBatch selections。记录实际命令、退出码、测试数、warning/error 和输出目录。

**Step 2: Build affected projects serially**

通过 `Build/Tools/Invoke-SerialDotnet.ps1` 逐个构建：

- `src/NLCPG/NLCPG.csproj`；
- `src/NLCPG.ProjectExport/NLCPG.ProjectExport.csproj`；
- `tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`；
- `tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj`。

每次只运行一个 compile-capable 命令，使用 `-m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false`，确认输出位于 `Build/bin` 或声明的 benchmark 目录。

**Step 3: Run Version4 comparison**

针对同一 source snapshot，使用独立输出目录运行 DOP1、DOP2 和 DOP12；DOP16 只在资源允许时作为上限压力测试。不要覆盖已有 `D:\TRbackup\Version4\Build\NLCPG-workbatch-version4-dop-12` 输出。至少记录：

- 930 文档的 completed/failed count 和 manifest 是否生成；
- graph/payload/catalog/manifest signature；
- peak working set、GC、catalog 大小、queued bytes、fragment bytes；
- `WorkerCount`、`WorkerTaskCount`、active worker ratio、queue/reducer/catalog/manifest 时间；
- OOM、取消、失败清理和 resume 结果。

**Step 4: Decide acceptance honestly**

只有语义等价、固定 worker、不嵌套 DOP、有界内存、失败不发布半成品和 Version4 成功完成同时成立，才能把 project pool 标记为完成。CPU 在 SQLite、GC、Roslyn barrier 或 manifest 阶段降低不构成失败；但这些时间必须有 `IdleReason` 或阶段耗时证据。若仍 OOM 或吞吐不改善，保持 project pool opt-in，记录实际瓶颈，不声称“worker 数量增加即性能提升”。

**Step 5: Run documentation gates**

回读两个 CPG WorkBatch 设计页、新执行计划和 benchmark。检查本次文档修改的 Markdown 链接，再运行：

```powershell
pwsh -NoProfile -File .\Miscellaneous\scripts\check-harness-consistency.ps1 -RepoRoot (Get-Location).Path
git diff --check
```

Expected: Harness consistency `OK`，`git diff --check` 无本次修改引入的问题；不要把未运行的 Fast/Host/Performance tier 写成通过。

**Step 6: Commit**

```powershell
git add 设计docs/目前设计/cpg-project-workbatch-pool.md 设计docs/目前设计/cpg-workbatch-concurrency.md 设计docs/README.md docs/benchmarks/cpg-project-workbatch-version4.md Context/progress.md Context/feature_list.json
git commit -m "docs: record project work batch acceptance"
```

## 完成清单

- [ ] project pool 的 worker budget、nested DOP 禁止和 idle reason 有合同测试。
- [ ] 所有待计算文档进入一个稳定 project plan，resume payload 不重复计算。
- [ ] 一次 project run 只有固定 12 个长期 worker，不随文档或方法数创建 task。
- [ ] worker 内 NLCPG DOP=1，不能嵌套第二个 WorkBatch pool。
- [ ] project queue、fragment sink、estimated cost/bytes 和 catalog writer 有界。
- [ ] reducer、catalog 和 manifest 不依赖 worker 完成顺序。
- [ ] cancellation、worker failure、reducer failure、catalog failure 和 manifest failure 都不发布半成品 manifest。
- [ ] DOP1/2/12 的 graph、query、rule、rewrite、diff、payload、catalog 和 manifest 等价。
- [ ] telemetry 能解释 `ActiveWorkerCount < 12` 的非计算原因。
- [ ] Version4 运行结果、peak memory、OOM 状态、resume 状态和实际性能结论已经记录。
- [ ] Harness consistency 和 `git diff --check` 通过。
