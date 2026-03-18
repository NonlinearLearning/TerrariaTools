# Dome 架构总览

`dome` 是一个面向 C# 源码的静态分析、规则决策、计划编译与批量改写工具。

## 1. 当前结构

当前主链路是：

`CLI -> Application -> Analysis -> Rules -> Planning -> Rewrite -> Reporting`

这里的 `Planning` 指：

- 计划模型：`src/Model/Planning`
- 计划编译器：`src/Model/Rules/AuditPlanCompiler.cs`
- 应用阶段：`src/Application/DomeApplicationStages.cs` 中的 `CompilePlanStage`

它不再是独立 `src/Plan` 项目。

## 2. 顶层目录

| 目录 | 作用 |
| --- | --- |
| `src/Cli` | 命令行解析与进程入口 |
| `src/Application` | 主流程编排、报告与 artifact 策略 |
| `src/Application/Abstractions` | run、workspace、analysis、rewrite 等正式合同 |
| `src/Analysis/Roslyn` | workspace 加载、Roslyn 分析与查询服务 |
| `src/Rules` | 基于分析结果生成 `MarkDecision[]` |
| `src/Model/*` | analysis、planning、rules、primitives 的共享模型 |
| `src/Rewrite/Roslyn` | 根据计划改写源码 |
| `src/Reporting` | 将正式模型写成 JSON artifacts |
| `src/Application/Legacy` | runtime/shadow 兼容孤岛 |
| `src/Analysis/Legacy` | analysis 兼容孤岛 |

## 3. 共享契约

共享契约现在分为两层：

- `Application.Abstractions`
  - `RunRequest`
  - `RunResult`
  - `WorkspaceLoadResult`
  - `AnalysisEngineResult`
  - `IWorkspaceLoader`
  - `IAnalysisEngine`
  - `IRewriteExecutor`
- `Model.*`
  - `AnalysisResultModel`
  - `AnalysisExecutionSnapshot`
  - `AnalysisServices`
  - `MarkDecision`
  - `AuditPlan`
  - `PlanCompilationResult`
  - `RunReport`

## 4. 依赖方向

当前代码库遵循以下方向：

- `Cli` 只依赖 `Application` 与 `Application.Abstractions`
- `Application` 负责调度，不承载分析、规则和 rewrite 算法
- `Analysis`、`Rules`、`Rewrite`、`Reporting` 共享 `Application.Abstractions + Model.*`
- `Legacy` 项目不允许反向污染标准路径

## 5. 设计重点

当前架构重点不是做最大化抽象，而是稳定以下事实：

- 输入统一进入 `RunRequest`
- workspace 加载统一进入 `WorkspaceLoadResult`
- 分析统一进入 `AnalysisEngineResult`
- 规则统一输出 `MarkDecision[]`
- 计划统一输出 `PlanCompilationResult`
- 重写统一消费 `AuditPlan`
- artifact 统一从 `RunReport`、`AnalysisResultModel`、`AuditPlan` 序列化

## 6. 标准路径与兼容路径

标准路径：

- `src/Cli`
- `src/Application`
- `src/Analysis/Roslyn`
- `src/Rules`
- `src/Model/*`
- `src/Rewrite/Roslyn`
- `src/Reporting`

兼容路径：

- `src/Application/Legacy`
- `src/Analysis/Legacy`

兼容路径的职责是隔离 runtime/shadow 和旧行为覆盖，不是重新定义标准主链路。
