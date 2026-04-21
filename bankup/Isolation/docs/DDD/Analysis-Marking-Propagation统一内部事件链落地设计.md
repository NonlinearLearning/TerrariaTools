# Analysis-Marking-Propagation 统一内部事件链落地设计

## 1. 目的

本文描述当前 `Analysis -> Marking -> Propagation` 主链的真实落地方式。

## 2. 当前代码事实

### Analysis 阶段

- `AnalysisDomainEventPublisher`
- `AnalysisEventSequenceBuilder`
- 事件来源优先使用 `AnalysisCpgSnapshot` 原生事件

### Marking 阶段

- `MarkingDomainEventPublisher`
- `MarkingEventSequenceBuilder`
- 事件来源优先使用 `RuleTarget` 和 `ChangeCandidate` 原生事件

### Propagation 阶段

- `PropagationDomainEventPublisher`
- `PropagationEventSequenceBuilder`
- 事件来源优先使用 `ChangeCandidate` 原生事件

## 3. 当前协作模式

1. 各阶段先产生各自聚合/快照的原生事件
2. 阶段发布器再将其包装进统一发布输入
3. Workflow 层最后聚合阶段事件序列
4. `RewriteWorkflowEventStage` 负责触发 `WorkflowEventSequenceBuilder` 与 `IDomainEventRecorder` 完成收尾

## 4. 当前结论

- 当前已经做到事件链统一
- 当前统一的核心方式是“原生事件优先 + builder 补上下文”
- 下一步应继续补守护测试，而不是再做一层事件抽象

## 5. 当前落地锚点

- `src/Logic/Analysis/Events/AnalysisEventSequenceBuilder.cs`
- `src/Logic/Marking/Events/MarkingEventSequenceBuilder.cs`
- `src/Logic/Propagation/Events/PropagationEventSequenceBuilder.cs`
- `src/Logic/Workflow/RewriteWorkflowEventStage.cs`
- `src/Logic/Workflow/Events/WorkflowEventSequenceBuilder.cs`
- `src/Logic/Workflow/Events/IDomainEventRecorder.cs`
- `src/Logic/Workflow/Events/InMemoryDomainEventRecorder.cs`
- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/Workflow/*EventsTests.cs`

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
