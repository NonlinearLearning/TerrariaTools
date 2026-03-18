# 默认规则清单

本文档列出当前标准 registry 中的默认规则，以及它们的大致职责。

## Seed / Projection

- `DirectiveSeedRule`
  - 从源码 directive 生成初始 decision
- `ExpressionProjectionRule`
  - 把表达式级命中投影到 statement target

## Propagation

- `SanitizationPropagationRule`
  - 基于数据流传播删除意图
  - 遇到 sanitization 截断传播

## Protection

- `HighRiskProtectionRule`
  - 保护高风险 target
- `ObjectInitializerProtectionRule`
  - 保护对象初始化器赋值相关 target

## Method / Member / Class

- `FunctionMarkingRule`
- `PublicMethodPrivatizationRule`
- `UnusedMethodRule`
- `UnusedMemberRule`
- `ClassMarkingRule`
- `UnusedClassRule`
- `PublicMethodOrderingRule`

这些规则主要消费函数索引、引用查询、继承关系和类型信息。

## Boundary / Scope

- `InvocationBoundaryPromotionRule`
  - 把 direct statement delete 提升为 method delete 候选
- `ParentBlockPiercingScopeRule`
  - 在需要时扩大 statement snapshot 的 scope
