# Rules 规则设计与测试规范

返回 [Rules 层说明](./layers/rules.md)

## 目标

这份文档用于固定 `src/Rules` 的扩展方式，避免后续在没有规格、没有测试、没有传播语义定义的情况下直接向 `MarkingRuleEngine` 或各类 rule 接口里追加实现。

当前约束固定为：

- 先写规则规格，再写测试，再写实现
- 先定义命中行为，再讨论传播行为
- 每新增一条规则，都必须显式描述它的传播、阻断和边界提升语义
- propagation 是规则语义的一部分，不是后补细节

本规范适用于当前全部 rule family：

- `ISeedRule`
- `IExpressionProjectionRule`
- `IPropagationRule`
- `IProtectionRule`
- `IMethodRule`
- `IClassRule`
- `IBoundaryPromotionRule`
- `IStatementScopeRule`

## 工作顺序

新增规则时，必须按下面顺序推进：

1. 先写规则规格
2. 再写失败测试
3. 确认失败原因是行为缺失，而不是测试搭建错误
4. 再写最小实现
5. 最后补回归测试和文档同步

禁止的顺序：

- 先写实现，再补测试
- 先改 propagation 算法，再倒推规则语义
- 只写命中测试，不写传播和阻断测试

## 统一语义模板

每条规则在进入实现前，必须先写成统一规格。固定字段如下：

- 规则名称
- `rule family`
- 命中目标
- 直接产出
- 是否可传播
- 传播依据
- 阻断条件
- 是否可提升
- 最小测试样例

推荐模板：

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

### 字段解释

- `规则名称`
  - 使用源码中的规则类型名，例如 `DirectiveSeedRule`
- `rule family`
  - 只能填写当前正式扩展点之一
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

## 每条规则必须回答的问题

### 1. 它属于哪个 family

如果一条需求看起来同时跨多个 family，必须拆开描述，而不是做成混合规则。

### 2. 它的直接命中条件是什么

必须明确：

- 哪些输入会命中
- 哪些相似输入不应命中
- 命中后标记的是 statement、method 还是 class
- `Reason.RuleId` 和 `ReasonText` 应该是什么

### 3. 它是否产生传播源

必须明确：

- 它产出的 decision 是否可作为 propagation source
- 如果可传播，是 direct seed 还是 projection 结果
- 如果不可传播，是否完全不传播，还是只在特定条件下传播

### 4. 它如何传播

至少要写清：

- 传播发生在哪个 scope 内
- 依赖哪些关系：`uses`、`defines`、`use/def`、statement snapshot 邻接
- 传播范围是当前 block、parent block，还是更大范围
- 是否允许跨 function boundary
- 传播后生成什么 decision
- `PropagationChain` 如何记录

### 5. 它在什么情况下停止传播

必须显式描述：

- 遇到 sanitizing assignment 是否终止
- 遇到 clean redefinition 是否清除 taint
- 命中 `ProtectionRule` 是否阻断
- scope boundary 是否阻断
- 是否存在更高优先级 direct decision 覆盖传播结果

### 6. 它是否触发边界提升

如果规则可能先生成 statement-level delete，再提升到 method-level delete，必须写清：

- 触发 `BoundaryPromotionRule` 的条件
- 不应提升的条件
- 已有 method delete 时是否去重
- 是否允许消费 `dataflow-propagation` 产生的 delete

默认规则：

- 如果规格没有显式写明，默认不允许 propagated delete 进入 boundary promotion

## 测试优先原则

每条规则至少要有以下三类测试，第四类按需要补：

1. 命中测试
2. 不命中测试
3. 传播/阻断测试
4. 边界提升测试

### 命中测试

验证：

- 输入满足规则条件
- 产生正确的 `MarkDecision`
- `Target`、`Action`、`Reason.RuleId`、`SourceTargetKey` 符合预期

### 不命中测试

验证：

- 相似但不满足条件的输入不会误命中
- 不会产生多余 decision

### 传播/阻断测试

至少覆盖：

- 是否允许继续传播
- 传播到哪里停止
- sanitization 是否终止传播
- redefinition 是否清除 taint
- protected target 是否阻断
- `RuleExecutionContext.StatementScopeMode` 是否影响结果

如果 `ProtectionRule` 被定义为传播边界，必须额外补一条测试，明确验证 taint 不会穿过该 protected target 继续向后传播。

### 边界提升测试

如果规则结果可能进入 `BoundaryPromotionEngine`，必须补：

- statement delete 是否被提升到 method delete
- 哪些场景不提升
- 重复提升是否被去重

## 推荐的最小测试命名

```text
<RuleName>_MatchesExpectedTarget
<RuleName>_DoesNotMatchSimilarButInvalidTarget
<RuleName>_PropagatesAsSpecified
<RuleName>_StopsPropagationWhenBlocked
<RuleName>_PromotesBoundaryWhenApplicable
```

如果规则不传播或不提升，也要用测试显式验证“不传播”或“不提升”。

## 复杂可达性规则约束

当规则涉及“间接可达性”时，必须先在规格里写清什么算强引用或强可达性证据。当前重点关注三类场景：

- 委托 / event 订阅和回调缓存
  - 必须写明是否作为 method 强引用证据
- registry / resolver / manager / rules / options 等注册型容器
  - 必须写明是否提供 class 或 method 保护
- 框架入口
  - 必须写明是否基于已知基类、interface 或入口方法名提供保护

当前明确排除的噪音证据：

- `using (new CallTracker(...))`
- 其他 AOP 注入的资源守卫 / 埋点包装结构

## 当前推荐的设计策略

在实现新规则前，优先从直接标记规则开始，而不是先改 propagation 算法。

推荐顺序：

1. 先定义 `SeedRule` / `ExpressionProjectionRule` / `MethodRule` / `ClassRule`
2. 为这些规则写命中与传播规格测试
3. 再确认是否需要新增或调整 `PropagationRule`

原因：

- direct decision 的职责边界更清晰
- propagation 规则耦合面更大
- 先固定 direct decision 语义，后续更容易判断 propagation 是否合理

## 落地要求

后续在 `Rules` 层新增规则时，提交内容至少应包含：

- 规则规格说明
- 对应的失败测试
- 传播与阻断语义说明
- 最小实现

如果缺少传播说明或传播/阻断测试，该规则不应进入默认 registry。
