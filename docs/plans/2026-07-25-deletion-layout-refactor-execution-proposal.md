# 删除规则文件布局重构 Implementation Plan

> **状态：提案，未实施。**
>
> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 将删除规则原型重组为职责明确的 CLI、Application、Core 和 Rules 项目；消除 `Host`/`Application` namespace 混用、`RuleServices` 混合层、`RuleHelpers` 收容目录及 `Implementations` 二级冗余，同时保持所有规则、CLI 和 CPG 观察行为稳定。

**Architecture:** 规则实现仅按 `Mark`、`Propagate`、`Lift`、`Propose` 四个阶段组织。Core 提供流水线协议、受控 `RuleContext`、执行运行时和阶段模型；Application 组织单文件和目录用例；Cli 仅处理命令、文件系统、日志、工件和组合；Rules 仅含规则集、规则实现与同阶段的支持算法。`SObject` 和 `DeleteClass` 不再是目录边界，`Helpers`、`RuleHelpers`、`RuleServices` 和 `Implementations` 都不出现在目标布局中。

**Tech Stack:** .NET 10、C#、Roslyn、现有 MinimalRoslynCpg、xUnit、PowerShell harness。

---

## 1. 范围、决策与前置条件

### 1.1 明确决策

1. 这是一次文件、项目和 namespace 的直接迁移；所有生产代码改用 `Deletion.*` namespace。
2. CLI 行为兼容：已有 `RoslynPrototype.csproj` 保留为只含 `Program.cs` 的过渡 launcher，转交 `Deletion.Cli`。现有 `dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- ...` 命令、选项、退出码和输出继续有效。
3. `MinimalRoslynCpg`、routing sidecar 格式、NodeId、CPG 查询与持久化代码不在本提案的移动范围内。
4. `RuleId`、capability ID、规则顺序、mark/propagate/lift/propose 结果、rewrite plan、diff、`--no-diff`、`--skip-rewrite`、目录 DOP 与 CPG DOP 均属于兼容契约。
5. 不新引入 NuGet 包、通用 `Helpers` 项目、服务定位器或额外规则执行器。

### 1.2 执行前置条件

- 当前 feature `cpg-minimal-routing-catalog` 尚未关闭，且工作树已有 sidecar 相关未提交改动。实施者必须从已提交的基线建立独立 worktree；不得在当前工作树搬迁文件。
- 先记录基线：ContractTests、HostTests、`Run-TestTiers.ps1 -Fast`、CLI 单文件/目录 smoke、harness consistency 的命令、通过数和已知失败。
- 先修复现有 `ArchitectureBoundaryTests.RuleIdentity_DoesNotUseLegacyMetadataFiles`：缺失目录应视为通过，不得对不存在的 `RuleIds`/`RuleMetadata` 目录调用 `Directory.GetFiles`。
- 每一阶段只提交一个可编译、可测试的布局变化。禁止把 namespace 改名、规则逻辑调整和性能优化放在同一个提交。

### 1.3 当前问题与目标边界

| 当前位置 | 问题 | 目标归属 |
| --- | --- | --- |
| `src/Host/**`，但 namespace 为 `RoslynPrototype.Application` | 适配器和应用层无法从名字区分 | `src/Deletion.Cli/**`，namespace `Deletion.Cli.*` |
| `src/Application/DeletionRulePipeline.cs` | pipeline 协议被用例项目拥有 | `src/Deletion.Core/Pipeline/DeletionRulePipeline.cs` |
| `src/RoslynPrototype/RuleServices/**` | 契约、运行时、缓存和规则辅助算法混放 | `Deletion.Core/Pipeline/{Contracts,Runtime}` 或 `Deletion.Rules/<Stage>` |
| `RuleServices/RuleHelpers/**` | 物理目录与实际 Mark/Propagate/Lift/Propose namespace 不一致 | 同阶段 `Support` 或能力目录 |
| `src/Rules/Implementations/**` | `Implementations` 不提供额外导航价值 | `src/Deletion.Rules/{Mark,Propagate,Lift,Propose}/**` |

## 2. 目标布局与依赖规则

