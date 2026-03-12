# Dome v1.1 产物契约

## `analysis.json`

只在 `analyze` 模式下生成。

主要用途：

- 暴露投影后的 target 集合
- 暴露规则事实位
- 暴露三张图快照

关键字段包括：

- `Targets`
- `Edges`
- `TypeGraph`
- `FunctionGraph`
- `StatementGraph`

常见 target 字段：

- `Target.DocumentPath`
- `Target.MemberId`
- `Target.TargetKind`
- `DefinesSymbols`
- `UsesSymbols`
- `StatementKind`
- `IsHighRisk`
- `IsSanitizingAssignment`
- `IsObjectInitializerAssignment`
- `HasMarkedExpressionSeed`
- `MarkedExpressionKinds`

## `audit-plan.json`

在 `plan` 和 `run` 模式下生成。

这是可执行计划的正式契约。关键部分包括：

- `Metadata`
- `Changes`
- `Conflicts`

每个 `Change` 包含：

- `ExecutionOrder`
- `Target`
- `Action`
- `Reason`
- `Chain`

`Chain` 在 JSON 结构中固定存在：

- direct-hit 时为 `null`
- propagation 时为 `PropagationChain` 对象

兼容字段仍保留在 `Reason` 中：

- `SourceTargetKey`
- `SourceTargetDisplayText`
- `RelatedSymbolKeys`
- `RelatedSymbolNames`

这些字段描述最后一跳摘要，并与 `Chain.Hops` 保持一致。

当前 `TargetKind` 可能出现：

- `Statement`
- `Method`
- `Class`

## `report.json`

所有运行模式都会生成，包括失败场景。

主要用途：

- 汇总运行结果
- 暴露失败语义
- 暴露风险和冲突摘要
- 暴露覆盖统计
- 暴露产物清单

关键字段：

- `IsSuccess`
- `FailureCode`
- `AnalysisTargets`
- `PlannedChanges`
- `Conflicts`
- `RewrittenDocuments`
- `GeneratedArtifacts`
- `FailureSummary`
- `ConflictSummaries`
- `RiskSummary`
- `PlanCoverageSummary`
- `Message`

`PlanCoverageSummary` 当前用于说明高粒度计划覆盖低粒度计划的结果，包含：

- `CoveredMethodCount`
- `CoveredStatementCount`
- `SampleCoveredTargetDisplayTexts`

## `rewritten/**`

只在 `run` 模式且 rewrite 成功时生成。

性质：

- 相对目录结构与输入目录树保持一致
- 每个输出文件都应能被 `audit-plan.json` 完整解释
- 当前 rewrite 仍以 statement 为中心，但已支持 method/class 删除

## 冲突语义

计划冲突会同时出现在 `audit-plan.json` 和 `report.json` 中。

稳定字段包括：

- `ConflictCode`
- `Reason`
- 冲突动作集合
- 冲突 target 标识

当前版本不会自动解决未定义的动作冲突。

## 失败语义

典型失败约定：

- `PlanCompileFailed`
  仍生成 `report.json`，并解释冲突原因
- `RewriteFailed`
  仍生成 `report.json`，并解释 rewrite 失败原因
- `AnalyzeOnly`
  不生成 `audit-plan.json` 和 `rewritten/**`
