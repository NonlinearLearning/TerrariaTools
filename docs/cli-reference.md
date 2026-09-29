# 删除规则配置参考

## 统一入口

`NLISSN` 和 `NLCPG` 都从当前工作目录读取固定文件 `nlissn.yml`。两个可执行入口都
拒绝命令行参数；包括 `--help` 在内的参数不会再覆盖配置文件中的值。

Schema 3 用根字段 `tool` 选择入口：

| `tool` | 可执行项目 | 配置分支 |
| --- | --- | --- |
| `nlissn` | `src/NLISSN/NLISSN.csproj` | `input`、`analysis`、`execution`、`artifacts`、`logging` |
| `nlcpg` | `src/NLCPG/NLCPG.csproj` | `input`、`nlcpg` |

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

## CPG 项目 JSON 导出

项目级 JSON 导出是 **NLISSN 运行的后置步骤**，由 `artifacts.projectJson` 开关控制。
它没有独立可执行入口：导出实现在 `src/NLISSN.Infrastructure/ProjectJson/` 下的
`NLCPG.ProjectJson` 组件，NLISSN 在分析完成后内联调用它，并复用同一份
`input`（项目或解决方案路径、`targetFramework`、`configuration`、`platform`、
`restore`、`generatedSources`）。

```yaml
schemaVersion: 3
tool: nlissn
runId: version4-cpg-export
input:
  path: D:/TRbackup/Version4/TerrariaServer.csproj
  targetFramework: net40
  configuration: Debug
  platform: AnyCPU
  restore: disabled
  generatedSources: exclude
analysis:
  targetName: TargetName
execution:
  directoryMaxDegreeOfParallelism: 8
  cpgMaxDegreeOfParallelism: 8
  groupMaxDegreeOfParallelism: 8
  helperMaxDegreeOfParallelism: 8
  replayMaxDegreeOfParallelism: 8
  maxConcurrentOperations: 8
artifacts:
  root: ./Build/Result
  projectJson:
    enabled: true
    output: D:/TRbackup/Version4/Build/NLCPG-json
    projectWorkerCount: 8
    requestedCapabilities: ["InterproceduralDataFlow"]
```

字段映射如下：

| YAML 字段 | 说明 |
| --- | --- |
| `artifacts.projectJson.enabled` | 必填；`true` 才在分析后执行导出，默认 `false`（完全不进入该步骤）。 |
| `artifacts.projectJson.output` | 输出根目录；省略时为 `<runRoot>/ProjectJson`。 |
| `artifacts.projectJson.projectWorkerCount` | 正整数，默认 12，实际取值上限为 12；它经 `EffectiveMaxDegreeOfParallelism` 作为**单文档内部** builder 的并行度（`CpgWorkBatchExecutor` 的 DOP）。文档之间串行处理，故该值不构成跨文档并行度。大文件上它同时决定峰值内存，内存受限时建议设 1。 |
| `artifacts.projectJson.documentShardCount` | 正整数，默认 1（即每个源文件写出单个 `<rel>.cs.json`）。大于 1 时，**单个文档**的 payload 被按节点连续区间切成至多该数量的分片，文件名形如 `<rel>.cs.part-000.json`。`manifest.files[]` 会为每个分片各记一条（`sourcePath` 相同、`outputPath` 不同）。实际分片数还会被文档节点数收敛，不会产出空分片。**分片不是节点的精确划分**：边归其源节点所在片，目标若在别片则该目标节点会被复制进该片作为"边界节点"，故各片节点数之和 ≥ 文档节点数、各片字节数之和 > 单文件时的字节数。实测（591023 节点文档）：8 片时节点记录膨胀 **109%**、payload 字节膨胀 **40%**，各片大小极不均（最小 73878 / 最大 279682 节点）——因为节点按 `NodeId`（内容哈希）排序，连续区间相对边拓扑等价于随机划分。⚠ 该开关的**内存收益尚未被实测证实**：端到端测量中导出阶段的峰值由分析/构图主导（导出关闭时峰值 1210 MB，开启并分片 8 片约 1399 MB、不分片约 1459 MB，5 组配对样本方向一致但仅约 −4%），而投影与写盘耗时反而上升。 |
| `artifacts.projectJson.documentShardParallelism` | 正整数，默认 1。单文档各分片的**并行度**（物化 + 落盘）。1 = 逐片串行（默认，与引入前逐字节相同）；>1 时最多该数量的分片同时在途。只在 `documentShardCount` > 1 时有实际作用。**不改变任何输出**：实测同一 8 片配置下，并行（4）与串行的 payload 逐文件 SHA-256 全部相同，manifest 仅 `outputRoot` 因运行目录不同而不同。⚠ 代价是多片 DTO 同时驻留，峰值高于串行（实测 1442 → 1528 MB，约 +6%）。⚠ **实测净收益接近零**：投影 8474 → 4183 ms（约 2.0x 加速），但写盘 3434 → 6705 ms（约 2.0x 变慢），总墙钟基本持平——并行写手争用同一磁盘。故默认保持 1；除非落盘在更快介质或分片落到不同卷，否则不建议开启。<br>`> 1` 时该值只是**条数**上限；实际同时在途还受 `artifacts.projectJson` 所用的内存预算约束（`ProjectMemoryBudgetBytes`，默认取物理内存的一半），两者取小。片大小本就不均（实测 8 片时节点数 3.8x 偏斜），故按**估算字节**逐片记账而非只限条数；超额度时**等待**而不失败，单片额度超过整个预算时仍放行（否则最大片永远排不到）。⚠ **该闸门在默认预算下恒不生效**：各片额度之和恒等于整篇文档估算（= 源码字节 × 2000 + 48 MB），而语料最大文档 `NPC.cs` 仅 2.05 MB ⇒ 估算 4.05 GB < 默认预算约 6.93 GB；换算即源码需 > 约 3.55 MB 才会阻塞。故它今天是**安全网与观测面**，不是性能开关。 |
| `artifacts.projectJson.requestedCapabilities` | 字符串数组，默认空 `[]`。导出侧单文档构图在 `NLCPGBuilderOptions.CreateDefault()` 的默认能力集（`NLCPGCapability.Default` = `SyntaxSemantic \| MethodModel \| CallTargets \| Cfg \| DataFlow \| QueryIndex \| SyntaxToken \| Reference \| TypeRef`）**之外额外请求**的 `NLCPGCapability` 名称。空数组与省略等价，都沿用默认能力集。写 `["InterproceduralDataFlow"]` 会让 `RunInterproceduralDataFlowPass` 真正运行并发布 `InterproceduralDataFlow` 桥接边（`ArgumentToParameter` / `ReturnToMethodReturn` / `MethodReturnToCallResult`），这正是**默认导出图中完全没有**的边种类（实测默认产物中该 `kind` 计数为 **0**）。⚠ **这是契约变更而非度量开关**：能力位决定哪些后置阶段运行，新增节点与边会改变 `NodeId`/`localNodeId`，整份 payload 都会与默认基线不同，需独立输出目录做对照。⚠ 它同样抬高峰值内存——`InterproceduralDataFlow` 闭包出 `DataFlow \| CallTargets \| MethodModel \| QueryIndex`，其计划载体历史上曾占 LOH 分配的榜首。⚠ 非法名**硬报错**（不静默忽略），有效值即 `NLCPGCapability` 的全部成员名。 |
| `input.path` | 触发导出时必须是项目 `.csproj` 或解决方案 `.sln`；单文件或目录输入没有项目级导出。 |



