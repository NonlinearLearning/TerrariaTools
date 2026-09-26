# 删除规则配置参考

## 统一入口

`NLISSN`、`NLCPG` 和 `NLCPG.ProjectExport` 都从当前工作目录读取固定文件
`nlissn.yml`。三个可执行入口都拒绝命令行参数；包括 `--help` 在内的参数不会
再覆盖配置文件中的值。

Schema 3 用根字段 `tool` 选择入口：

| `tool` | 可执行项目 | 配置分支 |
| --- | --- | --- |
| `nlissn` | `src/NLISSN/NLISSN.csproj` | `input`、`analysis`、`execution`、`artifacts`、`logging` |
| `nlcpg` | `src/NLCPG/NLCPG.csproj` | `input`、`nlcpg` |
| `nlcpg-project-export` | `src/NLCPG.ProjectExport/NLCPG.ProjectExport.csproj` | `input`、`projectExport` |

任何入口读取到其他 `tool` 值都会失败，不会把一份配置解释成另一种工具的配置。
相对路径相对于 `nlissn.yml` 所在目录解析。Schema 2 仍由 NLISSN loader 兼容读取，
但新的配置和仓库示例使用 Schema 3。

## NLCPG

不指定 `input.path` 时，NLCPG 使用内置的最小示例源码并输出统计信息：

```yaml
schemaVersion: 3
tool: nlcpg
nlcpg:
  view:
    mode: stats
```

读取指定源码并输出局部视图时，使用一个 anchor。`anchor` 只能选择
`nodeId`、`fullName` 或 `name` 其中之一；`edgeKinds` 是 YAML 数组，不再是逗号
分隔的命令行字符串：

```yaml
schemaVersion: 3
tool: nlcpg
input:
  path: ./source/Sample.cs
nlcpg:
  view:
    mode: local
    anchor:
      fullName: Demo.Sample.Add:int(int, int)
    hops: 1
    direction: both
    edgeKinds: [Call, Argument]
  output:
    json: ./Build/local-view.json
```

`mode: stats` 不需要 anchor；`mode: local` 必须提供且只能提供一个 anchor。
`hops` 必须为非负整数，`direction` 可以是 `both`、`incoming` 或 `outgoing`。
局部视图 JSON 的父目录会自动创建。

运行时先切换到配置目录：

```powershell
Push-Location .\cpg-run
dotnet run --project ..\src\NLCPG\NLCPG.csproj
Pop-Location
```

## NLCPG 项目 JSON 导出

项目导出把原来的命令行字段放在 `input` 和 `projectExport`：

```yaml
schemaVersion: 3
tool: nlcpg-project-export
input:
  path: D:/TRbackup/Version4/TerrariaServer.sln
  targetFramework: net40
  configuration: Debug
  platform: AnyCPU
  restore: disabled
  generatedSources: exclude
projectExport:
  output: D:/TRbackup/Version4/Build/NLCPG-json
  projectWorkerCount: 12
  resume: false
```

字段映射如下：

| YAML 字段 | 说明 |
| --- | --- |
| `input.path` | 必填；项目 `.csproj` 或解决方案 `.sln` 路径，替代旧的 `--project`。 |
| `projectExport.output` | 输出根目录；省略时为项目目录下的 `Build/NLCPG-json`。 |
| `input.targetFramework` | 多目标项目或兼容性项目选择目标框架。 |
| `input.configuration`、`input.platform` | MSBuild 配置，默认 `Debug`、`AnyCPU`。 |
| `input.restore` | `disabled` 或 `enabled`，默认关闭。 |
| `input.generatedSources` | `include` 或 `exclude`，默认排除生成源。 |
| `projectExport.projectWorkerCount` | 每个文档内部构图（`CpgWorkBatchExecutor`）的 DOP 请求数，必须为正整数；实际上限为 12。逐文档导出不再有项目级 worker 池。 |
| `projectExport.resume` | 是否复用已有 JSON payload，默认 `false`。 |

