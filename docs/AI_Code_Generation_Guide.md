# AI 代码生成指挥规范 (AI Code Generation Specification)

本规范用于指导 AI 持续推进 `TerrariaTools` 的 Hybrid 重写系统迁移。
当前阶段目标：**继续迁移能力，不处理既有 bug 修复**。

## 1. 系统架构与阶段目标

系统采用 **Two-Pass + Rule Engine + Middleware Pipeline**：

1. **Pass 1 / Analysis**：只读遍历，构建作用域和重写计划。
2. **Pass 2 / Execution**：按规则执行中间件并回写语法树。

当前迁移优先级：
1. 迁移旧 Pipeline/Operation 能力到 Hybrid。
2. 补齐规则覆盖与可观测性（metrics）。
3. 在不改既有 bug 的前提下，保证新增代码可集成。

---
#### 1.4 实现复合节点拆分与MRU识别 (Implement Composite Node Splitting & MRU Identification) [DONE]
*   **任务描述**：升级 `AnalysisVisitor`，使其遵循“复合节点拆分”与“最小重写单元(MRU)识别”的双层处理逻辑，确保重写粒度精确且遍历完整。
*   **技术实现**：
    *   **定义节点分类**：在 `AnalysisVisitor` 中明确区分 **Composite Node** (如 `BlockSyntax`, `MethodDeclarationSyntax`) 和 **MRU** (如 `StatementSyntax`, `ExpressionSyntax`)。
    *   **实现拆分逻辑 (Splitting)**：
        *   重写 `VisitBlock`, `VisitIfStatement` 等复合节点的访问方法。
        *   在访问复合节点时，显式递归调用 `Visit` 遍历其子节点。
    *   **实现识别逻辑 (Identification)**：
        *   在访问潜在 MRU 节点时，调用 `RuleEngine.FindMatchingRule(node, context)`。
        *   若匹配成功，将 `(Node, Rule)` 对加入 `RewritePlan`。
    *   **防止重复处理**：确保节点一旦被识别为 MRU，不再对其内部子节点进行默认的 Rule 匹配（除非规则定义允许）。
*   **所需资源**：Roslyn Syntax API, `RuleEngine`, `RewritePlan`.
*   **验收标准**：
    *   能正确遍历深层嵌套的结构（如 `Block` -> `If` -> `Block` -> `Statement`）。
    *   仅对 MRU 节点触发规则匹配，复合节点仅负责结构遍历（除非其本身也是 MRU）。
    *   生成的 `RewritePlan` 包含所有层级中命中的待重写节点。

## 2. 模块边界与约束

### 2.1 Contracts（稳定层）

关键接口：
- `IRewriteContext`
- `IMiddleware<TNode>`
- `IRule`

约束：
1. 非架构升级时，不修改 Contracts 的语义契约。
2. 中间件必须遵守 `Invoke(node, context, next)` 签名。

### 2.2 Analysis（Pass 1）

当前实现：
- `AnalysisVisitor`
- `ScopeBuilder`
- `LexicalScope`
- `RewritePlanner`
- `RewritePlan`
- `AnalysisPass`

约束：
1. Analysis 阶段只读，不直接改语法树。
2. `CurrentScope` 必须在进入/退出块级节点时正确维护。
3. 状态键使用常量（如 `AnalysisStateKeys`）。

### 2.3 Rules（声明式规则）

当前实现：
- `RuleEngine`
- `RewriteRule<TNode>`
- `DefaultHybridRuleRegistry`

约束：
1. 使用链式 `When(...).Use<...>()` 声明规则。
2. `TNode` 必须是具体语法节点类型。
3. `Use` 顺序决定执行顺序，禁止隐式重排。

### 2.4 Middleware（Pass 2 行为层）

