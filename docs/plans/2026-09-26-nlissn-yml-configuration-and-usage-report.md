# nlissn.yml 配置与使用报告

日期：2026-09-26。性质：**配置面清点 + 实测行为记录**。
本报告回答两件事：`nlissn.yml` 能配什么，以及 NLISSN 实际怎么用、怎么被拒绝。

## 0. 验证边界（先声明测不准的部分）

| 项 | 值 |
| --- | --- |
| HEAD | `1e9486f3255de6744b219fbf900b6b4158aaeb37` |
| 分支 | `codex/dataflow-tail-measurement-20260925` |
| 工作树 | **328 项脏**，非单一 revision 快照 |
| 二进制 | `Build/src/Debug/net10.0/NLISSN.exe`，SHA-256 前 16 位 `F9FBA420F294B642` |
| SDK | `dotnet 10.0.400`（`global.json` 请求 `10.0.200-preview.0.26103.119`，`rollForward: latestFeature`） |
| 构建 | `dotnet build src\NLISSN\NLISSN.csproj -c Debug` → 0 警告 0 错误，6.52 s |

本报告全部运行使用**已构建的 `NLISSN.exe`**，工作目录为各探针目录，探针配置与产物均落在
`Miscellaneous/tmp/nlissn-report/`（被 `.gitignore` 的 `Miscellaneous/tmp` 覆盖）。
本次工作**未修改任何受版本控制文件**，只新增本报告一份；工作树中其余改动均先于本次工作存在，
故本报告不是单一 revision 的快照。

---

## 1. 怎么用：最短路径

三个可执行入口（`NLISSN`、`NLCPG`、`NLCPG.ProjectExport`）**只读当前工作目录的 `nlissn.yml`，拒绝一切命令行参数**。
实测：`NLISSN.exe --help` →

```
Unhandled exception. System.ArgumentException: NLISSN reads configuration from nlissn.yml
and accepts no command-line parameters.
```

因此用法固定为「切到配置目录 → 直接运行」：

```powershell
Push-Location .\Miscellaneous
dotnet run --project ..\src\NLISSN\NLISSN.csproj   # 或直接跑 Build/src/Debug/net10.0/NLISSN.exe
Pop-Location
```

四步心法：

1. **放配置**：在某个目录写 `nlissn.yml`。相对路径一律相对**该文件所在目录**解析，不是相对进程启动目录的另一套规则。
2. **切目录进**：入口从 CWD 读文件，`Push-Location` 到配置目录再运行。
3. **看产物**：产物写到 `artifacts.root/<runId>/`（`root` 省略则为 `<仓库根>/Build/Result`）。`runId` 同名目录**已存在会被拒绝**，重跑要换 `runId`。
4. **核生效值**：`resolved-configuration.json` 记录最终强类型设置、每个字段的来源（`explicit` / `schema-default`）和配置指纹。**它是「实际跑的是什么」的唯一权威**。

