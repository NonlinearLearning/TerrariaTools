# NLCPG 边界演进执行提案 Implementation Plan

> **状态：提案，未实施。**
>
> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 将 `NLCPG` 拆成显式的 Core、Persistence、Query 与 CLI 边界；让 shard/routing 格式和查询结果拥有独立兼容性契约；为每条删除规则建立并列的说明、实现和测试入口，同时保持现有 `Mark -> Propagate -> Lift -> Propose -> Rewrite` 流水线与 CLI 行为稳定。

**Architecture:** 以 `NLCPG.Core` 作为 Roslyn 图事实、冻结图与构图扩展点的唯一依赖根。Persistence 实现已声明的持久化 SPI，Query 只通过稳定的 query/result 契约读取内存图或已完成 build，CLI 仅负责参数解析与渲染。删除规则继续由 `Application` 编排，`Host` 继续承担命令、目录调度、日志和报告；规则目录只提供说明、fixture 与测试定位信息，运行时不读取文档。

**Tech Stack:** .NET 10、C#、Roslyn、SQLite、现有 CPG shard/routing sidecar、xUnit、PowerShell harness。

---

## 1. 依据、范围和决策

### 当前事实

- `src/NLCPG/NLCPG.csproj` 同时是 `Exe`、`Program.cs`/`Cli/` 的宿主，又被 Application、Rules、四类测试和 benchmark 作为库引用。
- `Builder/CpgShardBuildSession.cs` 与 `CpgShardBuildCoordinator.cs` 直接依赖 `Persistence/`；`Analysis/CpgShardQueryResolver.cs` 直接依赖 `ICpgShardCatalog` 与 `ICpgShardStore`。因此不能只移动目录，必须先建立反向依赖的抽象点。
- 当前 active feature `cpg-minimal-routing-catalog` 尚未关闭。其真实源测量、legacy fallback、sidecar corruption 和 DOP 等价验证是本提案的前置基线，不能与大规模移动文件混在同一提交或同一性能结论中。
- 删除规则的正式执行顺序已固定为 `Mark -> Propagate -> Lift -> Propose -> Rewrite`；目录级 fast path 仍属于 Application 边界，Host 不能吸收规则判断。

### 外部结构参考的可采纳部分

| 参考 | 采用 | 不采用 |
| --- | --- | --- |
| `dotnet/roslyn` | 独立 compiler/library、command host 与 test 边界；共享构建属性留在根目录。 | IDE、VS、workspace 相关层。 |
| `joernio/joern` | CLI、语义 CPG、数据流与查询能力分开演进。 | Scala schema、语言前端实现和 query language。 |
| `github/codeql` | extractor、query、工具、文档按稳定契约分层；语言特定目录自包含。 | CodeQL runtime、Bazel 构建与多语言矩阵。 |
| `SonarSource/sonar-dotnet` | rule spec、analyzer source、tests 三个可导航入口；规则 ID 驱动追踪。 | 运行时加载规则说明、引入 Sonar 打包模型。 |

### 明确选择

1. 保留 `src/NLCPG/NLCPG.csproj` 作为兼容 CLI launcher，使现有 `dotnet run --project .\\src\\NLCPG\\NLCPG.csproj` 命令继续可用。
2. 新建 `NLCPG.Core`、`NLCPG.Persistence`、`NLCPG.Query` 三个库项目；命名空间保持现有 `NLCPG`，避免一次性 public namespace 迁移。
3. 规则采用“同一 RuleId 的三入口”而非将测试源码放入生产项目：`rules/<rule-id>/` 放说明和 shared fixture，`src/Rules/Implementations/<rule-id>/` 放实现，`tests/**/Rules/<rule-id>/` 放测试。根 `rules/catalog.json` 负责连接三者。
4. 不引入新 NuGet 包、通用查询 DSL、跨项目查询、完整 Joern 兼容或新的规则执行器。

### 目标依赖图

```text
NLCPG.Core
  <- NLCPG.Persistence
  <- NLCPG.Query
  <- NLCPG (CLI launcher)

Application -> Core + Query + Persistence
Rules       -> Core + Query
Host        -> Application + Rules + Core
RoslynPrototype.Core -> Core
tests/tools -> only the production projects they exercise

Host -> Application -> Mark -> Propagate -> Lift -> Propose -> Rewrite
```

`Query` 可引用 `Persistence` 的读取接口；`Persistence` 不可引用 `Query`。`Core` 不可引用前三者之外的任一项目。任何违反该图的引用均视为架构回归。

## 2. 全局不变式与前置门

