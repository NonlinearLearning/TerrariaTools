# src 目录分层说明

## 1. 当前主目录

当前 `src` 主线目录只有四层：

- `Application`
- `Domain`
- `Infrastructure`
- `Logic`

## 2. 当前职责

### Domain

- 稳定业务语义
- 不变量
- 聚合
- 值对象
- 领域事件
- 稳定接口

### Logic

- 单阶段复用能力
- 工作流阶段组合
- Analysis.Engine 主要实现

### Application

- AppService 对外入口
- request / response / mapper
- 用例编排

### Infrastructure

- 技术实现
- 网关实现
- Roslyn / 持久化 / Analysis 接入

## 3. 当前重要目录事实

- `Analysis.Engine` 当前已经进入 `Domain + Logic` 双层表达
- `RewriteWorkflow` 当前稳定驻留在 `Logic.Workflow`
- `Application.Contracts` 与 `Application.Mappers` 负责边界契约与转换
- `RewriteWorkflowAppService` 当前停留在用例编排入口，传播、决策、计划、执行、证据、报告、事件主链已下沉到 `Logic.Workflow`
- `RewriteWorkflowArtifactAssembler` 当前作为单一工作流装配入口，内部只委派 `Plan / Execution / Evidence / Report / Event` 五段能力
- `RewriteWorkflowAssemblyInput` 当前只负责五类 `To*StageInput()` 映射，事件记录职责留在 `RewriteWorkflowEventStage`
- `WorkflowEventSequenceBuilder` 与 `IDomainEventRecorder` 当前负责工作流主链事件补齐、去重记录与按关联标识查询
- 上述边界已由 `tests/ArchitectureTests/Program.cs` 持续守护

## 4. 当前结论

任何文档仍把当前主目录写成别的结构，都应视为过时事实。

当前更具体的分层判断：

1. `Application` 继续保持薄壳。
2. `Logic.Workflow` 已经形成稳定阶段链，而不是新的大编排层。
3. `Domain` 持续承载决策、计划、结果、证据、报告等核心语义。
4. 稳定目录边界已经开始由 ArchitectureTests 固化，而不是只靠口头约定。

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