```text
src/
  Deletion.Cli/
    Deletion.Cli.csproj
    Cli/
      DeletionCliRunner.cs
      DeletionCliParser.cs
      DeletionCliOptions.cs
    Composition/
      DefaultDeletionPipelineFactory.cs
    Filesystem/
      DirectorySourceReader.cs
      DirectoryRewriteWriter.cs
      DiffPathResolver.cs
      RewritePlanArtifactWriter.cs
      RewritePlanReplayReader.cs
    Diagnostics/
      PostRewriteDiagnostics.cs
      ClassPostRewriteCleanup.cs
    Logging/

  Deletion.Application/
    Deletion.Application.csproj
    Analysis/
      SingleFileAnalysisUseCase.cs
      DirectoryAnalysisUseCase.cs
      DirectoryAnalysisCoordinator.cs
      DirectoryResultAggregator.cs
    Compilation/
      RoslynCompilationFactory.cs

  Deletion.Core/
    Deletion.Core.csproj
    Pipeline/
      DeletionRulePipeline.cs
      RuleContext.cs
      RuleStageGroupKey.cs
      Contracts/
        IRuleDefinition.cs
        IRuleOptions.cs
        IRuleAnalysisServices.cs
        IRuleGraphBindingServices.cs
        IRuleStructureViewServices.cs
        MarkRuleBase.cs
        PropagationRuleBase.cs
        LiftRuleBase.cs
        ProposalRuleBase.cs
      Runtime/
        RuleExecutionOptions.cs
        DeletionAnalysisEpoch.cs
        IRuleStageScheduler.cs
        BoundedRuleStageScheduler.cs
        DeletionAnalysisRuntime.cs

  Deletion.Rules/
    Deletion.Rules.csproj
    Catalog/
      RuleCatalog.cs
      RuleSet.cs
      DefaultRuleSets.cs
    Mark/
      AtomicExpressions/
      Types/
      Support/
    Propagate/
      Declarations/
      Parameters/
      References/
      Support/
    Lift/
      Structures/
      Support/
    Propose/
      Declarations/
      Parameters/
      Structures/
      Support/
      ParameterShrink/
        ParameterShrinkAnalyzer.cs
        Plans/
        Rewrites/

  RoslynPrototype/
    RoslynPrototype.csproj       # compatibility launcher only
    Program.cs
```

最终项目引用只能是：

```text
RoslynPrototype compatibility launcher -> Deletion.Cli
Deletion.Cli                            -> Deletion.Application + Deletion.Rules + Deletion.Core
Deletion.Application                    -> Deletion.Core + MinimalRoslynCpg
Deletion.Rules                          -> Deletion.Core + MinimalRoslynCpg
Deletion.Core                           -> MinimalRoslynCpg
```

- `Deletion.Application` 不引用 `Deletion.Cli` 或 `Deletion.Rules`。
- `Deletion.Rules` 不引用 `Deletion.Application`、`Deletion.Cli`、`System.IO` 输出实现或日志 sink。
- `Deletion.Core` 不引用前三层，也不含默认规则注册。
- `Support` 只能向同阶段规则、Core 事实模型和 Roslyn API 依赖；跨阶段共享代码必须移动至 `Deletion.Core` 的明确能力目录，不能回流为 helper。

## 3. 全局命名规则

1. 文件名采用主类型名；一个文件默认一个公开类型。只有同一 Roslyn 节点种类、无状态、短小且始终共同演进的规则允许同文件分组。
2. 目录名采用阶段、语义能力或基础设施能力，例如 `Propose/Parameters`、`Mark/AtomicExpressions`、`Filesystem`、`Pipeline/Runtime`；禁止 `Common`、`Misc`、`Utils`、`Helpers`、`Services`。
3. 规则类型名可保留 `SObject` 或 `Class` 前缀以表达语义适用范围，但目录不以规则族切分。
4. `Service` 只用于有稳定操作边界的对象，例如 `IRuleAnalysisServices`；纯算法使用 `Analyzer`、`Resolver`、`Classifier`、`Factory`、`Writer` 或 `Reader`。
5. `Host` 一词从生产 namespace 与类型名移除，统一使用 `Cli`、`Command`、`Runner`、`Writer`、`Reader`、`Factory`。兼容 launcher 是唯一允许保留 `RoslynPrototype` 名称的位置。

