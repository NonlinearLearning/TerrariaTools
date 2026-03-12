# 测试用例迁移跟踪表 (Test Migration Progress)

本文档旨在指导 AI 助手完成 `TerrariaTools` 项目测试用例向新 `RewritingPipeline` 架构的迁移工作。请严格遵循以下任务分解和优先级执行。

## 1. 任务分解与优先级 (Task Breakdown & Prioritization)

### 🔴 Phase 1: 核心表达式重写修复 (High Priority)
**目标**: 确保基础表达式（字面量、参数、返回值）在 `SemanticIdentifierLayer` 和 `SyntaxTransformerLayer` 中被正确处理。
**当前状态**: `ExpressionPipelineTests` 中仍有多个失败用例，主要涉及参数替换和返回值处理。

- [x] **修复 Return 语句重写逻辑**
  - 任务: 解决 `Pipeline_ShouldReplaceReturnReferenceTypeWithEmptyString` 失败问题。
  - 状态: ✅ 已完成 (2026-03-04)
  - 方案: 修正 `Propagators.cs` 防止自动标记 `ReturnStatement`，由 `SemanticIdentifierLayer` 决定是否仅替换表达式。

- [ ] **修复参数替换逻辑**
  - 任务: 解决 `Pipeline_ShouldReplaceStringArgumentWithEmptyString` 和 `Pipeline_ShouldReplaceIntArgumentWithZero` 失败问题。
  - 描述: 参数列表中的表达式被移除后，应根据类型生成正确的占位符（`string.Empty`, `0` 等）。
  - 指导: 检查 `DecideArgumentAction` 和 `PlaceholderFactory` 的集成。

- [ ] **完善条件表达式合并 (MergeLeft/MergeRight)**
  - 任务: 确保 `SyntaxTransformerLayer` 正确处理 `ConditionalExpression` 的分支移除。
  - 描述: 当 `true` 或 `false` 分支被移除时，应将表达式简化为保留的分支。

### 🟡 Phase 2: 传播逻辑验证 (Medium Priority)
**目标**: 验证标记传播（Propagation）的准确性，防止过度标记或漏标。

- [ ] **验证 UpwardMarkCollector**
  - 任务: 确保 `UpwardMarkCollectorTests` 覆盖所有父节点传播场景（如 `AssignmentExpression`, `LocalDeclarationStatement`）。
  - 指导: 检查是否还有类似 `ReturnStatement` 的过度传播问题。

- [ ] **验证 LifecycleManagedPropagator**
  - 任务: 解决 `LifecycleManagedPropagatorTests` 中的潜在失败。
  - 描述: 确保变量声明周期的标记能够正确传播到所有引用点。

### 🟢 Phase 3: 集成与差异化测试 (Low Priority)
**目标**: 确保新管道与旧逻辑的一致性，最终替代旧代码。

- [ ] **PipelineDifferentialTests**
  - 任务: 运行差异化测试，对比新旧重写器的输出。
  - 指导: 任何差异都需要人工审查，确认为改进还是回归。

- [ ] **Legacy 代码清理**
  - 任务: 在所有 Pipeline 测试通过后，移除旧的 `ExpressionSimplifier` 相关代码。

## 2. 时间线与里程碑 (Timeline & Milestones)

| 里程碑 (Milestone) | 描述 (Description) | 目标日期 (Target) | 状态 (Status) |
| :--- | :--- | :--- | :--- |
| **M1: Core Stability** | `ExpressionPipelineTests` 全通过，核心重写逻辑无误。 | 2026-03-05 | 🔄 进行中 |
| **M2: Propagation Correctness** | 所有 Propagator 测试通过，标记逻辑精准。 | 2026-03-07 | ⏳ 待开始 |
| **M3: Full Migration** | `PipelineDifferentialTests` 通过，旧代码废弃。 | 2026-03-10 | ⏳ 待开始 |

## 3. 详细测试文件跟踪 (Detailed File Tracking)

| 测试文件 | 模块/层级 | 当前状态 | 责任人 | 备注 |
| :--- | :--- | :--- | :--- | :--- |
| `ExpressionPipelineTests.cs` | **Semantic/Syntax** | 🔄 修复中 | AI Assistant | 剩余约 9 个失败用例 (参数/返回值) |
| `TerrariaConditionPipelineTests.cs` | **Condition Layer** | ✅ 已通过 | - | 验证特定 API 条件重写 |
| `UpwardMarkCollectorTests.cs` | **Propagation** | 🔄 验证中 | - | 需检查 ReturnStatement 修复后的影响 |
| `LifecycleManagedPropagatorTests.cs` | **Propagation** | ⏳ 待验证 | - | 需确认变量生命周期传播 |
| `PipelineDifferentialTests.cs` | **Integration** | ⏳ 待运行 | - | 用于最终回归测试 |

---
*注：请其他 AI 助手在接手任务时，优先查看 **Phase 1** 中的未完成项，并更新此文档的状态。*

## Migration Update (2026-03-05)

### Scope Completed
- Migrated hardcoded inline test sources to `UnitTests/Scenarios/SharedScenarios.cs` for:
  - `UnitTests/AnalysisTests/CodeDependencyAnalyzerAttributeTests.cs`
  - `UnitTests/AnalysisTests/CodeDependencyAnalyzerImplicitTests.cs`
  - `UnitTests/AnalysisTests/CodeDependencyAnalyzerOperatorTests.cs`
  - `UnitTests/StaticAnalysis/DependencyGraphTests.cs`

### Scenario Additions
- Added `SharedScenarios.ImplicitDependencyScenarios`.
- Added `SharedScenarios.OperatorDependencyScenarios`.

### Migration Metrics
- Test files migrated: 4
- Inline source cases migrated to scenarios: 13
- New scenario groups added: 2

### Verification Note
- Build still blocked by existing known issue:
  - `RewriteCodeExpressions/Hybrid/Middleware/LoggingMiddleware.cs` (`TNode.Kind()`)
- No bugfix was applied, only migration changes.
