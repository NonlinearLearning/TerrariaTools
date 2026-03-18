# Analysis 层

标准 Analysis 位于 `src/Analysis/Roslyn`。

## 职责

- 加载 workspace 或 source-only 输入
- 运行 Roslyn 分析
- 生成 `AnalysisResultModel`
- 组装 `AnalysisExecutionSnapshot`
- 暴露 `AnalysisServices`

## 关键入口

- `WorkspaceLoadCoordinator`
- `CodeAnalysisWorkspaceLoader`
- `SourceOnlyLoader`
- `RoslynAnalysisEngine`

## 当前标准输出

- `WorkspaceLoadResult`
- `AnalysisEngineResult`
- `AnalysisResultModel`
- `AnalysisExecutionSnapshot`
- `AnalysisServices`

## 说明

`src/Analysis/Legacy` 仍保留兼容职责，但不是标准主路径的一部分。
