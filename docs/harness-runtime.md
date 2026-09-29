# Harness Runtime

[`AGENTS.md`](../AGENTS.md) 路由仓库契约、启动和验证主题；本文件记录可变的 harness 运行时操作。

## State

OMX 运行时状态位于 `.omx/state/`；计划、日志和跨会话笔记分别位于 `.omx/plans/`、`.omx/logs/` 和 `.omx/notepad.md`。未启用 OMX runtime 时，不应把这些目录当作实现或验证前提。

## Entry Points

- `Miscellaneous/init.ps1`：设置 `DOTNET_CLI_HOME` 并构建 `src/NLISSN/NLISSN.csproj`。
- `Miscellaneous/scripts/check-harness-consistency.ps1`：核对当前 CLI、局部指南、门户文档和状态文件的路径契约。
- `Miscellaneous/scripts/Run-TestTiers.ps1`：按 Unit、Contract、Host 和 Performance 层运行测试并写入证据。
- `Miscellaneous/scripts/harness-classify-change.ps1`、`Miscellaneous/scripts/harness-verify.ps1` 和 `Miscellaneous/scripts/harness-audit.ps1`：提供本地变更分类、验证汇总和审计入口；使用前先阅读各脚本参数与输出范围。

## DataFlow 小批量测量

DataFlow 方法内阶段测量使用 [Run-DataFlowTailMeasurement.ps1](../Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1)。
在仓库根运行该脚本，以 OutputDirectory 指定尚不存在的证据目录；默认独立构建 Release，
固定执行 21 次预热、正式测量及等价检查，单次 5 秒、整批 45 秒限制均包含预热。
NoBuild 只用于已有匹配 Release 产物。原始 JSONL、完整有序图及候选提交序列保存在输出目录，
随后用 python tools/DataFlowTailMeasurement/summarize.py 加该目录路径进行离线审计和 CSV 汇总。
该入口是内部诊断宿主，不改变业务 CLI。计数口径、命令示例、实际结果与限制见
[同次测量报告](benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md)。

## 测试并行

测试并行配置的唯一权威来源是 `tests/xunit.runner.json`（受版本控制）。
它通过四个测试 csproj 里的 `None` + `CopyToOutputDirectory="PreserveNewest"` 项
复制到输出目录 `Build/test/<Configuration>/<TFM>/`——xUnit **只**读输出目录这一份。

当前值：`parallelizeTestCollections: true`、`maxParallelThreads: 4`。

- **`maxParallelThreads` 是必要的质量旋钮，不是可选优化。** 实测（Contract 976 项，
  `maxParallelThreads` 分别取 4/8/12）：12 时 `CpgWorkBatchExecutorWorkerUtilizationTests`
  在 11 次运行中**失败 6 次**（该用例断言 8 个 worker 都 `BusyTime > 0`，
  高并发下线程池给不出 8 个 worker 各自的活）；8 与 4 均为 **0 次**。
  4 与 8 的墙钟与 12 相当（约 105–170 s），故取 4。
- **不要**把配置只放在源目录：csproj 未声明复制项时源目录的 json 不会被复制，
  极易被误判为"该开关无效"。
- 进程级状态（`Directory.SetCurrentDirectory`、`Console.SetOut`）会污染并行中的其他集合。
  改写 CWD 的用例必须归入 `DisableParallelization = true` 的集合
  （见 `ProcessCurrentDirectoryCollection`），否则相对路径会经
  `Path.GetFullPath` 解析到别人的临时目录。

## Verification Order

1. 先运行 `pwsh -File .\Miscellaneous\init.ps1`。
2. 按修改边界选择定向 build、test 和 CLI smoke。
3. 对文档或 harness 改动，至少回读修改文件、运行 `Miscellaneous/scripts/check-harness-consistency.ps1`，并运行引用项目的 build/test。
4. 将实际命令与结果记录在提交说明或当前交接中；不要把未执行的 harness 脚本写成验证证据。

## Local Hook

需要 commit 前的本地阻断时，执行：

```powershell
git config core.hooksPath .githooks
```
