# 文档门户

本目录提供当前可运行入口、开发流程和命令参考。设计推导与历史记录位于 [`设计docs/`](../设计docs/README.md)。

## 按目标阅读

| 你的目标 | 从这里开始 | 接下来 |
| --- | --- | --- |
| 第一次运行仓库 | [快速开始](quick-start.md) | [核心概念](concepts.md) |
| 理解项目在验证什么 | [核心概念](concepts.md) | [当前设计](../设计docs/目前设计/项目概览.md) |
| 使用删除规则原型 | [CLI 参考](cli-reference.md) | [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md) |
| 改 CPG、规则或改写逻辑 | [开发者指南](developer-guide.md) | [贡献指南](contributing.md) |
| 选择验证层级 | [Harness 验证矩阵](harness-verification-matrix.md) | `Context/progress.md` |
| 优化大对象 / 撞内存墙 | [LOH 大对象优化指导](loh-large-object-optimization-guide.md) | [LOH 大对象清单](research/2026-09-24-loh-large-object-inventory-from-reports.md) |
| 了解进程内存配置（`ConserveMemory`） | [GC 内存总量配置](gc-conserve-memory.md) | [LOH 大对象优化指导](loh-large-object-optimization-guide.md) |
| 查 LOH 里到底有哪些大对象 | [LOH 大对象清单](research/2026-09-24-loh-large-object-inventory-from-reports.md) | [Version4 DOP12 诊断](benchmarks/nlissn-version4-dop12-diagnostics.md) |
| 评估「异步事件循环/外置 runtime」能否提速 | [异步事件循环与吞吐研究](research/2026-09-26-async-event-loop-agent-throughput-research.md) | [统一工作调度内核设计](plans/2026-09-24-unified-work-scheduler-design.md) |
| 查调度派发开销与小/中/大对象粒度 | [调度派发平方项与粒度测量报告](benchmarks/2026-09-26-scheduler-dispatch-scaling-and-granularity-report.md) | [统一工作调度内核设计](plans/2026-09-24-unified-work-scheduler-design.md) |
| 执行 CPG 内存优化 | [内存优化执行总索引](plans/2026-09-25-memory-optimization-execution-index.md) | 七份独立执行文档 |
| 核对「已实现但生产未用」的设计 | [未接线设计审计报告](plans/2026-09-26-unwired-design-audit-report.md) | [已拒绝项审计报告](plans/2026-09-26-rejection-audit-report.md) |
| 使用本地 harness 与运行时状态 | [Harness Runtime](harness-runtime.md) | [Harness 验证矩阵](harness-verification-matrix.md) |
| 导出 CPG 项目级 JSON | [CLI 参考](cli-reference.md)（`nlcpg-project-export`；组件在 `src/NLISSN.Infrastructure/ProjectJson/`） | [项目级基线](benchmarks/cpg-project-workbatch-baseline.md) |
| 让代理处理仓库工作 | [根级 AGENTS.md](../AGENTS.md) | [docs/AGENTS.md](AGENTS.md) |

DataFlow 长尾的阶段/扫描量定位见[小批量同次测量报告](benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md)，可复现运行入口见 [Harness Runtime](harness-runtime.md)。

## 页面职责

| 页面 | 回答的问题 |
| --- | --- |
| [快速开始](quick-start.md) | 怎样在本机完成最短的构建、运行和测试路径？ |
| [核心概念](concepts.md) | 两个原型分别解决什么问题，当前有哪些边界？ |
| [CLI 参考](cli-reference.md) | 统一 `nlissn.yml` 如何选择工具并控制输入、分析和输出？ |
| [开发者指南](developer-guide.md) | 代码、测试和设计文档应如何定位与验证？ |
| [贡献指南](contributing.md) | 一项改动从开始到交付需要满足哪些约束？ |
| [Harness 验证矩阵](harness-verification-matrix.md) | 不同改动需要哪些可复现证据？ |
| [Harness Runtime](harness-runtime.md) | 当前 harness 的状态目录、入口命令和执行顺序是什么？ |
| [LOH 大对象优化指导](loh-large-object-optimization-guide.md) | 托管堆高位常驻、撞物理内存墙时，按什么流程诊断、用哪些模式优化、怎样验证？ |
| [GC 内存总量配置](gc-conserve-memory.md) | 三个入口固化了哪条 GC 配置？它降的是什么、为什么不放 `nlissn.yml`、怎么关掉？ |

三个可执行入口都使用当前工作目录的 `nlissn.yml`，由 Schema 3 的 `tool` 字段
选择 `nlissn`、`nlcpg` 或 `nlcpg-project-export` 分支；入口不接受业务命令行参数。

## 状态来源

门户说明当前稳定入口，不取代工作状态文件：

- `Context/feature_list.json` 是 feature 状态和完成条件的唯一来源。
- `Context/progress.md` 记录当前事实、验证边界和下一步。
- [AGENTS.md](../AGENTS.md) 定义工作顺序、局部约束和验证规则。
