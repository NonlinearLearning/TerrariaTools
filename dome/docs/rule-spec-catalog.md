# Rules 规则规格模板

返回 [Rules 层说明](./layers/rules.md)

这份文档只维护规则规格的格式和填写约束，不再承载逐条已实现规则的详细内容。

已实现规则的详细规格说明，统一维护在对应规则实现的源码注释中。当前主入口位于：

- [MarkingRuleEngine.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\MarkingRuleEngine.cs)
- [StatementPropagationEngine.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\StatementPropagationEngine.cs)
- [BoundaryPromotionEngine.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\BoundaryPromotionEngine.cs)

## 使用规则

- 已实现规则：
  - 详细语义维护在源码注释里
  - 文档层只保留模板和填写要求
- 候选规则：
  - 在尚未实现前，可先按本模板登记到设计文档或任务计划
  - 一旦实现，应把详细规格迁到源码注释中

## 统一模板

每条规则都必须按下面格式描述：

```md
### <RuleName>

- rule family:
- 命中目标:
- 直接产出:
- 是否可传播:
- 传播依据:
- 阻断条件:
- 是否可提升:
- 最小测试样例:
```

## 字段要求

- `rule family`
  - 必须是当前正式扩展点之一：
    - `ISeedRule`
    - `IExpressionProjectionRule`
    - `IPropagationRule`
    - `IProtectionRule`
    - `IMethodRule`
    - `IClassRule`
    - `IBoundaryPromotionRule`
    - `IStatementScopeRule`
- `命中目标`
  - 写清 target kind、命中前提、相邻但不命中的边界条件
- `直接产出`
  - 写清 `MarkDecision` 的 action、reason、target level
- `是否可传播`
  - 必须显式写 `是`、`否` 或 `不适用`
- `传播依据`
  - 只允许填写明确机制，例如 `use/def`、statement snapshot、scope rule、reference query
- `阻断条件`
  - 必须列出 protection、sanitization、redefinition、scope boundary 等终止条件
- `是否可提升`
  - 必须写明是否允许进入 boundary promotion
- `最小测试样例`
  - 必须列出最少需要的测试名集合

## 维护约束

- 文档不再复制已实现规则的详细规格
- 当规则实现发生语义变化时，先改源码注释，再补测试
- 当新增规则尚未实现时，可以临时使用本模板写在计划文档里
- 一旦规则进入默认 registry，详细规格必须迁入源码注释
