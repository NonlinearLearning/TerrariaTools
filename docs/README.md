# 文档门户

本目录提供当前可运行入口、开发流程和命令参考。设计推导与历史记录不在这里维护：它们位于 [`设计docs/`](../设计docs/README.md) 与 [`docs/plans/`](plans/)。

## 按目标阅读

| 你的目标 | 从这里开始 | 接下来 |
| --- | --- | --- |
| 第一次运行仓库 | [快速开始](quick-start.md) | [核心概念](concepts.md) |
| 理解项目在验证什么 | [核心概念](concepts.md) | [当前设计](../设计docs/目前设计/项目概览.md) |
| 使用删除规则原型 | [CLI 参考](cli-reference.md) | [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md) |
| 改 CPG、规则或改写逻辑 | [开发者指南](developer-guide.md) | [贡献指南](contributing.md) |
| 选择验证层级 | [Harness 验证矩阵](harness-verification-matrix.md) | 根目录 `progress.md` |
| 使用本地 harness 与运行时状态 | [Harness Runtime](harness-runtime.md) | [Harness 验证矩阵](harness-verification-matrix.md) |
| 让代理处理仓库工作 | [docs/AGENTS.md](AGENTS.md) | 根目录 `AGENTS.md` |

## 页面职责

| 页面 | 回答的问题 |
| --- | --- |
| [快速开始](quick-start.md) | 怎样在本机完成最短的构建、运行和测试路径？ |
| [核心概念](concepts.md) | 两个原型分别解决什么问题，当前有哪些边界？ |
| [CLI 参考](cli-reference.md) | 删除规则命令接收什么输入，常用选项如何影响结果？ |
| [开发者指南](developer-guide.md) | 代码、测试和设计文档应如何定位与验证？ |
| [贡献指南](contributing.md) | 一项改动从开始到交付需要满足哪些约束？ |
| [Harness 验证矩阵](harness-verification-matrix.md) | 不同改动需要哪些可复现证据？ |
| [Harness Runtime](harness-runtime.md) | 当前 harness 的状态目录、入口命令和执行顺序是什么？ |

## 状态来源

门户说明当前稳定入口，不取代工作状态文件：

- `feature_list.json` 是 feature 状态和完成条件的唯一来源。
- `progress.md` 记录当前事实、验证边界和下一步。
- `AGENTS.md` 定义工作顺序、局部约束和验证规则。
