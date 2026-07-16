# 快速开始

## 这篇解决什么问题

这篇只回答一件事：如何最快把这个仓库跑起来，并确认当前最小主线是正常的。

这篇不负责解释完整架构，也不负责讲设计细节。

## 你会学到什么

1. 仓库当前有哪两个活跃主线项目
2. 应该先跑哪个健康检查
3. 如何运行最小 CPG 原型
4. 如何运行删除规则原型
5. 如何运行测试

## 前置条件

1. 已安装与仓库 `global.json` 对应的 .NET SDK
2. 能在 PowerShell 中执行 `dotnet`
3. 在仓库根目录下工作

建议先执行：

```powershell
pwsh -File .\init.ps1
```

这一步会做三件事：

1. 设置 `DOTNET_CLI_HOME`
2. 探测当前活跃项目
3. 执行最小构建健康检查

## 1. 先确认仓库能正常初始化

```powershell
pwsh -File .\init.ps1
```

如果这一步通过，说明：

1. 仓库根路径有效
2. 当前活跃项目可被探测到
3. 最小构建路径至少能走到检查阶段

## 2. 运行最小 CPG 原型

先构建：

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

再运行：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

再跑样例：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj .\src\MinimalRoslynCpg\samples\analysis-sample.cs
```

## 3. 运行删除规则原型

先构建：

```powershell
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj
```

再运行最小样例：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s
```

这个样例会输出：

1. `SeedMarks`
2. `PropagatedMarks`
3. `Decisions`
4. `Edits`
5. `RewrittenSource`

目录分析需要查看每个文件完成时间时，传入 `--per-file-timing-log` 指定 JSONL 文件。每个成功完成的文件都会立即追加一行，包含 `filePath`、`elapsedMs` 和 `completedAtUtc`：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- `
  'D:\source\project' `
  --max-degree-of-parallelism 16 `
  --per-file-timing-log .\Build\per-file-timing.jsonl `
  --no-diff
```

需要分别定位非编辑阶段时，改用 `--per-file-phase-timing-log-directory`。它会生成 `semantic-model.jsonl`、`cpg-build.jsonl`、`mark.jsonl`、`propagate.jsonl`、`lift.jsonl`、`decide.jsonl` 和 `total.jsonl`；配合 `--skip-rewrite` 只运行到决策阶段，不调用改写器：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- `
  'D:\source\project' `
  --max-degree-of-parallelism 16 `
  --per-file-phase-timing-log-directory .\Build\phase-timing `
  --skip-rewrite `
  --no-diff
```

需要观察单次分析的线程池、GC 和内存变化时，传入 `--runtime-metrics-log`。分析未结束时每五秒追加一条 `running` JSONL 记录，结束时追加一条 `completed` 或 `failed` 记录。比较 DOP 时，每个 DOP 必须顺序执行三次，并为每次运行使用独立日志文件：

```powershell
foreach ($dop in 8, 12, 14, 16) {
  foreach ($run in 1..3) {
    dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- `
      'D:\source\project\LargeFile.cs' `
      --max-degree-of-parallelism $dop `
      --runtime-metrics-log ".\Build\dop-$dop-run-$run.jsonl" `
      --skip-rewrite `
      --no-diff
  }
}
```

记录字段包括 `maxDegreeOfParallelism`、`elapsedMs`、`allocatedBytes`、Gen0/1/2 次数、托管堆、工作集和 ThreadPool 状态。每行对应一个时间点，不能把不同 DOP 的累计值直接相加。

需要定位某个文件完成 CPG 分析后的内存状态时，传入 `--per-file-memory-diagnostics-log`。它对每个正常完成的文件追加一条 JSONL，包含该文件的分配增量、托管堆/提交量/碎片、工作集、私有内存、ThreadPool 状态，以及 CPG 节点、边、Syntax、DataFlow 和分区缓冲计数：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- `
  'D:\source\project' `
  --max-degree-of-parallelism 1 `
  --per-file-memory-diagnostics-log .\Build\per-file-memory.jsonl `
  --skip-rewrite `
  --no-diff
```

### 并行度边界

`--max-degree-of-parallelism N` 是当前唯一的运行时 DOP 选项。省略或无法解析时取 `Environment.ProcessorCount`；可解析但小于 `1` 的值按 `1` 处理。

该值同时限制目录文件分析、启用的规则组并行、参数收缩辅助扫描，以及单个文件内 CPG 的 Syntax、Operation 和 DataFlow 分片。它限制的是各调度点的并发工作量；ThreadPool 的实际线程数仍由 .NET 运行时决定。

当前没有 `--large-file-max-degree-of-parallelism`。`LargeFileLineThreshold` 等大文件阈值属于 CPG builder 内部配置，未暴露为 CLI 参数，也不会选择另一套 DOP。大文件的 CPG 分片仍复用 `--max-degree-of-parallelism`。

## 4. 跑测试

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj
```

## 5. 如何判断是否“跑对了”

### 最小 CPG 原型

重点看：

1. 是否能构建成功
2. 是否能输出样例结果
3. 是否能继续推进 CFG / DataFlow / 调用关系

### 删除规则原型

重点看：

1. 是否能产出 seed marks
2. 是否能产出 propagated marks
3. 是否能生成 decisions
4. 是否能生成 rewrite edits
5. 是否能得到 rewritten source

### 测试

重点看：

1. 是否全部通过
2. 是否覆盖当前最小样例主线
3. 是否能作为后续改动的回归基线

## 6. 如果失败，先看什么

1. `progress.md`
2. `feature_list.json`
3. `AGENTS.md`
4. `init.ps1`

## 7. 下一步

1. 想继续改代码，去看 `developer-guide.md`
2. 想先看项目入口，回到根目录 `README.md`
