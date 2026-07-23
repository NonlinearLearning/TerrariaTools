# 核心概念

## 这篇解释什么

本页说明 NL 当前两个可运行原型的职责、它们之间的关系和明确边界。它不替代算法设计、规则细节或性能记录；这些内容以 [`设计docs/`](../设计docs/README.md) 和当前 feature 记录为准。

## 两条主线

### MinimalRoslynCpg

`src/MinimalRoslynCpg/` 从 Roslyn 的语法、符号和操作事实构建最小 CPG。当前工作覆盖图节点与边、查询、局部 CFG、DataFlow、调用上下文和可选持久化分片。

构建器中的并行 worker 只读取 Roslyn semantic facts；图节点、边、去重和顺序由稳定调用线程物化。这是保持不同并行度图等价的核心约束。

### RoslynPrototype

`src/RoslynPrototype/` 是删除规则的可执行原型。它从一个 C# 文件或目录获取输入，完成：

```text
分析 → 标记 → 传播 → 提升 → 决策 → 改写
```

`DeletionApplicationService` 是最小应用层入口；命令行宿主负责输入、目录调度、diff、日志、计划保存与可选写回。

## Roslyn-first

规则优先使用 Roslyn 已提供且可验证的语法、符号和语义信息。CPG 用于表达需要图结构的问题；它不是每个规则命中都必须经过的厚重中间层。

## 当前边界

- 这不是完整 Joern 实现，图模型与查询面仍在演进。
- 这不是稳定发布的删除工具。规则只承诺测试覆盖的场景。
- `--write-back` 会改写输入文件；默认路径应先观察生成的 diff。
- 当前 CLI 没有 `--help` 命令。可用输入与常用选项见 [CLI 参考](cli-reference.md)。

## 进一步阅读

- 要运行代码：看 [快速开始](quick-start.md)。
- 要理解删除规则设计：看 [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md)。
- 要修改实现：看 [开发者指南](developer-guide.md)。
