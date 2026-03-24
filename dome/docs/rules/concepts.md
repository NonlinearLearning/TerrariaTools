# 规则概念

本页只描述当前实现里的规则概念，不讨论假设中的未来规则系统。

## 决策从哪里来

当前规则引擎会把决策来源分成几类：

- 种子决策
- 表达式投影决策
- 传播决策
- 边界提升决策
- 清理决策

这些来源最终都会汇总成 `MarkDecision`，再进入计划编译阶段。

## 核心阶段

### 1. 种子

种子来自显式指令。当前默认种子规则是 `DirectiveSeedRule`。

它的职责是：

- 读取目标上的指令
- 将指令映射为初始动作
- 为控制流目标生成统一规则标识

### 2. 表达式投影

表达式投影把表达式级标记提升到语句级目标。当前默认规则是 `ExpressionProjectionRule`。

它适合处理这样的情况：

- 标记落在表达式上
- 但真正可执行的动作要落在语句目标上

### 3. 传播

传播阶段沿着语句图里的 use/def 关系扩散决策。当前由 `StatementPropagationEngine` 驱动，默认传播规则是 `SanitizationPropagationRule`。

传播的结果会带上：

- 来源目标
- 传播证据
- 传播链

### 4. 边界提升

有些语句级删除意图最后会提升为方法级删除候选。当前由 `BoundaryPromotionEngine` 驱动，默认规则是 `InvocationBoundaryPromotionRule`。

这是“语句动作跨出语句边界”的处理机制。

### 5. 清理

清理规则主要处理成员和类型级优化，例如：

- 删除无引用方法
- 删除无引用字段或属性
- 删除无引用类型
- 收缩公共方法可见性
- 重排公共方法顺序

## 保护规则

保护规则会阻止某些目标继续参与处理。当前默认保护规则有：

- `HighRiskProtectionRule`
- `ObjectInitializerProtectionRule`

如果目标被保护，后续传播和部分规则会直接跳过它。

## 作用域规则

传播默认使用最小块作用域。当前还存在一个作用域选择规则 `ParentBlockPiercingScopeRule`，它会在发现跨块依赖时把传播范围扩大到父级块。

## 最终产物

规则阶段输出 `DecisionSet`：

- `InitialDecisions`
- `PredictedDecisions`

计划编译阶段会把它们合并、去重、排序，并生成 `audit-plan.json`。