当前已迁移能力（代表）：
- 常量条件简化：`If/While/Conditional`
- 标记节点处理：`Marked*Middleware`
- 旧操作适配：`LegacyOperationMiddleware<TNode>`
- 方法动作：`MethodActionMiddleware`
- 名称策略方法动作：`NameBasedMethodActionMiddleware`
- 类级动作：`ClassActionMiddleware<TNode>`

约束：
1. 中间件默认必须调用 `next`；仅在明确终结时可不调用。
2. 中间件不保存跨节点私有状态，状态写入 `context.SetState`。
3. 旧 `IRewritingOperation` 的 `null` 返回必须映射为删除语义（见 2.5）。

### 2.5 Execution（删除语义与回写）

当前实现：
- `RewriteExecutionPass`
- `ExecutionAnnotations.DeleteNode`
- `ExecutionSummary`

删除语义规范：
1. Hybrid 中间件不返回 `null`。
2. 需要删除节点时，给节点打 `DeleteNode` 注解。
3. 执行层统一 `RemoveNode(...)`。

---

## 3. Context 状态规范

推荐状态键来源：
- `AnalysisStateKeys`
- `HybridInputStateKeys`
- `HybridMetricsStateKeys`

当前核心输入状态：
- `MarkedNodes`
- `GlobalMethodActions`
- `NamePattern`
- `DeleteMatched`
- `ClearBodyMatched`
- `Solution`

当前核心统计状态：
- `PlanItemCount`
- `ExecutedRuleCount`
- `ReplacedNodeCount`
- `DeletedNodeCount`

约束：
1. 必须类型安全读取：`GetState<T>`。
2. 禁止散落魔术字符串键。

---

## 4. 入口编排规范

主入口：`PipelineExpressionSimplifier`

Hybrid 开关行为：
1. `useHybrid = false`：走现有 Pipeline。
2. `useHybrid = true && model != null`：走 `HybridRewriteEngine`。

Hybrid 分支必须完成：
1. 收集标记节点（predicate + nodesToMark）。
2. 应用 Terraria 条件标记。
3. 执行传播（upward + semantic）。
4. 注入方法动作、名称策略、solution 等状态。
5. 执行 Hybrid 重写并回传 metrics。

---

## 5. 可观测性与验收

### 5.1 Metrics

- 引擎在 context 写入 4 项核心指标。
- `SolutionRefactoringManager` 汇总并记录日志。
- `SolutionRefactoringResult` 保存结构化字段：
  - `HybridPlanItemCount`
  - `HybridExecutedRuleCount`
  - `HybridReplacedNodeCount`
  - `HybridDeletedNodeCount`

### 5.2 验收最低要求

1. 新增迁移代码在语义上符合 Hybrid 架构。
2. 不引入新的编译错误（允许既有已知错误继续存在）。
3. 关键行为具备可追踪统计输出。

---

## 6. 执行策略（当前阶段）

### 6.1 必须遵循

1. **继续迁移，不修复既有 bug**。
2. 仅在新增代码导致编译失败时，允许修正新增代码本身。
3. 不回退用户已完成迁移内容。

### 6.2 禁止事项

1. 借“顺手优化”修改与迁移无关的旧逻辑。
2. 在未明确要求时重写大面积既有模块。
3. 将 bug 修复混入迁移提交。

---

## 7. AI 任务模板（更新版）

### 7.1 迁移一个旧 Layer 到 Hybrid

```markdown
# Task
Migrate one legacy pipeline/layer capability into Hybrid rules + middleware.

# Inputs
- Legacy behavior source file: {FilePath}
- Target node types: {TNodeList}
- Context state keys needed: {StateKeys}

# Constraints
1. Do not fix existing unrelated bugs.
2. Keep behavior parity first, optimization second.
3. Use DeleteNode annotation for deletion.
4. Add/Update metrics path if behavior affects execution counts.

# Output
- New/updated middleware classes
- Rule registration updates
- Entry wiring updates (if required)
- Build result note (known blockers allowed)
```