- 已完成 build 的可见性、routing-sidecar 的版本/长度/hash 校验、corruption failure、legacy catalog fallback、DOP 1/8/12/14/16 图快照等价全部保持。
- NodeId、节点/边集合与稳定排序、slice path/truncation/unavailable 结果、规则 marks/decisions、rewrite 输出和 diff 保持。
- 兼容 CLI 路径、`--help`、默认 graph stats、local view 参数及退出码保持。
- `RuleCatalog` 只注册实现；文档 catalog 仅在 ContractTests 中校验，生产 CLI/Host 不扫描 Markdown 或 JSON。
- 默认 DOP、durability、SQLite writer、目录 fast path 与日志选项均不在本提案中改变。

开始实施前依次完成：

1. 关闭或明确隔离 `cpg-minimal-routing-catalog` 的剩余真实源验证；保留其 report.json 和测试证据。
2. 在未移动文件的基线上运行 ContractTests、HostTests、`Run-TestTiers.ps1 -Fast`、CLI smoke 与 harness consistency；把通过数、命令和当前已知失败写入实施分支的首个提交说明。
3. 对每一批实际改动运行 `pwsh -File .\\scripts\\harness-classify-change.ps1 -Paths <changed paths>`；按输出读取局部指引并选择验证等级。

## 3. 阶段 0：建立边界回归与依赖守卫

**文件：**

- 新建：`tests/Roslyn Prototype.ContractTests/Architecture/NLCPGProjectBoundaryTests.cs`
- 新建：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGCliCompatibilityTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Roslyn Prototype.ContractTests.csproj`
- 修改：`scripts/check-harness-consistency.ps1`

**步骤：**

1. 写失败的项目引用测试：读取仓库中四个 CPG 项目的 `.csproj`，断言 Core 不引用 Persistence/Query/CLI，Persistence 只引用 Core，Query 只引用 Core 与 Persistence，CLI 引用 Core/Query/Persistence。
2. 写失败的 CLI compatibility 测试：用现有 `NLCPGCli` 行为断言 `--help`、最小 sample、`--view local` 与未知参数的退出码和关键输出；不对完整帮助文本逐字断言。
3. 写失败的 frozen graph 测试：使用现有小 fixture 记录 NodeId、node/edge key、slice result、`UnavailableShards` 与 stable ordering 的快照键。
4. 先运行新增测试，确认它们在边界项目尚未存在时以预期的缺失路径失败。
5. 仅加入测试辅助路径和 harness 引用，不改变生产逻辑；让测试先在当前单项目布局下通过一份明确的“过渡期允许表”。
6. 将允许表设为临时项，并让测试在最终阶段拒绝旧项目直接被 Application、Rules、tests 或 tools 引用。

**验证：**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~NLCPGProjectBoundaryTests|FullyQualifiedName~NLCPGCliCompatibilityTests"
```

**提交：** 单独提交测试与过渡守卫；Lore trailers 必须记录临时允许表的删除条件。

## 4. 阶段 1：抽出 Core，并将原项目收缩为 CLI launcher

**文件：**

- 新建：`src/NLCPG.Core/NLCPG.Core.csproj`
- 移动至 Core：`Contracts/`、`Model/`、`Builder/NLCPGBuilder*.cs`、`Builder/Passes/` 中不含 shard publication 的文件
- 保留并修改：`src/NLCPG/NLCPG.csproj`
- 保留：`src/NLCPG/Program.cs`、`src/NLCPG/Cli/NLCPGCli.cs`
- 修改：`src/Application/Application.csproj`、`src/Rules/Rules.csproj`、`src/RoslynPrototype/RoslynPrototype.Core.csproj`
- 修改：所有直接引用旧 `NLCPG.csproj` 的测试和 `tools/CpgPersistenceBenchmark/CpgPersistenceBenchmark.csproj`

**步骤：**

1. 建立失败的 compile test：Application、Rules、RoslynPrototype.Core 与 CPG benchmark 引用 `NLCPG.Core` 后应仍可访问图 builder、NodeId、graph model 与现有 options。
2. 新建 Core `.csproj`，先仅纳入 Model、Contracts 和无持久化依赖的 builder pass；设置与当前项目相同的 target framework、nullable、analyzer 和 root build 属性。
3. 将 Core 内 public type 的 namespace 保持不变。禁止借此批次重命名 `NLCPG*` 类型、调整记录字段或重排图输出。
4. 将旧 `NLCPG.csproj` 改成只编译 `Program.cs` 与 `Cli/**`，并显式 `ProjectReference` Core；为暂未迁出的 Query/Persistence 保留过渡引用。
5. 逐个迁移 Application、Rules、RoslynPrototype.Core、Testing、Unit/Contract/Host/Performance tests 和 benchmark 的项目引用；运行每个 owning 项目的 build，发现缺失类型时只补目标库引用，禁止把所有项目都改为引用 CLI。
6. 运行旧命令和 `src/NLCPG/samples/analysis-sample.cs`，锁定命令路径、退出码和可见输出。
7. 删除 Core 已迁出文件在 launcher 项目的默认 compile 包含；用 project-boundary 测试确认 launcher 没有 `Model/`、`Builder/` 或 `Contracts/` 的实现源。

