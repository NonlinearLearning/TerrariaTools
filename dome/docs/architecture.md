# Dome v1.1 架构说明

## 执行模型

`dome` 使用固定流水线：

`Analysis -> Mark -> Plan -> Rewrite -> Report`

各阶段职责：

- `Analysis`
  将 Roslyn 语法和语义事实投影为 `AnalysisView`
- `Mark`
  执行规则，输出 `MarkDecision`
- `Plan`
  将规则决策编译为可执行的 `AuditPlan`
- `Rewrite`
  只按计划执行改写
- `Report`
  输出稳定 JSON 产物

当前架构是单次运行、计划驱动，不支持 checkpoint，也不在 rewrite 阶段补做规则推理。

## 项目结构

- `src/Core`
  稳定契约和共享模型
- `src/Analysis/Roslyn`
  Roslyn 投影、三张图和查询服务
- `src/Rules`
  seed、传播、保护、方法级、类级、表达式投影规则
- `src/Plan`
  计划编译、冲突裁决、覆盖裁决
- `src/Rewrite/Roslyn`
  计划驱动改写
- `src/Reporting`
  JSON 产物输出
- `src/Application`
  运行模式和阶段编排
- `src/Cli`
  命令解析、配置加载、退出码
- `tests/Dome.Tests`
  Analysis、Rules、Plan、Rewrite、Cli、Application 六类测试

## 稳定契约

当前对外稳定的核心契约：

- `RunRequest`
- `RunResult`
- `FailureCode`
- `AnalysisView`
- `AuditPlan`
- `PlanConflict`
- `RunReport`

当前已经固定的 `TargetKind`：

- `Statement`
- `Method`
- `Class`

当前动作集合：

- `Delete`
- `CommentOut`
- `ReplaceWithDefault`
- `AddReturn`

## Analysis 设计

`AnalysisView` 由两部分组成：

- target 事实
- 图快照

当前包含三张图：

- `TypeGraph`
  类型级依赖图
- `FunctionGraph`
  函数级依赖图
- `StatementGraph`
  语句级依赖图

当前仍然保持规则优先，而不是平台优先：

- 不做完整 CFG
- 不做符号执行平台
- 不做类型冲突分析
- 不做动态调用链

## Rules 设计

当前规则体系按职责分层：

- `Seed`
- `Propagation`
- `Protection`
- `Method`
- `Class`
- `ExpressionProjection`

当前规则基线：

- `dome:delete`
- `controlflow-mark`
- `dataflow-propagation`
- `function-mark`
- `class-mark`
- `expression-mark`

当前规则输出统一为 `MarkDecision`，不会直接产出 rewrite 行为。

## Plan 设计

`Plan` 是执行真源。职责包括：

- 将 `MarkDecision` 编译成 `AuditPlan`
- 做冲突检测
- 做覆盖裁决
- 为审计和报告固化原因链

当前裁决优先级：

- `Class Delete` 高于同类中的 `Method` 和 `Statement`
- `Method Delete` 高于同方法中的 `Statement`
- 同一 target 上不同 action 仍然是 `PlanCompileFailed`

## Rewrite 设计

`Rewrite` 只消费 `AuditPlan`，不会重新读取规则或分析图补决策。

当前支持：

- `TargetKind.Statement`
- `TargetKind.Method`
- `TargetKind.Class`

当前不支持：

- expression-level rewrite
- initializer rewrite
- class-level `CommentOut`
- 原地改写

目标定位顺序固定为：

1. `DocumentPath + MemberId`
2. `SpanStart + SpanLength`
3. `DisplayText`

任一层不匹配都会快速失败。

## 保护模型

保护发生在可执行计划产出之前。当前高风险保护包括：

- `virtual`
- `override`
- `abstract`
- 接口实现成员
- object initializer 路径
- 其他当前不安全的改写路径

被保护的 target 仍然可以被分析，但不会进入可执行计划。

## 当前边界

当前版本明确不做：

- expression-level target
- initializer rewrite
- struct / record / interface / enum target
- dynamic call graph
- checkpoint / 断点续跑
- 通用静态分析平台化
