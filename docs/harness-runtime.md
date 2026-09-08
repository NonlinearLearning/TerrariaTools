# Harness Runtime

`Context/AGENTS.md` 保留仓库契约、启动顺序和验证门槛。本文件记录可变的 harness 运行时操作。

## State

OMX 运行时状态位于 `.omx/state/`；计划、日志和跨会话笔记分别位于 `.omx/plans/`、`.omx/logs/` 和 `.omx/notepad.md`。未启用 OMX runtime 时，不应把这些目录当作实现或验证前提。

## Entry Points

- `Miscellaneous/init.ps1`：设置 `DOTNET_CLI_HOME` 并构建 `src/NLISSN/NLISSN.csproj`。
- `Miscellaneous/scripts/check-harness-consistency.ps1`：核对当前 CLI、局部指南、门户文档和状态文件的路径契约。
- `Miscellaneous/scripts/Run-TestTiers.ps1`：按 Unit、Contract、Host 和 Performance 层运行测试并写入证据。
- `Miscellaneous/scripts/harness-classify-change.ps1`、`Miscellaneous/scripts/harness-verify.ps1` 和 `Miscellaneous/scripts/harness-audit.ps1`：提供本地变更分类、验证汇总和审计入口；使用前先阅读各脚本参数与输出范围。

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
