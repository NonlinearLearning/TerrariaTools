# 20 个高星项目的架构与文件组织调研

> 状态：提案。调研日期：2026-07-29。生产代码未改动。

## 目标和方法

目标不是模仿某一种语言或框架的目录，而是为 NL 的两个可执行路径确定可执行的文件和组件边界：

- `NLCPG`：Roslyn CPG 的构建、查询和持久化。
- `NLISSN`：删除规则的 CLI、应用编排、阶段契约、规则实现和改写。

样本由认证 GitHub API 在 2026-07-29 读取。每个仓库当前星数均不低于 50,000；目录事实来自该仓库默认分支的根目录。链接指向项目自己的源码入口，不使用二手架构文章。

## 样本

| 项目 | Stars | 源码中的主要组织 | 可借鉴结论 |
| --- | ---: | --- | --- |
| [VS Code](https://github.com/microsoft/vscode) | 187,987 | `src/vs/{base,platform,editor,workbench}`、`extensions`、`test` | 共享基础、平台服务和产品功能分开；扩展不回流到内核。 |
| [React](https://github.com/facebook/react) | 246,754 | `packages`、`compiler`、`fixtures`、`scripts` | 可独立发布或可替换的能力按包边界，而不是按文件后缀归类。 |
| [Kubernetes](https://github.com/kubernetes/kubernetes) | 124,037 | `cmd`、`pkg`、`staging`、`test` | 可执行入口、内部库、对外契约和测试基础设施有明确位置。 |
| [TensorFlow](https://github.com/tensorflow/tensorflow) | 196,590 | `tensorflow`、`third_party`、`tools`、`ci` | 运行时、工具链和外部依赖不混在领域代码目录。 |
| [Flutter](https://github.com/flutter/flutter) | 177,968 | `engine`、`packages`、`dev`、`examples` | 引擎、SDK 包、开发工具和示例是不同的维护单元。 |
| [Rust](https://github.com/rust-lang/rust) | 114,928 | `compiler`、`library`、`src`、`tests` | 编译器实现、标准库和 bootstrap 工具分别拥有依赖边界。 |
| [Node.js](https://github.com/nodejs/node) | 118,547 | `src`、`lib`、`deps`、`test`、`tools` | 原生实现、语言层 API、嵌入依赖和工具不共用一个源码根。 |
| [Deno](https://github.com/denoland/deno) | 107,840 | `cli`、`core`、`runtime`、`ext`、`libs` | 运行时核心、宿主 CLI、扩展和共享库有独立方向。 |
| [Next.js](https://github.com/vercel/next.js) | 141,166 | `packages`、`crates`、`apps`、`test`、`bench` | 产品包、编译器、测试和基准各自成组，避免测试工具渗入产品包。 |
| [Spring Boot](https://github.com/spring-projects/spring-boot) | 81,174 | `core`、`autoconfigure`、`starter`、`cli`、`test-support` | 核心机制、可选集成、组合入口和测试支持分成可约束模块。 |
| [Laravel](https://github.com/laravel/laravel) | 84,707 | `app`、`config`、`routes`、`database`、`resources` | 应用模板中，运行代码、配置、输入路由、数据资产和 UI 资源分开。 |
| [Django](https://github.com/django/django) | 88,224 | `django`、`tests`、`docs`、`scripts` | 单一公开包可按稳定能力划分子包，不必为了目录而拆成很多二进制。 |
| [Rails](https://github.com/rails/rails) | 58,652 | `active*`、`action*`、`railties` | 稳定的子系统采用独立组件，而非一个巨大的 framework 目录。 |
| [Nest](https://github.com/nestjs/nest) | 76,246 | `packages`、`integration`、`sample`、`tools` | 核心包、平台适配器、集成回归和示例应用的边界清楚。 |
| [FastAPI](https://github.com/fastapi/fastapi) | 101,012 | `fastapi`、`docs_src`、`tests`、`scripts` | 规模仍可控时保留单包，先以领域子目录而不是过早多项目拆分。 |
| [Gin](https://github.com/gin-gonic/gin) | 88,978 | `binding`、`codec`、`render`、`internal` | 小而稳定的公开 API 可保持扁平；只有真实内部实现才进入 `internal`。 |
| [Elasticsearch](https://github.com/elastic/elasticsearch) | 77,613 | `server`、`libs`、`modules`、`plugins`、`distribution`、`qa` | 核心服务器、通用库、可选模块、插件、发行物和质量验证不能混淆。 |
| [Grafana](https://github.com/grafana/grafana) | 75,839 | `pkg`、`public`、`apps`、`packages`、`e2e-playwright` | 后端、前端、可复用包和端到端环境使用不同根目录。 |
| [Electron](https://github.com/electron/electron) | 122,228 | `shell`、`lib`、`npm`、`spec`、`typings` | 宿主实现、语言绑定、发布包、契约定义和规格测试独立。 |
| [Supabase](https://github.com/supabase/supabase) | 107,209 | `apps`、`packages`、`docker`、`e2e`、`examples` | 产品应用、共享包、部署资产、端到端验证和示例不能互相代替。 |

## 从样本得到的边界规则

1. 顶层目录只表达一种边界：可执行入口、可复用组件、领域能力、测试/基准或发布/部署。不能同时表示阶段、领域对象和历史迁移。
2. 物理路径、项目归属和命名空间必须说明同一件事。Kubernetes、Rust、Spring Boot 和 Electron 都让构建边界与源码位置相互印证。
3. 规则或插件实现依赖稳定契约，但应用编排不反向依赖全部实现。React、Nest 和 Elasticsearch 都以组合根连接可选实现。
4. 只有能独立演进、替换、发布或约束依赖的部分才拆项目。Django、FastAPI 和 Gin 说明单项目内的能力目录同样合理。
5. 测试、示例、基准、构建工具和文档不是生产组件；它们应在项目或解决方案层有可发现位置。

## NL 的现状检查

已落地且应保留的边界：

```text
NLISSN (composition root / CLI)
  -> NLISSN.Application (use cases)
  -> NLISSN.Rules (rule implementations)
  -> NLISSN.Core (contracts and engines)
  -> NLCPG, NL.Concurrency, NL.Caching, NLISSN.Logging
```

`Application` 不引用 `Rules`，而 `NLISSN` 作为 composition root 负责把实现注入应用层，这一方向应保留。现有 `LayoutArchitectureTests` 意图守护该图，但定向执行结果是 2 passed、1 failed：它仍预期 `NLISSN.Core` 只引用 `NL.Concurrency` 和 `NLCPG`，漏掉了已落地的 `NL.Caching`。

但当前结构仍有四个需要调整的点：

| 优先级 | 观察到的事实 | 影响 | 建议 |
| --- | --- | --- | --- |
| P0 | `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph` 未包含 `NL.Caching`，定向测试失败。 | 架构守卫不能作为当前项目引用图的回归证据。 | 先将 `NL.Caching` 写入预期引用列表；后续任何项目引用变更必须同批更新该测试。 |
| P0 | `src/NLISSN.Core/Pipeline/` 的 11 个文件中，10 个声明 `NLISSN.Rules`，`RulePipeline.cs` 声明 `NLISSN.Application`。 | 路径、程序集和命名空间给出冲突的所有权；新代码很容易放错位置。 | 先只移动文件：规则契约和 DAG 放到 `NLISSN.Core/Rules/Contracts`，执行器放到 `NLISSN.Core/Rules/Execution`，应用管线模型放到 `NLISSN.Application/Analysis`；命名空间与路径同步。 |
| P0 | `NLISSN.Rules` 的 `Mark/Lift/Propagate/Propose` 下有 17 个文件声明 `NLISSN.Core.*`。 | `Rules` 项目实际承载 Core 类型，项目拆分被物理文件削弱。 | 把通用模型/引擎移回 Core；若类型只服务一条规则族，保留在 Rules 并改为 `NLISSN.Rules.*`。 |
| P1 | 当前规则顶层按阶段组织。原子规则横跨四个目录，`Type*`/`Target*` 前缀又承担领域分类。 | 改一项删除能力要在四个顶层目录来回跳转，文件名不能表达规则职责。 | 在 `NLISSN.Rules` 内按原子规则职责优先：`AtomicRules/{Mark,Propagate,Lift,Propose,Support}`、`MethodReachability/{...}`；阶段仍作为职责内子目录。 |
| P1 | `NLISSN` 下有 10 个生产 `.cs` 文件全部平铺，既含 CLI parsing、host、目录 I/O、计划产物、日志遥测和组装。 | composition root 外的适配器难以定位，Host 容易继续吸收业务逻辑。 | 物理分为 `Cli/Parsing`、`Cli/Hosting`、`Artifacts`、`Telemetry`、`Composition`；`Program.cs` 保持唯一薄入口。 |
| P2 | `NLCPG/Builder/Passes` 中 `PartitionedSyntaxPass.cs` 和 `PartitionedOperationPass.cs` 声明为 `NLCPG.Builder`，同目录其余 pass 为 `NLCPG.Builder.Passes`。 | 局部导航和可见性约定不一致。 | 统一为 `NLCPG.Builder.Passes`，或把两个协调器移到 `Builder/Partitioning`；二选一，不同时保留两种含义。 |

`NLCPG` 的大边界本身较好：`Builder`、`Model`、`Analysis`、`Persistence`、`Cli` 已对应构图、图模型、查询、存储和入口。暂不建议拆出更多项目。其 `Persistence/Sqlite` 和 `Analysis/FlowSummaries` 已是正确的能力子目录；`src/NLCPG/docs` 也应保留在项目旁，因为它记录该项目自己的节点边契约。

## 建议的目标目录

```text
src/
  NLISSN/
    Program.cs
    Composition/
    Cli/Parsing/
    Cli/Hosting/
    Artifacts/
    Telemetry/
  NLISSN.Application/
    Analysis/
    Compilation/
    Diagnostics/
  NLISSN.Core/
    Analysis/
    Marking/
    Propagation/
    Lifting/
    Decision/
    Rewrite/
    Rules/Contracts/
    Rules/Execution/
  NLISSN.Rules/
    Catalog/
    AtomicRules/{Mark,Propagate,Lift,Propose,Support}/
    MethodReachability/{Mark,Propose,Support}/
  NLCPG/
    Builder/{Passes,Partitioning,Preallocation,Streaming}/
    Model/
    Analysis/{FlowSummaries}/
    Persistence/{Sqlite}/
    Contracts/
    Cli/
```

这不是新增程序集的设计。首轮仅做物理移动和命名空间校正，保持现有项目引用图、`Mark -> Propagate -> Lift -> Propose -> Rewrite` 契约、规则 DAG 依赖和 CLI 行为不变。

## 分批执行和验收

1. 先冻结当前规则 DAG 改动，修复 `LayoutArchitectureTests` 对 `NL.Caching` 的过期预期；再新增一个架构测试：每个生产 `.cs` 的命名空间前缀必须与所属项目和目录一致，并为例外建立明确白名单。先让后者在当前问题上失败。
2. 执行 `NLISSN.Core/Pipeline` 与 `NLISSN.Rules/*/Support` 的移动，逐项目顺序 `dotnet build --no-restore -p:UseSharedCompilation=false`，并运行 `LayoutArchitectureTests` 和规则 DAG 的 Host 测试。
3. 按原子规则职责完成纵向归位；验证同一输入的 Mark、Decision、Rewrite 结果和 DOP 1/8/12/14/16 快照相等，再迁移下一项职责。
4. 最后整理 `NLISSN` 的 adapter 目录和 `NLCPG` 两个 pass 的命名空间。运行 CLI 单文件、目录和 rewrite-plan 回放，再运行 `pwsh -File .\\scripts\\check-harness-consistency.ps1` 与 `git diff --check`。

不得把这次目录迁移和缓存容量、CPG 并行度、规则行为改动混在一个 feature 中。前者的完成条件是依赖方向、编译、架构守卫和行为快照；性能变更需要独立的 warmed 测量。

## 来源

- GitHub repository REST API：`gh api repos/<owner>/<repo>` 与 `gh api repos/<owner>/<repo>/contents`，2026-07-29。上表项目名链接为对应的第一方源码仓库。
- 本仓库：`src/*/*.csproj`、`设计docs/目前设计/deletion-pipeline.md`、`设计docs/目前设计/cpg-architecture.md`、`src/NLCPG/docs/code-layout.md`。
- 架构守卫：`tests/RoslynDeletionPrototype.ContractTests/Architecture/LayoutArchitectureTests.cs` 与 `ArchitectureBoundaryTests.cs`。
