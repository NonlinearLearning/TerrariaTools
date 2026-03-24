# 执行流程

本页按当前组合根和 stage 实现描述三条主流程。

## 标准流程

标准流程由 `apps/Dome.Application/Composition/DomeApplicationComposition.cs` 组装，阶段定义在 `src/Application/Pipeline/DomeApplicationStages.cs`。

### `dome analyze`

1. 加载工作区。
2. 执行 Roslyn 分析。
3. 输出 `analysis.json` 和 `report.json`。
4. 结束，不进入规则和重写阶段。

### `dome plan`

1. 加载工作区。
2. 执行 Roslyn 分析。
3. 构建初始决策和预测决策。
4. 编译 `audit-plan.json`。
5. 输出 `audit-plan.json` 和 `report.json`。
6. 结束，不执行源码重写。

### `dome run`

1. 加载工作区。
2. 执行 Roslyn 分析。
3. 构建初始决策和预测决策。
4. 编译 `audit-plan.json`。
5. 按文档切分重写输入。
6. 并行执行源码重写。
7. 输出 `rewritten/`、`audit-plan.json` 和 `report.json`。
8. 返回成功结果。

## 运行时流程

运行时流程由 `apps/Dome.Application.Runtime/Composition/TerrariaRuntimeComposition.cs` 组装，阶段定义在 `src/Application/UseCases/Runtime/TerrariaRuntimeApplicationStages.cs`。

执行顺序如下：

1. 创建运行时布局。
2. 创建输出目录。
3. 刷新 `dependency-env/`。
4. 调用标准 Dome 流程，把标准产物写入 `artifacts/`。
5. 读取 `artifacts/report.json`。
6. 准备 `workspace/`。
7. 在 `workspace/` 上执行 `dotnet build`。
8. 持久化更新后的报告。
9. 根据构建结果返回成功或失败。

运行时流程的关键点：

- 标准 Dome 产物不会直接写到输出根目录，而是写到运行时布局下的 `artifacts/`。
- `workspace/` 会合并源码、重写结果和非 `.cs` 依赖。
- 最终报告路径是 `artifacts/report.json`。

## Shadow extraction 流程

shadow extraction 流程由 `apps/Dome.Application.ShadowExtraction/Composition/TerrariaRuntimeShadowExtractionComposition.cs` 组装，阶段定义在 `src/Application/UseCases/ShadowExtraction/TerrariaRuntimeShadowExtractionPipelineStages.cs`。

执行顺序如下：

1. 解析输入和输出布局。
2. 对输入执行分析。
3. 以种子成员为起点构建闭包。
4. 写出 shadow 工作区源码。
5. 生成 shadow 报告对象。
6. 在 shadow `workspace/` 上执行构建。
7. 将报告写入 `artifacts/shadow-report.json`。
8. 根据构建结果返回成功或失败。

shadow extraction 的关键点：

- 输入必须包含种子成员名。
- 输出目录同时包含 `workspace/`、`dependency-env/` 和 `artifacts/`。
- 最终报告文件名固定为 `shadow-report.json`。
