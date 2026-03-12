# RewriteCodeExpressions 架构说明

`RewriteCodeExpressions` 是一个基于 Roslyn (Microsoft.CodeAnalysis) 的复杂代码重构与重写引擎。它采用了一种混合（Hybrid）架构，结合了静态分析、规则驱动引擎和中间件流水线，能够对 C# 代码进行深度的语义转换。

## 核心架构设计

整个引擎的工作流程分为四个主要阶段：

1.  **标记阶段 (Marking Phase)**: 根据预定义的谓词、条件或 Terraria 特有的逻辑，识别出需要进行转换的语法节点（Marked Nodes）。
2.  **分析阶段 (Analysis Phase)**: 对标记的节点进行静态分析，包括词法作用域构建、定义-使用链（Def-Use）分析，并生成最终的重写计划（Rewrite Plan）。
3.  **执行阶段 (Execution Phase)**: 根据分析阶段生成的重写计划，通过中间件流水线和规则引擎执行实际的代码替换、删除或修改操作。
4.  **后处理阶段 (Post-Processing Phase)**: 执行代码清理、布尔表达式简化、控制流优化等后续工作，确保生成的代码简洁且语义正确。

## 目录结构及文件作用

### 根目录

*   `ClassRefactorer.cs`: 专门用于类级别重构的工具类。
*   `ExpressionProcessor.cs`: 处理各种表达式转换的核心组件。
*   `ExpressionSimplifier.cs`: 表达式简化器，用于减少代码冗余。
*   `MemberSlicingRewriter.cs`: 实现成员切分（Slicing）逻辑的重写器。
*   `MethodRefactorer.cs`: 方法级别重构的基类或通用工具。
*   `NameBasedMethodRefactorer.cs`: 基于方法名称模式的重构实现。
*   `RefactoringTool.cs`: 提供高层 API 供外部调用的重构工具入口。
*   `SolutionRefactoringManager.cs`: 管理整个解决方案级别的重构任务。
*   `TerrariaConditionRewriter.cs`: 针对 Terraria 特定业务逻辑的条件重写器。

### `Hybrid/` (混合引擎核心)

这是整个重写引擎的核心实现，采用了基于状态和中间件的设计。

#### `Hybrid/Analysis/` (分析组件)
*   `AnalysisPass.cs`: 编排整个分析阶段的执行流程。
*   `DefUseAnalyzer.cs` / `DefUseGraph.cs` / `DefUseNode.cs`: 实现数据流分析中的定义-使用链，用于理解变量的生命周期和依赖关系。
*   `RewritePlanner.cs` / `RewritePlan.cs`: 根据分析结果制定具体的重写策略。
*   `ScopeBuilder.cs` / `LexicalScope.cs`: 构建词法作用域树，确保重写过程中标识符的正确性。

#### `Hybrid/Context/` (上下文)
*   `RewriteContext.cs`: 核心上下文对象，在分析和执行阶段承载所有状态、语义模型和配置信息。

#### `Hybrid/Contracts/` (接口定义)
*   定义了 `IMiddleware`, `IRewriteContext`, `IRule` 等核心接口，确保了系统的可扩展性。

#### `Hybrid/Execution/` (执行组件)
*   `RewriteExecutionPass.cs`: 负责按照计划执行代码重写。
*   `HybridPostProcessing.cs`: 定义 `HybridPostProcessingPass`，负责后处理阶段的执行，集成 `PostProcessingRuleEngine` 进行布尔简化、代码清理和格式化。

#### `Hybrid/Middleware/` (中间件流水线)
这里包含了大量的中间件，每个中间件负责处理特定类型的语法节点或转换逻辑：
*   `LoggingMiddleware.cs`: 提供执行过程中的日志记录。
*   各种 `Marked...Middleware.cs`: 针对被标记的不同语法节点（语句、表达式、成员声明等）的专用处理器。

注意：`AtomicOperationMiddleware`、`MethodActionMiddleware` 等通过 `RegisterExecutionRules` 注册到 **执行阶段 (Execution Phase)**；`BooleanSimplificationMiddleware` 等通过 `RegisterPostProcessingRules` 注册到 **后处理阶段 (Post-Processing Phase)**。它们不应直接混入通用的中间件列表，而是由各自的阶段按需调用。

#### `Hybrid/Rules/` (规则引擎)
*   `RuleEngine.cs`: 规则调度中心，处理规则匹配、冲突解决和优先级。
*   `PostProcessingRuleEngine.cs`: 专门用于后处理阶段的规则引擎，仅加载清理和简化相关的规则。
*   `RewriteRule.cs`: 定义了具体的重写规则，包括匹配条件和执行动作。
*   `DefaultHybridRuleRegistry.cs`: 分别注册执行阶段 (`RegisterExecutionRules`) 和后处理阶段 (`RegisterPostProcessingRules`) 的默认规则。

### `Pipeline/` (流水线与指标)

*   `HybridRewriteMetrics.cs`: 收集并统计重写过程中的各项指标（如替换节点数、耗时等）。
*   `PipelineExpressionSimplifier.cs`: 在流水线中集成的表达式简化工具。
*   `RewriteCondition.cs`: 定义了用于触发重写的各种复杂条件。

---

## 开发者指南

当需要添加新的重写逻辑时，通常有两种方式：
1.  **添加规则**: 如果逻辑可以通过“匹配-替换”模式描述，应在 `Hybrid/Rules/` 中添加新的 `RewriteRule`。
2.  **添加中间件**: 如果逻辑涉及复杂的上下文感知或需要干预重写的核心流程，应在 `Hybrid/Middleware/` 中添加新的 `IMiddleware` 并将其注册到 `MiddlewarePipeline` 中。
