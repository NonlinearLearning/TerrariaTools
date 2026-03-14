# Dome v1.1

`dome` 是一个基于 Roslyn 的计划驱动代码分析与改写工具，固定执行链为：

`Analysis -> Mark -> Plan -> Rewrite -> Report`

`Plan` 是执行真源。`Rewrite` 只执行 `audit-plan.json` 中已经编译好的动作，不会在执行阶段再次推导新规则。

## 快速开始

执行前要求：

- 工作目录固定为 `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome`
- SDK 由 [global.json](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/global.json) 固定为 `.NET 10 preview`
- 详细执行说明见 [rapid-build-test.md](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/rapid-build-test.md)

构建 CLI：

```powershell
dotnet build .\src\Cli\Dome.Cli.csproj -nologo
```

执行完整流程：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples .\out
```

只生成分析结果：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- analyze .\samples .\out
```

只生成计划，不执行改写：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples .\out
```

通过配置文件运行：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- --config .\dome.config.json
```

## 最短闭环演示

如果你要直接证明 `dome` 不只是“能出计划”，而是真的会删掉被标记的代码，先看这两组仓内样本：

- 纯 statement direct hit：
  - 样本：[samples\closed-loop\Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\samples\closed-loop\Player.cs)
  - 说明：[closed-loop-demo.md](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\docs\closed-loop-demo.md)
- expression projection + propagation：
  - 样本：[samples\expression-loop\Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\samples\expression-loop\Player.cs)
  - 说明：[closed-loop-demo.md](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\docs\closed-loop-demo.md)

纯 statement 样本的最短命令：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\closed-loop .\.tmp\closed-loop-demo\plan
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\closed-loop .\.tmp\closed-loop-demo\run
```

看结果时固定顺序：

1. 看 `audit-plan.json`
2. 看 `report.json`
3. 看 `rewritten\*.cs`

当前仓内已经生成好的证据产物：

- [closed-loop audit-plan.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\plan\audit-plan.json)
- [closed-loop report.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\run\report.json)
- [closed-loop rewritten Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\run\rewritten\Player.cs)
- [expression-loop audit-plan.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\plan\audit-plan.json)
- [expression-loop report.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\run\report.json)
- [expression-loop rewritten Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\run\rewritten\Player.cs)

## 命令

- `run <input-path> <output-path>`
  执行分析、标记、计划编译、改写和报告输出。
- `analyze <input-path> <output-path>`
  输出 `analysis.json` 和 `report.json`。
- `plan <input-path> <output-path>`
  输出 `audit-plan.json` 和 `report.json`。
- `--config <path>`
  从 JSON 配置文件加载同样的输入参数。

`input-path` 可以是单个 `.cs` 文件，也可以是目录树。对于目录输入，`audit-plan.json` 和 `rewritten/**` 会保留相对路径结构。

## 配置文件

最小配置示例：

```json
{
  "Command": "run",
  "InputPath": "D:\\input",
  "OutputPath": "D:\\output",
  "RuleSet": [],
  "LogLevel": "Info"
}
```

当前接受的字段：

- `Command`：`run`、`analyze`、`plan`
- `InputPath`：输入文件或目录
- `OutputPath`：输出根目录
- `RuleSet`：保留字段，v1.1 会接受但不做细粒度规则开关
- `LogLevel`：保留字段，v1.1 会接受但不做复杂日志分层

## 当前规则基线

v1.1 固化的规则能力：

- statement seed / propagation / sanitization
- high-risk / object-initializer protection
- method delete / add-return
- class delete
- expression-to-statement projection

当前固定的 `RuleId` 集合：

- `dome:delete`
- `controlflow-mark`
- `dataflow-propagation`
- `function-mark`
- `class-mark`
- `expression-mark`

## 支持的目标粒度

当前只支持以下 `TargetKind`：

- `Statement`
- `Method`
- `Class`

其中：

- `Statement` 是当前 rewrite 的主粒度
- `Method` 支持 `Delete` 和 `AddReturn`
- `Class` 当前只支持 `Delete`

## 输出产物

`analyze`

- `analysis.json`
- `report.json`

`plan`

- `audit-plan.json`
- `report.json`

`run`

- `audit-plan.json`
- `report.json`
- `rewritten/**`

稳定契约说明见 [architecture.md](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/architecture.md) 和 [artifacts.md](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/artifacts.md)。

## 退出码

- `0`：成功
- `1`：CLI 参数或配置解析失败
- `2`：`WorkspaceLoadFailed`
- `3`：`AnalysisFailed`
- `4`：`PlanCompileFailed`
- `5`：`RewriteFailed`
- `6`：`ReportFailed`

## 失败语义

`report.json` 是主要失败摘要产物。重点字段包括：

- `FailureCode`
- `FailureSummary`
- `ConflictSummaries`
- `RiskSummary`
- `PlanCoverageSummary`
- `GeneratedArtifacts`

典型失败场景：

- 没有找到任何 C# 输入文件
- 分析阶段异常
- 计划存在未解决冲突
- 改写目标成员不存在
- 改写目标的 span 不匹配
- 改写目标的文本不匹配
- 动作和目标类型组合不受支持

`RewriteFailed` 时，系统仍会尽量保留 `audit-plan.json` 和 `report.json`。

## 多文件输入示例

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\input-project .\out
```

如果 `input-project` 包含：

- `Root.cs`
- `Features\Nested.cs`

则 `run` 会输出：

- `out\audit-plan.json`
- `out\report.json`
- `out\rewritten\Root.cs`
- `out\rewritten\Features\Nested.cs`

## 当前非目标

以下内容明确不属于当前版本范围：

- expression-level target
- expression-level rewrite
- initializer rewrite
- struct / record / interface / enum target
- dynamic call graph
- checkpoint / 断点续跑
- 原地改写
- 动态插件
- 完整 CFG / 符号执行平台
- 类型冲突分析

## 相关文档

- [架构说明](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/architecture.md)
- [产物契约](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/dda/dome/docs/artifacts.md)
- [原始设计记录](/D:/ProjectItem/SourceCode/Net/TerrariaTools/docs/plans/2026-03-12-dome-architecture-design.md)
