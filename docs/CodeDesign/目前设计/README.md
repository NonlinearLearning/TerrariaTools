# 当前设计

> **状态：当前实现的设计索引。** 本目录只描述已落地或能由当前源码验证的边界；历史决策在 `设计docs/历史设计/`，外部研究在 `docs/research/`，未完成 feature 以 `Context/feature_list.json` 为准。

## 先读什么

| 读者问题 | 入口 | 继续阅读 |
| --- | --- | --- |
| Application 应该负责什么 | [项目概览](项目概览.md) | [Application 层审查结果](../../docs/research/2026-08-04-application-layer-review.md) |
| 一次删除分析怎样运行 | [删除规则流水线](deletion-pipeline.md) | [规则 DAG](规则DAG.md)、[Fast Path 边界](fast-path-boundaries.md) |
| 规则如何产生和传递事实 | [原子表达式标记](atomic-marking.md) | [原子规则组件](delete-class-components.md)、[原子规则传播事实](delete-class-propagation.md) |
| 事实端口为什么需要类型化 | [规则事实端口类型化](规则事实端口-枚举化.md) | [规则 DAG](规则DAG.md)、[原子表达式标记](atomic-marking.md) |
| CPG 能提供什么证明 | [最小 CPG 架构](cpg-architecture.md) | [CPG 能力边界](cpg-capabilities.md)、[CPG 演进优先级](cpg-roadmap.md) |
| 怎样分析 `.sln` 或 `.csproj` | [Workspace / MSBuild 输入](workspace-input.md) | [配置与制品](配置与制品.md)、[删除规则流水线](deletion-pipeline.md) |
| 决策怎样变成源码 | [决策与冲突裁决](decision-resolution.md) | [Rewrite 与 Diff](rewrite-and-diff.md)、[从 Propose 提取事实](proposal-fact-extraction.md) |
| 运行时和验证怎样分层 | [运行时基础设施](运行时基础设施.md) | [配置与制品](配置与制品.md)、[日志与并发](日志与并发.md)、[缓存边界](缓存边界.md)、[测试组件](测试组件.md)、[测试策略](testing-strategy.md) |

## 统一读法

每篇专题页按同一顺序回答：

1. **范围**：本页解决哪个问题，不解决什么问题。
2. **当前实现**：给出实际代码路径、输入和输出。
3. **流程或契约**：用表格、流程图或规则列出可验证关系。
4. **限制**：写出缺少证明时的失败关闭行为和未实现能力。
5. **验证**：给出测试项目、测试类型或可运行命令。
6. **下一步**：只写当前代码和 feature 状态支持的下一项，不把愿景写成现状。

## 当前主线

```text
nlissn.yml
  -> ConfigurationRunHost
  -> Hosting 读取源码和创建运行时
  -> Application 编排 CPG 与 Mark -> Propagate -> Lift -> Propose
  -> Core 决策和 Rewrite
  -> Hosting 发布 diff、evidence、日志和回写结果
```

Application 接收内存中的源码和语义模型，不读取文件、不解析 YAML、不写 artifact。`ExecutionRuntime.cs` 当前仍位于 Application 物理目录，但命名空间属于 `NLISSN.Core.Pipeline`，Core 项目通过链接项编译它；迁移建议和证据见 [Application 层审查结果](../../docs/research/2026-08-04-application-layer-review.md)。

## 状态来源

- `Context/feature_list.json`：feature 状态、definition of done 和验证证据的唯一来源。
- `Context/progress.md`：当前交接事实、阻塞和下一步；不保存长篇历史。
- [2026-07-29 至 08-05 实施计划归档](../历史设计/2026-07-29至08-05-实施计划归档.md)：已清理计划的压缩结论和当前专题去向。
- `docs/research/`：外部来源、审查和实验记录。
- 高星文档结构研究：[项目样本与入口](../../docs/research/2026-08-04-high-star-github-docs-study.md)、[设计文档样式](../../docs/research/2026-08-04-high-star-github-documentation-style.md)、[live audit](../../docs/research/2026-08-04-high-star-github-docs-live-audit.md)。
- 长度口径与逐篇统计：[目前设计文档长度审查](../../docs/research/2026-08-04-design-doc-length-audit.md)。

## 文档验证

修改本目录后，在仓库根目录顺序执行：

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

代码路径、测试名称和命令必须先从当前源码确认。文档长度统计使用非空白字符，同时报告行数；目标是同层专题页接近，而不是用重复文字填充。
