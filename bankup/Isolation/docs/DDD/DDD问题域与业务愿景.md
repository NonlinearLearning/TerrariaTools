# DDD 问题域与业务愿景

## 1. 当前业务愿景

当前仓库的业务目标已经可以用一句话概括：

**围绕规则驱动的代码隔离与重写工作流，形成“分析 -> 标记 -> 传播 -> 决策 -> 执行 -> 证据 -> 审计”的闭环。**

## 2. 当前问题域

仓库当前解决的是同一个大问题域下的多阶段协作：

- 从工作区输入中识别分析目标
- 构建最小分析事实
- 标记候选与传播影响
- 做风险解释和重写决策
- 编译计划并执行重写
- 收集证据并形成审计报告

## 3. 当前成功标准

1. 规则语义稳定
2. 分层边界清晰
3. 工作流阶段可追踪
4. 执行结果可验证
5. 审计输出可信
6. 主链事件可回放、可去重、可审计

## 4. 当前结论

这个仓库当前的愿景已经从“做一套分析工具”收敛为“做一套可验证的规则驱动重写闭环系统”。

## 5. 当前愿景的实现锚点

当前业务愿景已经对应到真实实现：

- `src/Application/Services/RewriteWorkflowAppService.cs`
- `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs`
- `src/Logic/Workflow/RewriteWorkflow*Stage.cs`
- `src/Logic/Workflow/Events/WorkflowEventSequenceBuilder.cs`
- `src/Domain/Decision/RewriteDecision.cs`
- `src/Domain/Execution/RewritePlan.cs`
- `src/Domain/Execution/RewriteResult.cs`
- `src/Domain/Output/Verification/VerificationEvidence.cs`
- `src/Domain/Output/Audit/RunReport.cs`
- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/Workflow/**`

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
