# 领域模型设计 Checklist

## 1. 目的

本文按 2026-04 当前代码事实，给出 `Isolation` 的领域模型设计检查清单。

它服务于三类场景：

- 新增领域类型前
- 调整现有聚合/值对象前
- 审查 `Domain / Logic / Application` 落位前

## 2. 当前仓库级事实

当前领域主对象已经稳定落到以下目录：

- `Domain.Workspaces`
- `Domain.Analysis`
- `Domain.Decision`
- `Domain.Execution`
- `Domain.Marking`
- `Domain.Propagation`
- `Domain.Output`
- `Domain.Rewrite`
- `Domain.Rules`

当前高价值聚合包括：

- `WorkspaceContext`
- `RuleTarget`
- `ChangeCandidate`
- `RewriteDecision`
- `RewritePlan`
- `RewriteResult`
- `VerificationEvidence`
- `RunReport`

## 3. 设计前检查

### 3.1 这个对象是否真的属于 Domain

- 去掉 DTO、数据库、文件系统、Roslyn 后，它是否仍然成立
- 它是否表达业务事实、业务裁决、业务状态或业务不变量
- 它是否会被多个用例共享语义

### 3.2 它应该是值对象还是实体

优先值对象的情况：

- 当前仓库里的 `DocumentPath`
- `TargetName`
- `MemberSignature`
- `RuleCode`
- `ReferenceName`
- `ReferenceVersion`

优先实体/聚合的情况：

- 需要跨时间保持身份
- 需要集中维护状态迁移
- 需要自然发出领域事件

### 3.3 不变量是否说得清

如果定义聚合，至少要回答：

- 哪些状态允许进入
- 哪些状态禁止进入
- 哪些修改必须通过聚合根
- 哪些事件由聚合自然发出

## 4. 当前仓库里的已验证模式

### 4.1 决策模式

`RewriteDecision` 当前模式：

- Logic 负责准备事实
- Domain Policy 负责解释事实
- Aggregate 负责施加结果与维护状态迁移

### 4.2 执行模式

`RewritePlan / RewriteResult` 当前模式：

- 计划编译前先校验计划项与排序
- 执行完成前先校验最小观察结果

### 4.3 输出模式

`VerificationEvidence / RunReport` 当前模式：

- 零证据不能 collect
- 未挂接 evidence 不能 finalize report
- finalize 后不能继续重算

### 4.4 传播模式

`ChangeCandidate` 当前模式：

- 切片边界必须显式方向 + 正深度
- 传播轨迹步序必须从 1 开始
- 被父动作覆盖后，传播边界冻结

## 5. 设计后检查

- 是否已有对应测试
- 是否需要补 `ArchitectureTests`
- 是否需要同步更新 `docs/约束/*.md` 和 `docs/DDD/*.md`

## 6. 当前建议

当前仓库做领域建模时，优先沿用已经跑通的八个核心聚合/值对象模式，不重新发明抽象层。

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
