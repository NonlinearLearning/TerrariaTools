# Harness Runtime

根 `AGENTS.md` 保留仓库契约、启动顺序和验证门槛。本文件记录可变的 harness 运行时操作。

## State

OMX 运行时状态位于 `.omx/state/`；计划、日志和跨会话笔记分别位于 `.omx/plans/`、`.omx/logs/` 和 `.omx/notepad.md`。未启用 OMX runtime 时，不应把这些目录当作实现或验证前提。

## Entry Points

- `init.ps1`：设置 `DOTNET_CLI_HOME` 并执行最小构建健康检查。
- `scripts/check-harness-consistency.ps1`：验证入口、文档链接与 feature/progress 契约；默认包含 CLI smoke。
- `scripts/harness-classify-change.ps1`：按改动路径给出验证等级、局部指引和受影响区域；未分类路径会失败。
- `scripts/harness-verify.ps1`：运行 L1 harness 验证并写入 `Build/harness-verification/` 证据。
- `scripts/harness-audit.ps1`：报告 harness 版本、入口存在性、hook 状态和最近验证证据。

## Verification Order

1. 先运行 `pwsh -File .\init.ps1`。
2. 用 `pwsh -File .\scripts\harness-classify-change.ps1 -Paths <changed-paths>` 确定局部指引和验证等级。
3. 运行对应的定向 build/test；L1 文档或 harness 改动至少运行 `pwsh -File .\scripts\check-harness-consistency.ps1`。
4. 需要可交接证据时运行 `pwsh -File .\scripts\harness-verify.ps1 -Level L1`。

## Local Hook

需要 commit 前的本地阻断时，执行：

```powershell
git config core.hooksPath .githooks
```
