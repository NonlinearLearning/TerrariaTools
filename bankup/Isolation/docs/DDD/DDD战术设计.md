# DDD 战术设计

## 1. 目的

本文按当前代码事实总结仓库已经稳定采用的战术模式。

## 2. 当前主要战术模式

### 聚合根

- `WorkspaceContext`
- `RuleTarget`
- `ChangeCandidate`
- `RewriteDecision`
- `RewritePlan`
- `RewriteResult`
- `VerificationEvidence`
- `RunReport`

### 值对象

- `DocumentPath`
- `TargetName`
- `MemberSignature`
- `RuleCode`
- `ReferenceName`
- `ReferenceVersion`
- `ProjectDescriptor`
- `ReferenceDescriptor`
- `InputDescriptor`

### 领域事件

各阶段都已存在原生领域事件，详见 `src/Domain/**/Events/*.cs`。

### Domain Policy

- `RewriteDecisionAssessmentPolicy`
- `RewriteDecisionResolutionPolicy`

### Workflow 组装与阶段模式

- `RewriteWorkflowArtifactAssembler`：单一装配入口
- `RewriteWorkflowPropagationStage`
- `RewriteWorkflowDecisionStage`
- `RewriteWorkflowPlanStage`
- `RewriteWorkflowExecutionStage`
- `RewriteWorkflowEvidenceStage`
- `RewriteWorkflowReportStage`
- `RewriteWorkflowEventStage`

### 收尾与回传模式

- `WorkflowEventSequenceBuilder`：事件补齐
- `IDomainEventRecorder / InMemoryDomainEventRecorder`：事件记录与查询
- `RewriteWorkflowStageResults.ToArtifacts(...)`：统一工作流产物回传

## 3. 当前战术结论

1. 当前仓库已经不是贫血 DTO + service 拼装模式。
2. 当前最成功的模式是“Domain Policy 解释 + Aggregate 施加结果”。
3. 继续做战术设计时，应先复用现有聚合和策略模式。
4. `RewriteWorkflow` 当前采用的是“Application 编排 + Logic 阶段组合 + Domain 聚合承载 + Workflow.Events 收尾”的混合战术模式。
5. 当前这些战术模式已经开始被 `tests/ArchitectureTests/Program.cs` 固化为 fitness functions。

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：DDD / 领域设计文档。

### Prompt Spec 对齐要求

- 维护本文时，显式加载 `ai-rules/common/prompt-spec-writing.mdc`。
- 本文中的目标、边界、主流程、默认值、允许 / 禁止事项、边缘场景、交付物、验收标准，统一按 prompt spec 规格语言书写。
- 本文里的关键结论必须绑定真实代码路径、真实类型名、真实测试路径；禁止保留空泛愿景式表述。
- 本文如果描述 `RewriteWorkflow`、规则传播、决策、计划、执行、证据、报告，优先绑定：
  - `src/Application/Services/RewriteWorkflowAppService.cs`
  - `src/Application/Services/WorkspaceContextAppService.cs`
  - `src/Logic/Workflow/*.cs`
  - `src/Domain/Decision/*.cs`
  - `src/Domain/Execution/*.cs`
  - `src/Domain/Output/**/*.cs`
  - `tests/ArchitectureTests/Program.cs`
  - `tests/Isolation.AnalysisTests/**`
- 本文更新后，默认同步检查 `ai-rules/`、`.codex/skills/`、`.agents/skills/` 是否仍与本文口径一致。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `src/Domain/**`
- `src/Logic/**`
- `src/Application/**`
- `tests/Isolation.AnalysisTests/**`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