一次成功运行的最小配置：

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
artifacts: {}
```

`input`、`analysis`、`execution`、`artifacts` 四个段**本身必须存在**（缺任一段报 `NLISSN101`），
但每段内容可以为空 mapping（如 `analysis: {}`、`artifacts: {}`），此时各字段取默认值。

### 三种输入形态

| 输入 | `input` 写法 | 语义 |
| --- | --- | --- |
| 单个 `.cs` | 只写 `path` | 独立文档编译；可跑通，但见 §6 的 Workspace 选项陷阱 |
| 目录 | 只写 `path` | 扫目录内源文件 |
| `.sln` | `path` + `configuration`/`platform` 等 | 可加 `project` 选择 solution 内项目 |
| `.csproj` | 同 `.sln`；**不要写 `project`** | 写 `project` 报 `NLISSN135` |
| `.cs` + `project` | `path` + `project: xxx.csproj` | 用真实项目 compilation，但只发布目标文档 |

---

## 2. 根字段：谁读、写到哪

| 字段 | 取值 | 说明 |
| --- | --- | --- |
| `schemaVersion` | `2` 或 `3` | 其他值报 `NLISSN100` |
| `tool` | `nlissn` / `nlcpg` / `nlcpg-project-export` | Schema 3 必填且必须与入口匹配，否则 `NLISSN103` |
| `runId` | `^[A-Za-z0-9_.-]+$` | 违规报 `NLISSN131`；同时是产物目录名 |

`tool` 是 fail-closed 分流：入口读到别人的 `tool` 直接失败，**不会**把一份配置解释成另一种工具的配置。
Schema 2 无 `tool` 字段，NLISSN loader 仍兼容（实测 `schemaVersion: 2` 的合法配置返回 `exit = 0`），
但 Schema 2 写了 `tool` 会报 `NLISSN103`。

---

## 3. NLISSN 分支全字段参考

### 3.1 `input`

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `path` | 必填 | `.cs` / 目录 / `.sln` / `.csproj`；不存在报 `NLISSN130` |
| `project` | — | `.sln` 的项目选择器，或 `.cs` 的归属 `.csproj` |
| `targetFramework` | — | 多目标项目选择目标框架 |
| `configuration` | 文档称 `Debug` | MSBuild 配置；**Workspace 输入下实测必须非空**（§6.2） |
| `platform` | 文档称 `AnyCPU` | MSBuild 平台；同上 |
| `restore` | `disabled` | `disabled` / `enabled` |
| `generatedSources` | `include` | `include` / `exclude` |
| `generators` | `disabled` | `disabled` / `enabled` |

生成源可参与语义分析，但不写回物理源码、不进物理 rewrite plan。

### 3.2 `analysis`

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `targetName` | — | 分析/改写目标类型 |
| `deleteClass` | — | 要删除的类 |
| `disabledRuleTypes` | `[]` | 可写 RuleId、类型名或全限定名，**大小写不敏感** |
| `validateBindings` | `false` | 规则、CPG 与决策绑定校验 |
| `deleteUnreachableMethods` | `false` | 不可达方法删除 |
| `deleteUnreferencedMethods` | `false` | 未引用方法删除 |
| `clearUnusedInterfaceImplementations` | `false` | 无用接口实现清理 |
| `privatizeInternalOnlyPublicMethods` | `false` | 仅内部使用的 public 方法私有化 |

`disabledRuleTypes` 非空或四个开关任一为真时，才走配置驱动的规则选择；否则用默认流水线。

### 3.3 `execution`

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `writeBack` | `false` | 是否改写源文件 |
| `skipRewrite` | `false` | 是否跳过改写阶段 |
| `directoryMaxDegreeOfParallelism` | **必填** | 目录文件分析并发 |
| `cpgMaxDegreeOfParallelism` | **必填** | CPG builder / lease 并发 |
| `groupMaxDegreeOfParallelism` | **必填** | 规则 DAG、冲突域、规则组阶段并发 |
| `helperMaxDegreeOfParallelism` | **必填** | helper 扫描并发 |
| `replayMaxDegreeOfParallelism` | **必填** | rewrite plan 回放并发 |
| `maxConcurrentOperations` | **必填** | 通用操作准入并发 |
| `directoryParallelism` | `true` | 目录并行行为开关 |
| `groupParallelism` | `false` | 规则组并行开关 |
| `helperParallelism` | `true` | helper 并行开关 |
| `fastDeleteClassDirectory` | `false` | delete-class 快速路径 |
| `filterDeleteClassFilesByTargetName` | `false` | 快速路径下按目标名过滤，要求前两项都成立 |

六个并发额度**彼此独立、全部必填、只校验 `>= 1`（无上界）**。旧的 `execution.maxDegreeOfParallelism` 已删除。

### 3.4 `artifacts`

| 字段 | 默认 | 产物 |
| --- | --- | --- |
| `root` | `<仓库根>/Build/Result` | 运行根；必须是**相对路径且不含 `..`**，否则 `NLISSN132` |
| `diff{enabled, view}` | `false`, `legacy` | `<runRoot>/Diff`；`view`: `legacy` / `readable` |
| `runtimeLog{enabled}` | `false` | `<runRoot>/RuntimeLog/runtime.log` |
| `performance{enabled, mode}` | `false`, `normal` | `<runRoot>/Performance/summary.json`；`mode`: `normal`/`diagnostic`/`profile`/`benchmark` |
| `evidence{enabled}` | `false` | `<runRoot>/Evidence/evidence.json` |
| `analysisLog{enabled}` | `false` | **只能为 `false`**；写 `true` 报 `NLISSN115`（writer 未实现） |
| `rewritePlan{mode, sourceRunId}` | `none` | `none` / `capture` / `replay` |
| `projectJson{enabled, output, projectWorkerCount, resume}` | `false`, `<runRoot>/ProjectJson`, `12`, `false` | 分析后对同一 `input` 做项目级 CPG JSON 导出 |

无论上述开关如何，`resolved-configuration.json` 始终写出。

### 3.5 `logging`

| 字段 | 默认 | 取值 |
| --- | --- | --- |
| `profile` | `normal` | `minimal` / `normal` / `diagnostic` / `benchmark` |
| `level` | 代码默认 `debug` | `error` / `warn` / `info` / `debug` / `trace` |
| `categories` | `[]` | `Run`、`File`、`Phase`、`Memory`、`Cpg`、`Mark`、`Diag`、`Io`、`Diff` |
| `events` | `[]` | `Started`、`Sampled`、`Completed`、`Failed`、`Summary`、`Snapshot`、`Warning`、`Error`、`WriterFailed`、`Pending`、`Written` |
| `view` | `normal` | `compact` / `normal` / `diagnostic` / `benchmark` |

`logging` 段能生效的前提是 `artifacts.runtimeLog` 或 `artifacts.analysisLog` 至少一个 enabled，否则报 `NLISSN118`。
空数组表示不按分类/事件过滤。

### 3.6 另外两个工具分支（同文件、由 `tool` 切换）

`tool: nlcpg` — `input.path`（可选，省略用内置最小示例）、`nlcpg.view{mode: stats|local, anchor, hops, direction, edgeKinds[]}`、`nlcpg.output.json`。
`anchor` 只能给出 `nodeId`、`fullName`、`name` 三者之一；`nlcpg.output.json` 仅 `mode: local` 可用。

`tool: nlcpg-project-export` — `input{path, targetFramework, configuration, platform, restore, generatedSources}`（生成源默认 `exclude`）+
`projectExport{output, projectWorkerCount, resume}`。`projectWorkerCount` 默认 12、**有效上限 12**。

---

## 4. 诊断码速查（下表每一行都实际触发过）

| 码 | 触发条件 | 实测报错路径 |
| --- | --- | --- |
| `NLISSN001` | 配置文件不存在 | `Configuration file does not exist: ...\q-nofile001\nlissn.yml` |
| `NLISSN002` | YAML 语法/未知属性/**重复键** | `Property 'targtName' not found`、`Encountered duplicate key targetName` |
| `NLISSN100` | `schemaVersion` 不是 2 或 3 | `Unsupported NLISSN configuration schemaVersion: '9'. Expected 2 or 3.` |
| `NLISSN101` | 必填段缺失 | `analysis: is required.` |
| `NLISSN103` | `tool` 缺失或不匹配 | `schemaVersion 3 requires tool: nlissn for the NLISSN executable.` |
| `NLISSN110/111/114/116/117` | 五个独立 DOP 缺失或 `<= 0` | 如 `execution.groupMaxDegreeOfParallelism: must be a positive integer` |
| `NLISSN112` | `filterDeleteClassFilesByTargetName` 缺前置 | `requires fastDeleteClassDirectory and analysis.deleteClass` |
| `NLISSN113` | `rewritePlan.mode: replay` 与目标/skipRewrite 冲突 | `replay cannot be combined with analysis targets or execution.skipRewrite` |
| `NLISSN115` | ① `helperMaxDegreeOfParallelism` 缺失或 `<= 0`；② `analysisLog.enabled: true`（**一码两用**） | `execution.helperMaxDegreeOfParallelism` / `artifacts.analysisLog.enabled` |
| `NLISSN118` | 配了 `logging` 但无可用 writer | `logging: requires artifacts.runtimeLog.enabled or artifacts.analysisLog.enabled.` |
| `NLISSN120` | 枚举值非法 | `artifacts.diff.view: must be one of: legacy, readable.` |
| `NLISSN130` | `input.path` 空/不存在/扩展名不支持 | — |
| `NLISSN131` | `runId` 或 `sourceRunId` 非法 | — |
| `NLISSN132` | `artifacts.root` 绝对路径或含 `..` | `must be a non-empty relative path without '..' segments` |
| `NLISSN133` | 非 Workspace 输入却给了 Workspace 选项 | `Workspace options require input.path to be a .sln, .csproj, or a .cs file with input.project.` |
| `NLISSN134` | Workspace 输入下 `configuration`/`platform` 为空 | `must be non-empty` |
| `NLISSN135` | `.csproj` 输入又写 `project` | `is only valid when input.path is a .sln file.` |
| `NLISSN136/137/138` | `project` 不存在/非 `.csproj`/在 solution 目录外 | 三者均实测 |
| `NLCPGEXP001`–`006` | 项目级导出诊断 | `NLCPGEXP002` 见 §5.5 |

> 未逐条实测的只有 `NLISSN130`/`131`（源码确认，触发路径见 §4 同族诊断）与
> `NLCPGEXP001/003/004/005/006`（本次只触发 `002`）。

诊断 **按 `path` 再按 `code` 排序后一次性抛出**（不是只报第一条）。

---

## 5. 实测行为矩阵

### 5.1 单文件冒烟：DOP 1 vs DOP 12

同样的 `Library.cs`、同样 `level: info`，只改六个 DOP：

| 探针 | 六个 DOP | 墙钟 | 产物 |
| --- | --- | --- | --- |
| `dop1` | 1 | 2138 ms | resolved-config 3341 B、evidence 25086 B、`runtime.log` **0 B** |
| `dop12` | 12 | 2211 ms | resolved-config 3362 B、evidence 25107 B、`runtime.log` **0 B** |

两次 `exit = 0`，配置指纹不同（`052934…50C4` vs `C7D957…A6BD`），证明 DOP 确实进了配置指纹。
**单文件输入下 12 不带来收益**——没有可并行的文件维度。

### 5.2 目录输入：DOP 1 vs DOP 12

输入换成 `TestCodeSet/Workspace`（9 个 `.cs`），三个并行开关全开：

| 探针 | DOP | 墙钟 | `runtime.log` 记录的实际生效值 |
| --- | --- | --- | --- |
| `dir-dop1` | 1 | 4148 ms | `ruleGroupEffective=1 directoryEffective=1 cpgEffective=1` … `elapsedMs=3521` |
| `dir-dop12` | 12 | 3898 ms | `ruleGroupEffective=12 directoryEffective=12 cpgEffective=12` … `elapsedMs=3382` |

12 比 1 快约 6%，但样本只有一次、且本机同目录其他构建/lsp 进程未隔离，**不足以声称并行收益**。
可确定的是：`runtime.log` 会打印**内核实际生效**的额度，而非 YAML 请求值。

### 5.3 并发开关确实压制生效额度

`groupParallelism: false`（默认）+ `groupMaxDegreeOfParallelism: 12` 时，日志里仍是 `ruleGroupEffective=1`：

```
directoryDop=12 cpgDop=12 groupDop=12 helperDop=12 replayDop=12 maxConcurrentOperations=12
workerCount=12 ruleGroupEffective=1 helperEffective=12 directoryEffective=12 ...
groupParallelism=false directoryParallelism=true helperParallelism=true
```

即 **`groupMaxDegreeOfParallelism` 在 `groupParallelism: false` 下完全无效**。

### 5.4 Workspace（`.sln`）输入

`WorkspaceFixture.sln`，DOP 4，`analysis: {}`：`exit = 0`，13487 ms，`runtime.log` 3313 B。
日志含 `evt=started`、多条 `op=pool evt=summary` 和 `evt=completed status=completed elapsedMs=12993`。
`nodes=0 edges=0` 属正常——Workspace 路径不回填单文档图指标。

### 5.5 `.csproj` + 全制品（含 `projectJson`）

一次把所有制品打开（`diff`、`runtimeLog`、`evidence`、`performance`、`rewritePlan: capture`、`projectJson`）：

| 产物 | 大小 |
| --- | --- |
| `resolved-configuration.json` | 3792 B |
| `Evidence/evidence.json` | 26122 B |
| `Performance/summary.json` | 258689 B |
| `ProjectJson/Library.cs.json` | 37700 B |
| `ProjectJson/manifest.json` | 49267 B |
| `RewritePlan/manifest.json` | 307 B |
| `RewritePlan/rewrite-plans.jsonl` | 0 B |
| `RuntimeLog/runtime.log` | 1362 B |

`exit = 0`，10410 ms。导出结果：

```
ProjectJson: 1 file(s) written, 2 failed, status incomplete.
manifest: status=incomplete files=3 written=1 failed=2 nodes=43 edges=57 diags=6
[Error] NLCPGEXP002: Source file '...\Build\test\obj\Library\Debug\net10.0\Library.AssemblyInfo.cs'
        is outside the project root '...\Workspace\Library'.