### 7.2 迁移硬编码测试到 Scenarios (Migrate Hardcoded Tests to Scenarios)

```markdown
# 任务 (Task)
将硬编码的测试用例（内联源码字符串）迁移到可复用的 Scenarios 中。

# 输入 (Inputs)
- 进度跟踪文件：`UnitTests/TestMigrationProgress.md`
- 源测试文件：`UnitTests/**/*.cs`

# 步骤 (Steps)
1. **分析进度**：阅读 `TestMigrationProgress.md` 以了解当前状态和优先级模块。
2. **扫描并识别**：
    - 扫描 `UnitTest` 目录，查找包含硬编码测试逻辑的文件（例如 `ShadowGeneratorTests.cs` 中的内联 C# 源码字符串）。
    - **排除无硬编码代码的文件**：识别并跳过那些不包含内联源码字符串的文件。
3. **重构为 Scenarios**：
    - 将内联源代码提取到独立的 Scenario 文件或结构化数据中。
    - 更新测试方法以加载 Scenarios。
4. **更新跟踪**：
    - 在 `TestMigrationProgress.md` 中标记已完成的任务。
    - 记录关键指标：迁移的代码行数、测试用例数量、覆盖率提升等。

# 排除文件 (Excluded Files)
以下文件不包含硬编码的测试源码，应在迁移任务中排除：
- `UnitTests/Scenarios/SharedScenarios.cs` (本身即为 Scenario 存储)
- `UnitTests/RewriteCodeExpressionsTest/HybridMruPlanningTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/HybridRuleEngineEnhancementTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/HybridUtilityMiddlewareTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/HybridAtomicMiddlewareTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/HybridContextQueryApiTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/HybridDefUseAnalysisTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/PropagationTestCases.cs`
- `UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/DynamicModelPropagatorTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/LifecycleManagedPropagatorTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/NodeTrackingPropagatorTests.cs`
- `UnitTests/AnalysisTests/CompressedSparseRowGraphTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/SemanticPropagationTestBase.cs`
- `UnitTests/RewriteCodeExpressionsTest/PreprocessedSymbolPropagatorTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/PipelineDifferentialTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs`
- `UnitTests/DynamicAnalysis/TracerTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/ReferencePropagatorTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/UpwardPropagationTestCases.cs`
- `UnitTests/RewriteCodeExpressionsTest/UpwardMarkCollectorTests.cs`
- `UnitTests/RewriteCodeExpressionsTest/BehaviorGuaranteeTests.cs`
- `UnitTests/GlobalInitializer.cs`
- `UnitTests/AnalysisTests/DependencyAnalysisTests.cs`

# 约束 (Constraints)
1. 不修改实际的测试逻辑/断言，仅修改数据源。
2. 确保在每批次处理后更新 `TestMigrationProgress.md`。
```

### 7.3 扩展 Hybrid 可观测性

```markdown
# Task
Add structured metrics for new migrated behavior.

# Constraints
1. Metrics must be stored in context state and propagated to result model.
2. Keep field names stable and explicit.
3. Do not change business behavior while adding metrics.
```

---

## 8. 路线图（精简版）

### 8.1 已完成（归档）

- [x] 1.1 作用域构建器（ScopeBuilder）
- [x] 1.2 Def-Use 依赖分析图（基于 QuikGraph）
- [x] 1.3 Context 查询 API（`IsVariableDefined` / `FindReferences`）
- [x] 2.1 原子操作中间件（Remove/Replace/InsertBefore/InsertAfter）
- [x] 2.2 Utility 中间件（PreserveTrivia/FormatNode/LogMetric）
- [x] 3.1 Fluent API（`And/Or/Not` + `WithPriority`）
- [x] 3.2 预定义条件库（`CommonConditions`）
- [x] 3.3 规则验证与冲突解决（Priority + Conflict + Middleware 兼容校验）

### 8.2 当前状态

