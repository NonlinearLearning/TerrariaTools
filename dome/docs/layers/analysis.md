# Analysis 层说明

返回 [架构总览](../architecture.md)。

## 1. 这一层做什么

`src/Analysis/Roslyn` 负责把外部输入路径转换成语义化的分析结果，并进一步包装成规则可消费的 `AnalysisExecutionSnapshot`、`AnalysisServices` 与兼容 `AnalysisContext`。

这是整个项目中最靠近 Roslyn 的层，主要回答两个问题：

- “代码里有什么？”
- “这些结构如何以可查询的方式交给后续层使用？”

## 2. 子模块划分

Analysis 层当前可以按职责分成五组。

### 2.1 输入加载

- `IWorkspaceLoader`
- `CodeAnalysisWorkspaceLoader`
- `SourceOnlyLoader`
- `WorkspaceLoadCoordinator`

负责把输入路径解析为 `AnalysisInput`。

### 2.2 语义分析与目标抽取

- `RoslynAnalysisEngine`
- `DirectiveReader`
- `MetadataMemberIdBuilder`
- `MetadataTypeIdBuilder`
- `SymbolRefProjector`

负责扫描语法树、构造类型/函数/statement 目标、读取 directive、生成 symbol 引用。

### 2.3 运行时查询服务

- `AnalysisContext`
- `AnalysisExecutionSnapshot`
- `AnalysisServices`
- `QueryServices` 中的引用/继承查询实现
- `StatementAnalysisService`

负责为 Rules 层提供按需查询能力，而不只是静态数组。

### 2.4 惰性函数图

- `FunctionGraphProvider`
- `FunctionIndex`
- `FunctionFactsIndex`

负责把函数关系保持在“可索引、可按需快照”的状态。

### 2.5 影响与预测辅助

- `FunctionImpactAnalyzer`
- `ReferenceZeroPredictionAnalyzer`

负责在 plan/report 阶段补充摘要和预测结果。

## 3. 主要输入 / 输出

### 输入

- `AnalysisInput`
  - `SourceOnlyAnalysisInput`
  - `WorkspaceAnalysisContextInput`

### 输出

- `RoslynAnalysisResult`
- `AnalysisResultModel`
- `AnalysisContext`
- `WorkspaceLoadResult`

## 4. 对外 API

| API | 作用 | 主要调用方 |
| --- | --- | --- |
| `IWorkspaceLoader.LoadAsync` | 加载输入路径 | `Application` |
| `RoslynAnalysisEngine.AnalyzeAsync` | 执行静态分析 | `Application` |
| `RoslynAnalysisEngine.CreateSnapshot` | 从分析结果构建全局分析事实快照 | `Application` |
| `RoslynAnalysisEngine.CreateServices` | 兼容入口，单独暴露查询/快照服务 | 旧调用方、测试 |
| `RoslynAnalysisEngine.CreateContext` | 正式装配入口，围绕单一 snapshot 组合 services/context | `Application`、旧调用方 |
| `IStatementAnalysisService.Analyze` | 构建局部 statement snapshot | `Rules` |
| `IFunctionGraphProvider.GetWholeProjectSnapshot` | 显式物化全项目 call-only 函数图 | `Application`、分析辅助器 |
| `IFunctionGraphProvider.GetExpandedMembersSnapshot` | 按 root 成员构建局部函数图快照 | `Rules`、分析辅助器 |
| `FunctionImpactAnalyzer.Analyze` | 评估 method delete 影响范围 | `Application` |
| `ReferenceZeroPredictionAnalyzer.Predict` | 基于局部 snapshot 预测 method delete | `Application` |

## 5. 这一层承担的职责

### 5.1 统一多种输入形式

Analysis 层要把以下输入收口成统一分析入口：

- `.sln`
- `.csproj`
- 目录
- 单个 `.cs` 文件

### 5.2 建立正式分析视图

`AnalysisResultModel` 是 Analysis 层最重要的正式输出，包含：

- `Targets`
- `Edges`
- `TypeGraph`
- 图物化状态

它是 Rules 和 Reporting 能理解的稳定结构。

### 5.3 建立运行时上下文

仅有 `AnalysisResultModel` 还不够。当前正式模型被拆成两层：

- `AnalysisExecutionSnapshot`
- `AnalysisServices`

其中查询服务层继续提供：

- `IInheritanceQueryService`
- `IReferenceQueryService`
- `StatementFactsIndex`
- `IStatementAnalysisService`
- `IFunctionGraphProvider`

`AnalysisContext` 继续存在，但它只是这两层的兼容组合壳。
`AnalysisServices` 的装配围绕同一个 `AnalysisExecutionSnapshot` 闭合，不应再与第二份平行 facts 混用。

### 5.4 收口函数图成本

这是当前设计里最重要的约束之一。

默认行为不是先生成全量函数图，而是：

- 全项目建立 `FunctionIndex`
- 全项目建立 `FunctionFactsIndex`
- 仅在调用方明确请求时物化 `FunctionGraphSnapshot`

局部 snapshot 的首版规则固定为：

- `depth = 1`
- 双向 `Calls` 邻接扩张
- 先扩成员，再按文件集合重建 call-only snapshot

### 5.5 保留 statement 图占位但不把它当正式全局图

`AnalysisResultModel.StatementGraph` 目前是兼容字段，正式入口是：

- `AnalysisExecutionSnapshot.StatementFacts`
- `AnalysisServices.Statements`

因此文档和调用方都不应把 `AnalysisResultModel.StatementGraph` 当成默认完整输入。

## 6. 真实执行链路

### 6.1 `WorkspaceLoadCoordinator`

先根据路径类型和 loader 偏好选定加载方式。

### 6.2 `CodeAnalysisWorkspaceLoader`

若输入为 `.sln` 或 `.csproj`：

- 使用 `MSBuildWorkspace`
- 打开真实 solution/project
- 为每个文档建立 `WorkspaceAnalysisDocumentContext`，一次性固定 `Document`、`SourceDocument`、`Compilation`、`SemanticModel`、`Root`

### 6.3 `RoslynAnalysisEngine`

对每个文档：

- 取得语法树和 `SemanticModel`
- 抽取 class/method/statement target
- 记录 defines / uses / directives / invoked members
- 建立 type graph
- 建立 function index / facts

### 6.4 `CreateSnapshot` / `CreateContext`

从静态结果派生并装配：

- `AnalysisExecutionSnapshot`
- 继承查询
- 引用查询
- statement service
- function graph provider

`CreateContext` 通过同一份 snapshot 组装 `AnalysisServices` 和兼容 facade，调用方不应再先后重复构建 snapshot 与 services。

## 7. 与上下游层的边界

### 上游

- `Application`
- 输入路径和 workspace 选项

### 下游

- `Rules`
- `Application` 中的 impact / prediction 汇总
- `Reporting` 间接消费 `AnalysisResultModel`

Analysis 层不直接做最终改写，也不直接决定计划动作。

## 8. 本层不负责什么

Analysis 层不负责：

- 命令行解析
- 最终规则裁决
- 计划冲突裁决
- 语法树重写
- artifact 写盘

如果某段代码开始直接决定“删还是不删”，它通常已经越过 Analysis 的职责边界。
