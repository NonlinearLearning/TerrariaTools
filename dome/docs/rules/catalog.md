# 规则目录

本页列出当前默认注册表中的规则。来源是 `src/Core/Rules/Services/MarkingRuleRegistry.cs`。

## 种子规则

### `DirectiveSeedRule`

职责：把显式指令转换为初始决策。

## 表达式投影规则

### `ExpressionProjectionRule`

职责：把表达式级标记投影为语句级决策。

## 传播规则

### `SanitizationPropagationRule`

职责：遇到净化赋值语句时阻止传播继续扩散。

## 保护规则

### `HighRiskProtectionRule`

职责：保护高风险目标。

### `ObjectInitializerProtectionRule`

职责：保护对象初始化赋值目标。

## 方法规则

### `FunctionMarkingRule`

职责：

- 标记无引用的私有方法
- 为空体且非 `void` 的方法生成默认返回动作

### `PublicMethodPrivatizationRule`

职责：把只有内部引用的公共方法降为私有方法。

### `UnusedMethodRule`

职责：删除没有内部和外部引用的私有方法。

## 成员规则

### `UnusedMemberRule`

职责：删除没有引用的私有字段和属性。

## 类型规则

### `ClassMarkingRule`

职责：删除没有引用且不受继承保护的类型。

### `UnusedClassRule`

职责：删除清理分析中确认无引用的私有类型。

### `PublicMethodOrderingRule`

职责：为类型生成公共方法重排决策。

## 边界提升规则

### `InvocationBoundaryPromotionRule`

职责：把满足条件的语句删除决策提升为方法删除候选。

## 作用域规则

### `ParentBlockPiercingScopeRule`

职责：在最小块作用域不足时，把传播范围扩大到父级块。

## 阅读建议

如果你要理解一条规则在流程中的位置，按下面顺序读：

1. [规则概念](./concepts.md)
2. 对应规则实现文件
3. [执行流程](../architecture/flows.md)
4. 相关测试