- [x] 1.4 复合节点拆分与 MRU 识别（Composite/MRU 双层遍历与命中控制）
- [x] Legacy Removal Phase 1（补齐布尔优化 + 全局后处理 + 默认切换 Hybrid）
- [x] Legacy Removal Phase 2（删除 `Handlers` 与 `Operations`）
- [x] Legacy Removal Phase 3（删除旧 Layer 与 `RewritingPipeline`）
- [x] Legacy Removal Phase 4（入口清理为纯 Hybrid，移除 `IRewritingLayer` 相关基础设施）

---

## 9. 当前执行任务（2026-03-05）

### 9.1 任务结论

1. 主迁移任务已执行完成并归档（见 8.1）。
2. 去旧计划 Phase 1-4 已执行完成，旧 Pipeline 主链路已移除。
3. 继续遵循策略：**继续迁移，不修复既有 bug**（当前已知 blocker：`Hybrid/Middleware/LoggingMiddleware.cs`）。

### 9.2 下一步执行顺序

1. 当前迁移主线任务已清空；新任务按新增需求单独立项。
2. 既有 bug（如 `LoggingMiddleware`）留待专门修复阶段处理。

---

## 10. 去旧计划 (Legacy Removal Plan)

### 10.1 现状覆盖率分析 (Coverage Analysis)

| Legacy Layer | Hybrid Equivalent | Status | Note |
| :--- | :--- | :--- | :--- |
| `PredicateIdentifierLayer` | `PipelineExpressionSimplifier.BuildMarkedNodeSet` | ✅ Covered | 已集成在 Hybrid 入口 |
| `TerrariaConditionLayer` | `PipelineExpressionSimplifier.ApplyTerrariaConditionMarks` | ✅ Covered | 已集成在 Hybrid 入口 |
| `ClassRefactorerLayer` | `ClassActionMiddleware` | ✅ Covered | |
| `MethodRefactorerLayer` | `MethodActionMiddleware` | ✅ Covered | |
| `NameBasedMethodRefactorerLayer` | `NameBasedMethodActionMiddleware` | ✅ Covered | |
| `PropagationLayer` | `PipelineExpressionSimplifier.RunPropagation` | ✅ Covered | 已集成在 Hybrid 入口 |
| `SemanticIdentifierLayer` | `DefaultHybridRuleRegistry` + `Middleware` | ✅ Covered | 所有 Handler 均已映射为 Rule |
| `SyntaxTransformerLayer` | `RewriteExecutionPass` | ✅ Covered | 执行逻辑已迁移 |
| `ExpressionOptimizerLayer` | `ControlFlowSimplificationMiddleware` + `BooleanSimplificationMiddleware` | ✅ Covered | 已补齐布尔代数简化能力 |
| `PostProcessingLayer` | `FormatNodeMiddleware` + Global Post-Processing Pass | ✅ Covered | 已在 Hybrid 引擎末尾补齐格式化/简化后处理 |

### 10.2 触发旧链路的调用点 (Legacy Triggers)

已清零：旧 Pipeline 主链路已移除，不再存在回退分支。
当前兼容行为：`model == null` 时返回原语法树（不执行重写）。

### 10.3 安全删除顺序 (Safe Deletion Order)

#### Phase 1: 补齐与切换 (Gap Filling & Switch)
1.  [x] 实现 `BooleanSimplificationMiddleware` 补齐表达式优化能力。
2.  [x] 在 `HybridRewriteEngine` 末尾集成 `Simplifier` 和 `Formatter`。
3.  [x] 修改 `PipelineExpressionSimplifier` 默认参数 `useHybrid = true`。
4.  [x] 完成构建验证（保留既有 blocker）。

#### Phase 2: 移除业务逻辑 (Remove Logic)
1.  [x] 删除 `TerrariaTools.RewriteCodeExpressions.Handlers` 命名空间下所有文件。
2.  [x] 删除 `TerrariaTools.RewriteCodeExpressions.Operations` 命名空间下所有文件。

