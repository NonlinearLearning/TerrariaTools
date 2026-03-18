# Planning 阶段

Planning 现在不是独立项目，而是一个阶段和模型集合。

## 组成

- 模型：`src/Model/Planning`
- 编译器：`src/Model/Rules/AuditPlanCompiler.cs`
- 应用阶段：`src/Application/DomeApplicationStages.cs` 中的 `CompilePlanStage`

## 职责

- 消费 `MarkDecision[]`
- 归一化删除决策
- 检测冲突
- 生成稳定顺序的 `AuditPlan`

## 输出

- `PlanCompilationResult`
- `AuditPlan`
- `PlanConflict[]`

## 边界

Planning 不负责：

- 重新做语义分析
- 重新跑规则
- 直接改写源码
- 直接写 JSON 文件
