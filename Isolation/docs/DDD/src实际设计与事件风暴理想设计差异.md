# src 实际设计与事件风暴理想设计差异

## 1. 目的

本文说明当前实际实现与理想事件风暴图之间的真实差异。

## 2. 当前主要差异

### 差异 1：实际实现更强调四层依赖方向

理想图更偏业务流，当前实现先守：

`Domain <- Logic <- Application <- Infrastructure`

### 差异 2：Analysis.Engine 被拆成对外语义层和实现层

理想图里 Analysis 常被看成一个整体，当前代码把：

- `Domain.Analysis` / `Domain.Analysis.Engine`
- `Logic.Analysis.Engine`

分开表达。

### 差异 3：Workflow 被阶段化收敛，而不是一个巨型流程类

当前已经拆成多个稳定 stage，而不是单个 mega workflow。

当前这条主链已经具备更细的工程化边界：

- `RewriteWorkflowAppService` 负责请求入口与顺序编排
- `RewriteWorkflowArtifactAssembler` 负责单一装配入口
- `RewriteWorkflowAssemblyInput` 负责五类阶段输入映射
- `RewriteWorkflowEventStage` 负责事件装配与回传收尾
- `WorkflowEventSequenceBuilder + IDomainEventRecorder` 负责事件补齐与去重记录

### 差异 4：输出阶段具备明确生命周期约束

理想风暴图通常只写“收集证据 / 生成报告”，当前代码已经把 collect / finalize / recalculate 的限制落到了聚合内。

### 差异 5：理想图强调事件流，当前实现强调“聚合原生事件 + Workflow 收尾补齐”

当前代码已经形成：

- 聚合优先发原生事件
- `Logic.*.Events.*Publisher` 负责阶段发布序列
- `WorkflowEventSequenceBuilder` 负责主链缺失事件补齐
- `ArchitectureTests` 负责持续守住这条边界

## 3. 当前结论

当前实现已经比早期理想图更工程化、更可验证。后续文档应优先以当前代码为准，再回推理想图，而不是反过来。

当前最值得记住的事实：

1. 理想事件风暴图已经演进成四层 + 阶段链 + 事件收尾链的实现形态。
2. 当前代码的主风险不是“缺少概念”，而是“历史文档继续沿用旧图”。
3. 当前最值钱的动作是继续让文档、ArchitectureTests、AnalysisTests 三者对齐。

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
