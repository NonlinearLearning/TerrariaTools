# Dome 文档入口

这套文档只描述当前仓库已经存在并仍在维护的结构。

当前主链路：

`CLI -> Application -> Analysis -> Rules -> Planning -> Rewrite -> Reporting`

其中：

- `Planning` 是一个阶段与模型集合，不是独立 `src/Plan` 项目
- 共享契约位于 `src/Application/Abstractions` 与 `src/Model/*`
- `src/Application/Legacy` 与 `src/Analysis/Legacy` 是兼容/运行时孤岛，不是标准主路径

## 文档结构

- [架构总览](./architecture.md)
- [执行流程](./execution-flow.md)
- [产物说明](./artifacts.md)
- [测试说明](./testing.md)
- [快速构建与验证](./rapid-build-test.md)
- [闭环样例](./closed-loop-demo.md)

### 分层说明

- [Cli 层](./layers/cli.md)
- [Application 层](./layers/application.md)
- [Analysis 层](./layers/analysis.md)
- [Rules 层](./layers/rules.md)
- [Planning 阶段](./layers/planning.md)
- [Rewrite 层](./layers/rewrite.md)
- [Reporting 层](./layers/reporting.md)

### 规则文档

- [规则编写指南](./rule-authoring-guidelines.md)
- [规则术语](./rule-concepts.md)
- [默认规则清单](./rule-spec-catalog.md)

## 当前边界

- 仓库已经完成 `0 Core`
- 标准文档不再描述 `Core` 层
- 文档只保留当前可验证的行为和入口，不保留迁移期实施草案
