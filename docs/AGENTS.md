# `docs/` 工作约束

本目录包含面向读者的门户页、验证参考和代理执行约束。`Context/AGENTS.md` 是仓库级规则的权威来源。

## 文档分类

| 内容 | 位置 | 职责 |
| --- | --- | --- |
| 门户、上手、概念、命令、开发、贡献 | `docs/*.md` | 描述当前可验证入口，按读者任务分流。 |
| 计划与提案 | `docs/plans/` | 记录范围、验证、完成条件和已知风险。 |
| 设计事实与历史 | `设计docs/` | 保存架构推导与专题设计。 |

## 修改规则

- 每次只做一个小批次 patch；修改后回读该文件，检查标题、链接和交叉引用。
- 示例只能使用仓库中的真实路径、命令和选项；先从源码或测试确认 CLI 契约。
- 改 CLI、开发流程或验证说明时，同步检查 `quick-start.md`、`cli-reference.md`、`developer-guide.md` 和 `contributing.md`。
- 首页负责导航，专题页只解决一个读者任务；不要把设计历史复制到门户页面。
- 文档改动完成后运行 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1`，并检查 Markdown 链接和 `git diff --check`。

## 状态来源

`Context/feature_list.json` 是 feature 状态与完成条件的唯一来源。`Context/progress.md` 只保留当前事实、验证边界和下一步；详细历史留在 `Context/progressinfo.md`。
