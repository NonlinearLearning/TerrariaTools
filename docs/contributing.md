# 贡献指南

## 目标

本页定义一次可交付改动的最小闭环。它适用于代码、测试和文档；具体目录还可能有更严格的 `AGENTS.md`。

## 开始前

依次阅读：

1. 根目录 [`AGENTS.md`](../AGENTS.md)
2. [`progress.md`](../progress.md)
3. [`feature_list.json`](../feature_list.json)
4. 目标目录的局部 `AGENTS.md`

然后运行：

```powershell
pwsh -File .\init.ps1
```

## 工作方式

1. 用 `feature_list.json` 确认完成条件；不要只根据 `progress.md` 推断状态。
2. 为要改的行为选择最小可复现路径，并先定位现有测试或补充回归测试。
3. 保持 diff 小而可审查，复用现有工具和模式；不要无故加入依赖或扩大为全仓重构。
4. 修改 CLI 或开发流程时，同步检查快速开始、开发者指南和 CLI 参考。
5. 修改后按改动级别运行 build、test、命令行或 harness 验证。

## 文档改动

- 首页负责导航，专题页只解决一种读者任务。
- 示例必须使用真实路径、真实命令和真实选项。
- `docs/plans/` 保存提案与计划；`设计docs/` 保存设计事实与历史；不要把它们复制进门户页。
- 大文档采用小批次 patch，每次修改后回读标题、链接和交叉引用。

## 提交前验证

至少执行与改动匹配的命令：

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
```

完整分层要求见 [Harness 验证矩阵](harness-verification-matrix.md)。提交说明应记录实际执行的命令和未验证边界。

## 下一步

实现路径见 [开发者指南](developer-guide.md)；当前功能状态回到 [`feature_list.json`](../feature_list.json)。
