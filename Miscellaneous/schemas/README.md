# NLISSN 配置 Schema

本目录保存统一 `nlissn.yml` 配置文件的机器可读格式契约。当前配置 Schema 是
[`nlissn.schema.3.json`](nlissn.schema.3.json)；[`nlissn.schema.2.json`](nlissn.schema.2.json)
保留给 NLISSN 的历史兼容配置。

## 它解决什么问题

Schema 是配置格式的说明书，不是删除规则、CPG 构建器或配置执行器。它让支持
JSON Schema 的编辑器和工具能够在运行前知道：

- 哪些顶层字段和嵌套字段允许出现；
- 哪些字段是必填项；
- 字段应该使用什么类型；
- 字段可以使用哪些枚举值；
- 哪些字段有默认值提示；
- 哪些拼写错误或未知属性应该被拒绝。

配置文件实际使用 YAML，但 YAML 的对象、数组、字符串、数字和布尔值可以用
同一份 JSON Schema 描述。因此编辑器可以用这个 JSON 文件检查 `nlissn.yml`，不
需要把配置改写成 JSON。

## Schema 3 与工具分流

`nlissn.schema.3.json` 使用 JSON Schema Draft 2020-12，格式版本固定为
`schemaVersion: 3`。根字段 `tool` 必须是 `nlissn`、`nlcpg` 或
`nlcpg-project-export` 之一：

| `tool` | 主要字段 |
| --- | --- |
| `nlissn` | `runId`、`input`、`analysis`、`execution`、`artifacts`、`logging` |
| `nlcpg` | 可选 `input`、`nlcpg.view`、`nlcpg.output` |
| `nlcpg-project-export` | `input`、`projectExport` |

三个可执行入口都从当前工作目录读取 `nlissn.yml` 并拒绝全部命令行参数。相对路径
相对于配置文件所在目录解析；入口读取到不匹配的 `tool` 时会 fail closed。

NLISSN loader 仍接受没有 `tool` 字段的 Schema 2 文档，以保留现有测试 fixture 和
历史运行目录的兼容性。Schema 3 的 NLISSN 文档必须显式写 `tool: nlissn`。

### `nlcpg` 分支

`nlcpg.view.mode` 为 `stats` 时输出图统计；为 `local` 时必须提供一个只包含
`nodeId`、`fullName` 或 `name` 之一的 `anchor`。局部查询还可以配置非负的
`hops`、`direction` 和 `edgeKinds` 数组。`nlcpg.output.json` 只适用于局部视图，
路径相对于配置文件解析。

### `nlcpg-project-export` 分支

`input.path` 指向待导出的 `.csproj`，`input` 还可设置 `targetFramework`、
`configuration`、`platform`、`restore` 和 `generatedSources`。`projectExport` 提供
`output`、`projectWorkerCount` 和 `resume`；`projectWorkerCount` 的有效上限是 12。

### NLISSN 分支

NLISSN 分支要求以下字段：

| 字段 | 作用 |
| --- | --- |
| `schemaVersion` | 配置格式版本，必须是 `3`。 |
| `tool` | 必须为 `nlissn`。 |
| `runId` | 本次运行的标识，只允许字母、数字、下划线、点和连字符。 |
| `input` | 输入文件、目录、解决方案或项目，以及 MSBuild/Workspace 选项。 |
| `analysis` | 删除规则、目标名称和分析开关。 |
| `execution` | 并行度、写回和 rewrite 执行选项。 |
| `artifacts` | 运行制品、diff、证据、日志、rewrite plan 和 CPG 项目级 JSON 导出的输出设置。 |
| `logging` | 可选的日志 profile、级别、分类、事件和视图设置。 |

根对象和各个嵌套对象都关闭未知属性。也就是说，下面的拼写错误不会被当作
`targetName` 静默接受：

```yaml
analysis:
  targtName: TargetName
```

### `input`

`input.path` 是必填项，可以指向：

- 单个 `.cs` 文件；
- 源码目录；
- `.sln` 解决方案；
- `.csproj` 项目。

其他常用字段包括：

| 字段 | 作用 |
| --- | --- |
| `project` | `.sln` 输入时选择解决方案内的项目；单个 `.cs` 输入时指定拥有它的 `.csproj`。 |
| `targetFramework` | 多目标项目中选择目标框架。 |
| `configuration` | MSBuild 配置，默认 `Debug`。 |
| `platform` | MSBuild 平台，默认 `AnyCPU`。 |
| `restore` | 是否允许 Workspace 恢复依赖，默认 `disabled`。 |
| `generatedSources` | 是否让已有或 MSBuild 可见的生成源参与语义分析，默认 `include`。 |
| `generators` | 是否执行 source-generator pipeline，默认 `disabled`。 |

