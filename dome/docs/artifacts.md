# Dome 产物说明

`dome` 运行后会输出三类正式产物：

- `analysis.json`
- `audit-plan.json`
- `report.json`

标准模式还会输出：

- `rewritten/**`

## 1. 产物总表

| 产物 | 模式 | 来源 |
| --- | --- | --- |
| `analysis.json` | `AnalyzeOnly` | `AnalysisResultModel` |
| `audit-plan.json` | `PlanOnly`、`Standard` | `AuditPlan` |
| `report.json` | 所有模式 | `RunReport` |
| `rewritten/**` | `Standard` | `AuditPlan` + 原始源码 |

## 2. `analysis.json`

主要用于表达“工具看到了什么”。

核心字段：

- `Targets`
- `Edges`
- `TypeGraph`
- `FunctionGraph`
- `StatementGraph`
- `FunctionGraphMaterialization`
- `StatementGraphMaterialization`

注意：

- 默认函数图不是全量物化基线
- statement 图当前以 snapshot/facts 为主

## 3. `audit-plan.json`

主要用于表达“工具准备怎么改”。

核心字段：

- `Metadata`
- `Changes`
- `Conflicts`

其中：

- `Changes` 是最终稳定顺序的 `PlannedChange[]`
- `Conflicts` 表示未被自动裁决的计划冲突

## 4. `report.json`

主要用于表达“这次运行整体是否成功，以及停在了哪里”。

核心字段：

- `IsSuccess`
- `FailureCode`
- `AnalysisTargets`
- `PlannedChanges`
- `Conflicts`
- `RewrittenDocuments`
- `GeneratedArtifacts`
- `RiskSummary`
- `PlanCoverageSummary`
- `FunctionImpactSummary`
- `WorkspaceLoadMode`
- `WorkspaceFallbackUsed`
- `WorkspaceDiagnostics`
- `Message`

## 5. `rewritten/**`

只在 `RunMode.Standard` 生成。

特点：

- 不直接覆写输入源码
- 保留相对路径
- 是按照文档级计划重写后的副本

## 6. 推荐阅读顺序

- 想看分析视角：`analysis.json`
- 想看计划视角：`audit-plan.json`
- 想看结果摘要：`report.json`
- 想看最终源码：`rewritten/**`
