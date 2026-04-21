# DDD 战略设计

## 1. 目的

本文按当前实现重写战略设计视角，不再讨论理想化大图，只讨论当前仓库已经落地的边界。

## 2. 当前战略边界

当前仓库采用一个主域下的多上下文协作结构：

- `Workspaces`
- `Analysis`
- `Marking`
- `Propagation`
- `Decision`
- `Execution`
- `Rewrite`
- `Output`
- `Rules`

## 3. 当前战略原则

1. 四层依赖方向高于局部纯度
2. Domain 负责语义与不变量
3. Logic 负责复用阶段能力
4. Application 负责用例编排
5. Infrastructure 负责技术兑现
6. 稳定边界优先写入 `ArchitectureTests`

## 4. 当前战略结论

- 当前不需要新增更多项目来表达战略设计
- 当前更值钱的是把已有边界继续自动化守护
- 当前文档、代码、测试应统一使用这一套上下文词汇

## 5. 当前战略落地事实

当前战略设计已经落到以下稳定事实：

- `RewriteWorkflowAppService` 保持薄壳用例编排
- `Logic.Workflow` 已拆成传播、决策、计划、执行、证据、报告、事件七段主链
- `RewriteWorkflowArtifactAssembler` 保持单一装配入口
- `Workflow.Events` 已形成主链事件补齐与记录收尾机制
- `tests/ArchitectureTests/Program.cs` 已开始承担 workflow 主链 fitness functions

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