#### Phase 3: 移除管道层 (Remove Layers)
1.  [x] 删除 `SemanticIdentifierLayer.cs`。
2.  [x] 删除 `SyntaxTransformerLayer.cs`。
3.  [x] 删除 `ExpressionOptimizerLayer.cs`。
4.  [x] 删除 `PostProcessingLayer.cs`。
5.  [x] 删除 `RewritingPipeline.cs`。

#### Phase 4: 清理入口 (Cleanup Entry)
1.  [x] 移除 `PipelineExpressionSimplifier` 中的旧回退分支逻辑（保留兼容参数，不再影响执行路径）。
2.  [x] 移除 `IRewritingLayer` 接口及其相关基础设施。

## 11. Analysis 增强任务 (Analysis Enhancement Tasks)

### 11.1 基于设计问题分析报告的修复任务 (Fixes from Design Problem Analysis Report) [DONE]
*   **任务描述**：根据 2026-03-05 生成的 [DesignProblemReport.md](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/DesignProblemReport.md) 执行系统性修复。
*   **优先级**: **High**
*   **具体子任务**:
    1.  **移除硬编码路径 (D01)**:
        *   目标：修复 `ComprehensiveGetDataAnalyzer.cs` 中的绝对路径 `D:\lodes\TR\...`。
        *   方案：将路径移至 `appsettings.json` 配置，并支持通过命令行参数覆盖。
    2.  **优化并发调度模型 (D02)**:
        *   目标：消除 `CodeDependencyAnalyzer.cs` 中的忙等待 `while(true)` + `Task.Delay(10)`。
        *   方案：使用 `System.Threading.Channels` 或 `BlockingCollection<T>` 重构生产者-消费者队列，实现真正的事件驱动调度。
    3.  **架构解耦与 DI 集成 (D03)**:
        *   目标：解决 `AdvancedCodeAnalyzer` 对具体类的硬耦合。
        *   方案：为 `CodeDependencyAnalyzer`, `CallChainAnalyzer` 等提取接口，并通过 `Microsoft.Extensions.DependencyInjection` 进行注入。
    4.  **优化图遍历性能 (D04)**:
        *   目标：提升反向 DFS 性能。
        *   方案：将 `DependencyGraph` 的底层实现改为 `BidirectionalGraph`，利用 QuikGraph 原生的反向遍历支持。
    5.  **增强异常处理与日志 (D07)**:
        *   目标：解决异常信息丢失问题。
        *   方案：引入 `Microsoft.Extensions.Logging`，在 `AdvancedCodeAnalyzer` 中记录完整堆栈，并定义结构化的 `AnalysisException`。

### 11.2 借鉴 SonarAnalyzer.CSharp 控制流分析 (Adopt SonarAnalyzer CFG Logic) [DONE]
*   **任务描述**：参考 SonarAnalyzer.CSharp 的控制流图 (CFG) 和数据流分析 (DFA) 实现，增强 `TerrariaTools` 的 `DataFlowAnalyzer`。
*   **技术分析**：
    *   **现状**：当前的 `DataFlowAnalyzer` 仅依赖 Roslyn 原生 `AnalyzeDataFlow`，能力有限（无法跨方法追踪）。
    *   **目标**：实现类似 Sonar 的符号执行引擎，用于检测深层依赖。
    *   **可用技术点**：
        1.  **Exploded Graph**: 构建基于状态的爆炸图，追踪变量在不同路径下的值。
        2.  **Symbolic Execution**: 模拟执行路径，检测不可达代码（Dead Code）。
*   **注意**：SonarAnalyzer 是 GPL 协议，**严禁直接复制代码**。仅参考其公开的设计文档和算法思路（如 CFG 构建策略），使用 Roslyn API 自行实现。
*   **执行结果**：已在 `DataFlowAnalyzer` 中实现 CFG + 轻量符号执行骨架（Exploded State 计数、不可达块识别、路径状态传播），作为首版能力落地。