生成源可以参与语义分析，但不会被写回物理源码或放入物理 rewrite plan。

### `analysis`

这里配置分析目标和规则开关，包括 `targetName`、`deleteClass`、禁用的规则类型、
绑定校验，以及不可达方法、未引用方法、接口实现和内部方法相关的删除选项。

### `execution`

`execution` 要求以下六个彼此独立的正整数：

- `directoryMaxDegreeOfParallelism`：目录文件分析的最大并发数；
- `cpgMaxDegreeOfParallelism`：单个 CPG builder 和 CPG lease 的最大并发数；
- `groupMaxDegreeOfParallelism`：规则 DAG、冲突域和规则组阶段的最大并发数；
- `helperMaxDegreeOfParallelism`：helper 扫描的最大并发数；
- `replayMaxDegreeOfParallelism`：rewrite plan replay 的最大并发数；
- `maxConcurrentOperations`：通用 operation admission 的最大并发数。

旧的 `execution.maxDegreeOfParallelism` 已删除，出现时会因未知属性被拒绝，不会
继承或映射到任何新字段。其他字段控制：

- 是否写回源码（`writeBack`）；
- 是否跳过 rewrite（`skipRewrite`）；
- CPG、目录、分组和 helper 的并行行为；
- delete-class 快速路径及目标名称过滤。

### `artifacts` 和 `logging`

`artifacts` 控制运行根目录以及 diff、runtime log、evidence、analysis log、
rewrite plan 和 `projectJson`。`logging` 控制日志输出的详细程度和视图。运行结束后生成的
`resolved-configuration.json` 会记录最终生效的配置、字段来源和配置指纹，适合
审计“实际运行的是什么设置”。

`artifacts.projectJson` 在 NLISSN 分析完成后，对同一 `input` 指向的项目或解决方案
执行 CPG 项目级 JSON 导出，复用 NLCPG.ProjectJson 组件。`.sln` 输入导出全部项目，
每个项目一个以项目名命名的子目录、其下镜像该项目源目录；`.csproj` 输入保持扁平镜像。
输出根目录始终有 `manifest.json`，其 `status`（`complete`/`incomplete`）与
`failedFiles` 是判定导出是否完整的依据。它默认关闭；需要一个项目或解决方案输入
（单文件输入无法做项目级导出）。`output` 省略时写入 `<runRoot>/ProjectJson`。

## 一个最小配置示例

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

程序从当前工作目录读取 `nlissn.yml`。仓库示例位于 `Miscellaneous/nlissn.yml`，从仓库根目录运行时先切换到该目录：

```powershell
Push-Location .\Miscellaneous
dotnet run --project ..\src\NLISSN\NLISSN.csproj
Pop-Location
```

配置中的相对路径相对于 `nlissn.yml` 所在目录解析。

## 编辑器校验与运行时校验的关系

编辑器可以关联本文件，为 YAML 提供补全和静态诊断。运行时则由
[`YamlConfigurationLoader`](../../src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs)
重新读取 `nlissn.yml`，并执行 YAML 语法、重复键、字段值、路径、制品隔离和
rewrite replay 约束等校验。

因此：

1. Schema 是编辑器和工具使用的格式契约。
2. `YamlConfigurationLoader` 是程序运行时的实际加载和诊断入口。
3. Schema 中的 `default` 是默认值提示；不同 JSON Schema 工具不一定会自动修改
   配置文件。实际运行值应以 `resolved-configuration.json` 为准。
4. 通过编辑器校验不代表输入路径、项目引用、MSBuild 条件或制品目录一定有效；
   这些只能由运行时结合当前环境进一步判断。

## 版本和修改规则

修改配置格式时需要同时考虑：

- 更新或新增对应的 `nlissn.schema.<version>.json`；
- 更新 `YamlConfigurationLoader` 及其强类型配置模型；
- 更新快速开始、CLI 参考和开发者指南中的示例；
- 更新配置契约测试；
- 明确旧版本配置是否继续兼容，不能只修改 Schema 文件而不修改运行时。

如果只是增加一个兼容的可选字段，也必须保证编辑器 Schema、运行时模型和运行时
诊断三者保持一致。

## 不要混淆的其他 SchemaVersion

项目其他位置也有 `SchemaVersion`，例如 CPG shard/catalog 持久化格式和 rewrite-plan
制品格式。它们用于二进制或制品兼容性，不是本目录的 NLISSN YAML 配置 Schema，
不能因为配置版本是 `2` 就修改那些持久化版本。

相关入口：

- 配置文件名和加载：[`YamlConfigurationLoader.cs`](../../src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs)
- CLI 配置参考：[`docs/cli-reference.md`](../../docs/cli-reference.md)
- 快速开始：[`docs/quick-start.md`](../../docs/quick-start.md)
