# NL 工作导航

本文件是仓库任务的导航入口。开始工作时先阅读“开始工作”中的状态资料，随后在执行相关操作前阅读对应主题文档；涉及多个主题时读取全部相关条目。目标路径存在更深层 AGENTS.md 时，继续读取该局部入口。

## 开始工作

- 开始任务或恢复已有任务时，阅读[当前交接](Context/progress.md)。
- 判断 feature 状态、完成条件或验收证据时，阅读[feature 状态](Context/feature_list.json)。
- 修改任何受版本控制内容前，阅读[贡献指南](docs/contributing.md)。

## 仓库地图

| 路径 | 职责 | 首先阅读 |
| --- | --- | --- |
| src/ | NLCPG 与 NLISSN 源码 | 受影响项目及当前设计索引 |
| tests/ | Unit、Contract、Host、Performance 测试 | [测试目录入口](tests/AGENTS.md) |
| docs/ | 门户、计划、研究和流程文档 | [文档目录入口](docs/AGENTS.md) |
| 设计docs/ | 当前设计与历史设计 | [设计文档索引](docs/CodeDesign/README.md) |
| Context/ | 当前工作状态与待研究问题 | 本目录的状态文件 |
| Miscellaneous/ | 初始化与 harness 脚本 | [Harness Runtime](docs/harness-runtime.md) |
| 约束/ | C# 与测试编写规范 | 对应主题约束 |

## 任务路由

- 新增、修改或重构 C# 时，阅读[C# 风格约束](Context/约束/Google-CSharp-Style-Guide-约束.md)。
- 编写、修改或评审测试时，阅读[测试代码编写教程](Context/约束/测试代码编写教程.md)和[测试目录入口](tests/AGENTS.md)。
- 修改 CPG、分析管线、删除规则、授权、计划或改写语义时，阅读[领域术语与不变量](Context/CONTEXT.md)及[当前设计索引](docs/CodeDesign/README.md)中对应主题。
- 修改 NLISSN CLI、配置运行或改写链路时，阅读[NLISSN 局部入口](src/NLISSN/AGENTS.md)和[CLI 参考](docs/cli-reference.md)。
- 执行构建、测试、运行、发布或修改 harness 时，阅读[Harness Runtime](docs/harness-runtime.md)及适用的[验证矩阵](docs/harness-verification-matrix.md)。
- 修改门户文档、计划、研究或贡献流程时，阅读[文档目录入口](docs/AGENTS.md)和[文档门户](docs/README.md)。

## 局部入口

- src/NLISSN/：继续阅读 src/NLISSN/AGENTS.md。
- tests/：继续阅读 tests/AGENTS.md；目标位于 tests/NLISSN.Tests/ 时再阅读其局部入口。
- docs/：继续阅读 docs/AGENTS.md。

## Context 所有权

- feature_list.json 是 feature 状态、完成条件和验收证据的唯一来源。
- progress.md 只保留仍影响当前工作的事实、已验证边界、未验证边界和下一步；需要交接时替换过期内容，而非追加日志。
- 问题.md 保存待研究的问题，不可作为 feature 完成或验证通过的依据。

## 入口维护

- 移动、删除或重命名被链接的权威文件时，同步更新本导航和受影响的局部 AGENTS.md。
- 修改根级或局部 AGENTS.md 时，阅读[AGENTS 目录导航式写法研究报告](2026-09-16-AGENTS目录导航式写法研究报告.md)。
- 导航只说明何时阅读哪个权威来源；命令、参数、规则细则和历史背景保留在其主题文档、脚本或配置中。