`artifacts.projectJson` 需要一个项目或解决方案输入。**这个前提在配置加载阶段就校验**
（诊断码 `NLISSN139`）：目录或独立 `.cs` 文件会在解析配置时立刻失败，而不是先跑完整轮
分析、再在导出步骤才报错。显式写出非正的 `projectWorkerCount` 或 `documentShardCount`
也会被拒绝（`NLISSN119`），不会被静默改写成默认值。

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

manifest 保存文件索引、项目汇总和 Workspace/Roslyn 诊断，
并以项目维度汇总：

| manifest 字段 | 说明 |
| --- | --- |
| `status` | `complete` 或 `incomplete`；存在失败文件或失败项目即 `incomplete`。 |
| `project` | 首个项目的条目，保持单项目消费方兼容。 |
| `projects[]` | 每个项目一项：`name`、`path`、`outputDirectory`、`status`、`totalDocumentCount`、`writtenFileCount`、`failedFileCount`、`files[]`。 |
| `totalDocuments`、`writtenFiles`、`failedFiles` | 汇总计数。`totalDocuments` 与 `failedFiles` 按**文档**计；`writtenFiles` 按**写出的 payload 文件**计。分片导出时一个文档产出多片，故 `writtenFiles` 会大于文档数（`documentShardCount` 为默认 1 时两者相等）。 |
| `files[]` | 全部 payload 的 `sourcePath` / `outputPath` / `status` / `nodeCount` / `edgeCount` / `error`。分片导出时同一 `sourcePath` 会出现多条（`outputPath` 为各 `part-NNN`，`nodeCount`/`edgeCount` 为**该片**的计数，不是文档合计）。 |
| `diagnostics[]` | Workspace 与 Roslyn 诊断。 |

**manifest 不包含全局 `nodes[]` / `edges[]`。** 节点与边只存在于各自的 payload 内：
每个 `*.cs.json` 自带该文件的 `nodes[]` / `edges[]`，`files[].nodeCount` / `edgeCount`
给出索引。此前 manifest 还冗余承载一份「全部文件去重并集」，那要求在写出前先累积全部
图数据，只能落在内存（随文件数增长）或磁盘暂存库（实测峰值约 5.3 GB/次）；两者都不要，
故该产物已移除。节点 id 是内容派生的且含文件路径（`node-` + SHA-256），
跨文件引用可直接用 payload 内的 id 解析。

**验收必须看 `manifest.json`**：`status: complete` 且 `failedFiles: 0` 才算一次
完整导出。导出遵循"不中断丢结果"：正常结束、取消或致命异常都会写出 manifest，
已完成的 payload 保持原样；被强杀（如断电、`taskkill /F`）时不保证写出，
此时重新运行整次导出即可（导出不复用既有 payload，每次都全量重算）。

个别文件失败（例如项目根之外的生成文件）只记为 `status: failed` 并产出
`NLCPGEXP002`，导出继续；整个项目失败记 `NLCPGEXP003`，其余项目继续。

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
  projectJson: { enabled: false, projectWorkerCount: 12 }
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
| `artifacts.projectJson` | `enabled`、`output`、`projectWorkerCount`、`documentShardCount`、`documentShardParallelism`、`performanceDiagnostics`、`requestedCapabilities` | 分析完成后对同一 `input` 执行 CPG 项目级 JSON 导出；默认关闭，需要项目或解决方案输入。`output` 省略时为 `<runRoot>/ProjectJson`。 |
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
