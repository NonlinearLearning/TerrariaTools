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