**最小项目关系：**

```xml
<!-- src/NLCPG/NLCPG.csproj -->
<ItemGroup>
  <ProjectReference Include="..\NLCPG.Core\NLCPG.Core.csproj" />
</ItemGroup>
```

**准入门：** 不含持久化/查询的 Core build 与原图快照相同；CLI 兼容测试和原 `dotnet run` 命令成功。若 CLI path 或输出语义变化，回退该阶段，不接受“文档已更新”作为替代。

## 5. 阶段 2：将 Persistence 变成显式 writer/format 边界

**文件：**

- 新建：`src/NLCPG.Persistence/NLCPG.Persistence.csproj`
- 移动：`src/NLCPG/Persistence/**`
- 移动：`src/NLCPG/Builder/CpgShardBuildSession.cs`
- 移动：`src/NLCPG/Builder/CpgShardBuildCoordinator.cs`
- 新建：`src/NLCPG.Core/Contracts/ICpgBuildPublicationSink.cs`
- 新建：`src/NLCPG.Core/Contracts/CpgBuildPublicationRequest.cs`
- 修改：`src/NLCPG.Core/Builder/NLCPGBuilderOptions.cs` 与分片 pass 的调用点
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgShardContractTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/CpgPersistenceStateTests.cs`

**步骤：**

1. 先写失败测试，验证 Core assembly 不含 SQLite、`CpgShardStore`、routing 文件格式或 catalog 实现；同一份测试验证 Persistence assembly 只通过 Core 的 publication contract 接收已冻结图事实。
2. 在 Core 定义最小 `ICpgBuildPublicationSink`。接口只接收已排序、不可变的 build/shard publication request，并返回完成/失败/cancellation 结果；接口不得暴露 SQLite connection、文件流、临时目录或内部 writer lock。
3. 将 `CpgShardBuildSession`/`CpgShardBuildCoordinator` 与完整 `Persistence/` 移入 Persistence 项目，并让它们实现 publication sink。builder 仅在 options 给出 sink 时发布，不拥有 StoreRoot、writer lock 或 catalog 生命周期。
4. 保持 staging、manifest finalization、single writer、checkpoint、strict/throughput durability、legacy fallback 和 corruption 语义原样。文件格式版本不因项目移动升级。
5. 迁移 Application、Host、benchmark 与 ContractTests 对 persistence 的引用。Core-only consumer 不得因新增 Persistence 被迫加载 SQLite。
6. 对 completed 与 staging build 分别运行现有 contract tests，断言读取可见性和失败清理未变；同时运行 DOP 1/8/12/14/16 的 shard-backed graph equivalence。

**准入门：** Core 不含 durable storage 具体实现；Persistence 内所有可观察行为与当前 shard/catalog tests 等价；无 build/session 发布时的内存 CPG 流程仍不创建 StoreRoot。

## 6. 阶段 3：将 Query 变成独立读取与结果契约边界

**文件：**

- 新建：`src/NLCPG.Query/NLCPG.Query.csproj`
- 移动：`src/NLCPG/Analysis/**`
- 新建：`src/NLCPG.Query/Contracts/INLCPGQueryService.cs`
- 新建：`src/NLCPG.Query/Contracts/CpgQueryResult.cs`
- 修改：`src/NLCPG.Query/CpgShardQueryResolver.cs`
- 修改：`src/NLCPG.Query/NLCPGSliceQuery.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`
- 修改：`tests/Roslyn Prototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 修改：`docs/developer-guide.md`

**步骤：**

1. 写失败测试，固定 node/span/symbol lookup、slice result、path ordering、truncation、`UnavailableShards`、corruption exception 和 legacy build fallback 的公开行为。
2. 以 `INLCPGQueryService` 和不可变 `CpgQueryResult` 统一内存图与 completed persisted build 的查询结果。结果必须携带现有限制信息；禁止以空集合吞掉 budget、缺失 frontier 或校验失败。
3. 保持 `CpgShardQueryResolver` 的 LRU、max cached bytes、catalog lookup 和 shard read 作为 Query 实现细节；将其对 `ICpgShardCatalog`/`ICpgShardStore` 的依赖限定在 Query -> Persistence 方向。
4. 把 StructureView、slice 与 resolver source 移到 Query。Core 可定义图事实和 traversal 输入值对象，但不得知道 SQLite、routing sidecar 或 shard cache。
5. 将 Application、Rules 与 tests 仅按实际用途引用 Query；纯建图测试只引用 Core，持久化格式测试只引用 Persistence，查询行为测试引用 Query。
6. 为 Query 版本化边界建立 contract test：新字段只能追加；旧 completed build 的读取走既有 fallback；新增 query 选项默认值不得改变未传参行为。

**准入门：** Query 的 API 不泄露 SQLite/文件句柄；所有现有 query tests 保持通过；同一 build 的 in-memory 和 persisted query 可观察结果一致或明确返回既有 unavailable 事实。

## 7. 阶段 4：规则三入口目录与 catalog 守卫

**文件：**

- 新建：`rules/README.md`
- 新建：`rules/catalog.json`
- 新建：`rules/delete-s-object/rule.md`
- 新建：`rules/delete-class/rule.md`
- 新建：`rules/unreachable-method/rule.md`
- 新建：`tests/Roslyn Prototype.ContractTests/Rules/RuleCatalogDocumentationContractTests.cs`
- 移动或整理：`src/Rules/Implementations/<rule-id>/**`
- 新建或整理：`tests/Roslyn Prototype.UnitTests/Rules/<rule-id>/**`
- 新建或整理：`tests/Roslyn Prototype.HostTests/Rules/<rule-id>/**`
- 修改：`src/Rules/RuleCatalog.cs`
- 修改：`设计docs/目前设计/deletion-pipeline.md`
- 修改：`设计docs/目前设计/testing-strategy.md`

**目录契约：**

```text
rules/
  catalog.json
  delete-class/rule.md
  delete-s-object/rule.md
  unreachable-method/rule.md
src/Rules/Implementations/
  DeleteClass/
  DeleteSObject/
  UnreachableMethod/
tests/*/Rules/
  DeleteClass/
  DeleteSObject/
  UnreachableMethod/
