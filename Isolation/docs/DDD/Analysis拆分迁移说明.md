# Analysis 拆分迁移说明

## 1. 目的

本文说明 `Analysis` 当前已经完成的拆分迁移事实。

## 2. 当前迁移结果

历史上独立的 `src/Analysis` 主实现已经退场，当前事实是：

- `Domain.Analysis` 保留对外稳定事实、接口、快照语言
- `Domain.Analysis.Engine` 保留稳定引擎语义模型
- `Logic.Analysis.Engine` 承担引擎实现、前端、语言层、pass、query、semantic、slicing、x2cpg 组合逻辑
- `Infrastructure.Analysis` 承担网关实现与技术接入

## 3. 当前代码锚点

- `src/Domain/Analysis/**`
- `src/Domain/Analysis/Engine/**`
- `src/Logic/Analysis/**`
- `src/Logic/Analysis/Engine/**`
- `src/Infrastructure/Analysis/**`

## 4. 当前结论

1. `Analysis` 现在已经是四层主线的一部分。
2. 后续文档和术语应以 `Analysis + Analysis.Engine` 的双层表达为准。
3. 任何文档仍把旧 `src/Analysis` 写成当前主实现区，都应视为过时描述。

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
