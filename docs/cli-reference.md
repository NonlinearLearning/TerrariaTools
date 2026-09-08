# 删除规则配置参考

## 启动约定

`src/NLISSN/NLISSN.csproj` 不再提供分析 CLI。它只读取当前工作目录中的
`nlissn.yml`，传入任何参数都会失败。

```powershell
Push-Location .\my-analysis
dotnet run --project ..\src\NLISSN\NLISSN.csproj
```

配置中的相对路径相对于 `nlissn.yml` 所在目录解析。所有运行制品均位于
`artifacts.root/<runId>/`；未指定 `artifacts.root` 时为仓库根的 `Build/Result`。
编辑器可关联 [`Miscellaneous/schemas/nlissn.schema.2.json`](../Miscellaneous/schemas/nlissn.schema.2.json)。schema 与运行时均拒绝未知属性；运行时还会一次返回可独立判断的字段诊断，随后才解析路径和执行。

## 配置示例

```yaml
schemaVersion: 2
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
  maxDegreeOfParallelism: 12
  cpgMaxDegreeOfParallelism: 12
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
logging:
  profile: benchmark
  level: debug
  categories: []
  events: []
  view: normal
```

工程输入示例：

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

项目语义下的单文件输入示例：

```yaml
input:
  path: ./src/App/App.cs
  project: ./src/App/App.csproj
  targetFramework: net10.0
  configuration: Debug
  platform: AnyCPU
  restore: disabled
```

## 字段

| 组 | 字段 | 说明 |
| --- | --- | --- |
| 根 | `schemaVersion` | 必须为 `2`。 |
| 根 | `runId` | 必填；仅允许字母、数字、`-`、`_`、`.`。已有运行目录会被拒绝。 |
| `input` | `path` | 必填；现有 `.cs` 文件、目录、`.sln` 或 `.csproj`。`.cs` 同时提供 `project` 时使用真实项目 compilation，并只发布该文档的结果。 |
| `input` | `project` | 可选；对 `.sln` 是 solution 内的项目选择器；对 `.cs` 是拥有目标文档的 `.csproj`。`.csproj` 输入不填写此字段。目标文档不属于项目时返回 `NLISSNWS025`。 |
| `input` | `targetFramework` | 可选；多 TFM 工程必须指定，且必须是工程声明的 TFM。 |
| `input` | `configuration`、`platform` | 可选；MSBuild 全局属性，默认 `Debug`、`AnyCPU`，用于条件编译和工程选择。 |
| `input` | `restore` | `disabled` 或 `enabled`，默认 `disabled`；启用时先执行目标输入的 `dotnet restore`。 |
| `input` | `generatedSources`、`generators` | generated source 默认 `include`；`generators` 默认 `disabled`。设为 `enabled` 时执行 Workspace source-generator pipeline，并传入 MSBuild analyzer references、AdditionalFiles 和 analyzer config；`ProjectReference OutputItemType="Analyzer"` 的所选配置输出必须已构建，缺失时返回 `NLISSNWS024` 并 fail closed，加载器不会隐式 build。禁用时只对非 NuGet、非 .NET SDK 路径的 generator reference 提示 warning。生成源可参与真实 compilation 的语义分析，但永远不写回；`exclude` 只从文档快照排除生成源。 |
| `analysis` | `targetName`、`deleteClass`、`disabledRuleTypes` | 目标名、目标类名和禁用规则类型数组。 |
| `analysis` | `validateBindings` | 默认 `false`；启用规则、CPG 与决策绑定校验。 |
| `analysis` | 四个 `delete...` / `clear...` / `privatize...` 字段 | 全局方法、接口和可见性规则，默认均为 `false`，只有 `true` 才进入 DAG。 |
| `execution` | `writeBack`、`skipRewrite` | 改写控制，默认 `false`。 |
| `execution` | `maxDegreeOfParallelism`、`cpgMaxDegreeOfParallelism` | 正整数；后者省略时继承前者。 |
| `execution` | `directoryParallelism`、`groupParallelism`、`helperParallelism` | 正向并行开关；默认分别为 `true`、`false`、`true`。 |
| `execution` | `fastDeleteClassDirectory`、`filterDeleteClassFilesByTargetName` | 快速目录路径；后者要求前者为 `true` 且有 `deleteClass`。 |
| `artifacts` | `root` | 可选制品根目录。 |
| `artifacts.diff` | `enabled`、`view` | diff 开关和 `legacy` / `readable` 视图。 |
| `artifacts.runtimeLog`、`evidence` | `enabled` | 固定写入各自的类别目录。 |
| `artifacts.rewritePlan` | `mode`、`sourceRunId` | `none`、`capture` 或 `replay`；replay 从 `<root>/<sourceRunId>/RewritePlan` 读取。 |
| `artifacts.analysisLog` | `enabled` | 当前没有 writer；设为 `true` 会明确失败。 |
| `logging` | `profile`、`level`、`categories`、`events`、`view` | 文本日志筛选；必须启用 runtime 或 analysis writer，当前可用 writer 是 runtime。 |

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

类别仅在启用后创建。回放使用新的 `runId` 作为本次运行根，并通过
`rewritePlan.sourceRunId` 读取旧计划，因此不会覆盖 capture 的证据。
回放不重新分析规则图；启用 evidence 时会写入明确标记为 replay 的说明文件。

每次运行还会写入 `resolved-configuration.json`。它记录最终强类型设置、默认值来源、迁移来源和排除运行目录后的 SHA-256 配置指纹；`evidence.json` 包含相同的有效配置投影。字段来源当前为 `explicit` 或 `schema-default`，迁移来源为空数组，供后续 schema 迁移审计。

`rewritePlan.mode: replay` 不得与 `analysis.targetName`、`analysis.deleteClass` 或
`execution.skipRewrite` 共用。默认不写回源文件；`execution.writeBack: true` 才会改写。
