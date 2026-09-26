# 快速开始

## 这篇解决什么问题

本页给出在仓库根目录完成最小健康检查、运行最小 CPG 和运行删除规则宿主的路径。它不讲规则设计和完整命令语义。

## 前置条件

- 使用 `global.json` 固定的 .NET SDK。
- 在 PowerShell 中从仓库根目录运行命令。

## 1. 初始化并检查环境

```powershell
pwsh -File .\Miscellaneous\init.ps1
```

成功时会报告仓库根、SDK 版本，并通过 `src/NLISSN/NLISSN.csproj` 的构建健康检查。

## 2. 运行最小 CPG

NLCPG 从当前工作目录的 `nlissn.yml` 读取配置。先创建一个只输出统计信息的
运行目录：

```yaml
schemaVersion: 3
tool: nlcpg
nlcpg:
  view:
    mode: stats
```

将该内容保存为 `Build\nlcpg-smoke\nlissn.yml`，再构建内置样例：

```powershell
Push-Location .\Build\nlcpg-smoke
dotnet run --project ..\..\src\NLCPG\NLCPG.csproj
Pop-Location
```

成功时标准输出包含 `Nodes:` 和 `Edges:`，后续行按节点类型列出统计值。

CPG 的 shard 持久化是构建器 API 配置，不是当前 CLI 参数；存储布局、恢复和查询限制见[开发者指南](developer-guide.md#streaming-cpg-shard-store)。

## 2.1 按项目导出 CPG JSON

项目级导出使用完整 Roslyn project compilation，并按源文件目录镜像写出 JSON。先在
项目目录的 `nlissn.yml` 中配置输入和 worker 数：

```yaml
schemaVersion: 3
tool: nlcpg-project-export
input:
  path: D:/TRbackup/Version4/TerrariaServer.csproj
  targetFramework: net40
  configuration: Debug
  platform: AnyCPU
  restore: disabled
  generatedSources: exclude
projectExport:
  output: D:/TRbackup/Version4/Build/NLCPG-json
  projectWorkerCount: 1
  resume: false
```

然后从该配置目录运行，不附带任何参数：

```powershell
Push-Location D:\TRbackup\Version4
dotnet run --project D:\ProjectItem\SourceCode\Net\NL\src\NLCPG.ProjectExport\NLCPG.ProjectExport.csproj
Pop-Location
```

例如 `src\Server\Main.cs` 会生成 `src\Server\Main.cs.json`；项目根的 `manifest.json` 保存文件索引、全局节点索引、跨文件边和诊断。生成源通过 `input.generatedSources: include` 加入。

## 3. 运行删除规则宿主

`src/NLISSN/NLISSN.csproj` 只读取当前工作目录的 `nlissn.yml`，不接受分析参数。将配置放在运行目录，例如：

```yaml
schemaVersion: 3
tool: nlissn
runId: first-run
input:
  path: ./source
analysis:
  targetName: TargetName
execution:
  directoryMaxDegreeOfParallelism: 1
  cpgMaxDegreeOfParallelism: 1
  groupMaxDegreeOfParallelism: 1
  helperMaxDegreeOfParallelism: 1
  replayMaxDegreeOfParallelism: 1
  maxConcurrentOperations: 1
artifacts:
  diff:
    enabled: true
```

然后在该目录运行：

```powershell
dotnet run --project ..\src\NLISSN\NLISSN.csproj
```

`input.path` 可以是 `.cs` 文件、目录、`.sln` 或 `.csproj`。工程输入可进一步指定实际分析项目、TFM、MSBuild 配置和生成源策略：

```yaml
input:
  path: ./Sample.sln
  project: src/App/App.csproj
  targetFramework: net10.0
  configuration: Debug
  platform: AnyCPU
  restore: disabled
  generatedSources: include
  generators: enabled
```

只扫描一个文件但需要项目语义时，同时提供源文件和拥有它的项目：

```yaml
input:
  path: ./src/App/App.cs
  project: ./src/App/App.csproj
  targetFramework: net10.0
  configuration: Debug
  platform: AnyCPU
  restore: disabled
```

该模式用完整项目 compilation 解析符号，但只运行和发布 `App.cs` 的分析结果；目标文件不属于指定项目时返回 `NLISSNWS025`。未提供 `input.project` 的 `.cs` 输入保留原来的 best-effort 临时 compilation，不具备项目引用、包引用或条件编译语义。

对 `.sln` 输入，`input.project` 是 solution 内的项目选择器；对 `.cs` 输入，它是拥有该文件的 `.csproj`；`.csproj` 输入不再重复填写 `input.project`。`generatedSources: include` 允许已有或 MSBuild 可见的生成源参与语义分析，但生成源永远不会写回。`generators: enabled` 会执行 Workspace source-generator pipeline，并把 MSBuild 的 analyzer references、AdditionalFiles 和 analyzer config 传给 generator；对 `ProjectReference` 形式的 analyzer，所选 `Configuration`、`Platform` 和 `TargetFramework` 的 DLL 必须已经构建，缺失时以 `NLISSNWS024` fail closed，加载器不会隐式 build。默认 `disabled`，发现外部 generator reference 时只给出提示。默认不写回；只有确认 `Build\Result\<runId>\Diff` 后才将 `execution.writeBack` 设为 `true`。完整字段、全局规则开关和制品布局见 [配置参考](cli-reference.md)。

编辑器可关联 [`Miscellaneous/schemas/nlissn.schema.3.json`](../Miscellaneous/schemas/nlissn.schema.3.json)。该 schema 关闭未知属性；运行时仍执行路径、制品隔离与 replay 互斥校验。NLISSN 仍兼容读取已有 Schema 2 fixture，但 Schema 3 必须使用 `tool: nlissn`。

## 4. 运行回归测试

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
```

测试输出以通过/失败计数结束。需要按层执行时，使用 `pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast`；更窄的验证选择见 [Harness 验证矩阵](harness-verification-matrix.md)。

## 下一步

- 想理解结果：看 [核心概念](concepts.md)。
- 想配置删除规则：看 [配置参考](cli-reference.md)。
- 想开始修改：看 [开发者指南](developer-guide.md)。
