# 测试迁移进度 (Test Migration Progress)

## 迁移概览
本文档记录了从旧的单元测试类（如 `MethodRefactorerTests.cs`）到基于 `SharedScenarios` 的新测试框架的迁移进度。

## 场景迁移状态

| 场景分类 | 场景名称 | 场景库定义 | 集成测试使用 | 管道验证 | 状态 | 备注 |
| :--- | :--- | :---: | :---: | :---: | :---: | :--- |
| **基础逻辑** | `IfElse` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `WhileLoop` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `LogicalAnd` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `ConditionalAccess` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `MethodGroupOverloads` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| **IfElse 变体** | `SimpleIf` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `IfElseChain` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `NestedIf` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `TernaryExpression` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| **While 变体** | `SimpleWhile` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `DoWhile` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `WhileWithContinue` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| | `NestedWhile` | ✅ | ✅ | ✅ | 已完成 | [PipelineIntegrationTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/PipelineIntegrationTests.cs) |
| **方法重构** | `VirtualOverridePair` | ✅ | ✅ | ✅ | 已完成 | [MethodRefactorerTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/MethodRefactorerTests.cs) |
| | `VirtualWithNoCallers` | ✅ | ✅ | ✅ | 已完成 | [MethodRefactorerTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/MethodRefactorerTests.cs) |
| | `InternalPrivatization` | ✅ | ✅ | ✅ | 已完成 | [MethodRefactorerTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/MethodRefactorerTests.cs) |
| | `MethodWithOutParameters` | ✅ | ✅ | ✅ | 已完成 | [MethodRefactorerTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/MethodRefactorerTests.cs) |
| | `MethodWithNumericOutParameters` | ✅ | ✅ | ✅ | 已完成 | [MethodRefactorerTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/MethodRefactorerTests.cs) |
| **复杂表达式** | `AnonymousObject` | ✅ | ✅ | ✅ | 已完成 | [ExpressionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/ExpressionPipelineTests.cs) |
| | `TupleExpression` | ✅ | ✅ | ✅ | 已完成 | [ExpressionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/ExpressionPipelineTests.cs) |
| | `ObjectInitializer` | ✅ | ✅ | ✅ | 已完成 | [ExpressionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/ExpressionPipelineTests.cs) |
| **Terraria 条件** | `SimpleNetMode` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `LiteralOnRight` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `MainNetMode` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `AndConditions` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `OrConditions` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `IfElsePromote` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |
| | `IfElseIfPromote` | ✅ | ✅ | ✅ | 已完成 | [TerrariaConditionPipelineTests.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/UnitTests/RewriteCodeExpressionsTest/TerrariaConditionPipelineTests.cs) |

## 待处理测试文件清单

| 文件名 | 状态 | 优先级 | 备注 |
| :--- | :--- | :--- | :--- |
| `MethodRefactorerTests.cs` | 已清理 | 中 | 已替换主要场景，剩余边缘情况逐步替换 |
| `ExpressionTests.cs` | 已废弃 | 已移除 | 场景已完全迁移至 `ExpressionPipelineTests.cs` |
| `TerrariaConditionRewriterTests.cs` | 已废弃 | 已移除 | 场景已完全迁移至 `TerrariaConditionPipelineTests.cs` |
| `ExpressionPipelineTests.cs` | 已优化 | 中 | 核心复杂场景已使用 `SharedScenarios` |
| `TerrariaConditionPipelineTests.cs` | 已创建 | 中 | 负责验证 Terraria 相关的语义重写逻辑 |

## 变更日志 (Traceability Log)

| 日期 | 操作类型 | 对象 | 描述 |
| :--- | :--- | :--- | :--- |
| 2026-03-03 | 迁移测试 | `ExpressionTests.cs` | 将 AnonymousObject, Tuple, ObjectInitializer 等复杂场景迁移至 `ExpressionPipelineTests.cs` |
| 2026-03-03 | 迁移测试 | `TerrariaConditionRewriterTests.cs` | 将 netMode 语义识别逻辑迁移至 `TerrariaConditionPipelineTests.cs` |
| 2026-03-03 | 代码清理 | `ExpressionTests.cs` 等 | 删除已完成迁移的旧测试源文件 |
| 2026-03-03 | 场景提取 | `SharedScenarios.cs` | 增加 ComplexExpressions 和 TerrariaConditions 场景库 |
| 2026-03-03 | 替换代码 | `MethodRefactorerTests.cs` | 在方法重构测试中引入 `SharedScenarios` 替换硬编码字符串 |

## 下一步计划
- [x] 将 `ExpressionTests.cs` 中的复杂场景（AnonymousObject, Tuple）迁移至 `ExpressionPipelineTests.cs`。
- [x] 将 `TerrariaConditionRewriterTests.cs` 的语义测试迁移至管道架构。
- [x] 在 `MethodRefactorerTests.cs` 中使用 `SharedScenarios` 替换硬编码的字符串。
- [ ] 验证所有迁移后的测试在管道架构下通过。
- [ ] 针对 IfElse 变体和 While 变体增加具体的集成测试用例。
- [ ] 进一步清理 `MethodRefactorerTests.cs` 中剩余的硬编码测试代码。
