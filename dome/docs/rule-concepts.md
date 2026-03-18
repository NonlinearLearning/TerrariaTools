# Rules 术语

本文档只解释当前代码里仍然是正式概念的术语。

## 1. statement target

指 `AnalysisTarget` 中 `TargetKind == Statement` 的目标。

它不是一段文本，而是带有：

- `TargetIdentity`
- `TargetLocator`
- symbol use/define
- invoked members
- statement kind
- risk / protection 相关事实

## 2. direct decision

指规则直接产出的 `MarkDecision`，不是传播推导结果。

常见来源：

- directive seed
- expression projection
- method/class/member 规则

## 3. propagation

指已有 decision 沿 statement facts 和 symbol 依赖传播，产生新的 decision。

传播结果通常带：

- `Reason.RuleId = "dataflow-propagation"`
- 非空 `Chain`

## 4. protection

指某个 target 不仅自身不命中，还构成传播边界。

当前典型例子：

- high-risk target
- object initializer assignment

## 5. boundary promotion

指 direct statement delete 被提升成 method-level delete 候选。

promotion 只消费 direct delete，不消费 propagation 结果。

## 6. statement scope

指 statement snapshot 的构建范围。

当前主要模式：

- `MinimalBlock`
- `ParentBlockPiercing`

## 7. planning

指把 `MarkDecision[]` 编译成 `AuditPlan` 的阶段和模型集合。

它不再表示独立项目，只表示：

- `Model.Planning`
- `AuditPlanCompiler`
- `CompilePlanStage`
