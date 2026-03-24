# 术语表

## 标准流程

`dome run`、`dome analyze`、`dome plan` 这三条 CLI 命令共享的主流程。它们都从工作区加载开始，然后进入分析、规则决策、计划编译，最后根据模式决定是否重写代码。

## 运行时流程

`tr-run` 对应的专用流程。它会先运行标准 Dome 流程生成 `artifacts/report.json`，再准备 `dependency-env/` 和 `workspace/`，最后执行 `dotnet build`。

## shadow extraction

`tr-shadow` 对应的专用流程。它以一个种子成员为起点，构建闭包、重写可达源码、生成 `workspace/`，最后输出 `artifacts/shadow-report.json`。

## Core

仓库的领域核心层，位于 `src/Core/*`。这里放稳定模型、规则模型、分析结果模型和规则服务。Core 不依赖 Adapters。

## Application.Ports

应用层公共契约，位于 `src/Application/Ports/*`。这里定义请求、结果和端口接口，供应用流程和适配器共享。

## Adapter

适配器层实现，位于 `src/Adapters/*`。这里接 Roslyn、JSON 报告、重写器和运行时进程等外部能力。

## Host

面向调用方暴露的应用宿主对象，例如 `DomeApplication`、`TerrariaRuntimeApplication`、`TerrariaRuntimeShadowExtractionApplication`。

## Pipeline

按顺序执行的一组阶段。每个宿主都有自己的 pipeline builder 和 context。

## Analysis target

规则引擎消费的分析目标。目标可以是语句、方法、字段、属性或类型。

## Mark decision

规则引擎输出的单条决策。决策至少包含目标、动作和原因，必要时还带传播链。

## Audit plan

把决策整理、去冲突、排序之后得到的计划结果。标准流程在 `plan` 或 `run` 模式下会产出 `audit-plan.json`。

## Workspace loader

把输入路径转换成 `AnalysisInput` 的组件。当前支持三种偏好：`auto`、`codeanalysis`、`sourceonly`。

## Source-only

直接从 `.cs` 文件或目录收集源码，不依赖 Roslyn 工作区。样例目录通常用这个模式最直接。

## dependency-env

运行时和 shadow 流程中的非 `.cs` 依赖副本目录。它保留构建所需的配置和资源文件。

## workspace

运行时或 shadow 流程用于构建的工作目录。这里会放归并后的源码、项目文件和依赖资源。

## rewritten

标准流程在 `run` 模式下输出的重写源码目录，位于输出根目录下的 `rewritten/`。
