# Joern 裁剪为 C# 版的重写方案

## 1. 目标

本文按 2026-04-21 当前代码事实，说明本仓库如何把 Joern 能力裁剪并映射到当前四层架构。

当前交付目标：

- 保留最小 C# CPG 前端、图核心、Pass 管线、语义层、切片层、查询层
- 让图核心、执行逻辑和 Roslyn 前端分别落到 `Domain / Logic / Infrastructure`
- 让文档中的路径、类型、阶段和验证口径与当前代码一致

## 2. 当前实现映射

### 2.1 Domain：图核心与稳定分析事实

当前真实落点：

- `src/Domain/Analysis/AnalysisCpgSnapshot.cs`
- `src/Domain/Analysis/AnalysisCompositeLayerSnapshot.cs`
- `src/Domain/Analysis/Engine/Core/CpgGraph.cs`
- `src/Domain/Analysis/Engine/Core/CpgNode.cs`
- `src/Domain/Analysis/Engine/Core/CpgEdge.cs`
- `src/Domain/Analysis/Engine/Core/CpgNodeKind.cs`
- `src/Domain/Analysis/Engine/Core/CpgEdgeKind.cs`
- `src/Domain/Analysis/Engine/Model/*.cs`
- `src/Domain/Analysis/Engine/Semantic/**`
- `src/Domain/Analysis/Engine/Slicing/**`
- `src/Domain/Analysis/Engine/Query/**`

### 2.2 Logic：语言层、Pass、Layer 与分析执行

当前真实落点：

- `src/Logic/Analysis/Engine/Language/**`
- `src/Logic/Analysis/Engine/Passes/**`
- `src/Logic/Analysis/Engine/Layers/**`
- `src/Logic/Analysis/Engine/X2Cpg/**`
- `src/Logic/Analysis/Engine/Frontend/**`
- `src/Logic/Analysis/**`

### 2.3 Infrastructure：Roslyn 前端与技术兑现

当前真实落点：

- `src/Infrastructure/Analysis/AnalysisBackedCpgGateway.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/RoslynCpgFrontend.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/RoslynAstToCpgBuilder.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/RoslynProjectLoader.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/RoslynCompilationContext.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/Builders/DeclarationBuilder.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/Builders/ExpressionBuilder.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/Builders/StatementBuilder.cs`
- `src/Infrastructure/Analysis/Engine/Passes/BuildConfigFileCreationPass.cs`

## 3. 当前架构结论

- 图核心已经进入 `Domain.Analysis.Engine.Core`
- 语义、切片、查询模型已经进入 `Domain.Analysis.Engine`
- Pass、Layer、语言执行和大部分分析执行能力已经进入 `Logic.Analysis.Engine`
- Roslyn 前端和 Builder 已进入 `Infrastructure.Analysis.Engine.Frontend`
- `AnalysisAppService` 与 `AnalysisCpgAppService` 已能从当前实现出发对外提供快照和 CPG 能力

## 4. 与 Joern 的能力映射

当前仍有参考价值的 Joern 能力族：

- C# 前端
- X2Cpg 通用建图思想
- semanticcpg 语义层
- dataflowengineoss 数据流层

在本仓库中的映射方式：

- 前端与源码读取 -> `Infrastructure.Analysis.Engine.Frontend/**`
- 图核心 -> `Domain.Analysis.Engine.Core/**`
- 语义规则与语义验证 -> `Domain.Analysis.Engine.Semantic/**`
- 语言层与 Traversal -> `Logic.Analysis.Engine.Language/**`
- Pass 管线 -> `Logic.Analysis.Engine.Passes/**`
- Layer 管线 -> `Logic.Analysis.Engine.Layers/**`
- 数据流、切片、查询 -> `Domain.Analysis.Engine.*` + `Logic.Analysis.Engine.*`

## 5. 当前关键入口

- `src/Infrastructure/Analysis/Engine/Frontend/RoslynCpgFrontend.cs`
- `src/Infrastructure/Analysis/Engine/Frontend/RoslynAstToCpgBuilder.cs`
- `src/Domain/Analysis/Engine/Core/CpgGraphBuilder.cs`
- `src/Logic/Analysis/Engine/Passes/BuildMetadataPass.cs`
- `src/Logic/Analysis/Engine/Passes/LinkAstPass.cs`
- `src/Logic/Analysis/Engine/Passes/ResolveTypeRefsPass.cs`
- `src/Logic/Analysis/Engine/Passes/EvaluateNodeTypesPass.cs`
- `src/Logic/Analysis/Engine/Passes/DataFlow/BuildSemanticDataFlowPass.cs`
- `src/Logic/Analysis/Engine/Layers/LayerPipeline.cs`

## 6. 验证锚点

- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/**`
- `src/Application/Services/AnalysisAppService.cs`
- `src/Application/Services/AnalysisCpgAppService.cs`

## 7. Prompt Spec 对齐要求

- 维护本文时，显式加载 `ai-rules/common/prompt-spec-writing.mdc`
- 文中的目标、边界、主流程、默认值、能力映射、验收标准统一按规格语言书写
- 旧设计别名路径要及时回写为当前真实路径

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：方案 / 架构设计文档。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `src/Domain/Analysis/**`
- `src/Logic/Analysis/**`
- `src/Infrastructure/Analysis/**`
- `src/Application/Services/Analysis*.cs`
- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/**`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
