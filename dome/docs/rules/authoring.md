# 规则编写

本页说明你在当前仓库里添加新规则时应该遵循的最小流程。

## 第一步：先选规则接口

当前规则接口位于 `src/Core/Rules/Services/MarkingRuleContracts.cs`。

你要先回答一个问题：你的规则打算作用在哪一层？

- 处理显式指令：实现 `ISeedRule`
- 处理表达式到语句的投影：实现 `IExpressionProjectionRule`
- 控制传播是否继续：实现 `IPropagationRule`
- 保护目标：实现 `IProtectionRule`
- 处理方法：实现 `IMethodRule`
- 处理字段或属性：实现 `IMemberTargetRule`
- 处理类型：实现 `IClassRule`
- 执行边界提升：实现 `IBoundaryPromotionRule`
- 调整传播作用域：实现 `IStatementScopeRule`

不要先写代码，再倒推接口。先定规则阶段，后写实现。

## 第二步：把规则放到正确目录

当前默认规则实现位于 `src/Core/Rules/Services/`。

建议做法：

- 规则本身放在 `Core/Rules/Services`
- 规则只依赖 Core 模型和分析上下文
- 不要在规则中直接调用 Roslyn 类型
- 不要在规则中读写文件

如果你的实现需要 Roslyn 语义对象或文件系统访问，它大概率不该放在规则层。

## 第三步：注册到默认规则表

默认规则注册表在：

- `src/Core/Rules/Services/MarkingRuleRegistry.cs`

把新规则加入正确的数组，例如：

- `seedRules`
- `methodRules`
- `classRules`

如果你没有注册，新规则不会进入默认流程。

## 第四步：补测试

至少补下面两类测试中的一种：

1. 离规则最近的单元测试
2. 能证明规则进入真实流程的集成或切片测试

推荐顺序：

1. 先写规则级测试
2. 再写计划或流程级测试

## 第五步：检查输出影响

如果你的规则会改变下面任一内容，记得同步校验相关测试：

- `audit-plan.json` 的动作种类
- `report.json` 的统计字段
- 重写后源码形状
- 运行时或 shadow 报告里的摘要

## 编写规则时的约束

- 规则要输出稳定的原因文本
- 规则要尽量构造可追踪的 `RuleId`
- 规则不要在一个实现里混合多个职责
- 规则优先依赖 `AnalysisContext` 暴露的查询能力

## 什么时候不要新增规则

如果你的问题本质上是下面这些情况，先不要写规则：

- 只是产物写盘路径不对
- 只是 CLI 参数解析不对
- 只是工作区加载方式不对
- 只是 Roslyn 分析器没有把数据喂给 Core 模型

这些问题更可能属于 Application 或 Adapter，而不是 Rules。
