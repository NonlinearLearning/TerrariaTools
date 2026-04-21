# DDD 事件风暴：业务行为与领域对象

## 1. 目的

本文把当前仓库事件风暴中的主要业务行为和真实领域对象重新对应起来。

## 2. 当前行为到对象的映射

- 准备工作区 -> `WorkspaceContext`
- 构建分析快照 -> `AnalysisCpgSnapshot`
- 识别规则目标 -> `RuleTarget`
- 生成变更候选 -> `ChangeCandidate`
- 传播联动目标 -> `ChangeCandidate`
- 裁决候选 -> `RewriteDecision`
- 编译执行计划 -> `RewritePlan`
- 记录执行结果 -> `RewriteResult`
- 收集验证证据 -> `VerificationEvidence`
- 生成审计结论 -> `RunReport`

## 3. 当前最重要的行为归属

- 风险解释：`Domain.Decision`
- 传播边界冻结：`Domain.Propagation`
- 执行完成条件：`Domain.Execution`
- 输出完成条件：`Domain.Output`

## 4. 当前结论

当前仓库的事件风暴语言已经可以被真实聚合追踪，后续新增行为应优先落到已有对象，不优先新造 `Manager / Processor / Helper`。

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
