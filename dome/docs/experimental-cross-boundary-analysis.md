# 跨边界分析专题说明

本文是 `dome` 主架构文档之外的专题补充，聚焦“一个 statement 级命中如何影响 method 级删除判断”。

主入口文档：

- [架构总览](./architecture.md)
- [执行流程](./execution-flow.md)
- [Analysis 层说明](./layers/analysis.md)
- [Rules 层说明](./layers/rules.md)

## 1. 当前已经实现的跨边界能力

当前代码并没有实现一个通用的“跨任意层级传播框架”，而是实现了两种受控的跨边界行为。

### 1.1 statement 作用域穿透

`ParentBlockPiercingScopeRule` 会在特定条件下把 statement snapshot 的分析范围从最小 block 扩展到父 block。

触发条件包括：

- 种子 target 是 statement
- target 不是 high-risk
- target 不是 object initializer assignment
- target 不是 sanitizing assignment
- 当前 statement 使用了参数，或使用了在本 block 之前未定义的局部变量

它解决的是“数据依赖定义在父 block 中，但种子命中在子 block 中”的传播问题。

### 1.2 invocation 边界提升

`InvocationBoundaryPromotionRule` 会把某些 statement delete 决策提升成 method delete 候选。

当前严格限定为：

- target 必须是 statement
- action 必须是 `Delete`
- statement 必须且只能直接调用一个方法
- 被调用方必须是私有方法
- 被调用方必须有 body
- 被调用方不能是 override / interface implementation
- 移除当前 statement 后，该方法不能再被其他函数引用

这类提升会输出新的 `MarkDecision`，其 `Reason.RuleId` 为 `boundary-promotion`，并在 `RunReport.BoundaryPromotionSummary` 中体现。

## 2. 当前没有实现的能力

以下能力不应在主文档中被描述成“已经支持”：

- 通用的多跳跨函数传播
- 面向 `Creates` / `ReadsMember` / `WritesMember` 的跨边界扩张
- 默认全量函数图传播
- 基于 `AnalysisView.StatementGraph` 的全局 statement 图传播

## 3. 与惰性函数图设计的关系

当前函数级范围分析采用的是“索引 + 按需 snapshot”模式：

- 默认只保留 `FunctionIndex` 与 `FunctionFactsIndex`
- 需要全局影响范围时显式调用 `GetWholeProjectSnapshot()`
- 需要局部函数范围时显式调用 `GetExpandedMembersSnapshot(...)`

因此，跨边界分析的实现重点不是先构造一张全局大图，而是在真正需要时按上下文拉取局部快照。

## 4. 文档定位

如果要理解系统的正式行为，优先看：

1. [architecture.md](./architecture.md)
2. [execution-flow.md](./execution-flow.md)
3. [layers/analysis.md](./layers/analysis.md)
4. [layers/rules.md](./layers/rules.md)

本文只解释“边界提升”和“作用域穿透”这一专题，不再承担总体架构说明职责。
