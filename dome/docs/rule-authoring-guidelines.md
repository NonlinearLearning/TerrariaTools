# Rule Authoring Guidelines

本文档描述当前默认规则体系下，新增或修改规则时应遵守的约束。

## 1. 先写语义，再写实现

新增规则时，先明确三件事：

- 输入依赖什么事实
- 输出哪类 `MarkDecision`
- 是否参与 propagation、protection、promotion 或 scope 选择

不要先写条件分支，再倒推规则名义。

## 2. 规则必须落在现有分类中

默认分类：

- `ISeedRule`
- `IExpressionProjectionRule`
- `IPropagationRule`
- `IProtectionRule`
- `IMethodRule`
- `IMemberTargetRule`
- `IClassRule`
- `IBoundaryPromotionRule`
- `IStatementScopeRule`

如果一个规则横跨多类职责，应先拆职责，再决定落点。

## 3. 规则输出边界

规则层只输出 `MarkDecision[]`。

规则层不负责：

- 生成 `AuditPlan`
- 决定最终执行顺序
- 直接改写源码
- 直接写 artifact

## 4. 规则测试原则

- direct hit、projection、propagation、protection、promotion 分开测
- 断言 target、action、reason、chain，而不只断言数量
- 对高风险或边界行为给出反例测试

## 5. 当前默认规则集

当前默认 registry 包括：

- `DirectiveSeedRule`
- `ExpressionProjectionRule`
- `SanitizationPropagationRule`
- `HighRiskProtectionRule`
- `ObjectInitializerProtectionRule`
- `FunctionMarkingRule`
- `PublicMethodPrivatizationRule`
- `UnusedMethodRule`
- `UnusedMemberRule`
- `ClassMarkingRule`
- `UnusedClassRule`
- `PublicMethodOrderingRule`
- `InvocationBoundaryPromotionRule`
- `ParentBlockPiercingScopeRule`