运行：

```powershell
Push-Location D:\TRbackup\Version4
dotnet run --project D:\ProjectItem\SourceCode\Net\NL\src\NLCPG.ProjectExport\NLCPG.ProjectExport.csproj
Pop-Location
```

### 输出布局

`.sln` 输入会导出**全部** C# 项目，每个项目一个以项目名命名的子目录，
子目录内镜像该项目的源目录；`.csproj` 输入保持既有的扁平镜像。
`manifest.json` 始终位于输出根目录，是唯一一次导出的入口：

```
<output>/
  manifest.json
  App/App.cs.json                  # .sln：项目子目录
  App/Generated/Helper.cs.json
  Library/Library.cs.json
```

manifest 保存文件索引、全局节点索引、跨文件边和 Workspace/Roslyn 诊断，
并以项目维度汇总：

| manifest 字段 | 说明 |
| --- | --- |
| `status` | `complete` 或 `incomplete`；存在失败文件或失败项目即 `incomplete`。 |
| `project` | 首个项目的条目，保持单项目消费方兼容。 |
| `projects[]` | 每个项目一项：`name`、`path`、`outputDirectory`、`status`、`totalDocumentCount`、`writtenFileCount`、`failedFileCount`、`files[]`。 |
| `totalDocuments`、`writtenFiles`、`failedFiles` | 文档与产出的汇总计数。 |
| `files[]` | 全部 payload 的 `sourcePath` / `outputPath` / `status` / `nodeCount` / `edgeCount` / `error`。 |
| `nodes[]`、`edges[]` | 跨项目并集的全局节点与边。 |

**验收必须看 `manifest.json`**：`status: complete` 且 `failedFiles: 0` 才算一次
完整导出。导出遵循"不中断丢结果"：正常结束、取消或致命异常都会写出 manifest，
已完成的 payload 保持原样；被强杀（如断电、`taskkill /F`）时不保证写出，
此时用 `resume: true` 重跑即可复用已有 payload 补齐。

个别文件失败（例如项目根之外的生成文件）只记为 `status: failed` 并产出
`NLCPGEXP002`，导出继续；整个项目失败记 `NLCPGEXP003`，其余项目继续。

导出实现是 `src/NLISSN.Infrastructure/ProjectJson/` 下的 `NLCPG.ProjectJson` 组件；
`NLCPG.ProjectExport` 只剩薄 CLI。同一组件也可由 NLISSN 运行内联触发：
在 NLISSN 配置的 `artifacts.projectJson` 中设置 `enabled: true`（复用同一 `input`，
默认输出到 `<runRoot>/ProjectJson`）。

## NLISSN

一个完整的 Schema 3 NLISSN 配置如下：

```yaml
schemaVersion: 3
tool: nlissn
runId: playerinput-dop12
input:
  path: ./source
analysis:
  targetName: PlayerInput
  deleteClass: PlayerInput
  disabledRuleTypes: []
  validateBindings: false
  deleteUnreachableMethods: false
  deleteUnreferencedMethods: false
  clearUnusedInterfaceImplementations: false
  privatizeInternalOnlyPublicMethods: false
execution:
  writeBack: false
  skipRewrite: false
  directoryMaxDegreeOfParallelism: 4
  cpgMaxDegreeOfParallelism: 12
  groupMaxDegreeOfParallelism: 4
  helperMaxDegreeOfParallelism: 4
  replayMaxDegreeOfParallelism: 4
  maxConcurrentOperations: 8
  directoryParallelism: true
  groupParallelism: false
  helperParallelism: true
  fastDeleteClassDirectory: false
  filterDeleteClassFilesByTargetName: false
artifacts:
  root: ./Build/Result
  diff: { enabled: true, view: legacy }
  runtimeLog: { enabled: true }
  evidence: { enabled: true }
  rewritePlan: { mode: none }
  analysisLog: { enabled: false }
  projectJson: { enabled: false, projectWorkerCount: 12, resume: false }
logging:
  profile: benchmark
  level: debug
  categories: []
  events: []
  view: normal
```

