# Dome 文档

本目录描述的是当前仓库中的实际实现。它回答三个问题：

1. Dome 现在有哪些可用流程。
2. 代码应该放在哪一层。
3. 你在修改代码时应该先看哪份文档。

如果你第一次进入仓库，按下面的顺序阅读：

1. [架构总览](./architecture/overview.md)
2. [项目布局](./architecture/project-layout.md)
3. [执行流程](./architecture/flows.md)
4. [边界约束](./architecture/boundaries.md)
5. [术语表](./glossary.md)
6. [构建与测试](./guides/build-and-test.md)

如果你已经知道自己要做什么，直接按任务查阅：

- 修改标准 `dome run` 流程：先看 [执行流程](./architecture/flows.md) 和 [产物说明](./guides/artifacts.md)
- 修改运行时流程：先看 [执行流程](./architecture/flows.md) 和 [边界约束](./architecture/boundaries.md)
- 修改 shadow extraction：先看 [执行流程](./architecture/flows.md)、[规则概念](./rules/concepts.md) 和 [术语表](./glossary.md)
- 修改规则引擎：先看 [规则概念](./rules/concepts.md)、[规则目录](./rules/catalog.md) 和 [规则编写](./rules/authoring.md)
- 修改测试或新增测试：先看 [测试指南](./guides/testing.md)

## 文档索引

### 架构

- [架构总览](./architecture/overview.md)
- [项目布局](./architecture/project-layout.md)
- [执行流程](./architecture/flows.md)
- [边界约束](./architecture/boundaries.md)

### 指南

- [构建与测试](./guides/build-and-test.md)
- [测试指南](./guides/testing.md)
- [产物说明](./guides/artifacts.md)
- [闭环示例](./guides/closed-loop-demo.md)

### 规则

- [规则概念](./rules/concepts.md)
- [规则编写](./rules/authoring.md)
- [规则目录](./rules/catalog.md)

### 参考

- [术语表](./glossary.md)

## 使用约定

- 文档默认以 `dome/` 目录作为当前工作目录。
- 文档优先描述“当前实现”，不描述历史目录结构。
- 文档优先给出可执行步骤，不给抽象口号。
