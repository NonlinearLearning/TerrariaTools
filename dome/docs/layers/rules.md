# Rules 层

`src/Rules` 负责把分析结果转换成可编译的规则决策。

## 职责

- 生成 seed decisions
- 做 expression projection
- 做 propagation
- 做 protection
- 做 method/member/class 规则判定
- 做 boundary promotion

## 关键入口

- `MarkingRuleRegistry.CreateDefault()`
- `MarkingRuleEngine.Execute(...)`

## 输出

- `IReadOnlyList<MarkDecision>`

Rules 层不直接生成 `AuditPlan`，计划编译在后续 planning 阶段完成。