工程输入和单文件项目语义输入仍使用 `input` 字段：

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

`input.path` 可以是 `.cs` 文件、目录、`.sln` 或 `.csproj`。`.cs` 与
`input.project` 同时提供时，使用真实项目 compilation，但只发布目标文档的结果。
`.sln` 的 `input.project` 选择 solution 内项目；`.csproj` 输入不填写该字段。

## NLISSN 字段

| 组 | 字段 | 说明 |
| --- | --- | --- |
| 根 | `schemaVersion`、`tool` | 新配置必须为 `3` 和 `nlissn`。 |
| 根 | `runId` | 必填；仅允许字母、数字、`-`、`_`、`.`；已有运行目录会被拒绝。 |
| `input` | `path` | 必填；现有 `.cs` 文件、目录、`.sln` 或 `.csproj`。 |
| `input` | `project` | 可选的 solution 项目选择器或单文件 owning project。 |
| `input` | `targetFramework`、`configuration`、`platform` | Workspace 和 MSBuild 选项。默认配置为 `Debug`、`AnyCPU`。 |
| `input` | `restore`、`generatedSources`、`generators` | 依赖恢复、生成源和 source generator 策略。 |
| `analysis` | `targetName`、`deleteClass`、`disabledRuleTypes` | 分析目标和禁用的规则类型。 |
| `analysis` | `validateBindings` | 默认 `false`；启用规则、CPG 与决策绑定校验。 |
| `analysis` | 四个规则开关 | 不可达方法、未引用方法、接口实现和内部方法相关删除开关，默认均为 `false`。 |
| `execution` | `writeBack`、`skipRewrite` | 改写控制，默认 `false`。 |
| `execution` | 六个 `*MaxDegreeOfParallelism` / `maxConcurrentOperations` | 六个彼此独立的必填正整数；旧的 `maxDegreeOfParallelism` 已删除。 |
| `execution` | `directoryParallelism`、`groupParallelism`、`helperParallelism` | 并行行为开关，默认分别为 `true`、`false`、`true`。 |
| `execution` | `fastDeleteClassDirectory`、`filterDeleteClassFilesByTargetName` | delete-class 快速路径；过滤开关要求快速路径和 `deleteClass`。 |
| `artifacts` | `root`、`diff`、`runtimeLog`、`performance`、`evidence`、`analysisLog`、`rewritePlan`、`projectJson` | 运行制品、日志、性能摘要、证据、rewrite plan 和项目级 JSON 导出。 |
| `artifacts.projectJson` | `enabled`、`output`、`projectWorkerCount`、`resume` | 分析完成后对同一 `input` 执行 CPG 项目级 JSON 导出；默认关闭，需要项目或解决方案输入。`output` 省略时为 `<runRoot>/ProjectJson`。 |
| `logging` | `profile`、`level`、`categories`、`events`、`view` | 文本日志筛选。启用日志时必须有可用 writer。 |

旧的 `execution.maxDegreeOfParallelism` 不再支持，出现时会按未知属性拒绝，
不会继承或映射到任何新字段。

## 制品与回放

一次运行只写入一个隔离目录：

```text
Build/Result/<runId>/
  Diff/
  RuntimeLog/runtime.log
  Evidence/evidence.json
  RewritePlan/
  resolved-configuration.json
```

`rewritePlan.mode: replay` 不得与 `analysis.targetName`、`analysis.deleteClass` 或
`execution.skipRewrite` 共用。默认不写回源文件；只有
`execution.writeBack: true` 才会改写。`resolved-configuration.json` 记录最终强类型
设置、默认值来源、配置版本和 SHA-256 配置指纹。

Schema 文件见 [`Miscellaneous/schemas/nlissn.schema.3.json`](../Miscellaneous/schemas/nlissn.schema.3.json)。