```

**步骤：**

1. 为已注册的每个 `RuleId` 写失败的 catalog contract：catalog 条目必须有唯一 id、所属 stage、说明路径、实现目录、至少一个 Unit 或 Host test 定位；目录和文件必须实际存在。
2. 先为三个已有规则族建立最小 `rule.md`。每份说明固定包含：目标、适用阶段、输入事实、保守拒绝条件、输出 mark/decision、rewrite 所消费的 decision、fixture 和测试入口。文档不得复制实现细节或宣称未覆盖能力。
3. `catalog.json` 仅记录静态映射，例如：

```json
{
  "id": "delete-class",
  "stages": ["Mark", "Propagate", "Lift", "Propose"],
  "specification": "rules/delete-class/rule.md",
  "implementationRoot": "src/Rules/Implementations/DeleteClass",
  "testRoots": [
    "tests/Roslyn Prototype.UnitTests/Rules/DeleteClass",
    "tests/Roslyn Prototype.HostTests/Rules/DeleteClass"
  ]
}
```

4. 将实现和测试按既有 RuleId 目录归位。移动只改变物理布局，保留 namespace、RuleId、GroupKey、注册顺序、fixture 内容和测试 discovery。
5. `RuleCatalog` 继续只创建 `RuleDefinition`。在 ContractTests 从 `RuleCatalog` 的公开 RuleId 与 `catalog.json` 交叉验证；生产代码不读取 `rules/catalog.json`。
6. 为每条规则至少保留一个阶段产物断言和一个安全拒绝断言；只有改变 rewrite 时才添加完整 diff 测试。`Testing` 继续只持有共享 fixture/基础设施。

**准入门：** 任一注册规则缺少说明、实现目录或测试入口都会使 ContractTests 失败；目录整理后输出 marks、decisions、rewrite 和 diff 全部等价；阶段执行器与 Host 的职责未膨胀。

## 8. 阶段 5：文档、构建入口和最终验证

**文件：**

- 修改：`AGENTS.md`
- 修改：`docs/quick-start.md`
- 修改：`docs/developer-guide.md`
- 修改：`设计docs/README.md`
- 修改：`设计docs/目前设计/项目概览.md`
- 修改：`设计docs/目前设计/cpg-architecture.md`
- 修改：`设计docs/目前设计/cpg-capabilities.md`
- 修改：`设计docs/目前设计/deletion-pipeline.md`
- 修改：`设计docs/目前设计/testing-strategy.md`
- 修改：`src/NLCPG/docs/code-layout.md`
- 修改：`scripts/check-harness-consistency.ps1`

**步骤：**

1. 更新入口说明：`src/NLCPG` 是兼容 CLI launcher，Core/Persistence/Query 是库项目；精确列出各自职责、可引用方向和真实项目路径。
2. 更新 quick-start 与 CLI help sample，继续使用原 `dotnet run --project .\\src\\NLCPG\\NLCPG.csproj` 命令。
3. 更新 `code-layout.md`，删除“尚未持久化、未完整 CFG/数据流”的旧叙述，改为 Core/Persistence/Query/CLI 的当前事实。
4. 更新测试策略中的旧单测试项目路径，使其与 Unit/Contract/Host/Performance 分层一致；将 rule catalog 守卫写为 ContractTests 责任。
5. 运行改动分类、Markdown 链接检查、harness consistency 与 `git diff --check`。确认没有将实施细节迁入 `progress.md`；feature 变更只在真正切换状态时修改 `feature_list.json`。

**最终验证顺序：**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
pwsh -File .\init.ps1
pwsh -File .\scripts\harness-classify-change.ps1 -Paths <all-changed-paths>
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\Roslyn Prototype.UnitTests\Roslyn Prototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
dotnet run --project .\src\NLCPG\NLCPG.csproj -- --help
dotnet run --project .\src\NLCPG\NLCPG.csproj -- .\src\NLCPG\samples\analysis-sample.cs
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

在最终 fast suite 通过后，依当前 feature 的 retained matrix 复跑 persistence/slice/DOP focus；真实源 benchmark 需预热加至少三次有效样本，缺少完整完成事件、超时或资源耗尽均不计入结论。

## 9. 完成条件、风险与回滚

### 完成条件

- Core、Persistence、Query、CLI 四个项目及其依赖方向由 ContractTests 锁定。
- 原 CPG CLI path、关键参数、输出语义和退出码通过兼容测试。
- routing sidecar、SQLite catalog、shard、legacy fallback、corruption 与 query/slice 的结果契约通过现有及新增 ContractTests。
- RuleCatalog 中每个 RuleId 都有唯一说明、实现和测试三入口，且 `Mark -> Propagate -> Lift -> Propose -> Rewrite` 的归属未变化。
- 文档不再引用旧测试单项目或过时的“无 persistence/CFG/data-flow”范围。

### 主要风险与控制

| 风险 | 控制 |
| --- | --- |
| 移动文件掩盖 routing catalog 未完成的性能结果 | 当前 feature 验证完成后再开始；性能与结构迁移分开提交。 |
| `Builder` 反向依赖持久化实现，形成 Core/Persistence 循环 | 先以 `ICpgBuildPublicationSink` 拆掉 writer 生命周期；禁止 Core 引用 Persistence。 |
| CLI 项目移动导致现有命令失效 | 保留原 project path 为薄 launcher，并做 process-level smoke。 |
| 规则目录整理改变 discovery 或注册顺序 | RuleId/GroupKey/order contract 与阶段产物快照先行。 |
| 将文档 catalog 接入运行时造成 I/O 或部署耦合 | catalog 只由 ContractTests 读取；生产程序集不包含 runtime lookup。 |

### 回滚规则

- 每阶段单独提交；任一图、query、规则或 CLI 等价测试失败时，先回退该阶段，不跨阶段补丁修复。
- 若 Core/Persistence 分离无法在不改 persistence 格式的前提下完成，保留 Core 的抽象 seam 与测试，停止文件移动；不得借机升级 shard/routing 版本。
- 若规则三入口目录使测试项目边界恶化，保留 `rules/` 说明与 catalog contract，撤销测试源码的物理移动；不让生产项目编译测试代码。

### 建议提交切分

1. `test: lock CPG project and CLI compatibility boundaries`
2. `refactor: separate NLCPG core from CLI launcher`
3. `refactor: isolate CPG persistence publication boundary`
4. `refactor: isolate CPG query contracts and resolver`
5. `docs: align rules with specifications and test catalog`
6. `docs: align CPG boundary documentation and harness checks`

每个提交使用 Lore trailers，至少包含 `Constraint`、`Rejected`、`Confidence`、`Scope-risk`、`Directive`、`Tested` 与 `Not-tested`。