### 11.3 实现基于 Roslynator 的 Analysis 任务 (Implement Analysis Task via Roslynator) [DONE]
```markdown
# Task
Enhance Analysis capability using Roslynator.Core.

# Inputs
- Target: `TerrariaTools.Analysis` namespace
- Dependency: `Roslynator.Core`, `Roslynator.CSharp`

# Steps
1. Add `Roslynator.CSharp` package to `TerrariaTools.csproj`.
2. Create `RoslynatorAnalysisAdapter.cs` to wrap Roslynator's analysis APIs.
3. Implement `AnalyzeComplexity` using Roslynator's metrics.
4. Implement `SuggestRefactorings` to list applicable refactorings for a node.

# Output
- `RoslynatorAnalysisAdapter` class
- Unit tests demonstrating complexity calculation
```

### 11.4 执行状态（2026-03-05）

1. `11.1` 已完成：D01/D02/D03/D04/D07 已在 `Analysis` 模块落地。
2. `11.2` 已完成首版：`DataFlowAnalyzer` 已具备 CFG + Symbolic 基础能力。
3. `11.3` 已完成：已新增 `RoslynatorAnalysisAdapter` 与复杂度相关单测。
4. 构建状态：仅保留既有 blocker `RewriteCodeExpressions/Hybrid/Middleware/LoggingMiddleware.cs`，未修复（遵循“迁移阶段不修 bug”约束）。


### 11.5 Analysis QuikGraph 迁移状态（2026-03-05）

1. Analysis/DependencyGraph 已统一使用 QuikGraph（BidirectionalGraph + DepthFirstSearchAlgorithm + TopologicalSort + StronglyConnectedComponents）。
2. Analysis/CompressedSparseRowGraph 已改为 QuikGraph BreadthFirstSearchAlgorithm 计算可达性。
3. Analysis/TypeConflictAnalyzer 的命名空间遍历已改为 QuikGraph DFS。
4. 迁移原则保持：不执行 test 修复，不处理既有 bug。


## 12. UnitTests 硬编码测试迁移状态（精简）

### 12.1 当前状态
1. 已完成 `UnitTests` 内联源码变量迁移（`source/sourceCode/*Stub/*input/*code`）到 `AutoMigratedScenarios` 引用。
2. 已补充迁移文件：
   - `UnitTests/CodeRewriting/SlicingRewriterTests.cs`
   - `UnitTests/FeatureExtraction/PlayerFieldExtractorTests.cs`
   - `UnitTests/RewriteCodeExpressionsTest/NameBasedMethodRefactorerTests.cs`（新增 `callerSource/targetSource`）
   - `UnitTests/ShadowGeneratorComprehensiveTests.cs`（新增 `source2`）
   - `UnitTests/RewriteCodeExpressionsTest/NameBasedMethodRefactorerTests.cs`（新增 `baseSource/derivedSource`）
3. 统一场景入口已重建：`UnitTests/Scenarios/AutoMigratedScenarios.cs`。
4. 当前状态为“结构迁移 + 场景回填已完成”：
   - `AutoMigratedScenarios` 常量总数：26
   - 占位场景数：0
5. `UnitTests` 中可识别的内联源码声明（`var/string/const string ... = @"..."` / `"""..."""`）已清零。
6. 构建仍受既有 blocker 影响：`RewriteCodeExpressions/Hybrid/Middleware/LoggingMiddleware.cs`（迁移阶段不修 bug）。

### 12.2 后续迁移项（仅迁移，不修 bug）
1. 按测试用例拆分共用键（如同文件大量共用 `*_Source_1`）为独立场景键，进一步降低用例耦合。
2. 同步更新 `UnitTests/TestMigrationProgress.md` 指标（文件数、用例数、回填完成率）。
