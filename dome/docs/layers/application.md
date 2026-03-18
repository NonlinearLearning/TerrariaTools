# Application 层

`src/Application` 是主流程编排层。

## 职责

- 组装标准依赖图
- 调度 workspace、analysis、rules、planning、rewrite、reporting
- 统一失败语义与 artifact 策略

## 关键入口

- `DomeApplicationFactory.CreateDefault()`
- `DomeApplication.RunAsync(...)`
- `DomeApplicationStages.cs`

## 当前阶段

- `WorkspaceLoadStage`
- `AnalysisStage`
- `MarkDecisionsStage`
- `CompilePlanStage`
- `RewriteStage`
- finalize stages

## 边界

Application 决定“何时调用哪层”，但不实现 analysis、rule 或 rewrite 算法。
