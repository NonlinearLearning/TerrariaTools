# Reporting 层

`src/Reporting` 负责将正式模型写成 JSON artifacts。

## 职责

- 写 `analysis.json`
- 写 `audit-plan.json`
- 写 `report.json`

## 关键入口

- `JsonArtifactWriter.WriteAnalysisAsync`
- `JsonArtifactWriter.WritePlanAsync`
- `JsonArtifactWriter.WriteReportAsync`

## 边界

Reporting 不负责：

- 语义分析
- 规则执行
- 计划编译
- 代码重写

它只消费上游已经准备好的正式模型对象。
