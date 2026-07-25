# Harness Runtime

根 `AGENTS.md` 保留仓库契约、启动顺序和验证门槛。本文件记录可变的 harness 运行时操作。

## State

OMX 运行时状态位于 `.omx/state/`；计划、日志和跨会话笔记分别位于 `.omx/plans/`、`.omx/logs/` 和 `.omx/notepad.md`。未启用 OMX runtime 时，不应把这些目录当作实现或验证前提。

## Entry Points

- `init.ps1`：设置 `DOTNET_CLI_HOME` 并执行最小构建健康检查。
- 当前 checkout 仅提供 `scripts/New-CpgDopSmallFixture.ps1`。`check-harness-consistency.ps1`、`harness-classify-change.ps1`、`harness-verify.ps1` 和 `harness-audit.ps1` 未随该 checkout 提供，不能作为当前分支的验证入口。

## Verification Order

1. 先运行 `pwsh -File .\init.ps1`。
2. 按修改边界选择定向 build、test 和 CLI smoke。
3. 对文档或 harness 改动，至少回读修改文件并运行引用项目的 build/test。
4. 将实际命令与结果记录在提交说明或当前交接中；不要引用该 checkout 不存在的 harness 脚本。

## Local Hook

需要 commit 前的本地阻断时，执行：

```powershell
git config core.hooksPath .githooks
```