## 4. 阶段 0：先锁住行为与目标布局

**文件：**

- 修改：`tests/RoslynDeletionPrototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
- 新建：`tests/RoslynDeletionPrototype.ContractTests/Architecture/DeletionLayoutArchitectureTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/RoslynDeletionPrototype.ContractTests.csproj`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**步骤：**

1. 修正 legacy metadata 目录断言：目录不存在即通过，目录存在时才枚举 `*.cs`。
2. 新建过渡期布局测试，读取 `.csproj`，明确记录当前项目引用图和最终目标引用图；测试在阶段 1 至阶段 5 期间只允许声明的过渡引用。
3. 为单文件与目录流程增加黑盒 characterization：相同 fixture 在移动前后比较 `MarkRecord`、`PropagatedMarkRecord`、`LiftedMarkRecord`、decision、rewrite source、diff 和 diagnostics；不测试私有 helper。
4. 增加规则管线顺序断言：`Mark -> Propagate -> Lift -> Propose -> Rewrite`，并锁定 disabled rule、rule ID 与 capability ID 的排序。
5. 运行新增测试，确认它们在未移动文件时通过；保存输出作为迁移基线。

**验证：**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~DeletionLayoutArchitectureTests"
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

**准入门：** 基线通过；任何现有失败必须有独立、可复现的失败证据。若没有行为快照，不得移动生产文件。

## 5. 阶段 1：创建目标项目和兼容 launcher

**文件：**

- 新建：`src/Deletion.Core/Deletion.Core.csproj`
- 新建：`src/Deletion.Application/Deletion.Application.csproj`
- 新建：`src/Deletion.Rules/Deletion.Rules.csproj`
- 新建：`src/Deletion.Cli/Deletion.Cli.csproj`
- 修改：`src/RoslynPrototype/RoslynPrototype.csproj`
- 修改：`src/RoslynPrototype/Program.cs`
- 修改：所有测试和工具的 `ProjectReference`

**步骤：**

1. 复制当前 target framework、nullable、implicit usings 和 LangVersion 设置；不新增 package reference。
2. 先创建空目标项目及最终 project reference 图，允许旧项目作为迁移期间的 source owner；新 Core、Application、Rules、Cli 都必须能独立 build。
3. 将 `RoslynPrototype.csproj` 收缩成 launcher：只编译 `Program.cs`，仅引用 `Deletion.Cli`；它不得继续编译 Core、Rules、Host 或 Application 源文件。
4. 将 `Program.cs` 改成调用 `Deletion.Cli.DeletionCliRunner.RunAsync(args)`；保留返回码、异常输出和异步行为。
5. 新增 launcher compatibility 测试：旧 project path 的 `--help`、默认 demo、单文件 `--no-diff`、目录 `--skip-rewrite --no-diff` 均可执行。

**准入门：** 旧 CLI project path 可运行；新项目引用图不产生循环；生产程序集不依赖 compatibility launcher。

## 6. 阶段 2：迁移 Core 流水线、契约与运行时

**文件映射：**

| 当前文件 | 目标文件 |
| --- | --- |
| `Application/DeletionRulePipeline.cs` | `Deletion.Core/Pipeline/DeletionRulePipeline.cs` |
| `RoslynPrototype/RuleServices/RuleContext.cs` | `Deletion.Core/Pipeline/RuleContext.cs` |
| `RoslynPrototype/RuleServices/RuleStageGroupKey.cs` | `Deletion.Core/Pipeline/RuleStageGroupKey.cs` |
| `RoslynPrototype/RuleServices/RuleDefinition.cs` | `Deletion.Core/Pipeline/Contracts/IRuleDefinition.cs` 与四个 `*RuleBase.cs` |
| `RoslynPrototype/RuleServices/RuleServices.cs` | `Deletion.Core/Pipeline/Contracts/I{RuleOptions,RuleAnalysisServices,RuleGraphBindingServices,RuleStructureViewServices}.cs` |
| `RoslynPrototype/RuleServices/ExecutionRuntime.cs` | `Deletion.Core/Pipeline/Runtime/{RuleExecutionOptions,DeletionAnalysisEpoch,IRuleStageScheduler,BoundedRuleStageScheduler,DeletionAnalysisRuntime}.cs` |

**步骤：**

1. 写失败的 reflection/architecture tests：Core 中存在每个协议和运行时类型，`RuleServices` 与 `RuleHelpers` 目录不存在，`RuleContext` 仍不公开完整 graph 或 analysis context。
2. 用 `git mv` 迁移文件，先保持类型行为；随后将多公开类型文件拆成目标文件，并以类型名替换旧的 `RuleDefinitionMark`、`RuleDefinitionPropagate`、`RuleDefinitionLift`、`RuleDefinitionPropose` 为 `MarkRuleBase`、`PropagationRuleBase`、`LiftRuleBase`、`ProposalRuleBase`。
3. 更新 Core namespace 至 `Deletion.Core.Pipeline`、`Deletion.Core.Pipeline.Contracts`、`Deletion.Core.Pipeline.Runtime`，逐个修正 Rules、Application、Cli 和测试的 using。
4. 保留 `RuleContext` 的服务中心门面；不得在此次迁移中增加 `IRuleContext`、公开 CPG graph、公开 `CpgAnalysisContext` 或服务定位器。
5. 更新 Architecture tests，让它们只检查最终目录和 namespace；删除所有针对 `RuleServices` 旧路径的正向断言。

**验证：**

```powershell
dotnet build .\src\Deletion.Core\Deletion.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~RuleContext|FullyQualifiedName~PipelineComponentTests"
```

**准入门：** Core 不含 CLI、文件写入、日志 sink、默认规则创建或具体阶段 engine；pipeline 输出与阶段顺序快照未变。

## 7. 阶段 3：将所有规则统一到四个阶段目录

**文件：**

- 移动：`src/Rules/RuleCatalog.cs`、`src/Rules/RuleSet.cs` 至 `src/Deletion.Rules/Catalog/`
- 移动：`src/Rules/Implementations/Mark/**` 至 `src/Deletion.Rules/Mark/**`
- 移动：`src/Rules/Implementations/Propagate/**` 至 `src/Deletion.Rules/Propagate/**`
- 移动：`src/Rules/Implementations/Lift/**` 至 `src/Deletion.Rules/Lift/**`
- 移动：`src/Rules/Implementations/Propose/**` 至 `src/Deletion.Rules/Propose/**`
- 删除：`src/Rules/Implementations/`

**步骤：**

1. 先迁移每阶段的现有实现；目录第一层只允许 `Mark`、`Propagate`、`Lift`、`Propose`、`Catalog`。
2. 将 `TargetAtomicMarkRules.cs` 拆到 `Mark/AtomicExpressions/`，每个公开规则类一个文件；将 `TypeMarkRules.cs` 拆到 `Mark/Types/`。
3. 将 `TargetPropagationRules.cs` 按 declaration、parameter、reference 能力拆到 `Propagate/{Declarations,Parameters,References}/`。
4. 将 `TypeLiftingRules.cs` 和三个 target lifting 文件拆到 `Lift/Structures/` 与 `Lift/Support/`。
5. 将 `TypeProposalRules.cs` 按 declaration、parameter、structure 三类拆到 `Propose/{Declarations,Parameters,Structures}/`；每个公开 proposal rule 一文件。
6. 所有规则 namespace 改为 `Deletion.Rules.<Stage>[.<Capability>]`。规则 ID、`Name`、`CapabilityId`、`GroupKey` 和排序逻辑保持原值。
7. 为每个移动过的规则优先运行 owning UnitTests；对 mark 规则保留 atomic-marking 的 seed 语义、逻辑操作数独立标记和 `RuleContext` snapshot 复用。

**准入门：** `src/Deletion.Rules` 下不含 `Implementations`、`SObject`、`DeleteClass`、`Helpers` 或 `RuleHelpers` 目录；规则行为和 catalog validation 与基线一致。

## 8. 阶段 4：按阶段吸收所有现有 Helper

### 8.1 迁移表

| 当前文件 | 目标位置 |
| --- | --- |
| `DeleteClassMarkRuleHelpers.cs` | `Deletion.Rules/Mark/Support/ClassMarkFacts.cs` |
| `DeleteSObjectMarkRuleHelpers.cs` | `Deletion.Rules/Mark/Support/AtomicExpressionMarkFacts.cs` |
| `DeleteSObjectPropagationHelpers.cs` | `Deletion.Rules/Propagate/Support/PropagationFacts.cs` |
| `DeleteSObjectHostLiftingHelpers.cs` | `Deletion.Rules/Lift/Support/ExpressionHostResolver.cs` |
| `DeleteSObjectIfStructureLiftingHelpers.cs` | `Deletion.Rules/Lift/Structures/IfStructureLifter.cs` |
| `DeleteSObjectSwitchLiftingHelpers.cs` | `Deletion.Rules/Lift/Structures/SwitchStructureLifter.cs` |
| `DeleteSObjectLiftingCommon.cs` | `Deletion.Rules/Lift/Support/LiftedMarkFacts.cs` |
| `DeleteSObjectProposalHelpers.cs` | `Deletion.Rules/Propose/Support/ProposalFacts.cs` |
| `DeleteClassDeclarationHostProposalHelpers.cs` | `Deletion.Rules/Propose/Declarations/DeclarationHostProposals.cs` |
| `DeleteClassDelegateAndExtensionUsageProposalHelpers.cs` | `Deletion.Rules/Propose/Parameters/DelegateAndExtensionUsage.cs`，按两个公开类型拆文件 |
| `DeleteClassLocalFunctionAndIndexerUsageProposalHelpers.cs` | `Deletion.Rules/Propose/Parameters/LocalFunctionUsage.cs` 与 `IndexerUsage.cs` |
| `DeleteClassMethodParameterUsageProposalHelpers.cs` | `Deletion.Rules/Propose/Parameters/MethodParameterUsage.cs` |
| `DeleteClassMethodProposalSafety.cs` | `Deletion.Rules/Propose/Support/MethodProposalSafety.cs` |
| `DeleteClassReplaceDecisionFactory.cs` | `Deletion.Rules/Propose/Support/ReplaceDecisionFactory.cs` |
| `DeleteClassTypeSyntaxProposalHelpers.cs` | `Deletion.Rules/Propose/Declarations/TypeSyntaxProposals.cs` |
| `DeleteClassParameterShrinkAnalyzer.cs` | `Deletion.Rules/Propose/ParameterShrink/` |

### 8.2 ParameterShrink 的专项拆分

`DeleteClassParameterShrinkAnalyzer.cs` 当前同时含分析器和 12 个公开 plan/rewrite record。拆为：

```text
Propose/ParameterShrink/
  ParameterShrinkAnalyzer.cs
  Plans/
    PrivateMethodParameterShrinkPlan.cs
    PublicMethodParameterShrinkPlan.cs
    LocalFunctionParameterShrinkPlan.cs
    IndexerParameterShrinkPlan.cs
    DelegateParameterShrinkPlan.cs
    DelegateComplexShrinkPlan.cs
    DelegateUsageSummary.cs
  Rewrites/
    InvocationRewrite.cs
    ElementAccessRewrite.cs
    MethodRewrite.cs
    LocalFunctionRewrite.cs
    ExpressionRewrite.cs
```

**步骤：**

1. 写 characterization tests，覆盖 method/local function/indexer/delegate/extension 参数 shrink 的 proposal、rewrite operations 与拒绝路径。
2. 先仅拆 record 到独立文件，保持 namespace 和序列化/record equality 语义；再迁移 analyzer 与调用者。
3. 每次移动后运行参数 shrink 的 UnitTests 和 Host rewrite-plan persistence tests；禁止在迁移中改变 candidate 选择、操作排序或 rewrite span。
4. 全局搜索确认生产目录不存在 `Helper`、`Helpers`、`RuleHelpers`、`DeleteClass/` 或 `SObject/` 目录名。

**验证：**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ParameterShrink|FullyQualifiedName~MarkRule"
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~RewritePlanPersistenceTests|FullyQualifiedName~PipelineComponentTests"
```

**准入门：** 每个 support 文件只被所在阶段使用；跨阶段依赖必须显式提升到 Core 并增加 architecture test。不能以新建 `Common` 或 `Helpers` 目录解决循环引用。

## 9. 阶段 5：拆薄 CLI 与目录用例

**文件映射：**

| 当前文件 | 目标位置 |
| --- | --- |
| `Host/DeletionCommandHost.cs` | `Deletion.Cli/Cli/DeletionCliRunner.cs` |
| `Host/DeletionApplicationOptions.cs` | `Deletion.Cli/Cli/DeletionCliOptions.cs` 与 `DeletionCliParser.cs` |
| `Host/RuleRegistry.cs` | `Deletion.Cli/Composition/DefaultDeletionPipelineFactory.cs` |
| `Host/DeletionDirectoryAnalysisService.cs` | 拆为 `Deletion.Application/Analysis/{DirectoryAnalysisUseCase,DirectoryAnalysisCoordinator,DirectoryResultAggregator}.cs` 与 Cli `Filesystem` 读写适配器 |
| `Host/DeletionDiffPathResolver.cs` | `Deletion.Cli/Filesystem/DiffPathResolver.cs` |
| `Host/RewritePlanArtifactService.cs` | `Deletion.Cli/Filesystem/RewritePlanArtifactWriter.cs` |
| `Host/RewritePlanReplayService.cs` | `Deletion.Cli/Filesystem/RewritePlanReplayReader.cs` |
| `Host/DeletionPostRewriteDiagnostics.cs` | `Deletion.Cli/Diagnostics/PostRewriteDiagnostics.cs` |
| `Host/DeleteClassPostRewriteCleanupService.cs` | `Deletion.Cli/Diagnostics/ClassPostRewriteCleanup.cs` |
| `Host/Logging/**` | `Deletion.Cli/Logging/**` |
| `Application/DeletionApplicationService.cs` | `Deletion.Application/Analysis/SingleFileAnalysisUseCase.cs` |
| `Application/RoslynCompilationFactory.cs` | `Deletion.Application/Compilation/RoslynCompilationFactory.cs` |

**步骤：**

1. 写 HostTests，分别锁定 parser、runner、文件输出和 directory use case 的公开行为；测试通过命令、fixture 和结果断言，不访问新 private helper。
2. 从现有目录服务抽出只依赖 source text/tree/semantic model/runtime 的 `DirectoryAnalysisCoordinator`；它不调用 `Directory`、`File`、日志 sink 或 diff writer。
3. 抽出 Cli `DirectorySourceReader` 和 `DirectoryRewriteWriter`，保留文件枚举、编码、写回、diff 路径、rewrite plan artifact 与日志输出。
4. 让 `DeletionCliRunner` 仅完成：parse -> composition -> choose use case -> write output -> return exit code。禁止它直接访问 Marking、Propagation、Lifting、Decision、Rewrite engine。
5. 移除 `DeletionApplicationServiceCompatibilityExtensions`：先迁移其调用者到 launcher/Cli runner，再删除兼容 extension，避免 Application namespace 反向构造 CLI。
6. 将所有旧 `RoslynPrototype.Application` 与 `Rules` namespace 更新到目标 namespace；test namespace 只有在其 owning project 已迁移后才改名。

**准入门：** Cli 不含规则语义；Application 不含 `File`/`Directory` 写入或日志 sink；目录输入、单文件输入、demo、write-back、no-diff、rewrite plan capture/replay 的行为与基线一致。

## 10. 阶段 6：清理旧目录、文档与架构守卫

**文件：**

- 删除：`src/Host/`、`src/Application/`、`src/Rules/`、`src/RoslynPrototype/RuleServices/` 及其旧 `.csproj`
- 保留：`src/RoslynPrototype/RoslynPrototype.csproj`、`src/RoslynPrototype/Program.cs`
- 修改：`AGENTS.md`
- 修改：`docs/quick-start.md`
- 修改：`docs/cli-reference.md`
- 修改：`docs/developer-guide.md`
- 修改：`docs/contributing.md`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
- 修改：`scripts/check-harness-consistency.ps1`

**步骤：**

1. 删除所有过渡 compile include、旧 project reference 和空目录；compatibility launcher 之外，仓库不得包含旧 production source root。
2. 将 AGENTS 的 real entrypoints、目录指引和项目图更新为 `Deletion.*` 路径；同步 quick start、CLI reference、developer guide 与 contributing 的命令和测试名称。
3. 让 architecture tests 断言最终 project 图、无 `Helpers`/`RuleHelpers`/`RuleServices`/`Implementations` 目录、无旧 namespace，并保留 RuleContext 封装和 Marking 不依赖 Propagation 的既有守卫。
4. 更新 harness consistency 脚本中的 project path、测试项目和文档锚点。
5. 使用 `rg` 检查旧路径和 namespace；只允许本提案和历史文档中出现旧名称，运行时代码、测试和门户文档不得出现。

**验证：**

```powershell
rg -n --glob '!docs/plans/**' --glob '!设计docs/优化历史/**' 'RuleServices|RuleHelpers|Implementations|RoslynPrototype\.Application|^namespace Rules;' src tests docs
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

**准入门：** 搜索没有生产代码、测试或门户文档命中；harness consistency 和 Markdown 链接检查通过。

## 11. 最终验证矩阵

按依赖顺序执行，不并发共享输出目录：

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path

dotnet build .\src\Deletion.Core\Deletion.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\Deletion.Rules\Deletion.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\Deletion.Application\Deletion.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\Deletion.Cli\Deletion.Cli.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false

dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\Run-TestTiers.ps1 -Host

dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- --help
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- .\src\MinimalRoslynCpg\samples\analysis-sample.cs --target-name MissingType --no-diff
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

在以上通过后，运行现有 DOP 1/8/12/14/16 的 focused graph/rule equivalence。真实 Terraria 测量仅在 sidecar feature 的独立基线已完成、磁盘空间和 TEMP 均受控时执行；资源耗尽、超时或缺失 completion event 均不是性能或正确性结论。

## 12. 完成条件与风险

### 完成条件

- 四个新项目和一个兼容 launcher 遵守目标依赖图。
- 生产目录不存在 `Helpers`、`RuleHelpers`、`RuleServices`、`Implementations`、`SObject`、`DeleteClass` 目录。
- 所有具体规则位于唯一的 `Mark`、`Propagate`、`Lift` 或 `Propose` 阶段树中。
- `ParameterShrink` 的 analyzer、plan 和 rewrite value types 具有可导航的独立文件。
- 行为快照、Unit/Contract/Host 分层测试、CLI smoke、DOP 等价、harness consistency 和 diff check 均通过。
- 门户文档、AGENTS 和 architecture guards 指向新路径，旧路径只出现在历史记录与迁移提案中。

### 风险与控制

| 风险 | 控制 |
| --- | --- |
| namespace 改名引入遗漏 | 每阶段 build owning projects；最后全局 `rg` 和 architecture test。 |
| 文件移动掩盖规则行为变化 | 先建黑盒 characterization；移动提交禁止修改 RuleId、条件或排序。 |
| 目录服务拆分改变并发 | 锁定 DOP、输出顺序、文件统计和 rewrite plan；单独运行 Host/DOP 回归。 |
| 与 sidecar feature 冲突 | 仅在独立 worktree 实施；不触碰 `MinimalRoslynCpg`。 |
| compatibility launcher 长期滞留 | Architecture test 只允许它包含 `Program.cs` 和一条 `Deletion.Cli` 引用。 |

## 13. 提交边界

建议提交顺序：

1. 架构/行为基线与损坏的 legacy metadata guard 修复。
2. 新项目和 compatibility launcher。
3. Core pipeline/runtime 迁移。
4. Rules 四阶段迁移。
5. Helper 吸收与 ParameterShrink 拆分。
6. Cli/Application 边界拆分。
7. 旧目录删除、文档与最终 guards。

每个提交使用 Lore 格式，至少包含 `Constraint`、`Confidence`、`Scope-risk`、`Directive`、`Tested` 与 `Not-tested` trailer。不要为纯移动提交混入规则语义或性能结论。