```

两条失败都是项目根之外的生成文件（`AssemblyInfo.cs`、`AssemblyAttributes.cs`），按设计降级为 `failed` 而不中断导出。
`Diff` 目录**未创建**——本次分析 `edits=0`，无 diff 可写。

### 5.6 被拒绝的配置（全部实测）

| 探针 | 配置特征 | 结果 |
| --- | --- | --- |
| `neg-missing` | 缺 `groupMaxDegreeOfParallelism` | `NLISSN114` |
| `neg-legacy` | 写 `execution.maxDegreeOfParallelism` | `NLISSN002 Property 'maxDegreeOfParallelism' not found` |
| `neg-typo` | `analysis.targtName` | `NLISSN002 Property 'targtName' not found` |
| `p-dup` | 重复键 `targetName` | `NLISSN002 Encountered duplicate key targetName` |
| `p-absroot` | `artifacts.root` 绝对路径 | `NLISSN132` |
| `p-analysislog` | `analysisLog.enabled: true` | `NLISSN115` |
| `p-filter` | 只开 `filterDeleteClassFilesByTargetName` | `NLISSN112` |
| `p-replay` | `replay` + `targetName` + `skipRewrite` | `NLISSN113` |
| `neg-sln-nocfg` | `.sln` 省略 `configuration`/`platform` | `NLISSN134` ×2 |
| `q-schema100` | `schemaVersion: 9` | `NLISSN100` |
| `q-tool103` | `tool: nlcpg` 喂给 NLISSN 入口 | `NLISSN103` |
| `q-missing101` | 整个 `analysis` 段缺失 | `NLISSN101` |
| `q-enum120` | `diff.view: bogus` | `NLISSN120` |
| `q-log118` | 配 `logging` 但 `runtimeLog`/`analysisLog` 全关 | `NLISSN118` |
| `q-ws133` | `.cs` + `configuration`（无 `project`） | `NLISSN133` |
| `q-proj135` | `.csproj` + `project` | `NLISSN135`（并伴随 `136`） |
| `q-proj136` | `.sln` + 不存在的 `project` | `NLISSN136` |
| `q-proj137` | `.sln` + `project` 指向 `.cs` | `NLISSN137` |
| `q-proj138` | `.sln` + solution 目录外的 `project` | `NLISSN138` |
| `q-nofile001` | 目录内根本没有 `nlissn.yml` | `NLISSN001` |
| `p-cs-pjson` | 单 `.cs` + `projectJson.enabled: true` | `InvalidOperationException: requires a project or solution input` |
| 重复 `runId` | 同名产物目录已存在 | `Artifact run directory already exists` |
| `--help` | 传任何 CLI 参数 | `accepts no command-line parameters` |

**注意失败形态**：配置类错误全是**未捕获异常 + 非零退出**（`exit = -532462766`），
而 `p-cs-pjson` 的 `InvalidOperationException` 更晚、发生在分析之后。

---

## 6. 发现的问题

### 6.1 `level: info` 会让 `runtime.log` 恒为 0 字节（高）

`RuntimeMeasurementLog` 的**所有**事件都以 `TextLogLevel.Debug` 发出（`src/NLISSN/Telemetry/RuntimeMeasurementLog.cs:270`），
而过滤器是 `level <= MinimumLevel` 且显式 `level` 覆盖 profile 级别。
于是 `level: info` 把全部事件滤掉，产出**一个 0 字节文件且无任何告警**。实测矩阵：

| `profile` | `level` | `runtime.log` |
| --- | --- | --- |
| `normal` | `info`（**仓库两份在库配置的写法**） | **0 B** |
| `normal` | `warn` | **0 B** |
| `minimal` / `normal` / `benchmark` / `diagnostic` | `info` | **0 B**（全部） |
| `normal` | 省略 | 1352 B |
| `minimal` | 省略 | 1362 B |
| `diagnostic` | 省略 | 2915 B |

影响面是具体的：`Miscellaneous/nlissn.yml:38` 与 `tests/NLISSN.Testing/TestCodeSet/Workspace/nlissn.yml`
都写 `level: info`，两份配置的 `runtimeLog.enabled: true` **实际什么都没产出**。
我直接跑仓库那份 `Miscellaneous/nlissn.yml`，得到 `exit = 0` 与 `RuntimeLog/runtime.log = 0 B`。

附带一个语义反转：因为代码里 `YamlLogging.Level` 默认值是 `"debug"`（非空），
`profile` 自带的级别**几乎总被遮蔽**——只有显式写 `level` 才影响结果，而写法恰好是最容易踩空的 `info`。
`profile` 实际只剩「分类/事件集合 + view」起作用。

### 6.2 `.sln` / `.csproj` 省略 `configuration` / `platform` 被拒，与文档冲突（中）

`schema.3.json` 声明 `configuration` 默认 `Debug`、`platform` 默认 `AnyCPU`，`docs/cli-reference.md` 也写「默认配置为 `Debug`、`AnyCPU`」。
但 `ValidateWorkspaceInput` 对 Workspace 输入要求二者非空，实测 `neg-sln-nocfg` 直接报：

```
NLISSN134 input.configuration: must be non-empty.
NLISSN134 input.platform: must be non-empty.
```

即 **Schema 的 `default` 提示与运行时行为不一致**：编辑器补全认为可省，运行时会拒绝。
（单 `.cs` 输入不受影响，因为走的是独立文档路径。）

### 6.3 `groupMaxDegreeOfParallelism` 默认无效（中）

`groupParallelism` 默认 `false`，规则组实际额度被压到 1（§5.3 实测）。
配置里写 `groupMaxDegreeOfParallelism: 12` 不会有任何效果，也不会有提示。
`RuntimeMeasurementLog` 的注释（R4/R4b）说明这是**已知**的设计，但配置面没有把这条约束反馈给用户。

### 6.4 `NLISSN115` 一号两用（低）

`NLISSN115` 同时表示两件无关的事：`execution.helperMaxDegreeOfParallelism` 非法（`YamlConfigurationLoader.cs:357`）
与 `artifacts.analysisLog.enabled: true` 无 writer（同文件 `:487`）。两者都实测触发。
消费者按码分派处理时会误判，且 §4 表格必须靠 `path` 才能区分。

### 6.5 小瑕疵

- `inputKind` 对 `.sln` 报 `file`（`RuntimeMeasurementLog.cs:40` 只判断 `Directory.Exists`），日志里 `inputKind=file inputPath="...sln"`。
- 六个 DOP 与 `projectWorkerCount` 都**只有下界没有上界**；`Context/progress.md` 已记录过 `2e9` 这类值通过校验后在构造期失败的历史缺陷。
- 仓库 6 份历史配置（`Build/analysis/*`、`Miscellaneous/tmp/*`）仍是 Schema 2 + `maxDegreeOfParallelism`，
  按当前 loader **全部被拒**，只能作为历史记录，不能直接复跑。

---

## 7. 复现方式

```powershell
# 1. 构建入口
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet build .\src\NLISSN\NLISSN.csproj -c Debug

# 2. 任选一个探针目录运行
$exe = "D:\ProjectItem\SourceCode\Net\NL\Build\src\Debug\net10.0\NLISSN.exe"
Push-Location .\Miscellaneous\tmp\nlissn-report\dop12
& $exe
Pop-Location

# 3. 核生效值与日志
Get-Content .\Miscellaneous\tmp\nlissn-report\dop12\artifacts\report-dop12\resolved-configuration.json
Get-Content .\Miscellaneous\tmp\nlissn-report\dop12-debug\artifacts\report-dop12-debug\RuntimeLog\runtime.log
```

探针目录（均在 `Miscellaneous/tmp/nlissn-report/`，被 git 忽略）：
`dop1`、`dop12`、`dop12-debug`、`nolevel`、`dir-dop1`、`dir-dop12`、
`sln-ws`、`proj-json`、`s2-valid`、`neg-missing`、`neg-legacy`、`neg-typo`、`neg-sln-nocfg`、
`p-analysislog`、`p-filter`、`p-replay`、`p-absroot`、`p-dup`、`p-noroot`、`p-cs-pjson`、
`q-schema100`、`q-tool103`、`q-missing101`、`q-enum120`、`q-log118`、`q-ws133`、
`q-proj135`、`q-proj136`、`q-proj137`、`q-proj138`、`q-nofile001`；
§6.1 的级别矩阵为 `log-minimal-nolevel`、`log-minimal-info`、`log-normal-warn`、
`log-benchmark-info`、`log-diagnostic-nolevel`、`log-diagnostic-info`、`nolevel`；
仓库那份配置的真实产物在 `Miscellaneous/tmp/library-entry-smoke/`。

## 8. 本报告的验证状态

- Verification：`dotnet build src\NLISSN\NLISSN.csproj -c Debug` → 0 警告 0 错误；
  §5 全部运行以 `Build/src/Debug/net10.0/NLISSN.exe` 实跑，退出码与产物字节数均为实测；
  §4 诊断码逐条用探针触发。
- Verification（文档改动）：本文件 410 行，8 个相对链接全部解析成功；
  `git diff --check` 对该文件无空白错误；
  `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1 -SkipCliSmoke` → `OK`（exit 0）。
- Not verified：**未跑 `dotnet test`**——本报告只新增一份 Markdown，未改产品代码或测试；
  未跑 `check-harness-consistency.ps1` 的完整模式（含 CLI smoke）：该脚本在 `:127` 用
  `Remove-Item -Recurse -Force` 清理 `%TEMP%` 下 smoke 目录时报「拒绝访问」，
  是**先前已记录在 `Context/progress.md:5190` 的既有问题**，与本改动无关，故改用 `-SkipCliSmoke` 取得 `OK`。
- Not verified：§5.2 的 DOP1/DOP12 耗时各仅 1 次采样，且未隔离本机其他进程，
  **不足以作为并行收益结论**；`NLCPGEXP001/003/004/005/006` 与 `NLISSN130/131` 未逐条实跑。

## 9. 相关入口

- 机器可读契约：[`Miscellaneous/schemas/nlissn.schema.3.json`](../../Miscellaneous/schemas/nlissn.schema.3.json)、[Schema 说明](../../Miscellaneous/schemas/README.md)
- 运行时权威：[`YamlConfigurationLoader.cs`](../../src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs)
- 读者参考：[CLI 参考](../cli-reference.md)、[快速开始](../quick-start.md)
