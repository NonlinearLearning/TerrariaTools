# NL

NL 是一个以 Roslyn 为基础的 .NET 研究仓库，探索两条可运行的路径：

- `MinimalRoslynCpg`：构建最小的 Roslyn-native Code Property Graph（CPG）。
- `RoslynPrototype`：验证从 Roslyn 分析到代码删除与改写的规则流水线。

它服务于原型验证和架构研究；当前不提供稳定的产品级 CLI、API 契约或完整 Joern 实现。

## 快速开始

仓库根目录执行：

```powershell
pwsh -File .\init.ps1
```

该命令设置本仓库的 `DOTNET_CLI_HOME`，读取 `global.json` 指定的 SDK，并构建最小 CPG 项目作为健康检查。

完整的可运行路径见 [快速开始](docs/quick-start.md)。

## 从哪里开始

| 目标 | 入口 |
| --- | --- |
| 第一次运行仓库 | [快速开始](docs/quick-start.md) |
| 理解 CPG、规则流水线与当前边界 | [核心概念](docs/concepts.md) |
| 查删除规则 CLI 的输入、输出和常用选项 | [CLI 参考](docs/cli-reference.md) |
| 修改代码或测试 | [开发者指南](docs/developer-guide.md) |
| 提交可验证的改动 | [贡献指南](docs/contributing.md) |
| 查看当前设计记录 | [设计文档索引](设计docs/README.md) |
| 查看当前 feature 状态 | [feature_list.json](feature_list.json) |

## 可执行项目

| 项目 | 路径 | 用途 |
| --- | --- | --- |
| 最小 CPG | `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj` | 从 Roslyn 语法和语义事实构建、查询与持久化最小 CPG。 |
| 删除规则原型 | `src/RoslynPrototype/RoslynPrototype.csproj` | 运行分析、标记、传播、决策和源码改写原型。 |
| 回归测试 | `pwsh -File .\scripts\Run-TestTiers.ps1 -Fast` | 运行 Unit 与 Contract 的快速语义回归。 |

## 当前边界

- 图构建、CFG、DataFlow 与调用关系仍在迭代，兼顾图等价性与并行运行的稳定性。
- 删除规则可处理受测试保护的原型场景；改写前应先在副本或不带 `--write-back` 的路径验证 diff。
- 设计决策与历史记录位于 `设计docs/` 和 `docs/plans/`；门户文档只描述当前入口与可验证行为。

## 文档结构

`docs/` 按读者任务组织：上手、概念、命令、开发、贡献和验证。详见 [文档门户](docs/README.md)。

## 开发约束

提交改动前先阅读 [AGENTS.md](AGENTS.md)、[progress.md](progress.md) 和 [feature_list.json](feature_list.json)。它们定义当前工作边界、验收条件及运行前置步骤。
