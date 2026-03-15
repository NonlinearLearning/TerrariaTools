# Rules 层说明

返回 [架构总览](../architecture.md)。

## 1. 这一层做什么

`src/Rules` 负责把分析结果转换成“建议执行哪些动作”的规则决策。

它不直接输出 `AuditPlan`，而是输出 `MarkDecision[]`。这意味着 Rules 层负责的是判定和传播，不负责最终执行顺序和冲突收口。

## 2. 主要输入 / 输出

### 输入

- `AnalysisResultModel`
- `AnalysisContext`
- `AnalysisExecutionSnapshot`
- `AnalysisServices`

### 输出

- `IReadOnlyList<MarkDecision>`

## 3. 对外 API

| API | 作用 | 调用方 |
| --- | --- | --- |
| `MarkingRuleRegistry.CreateDefault()` | 组装默认规则集合 | `Application` |
| `MarkingRuleEngine.Execute(AnalysisResultModel)` | 执行简化规则模式 | 测试、轻量调用方 |
| `MarkingRuleEngine.Execute(AnalysisExecutionSnapshot, AnalysisServices, RuleExecutionContext)` | 执行显式分层上下文模式 | `Application` |
| `MarkingRuleEngine.Execute(AnalysisContext)` | 执行完整规则模式 | `Application` |

### 规则接口

Rules 层对扩展点做了显式分组：

- `ISeedRule`
- `IExpressionProjectionRule`
- `IPropagationRule`
- `IProtectionRule`
- `IMethodRule`
- `IClassRule`
- `IBoundaryPromotionRule`
- `IStatementScopeRule`

## 4. 默认规则组合

`MarkingRuleRegistry.CreateDefault()` 当前注册的实现包括：

- `DirectiveSeedRule`
- `ExpressionProjectionRule`
- `SanitizationPropagationRule`
- `HighRiskProtectionRule`
- `ObjectInitializerProtectionRule`
- `FunctionMarkingRule`
- `ClassMarkingRule`
- `InvocationBoundaryPromotionRule`
- `ParentBlockPiercingScopeRule`

### 规则语义模板与规格清单

Rules 层新增或调整规则时，正式文档入口固定为：

- [规则设计与测试规范](../rule-authoring-guidelines.md)
- [默认规则规格清单](../rule-spec-catalog.md)
- [规则术语定义](../rule-concepts.md)

约束固定为：

- 新规则必须先写统一语义模板，再写测试，再写实现
- propagation 是规则语义的一部分，不是后补细节
- 默认 registry 中的规则解释以规格清单为准，而不是只靠源码阅读
- 规则注释里出现的专有概念，以术语定义文档中的代码级解释为准

### 快速落地版默认边界

首轮 TR 项目试跑阶段，只保留已验证的最小保护集：

- 委托 / 事件方法组引用保护
- `Register<T>()` 类型保护
- 已知 manager / resolver indexer 注册保护
- `IItemDropRule` 组合器 `Add(new Xxx())` 保护
- 已知框架入口“类型结构 + 入口名”联合保护

首轮固定限制：

- 注册型保护默认只作用于 `class-mark`
- 不因“类型被注册”自动保护整类私有方法
- 不扩新的 propagation 语义
- 忽略 `CallTracker` 等 AOP 注入结构

## 5. 这一层承担的职责

### 5.1 从 directive 或表达式命中生成种子决策

Seed rules 和 expression projection rules 负责回答：

- 哪些 statement/class 先成为候选 target
- 命中的 action 是什么
- reason 应如何记录

### 5.2 在局部 statement snapshot 中传播

Rules 层不会直接扫描全局 statement 图，而是通过：

- `AnalysisServices.Statements.Analyze(seedTarget, scopeMode)`

拿到局部 snapshot 后进行数据流传播。

### 5.3 应用保护规则

Protection rules 用于阻止一些不应继续向下处理的目标，例如：

- 高风险方法
- object initializer assignment

### 5.4 做方法级与类级删除判断

完整上下文下，Rules 层会基于：

- `FunctionIndex`
- `ReferenceQueryService`
- `InheritanceQueryService`

评估私有方法是否可删、类是否无引用可删。

### 5.5 做边界提升

Rules 层还能把 statement delete 提升为 method delete 候选。这是跨层级判定，但依然只输出 `MarkDecision`，不会直接进入 rewrite。

## 6. 执行流程

`MarkingRuleEngine.Execute(context)` 当前大致顺序是：

1. 找出所有 seed decision。
2. 对 statement seed 在局部 snapshot 中做 propagation。
3. 做 boundary promotion。
4. 跑 method rule。
5. 跑 class rule。
6. 对完全重复的 decision 去重。

## 7. 与上下游层的边界

### 上游

- Analysis 层提供的 `AnalysisExecutionSnapshot`、`AnalysisServices` 和兼容 `AnalysisContext`

### 下游

- Plan 层消费 `MarkDecision[]`

Rules 层不做的事情：

- 不排序最终执行顺序
- 不做冲突 resolver
- 不直接操作 Roslyn 语法树

## 8. 本层不负责什么

Rules 层不负责：

- 加载源码和 workspace
- 建立真实 Roslyn semantic model
- 序列化 artifact
- 直接写出 `audit-plan.json`
- 直接改写文件
