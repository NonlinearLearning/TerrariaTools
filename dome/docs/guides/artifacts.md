# 产物说明

本页说明不同运行模式会写出哪些文件。

## 标准流程产物

标准流程的产物由 `src/Application/Pipeline/ArtifactPlanBuilder.cs` 决定。

### `analyze`

输出：

- `analysis.json`
- `report.json`

不会输出：

- `audit-plan.json`
- `rewritten/`

### `plan`

输出：

- `audit-plan.json`
- `report.json`

不会输出：

- `analysis.json`
- `rewritten/`

### `run`

输出：

- `audit-plan.json`
- `report.json`
- `rewritten/<relative-path>.cs`

如果重写阶段中途失败，已经成功写出的 `rewritten/` 文件仍会保留，报告里会反映失败状态。

## 运行时流程产物

运行时布局由 `src/Adapters/Runtime.Process/TerrariaRuntimeLayoutFactory.cs` 定义。

输出根目录下会出现：

- `dependency-env/`
- `workspace/`
- `artifacts/report.json`

说明：

- `dependency-env/` 保存非 `.cs` 依赖文件副本
- `workspace/` 是最终执行 `dotnet build` 的目录
- `artifacts/report.json` 是标准 Dome 流程的报告，并在运行时阶段完成后再持久化一次

## Shadow extraction 产物

shadow 布局由 `src/Adapters/Runtime.Process/TerrariaRuntimeShadowLayoutFactory.cs` 定义。

输出根目录下会出现：

- `dependency-env/`
- `workspace/`
- `artifacts/shadow-report.json`

说明：

- `workspace/` 中只包含闭包内需要的源码和构建依赖
- `shadow-report.json` 包含种子成员、包含文档、可达方法、重写摘要和构建摘要

## 你应该看哪个文件

- 想看规则结果：先看 `audit-plan.json`
- 想看最终源码：看 `rewritten/` 或 shadow 的 `workspace/`
- 想看总览状态：看 `report.json` 或 `shadow-report.json`
- 想排查运行时构建问题：看 `workspace/` 和报告里的构建摘要
