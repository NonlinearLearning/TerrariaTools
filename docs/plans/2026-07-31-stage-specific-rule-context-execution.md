# 阶段独立 Rule Context 执行计划

> **For Codex:** REQUIRED SKILL: Use `executing-plans` to implement this plan task-by-task.

**目标：** 将规则执行从一个公开、同时实现四个阶段接口的 `RuleContext` 迁移为一个内部 `AnalysisSession` 和四个阶段独立的 Context 实例，同时保持规则图输入、输出、缓存、证据、并发和 DOP 行为稳定。

**架构：** `AnalysisSession` 每次分析创建一次，保存 `CpgAnalysisContext`、原始选项、`AnalysisRuntime`、`MarkAnalysisSnapshot` 和 `AnalysisEvidenceCollector`。每个规则图节点由会话创建一个仅实现 `IMarkRuleContext`、`IPropagationRuleContext`、`ILiftRuleContext` 或 `IProposeRuleContext` 之一的内部适配器；规则无法取得 `AnalysisSession`，也无法将当前 Context 转型为其他阶段接口。Mark/Propagate/Lift/Propose 的 DAG 输入继续由规则方法参数和 `Consumes`/`Produces` 合约显式传递。

**技术栈：** .NET 10、Microsoft.CodeAnalysis.CSharp、xUnit、`NLISSN.Core.Pipeline`、`NL.Concurrency`。

**执行前置条件：** 在包含当前规则-DAG迁移的干净工作树执行；不要在现有共享脏工作树中按目录暂存或提交。2026-07-31 的 `check-harness-consistency.ps1` 当前被并行迁移中的 `src/NLISSN/Composition/RuleRegistry.cs` 阻塞，报 `CS0246`：四个 `RuleDefinition*` 类型无法解析。先完成或隔离该组合根迁移，再将 harness 结果作为本计划验收证据。

---

## 已接受的设计

- `AnalysisSession` 是 `internal sealed`，每次 `ApplicationService` 分析创建一个。它不是规则可见的接口，也不实现任何阶段 Context 接口。
- `MarkRuleContext`、`PropagationRuleContext`、`LiftRuleContext`、`ProposeRuleContext` 都是 `internal sealed`，每次规则图节点调用新建一个实例。每个实例只实现自己的阶段接口。
- `CpgAnalysisContext`、`AnalysisRuntime`、`MarkAnalysisSnapshot` 和 `AnalysisEvidenceCollector` 在同一个 `AnalysisSession` 内共享；不复制图、缓存或证据收集器。
- `MarkRecord`、`PropagatedMarkRecord` 和 `LiftedMarkRecord` 继续作为规则调用参数和 `RuleNodeResult` 值流动。Context 不持有动态 DAG 输入，也不收集全阶段输出。
- `IPropagationRuleContext` 拥有 `ResolveCallFlow`；它解析外部调用关系并记录 Flow Summary 证据。`IProposeRuleContext` 只保留决策所需的语义和运行时能力。
- Propagate 与 Lift 的结构查询通过惰性 `CpgStructureViewQueryResult` 暴露，保留 `Complete`、`Truncated`、`Disconnected` 和 `Ambiguous` 状态。当前没有规则读取结构视图，访问前不得构建该查询。
- `RuleContext`、`WithStructureView` 和任何兼容别名直接删除。旧类型继续存在会允许规则通过向下转型绕过阶段隔离。
- `NLISSN.Application` 与测试程序集通过受限 `InternalsVisibleTo` 使用 `AnalysisSession`；`NLISSN.Rules` 不获得该可见性。
- 本计划不实现新的外部边界删除候选规则。该规则仍属于 `flow-summary-2-execution` 的后续垂直切片；本计划提供它所需的 Propagate Context 契约和回归测试。

## 范围与不变量

- 保持 `Mark -> Propagate -> Lift -> Propose` 的事实所有权：Propagate 只生成关系或 token mark；Lift 生成结构或表达式宿主结论；Propose 生成 `DecisionUnit`。
- 保持 `RuleConsumedSyntax`、`RuleProducedSyntax`、`RuleInputCardinality` 和 `RuleGraph` 的现有调度语义。Context 拆分不引入 GroupKey、事实选择器或新的隐式依赖。
- 保持 `AnalysisEvidenceCollector` 的并发安全和最终稳定排序。规则只能经阶段 Context 的受控方法间接产生 Flow Summary 证据；Mark、传播、提升和提案证据仍由执行引擎在规则返回后记录。
- 保持现有 CLI 选项、重写行为、图内容、决策和 diff。不得在此迁移中改变默认 DOP、调度器、规则注册顺序或 CPG 构图策略。

## Task 1：先锁住阶段隔离和懒结构查询

**文件：**

- 修改：`tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
- 修改：`tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs`

**Step 1：添加架构契约的失败断言**

将当前源码文本断言替换为下列可观察行为：

```csharp
Assert.False(markContext is IPropagationRuleContext);
Assert.False(markContext is ILiftRuleContext);
Assert.False(markContext is IProposeRuleContext);
```

为 Propagate、Lift、Propose 各添加一条对称断言。测试探针规则只记录其收到的 Context，并经完整 `RuleGraphAnalysisExecutor` 路径执行；不测试私有字段或适配器类型名。

增加源码契约断言：`RuleContext.cs` 不存在，`AnalysisSession.cs` 存在，且 `NLISSN.Rules` 项目不拥有 `InternalsVisibleTo` 到 `AnalysisSession` 的访问权。

**Step 2：先运行并确认旧实现失败**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ArchitectureBoundaryTests'
```

预期：阶段探针断言失败，因为现有 `RuleContext` 同时实现四个阶段接口。

**Step 3：为惰性结构查询和 Flow Summary 归属添加失败测试**

在 `PipelineComponentTests` 添加一个 Propagate 探针：执行时不读取结构视图，断言未调用 `NLCPGStructureViewBuilder.Query`。再添加读取 `StructureViewQuery` 的探针，断言同一节点内查询只发生一次，且完整/非完整状态原样可见。

在 `FlowSummaryDeletionSafetyTests` 添加测试专用传播规则：它调用 `IPropagationRuleContext.ResolveCallFlow`、将 `ResolvedCallFlow` 包装为 `ExternalSummaryFlowPayload`，并断言仅 `Resolved` 映射进入传播输出；`Unknown`、`Blocked` 和 `Truncated` 不产生删除候选。Propose 规则只消费这个 payload，不调用解析器。

**Step 4：运行最小失败测试**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~StageContext|FullyQualifiedName~StructureView|FullyQualifiedName~FlowSummaryDeletionSafetyTests'
```

预期：新增测试因缺少独立 Context、惰性查询和 Propagate 解析器而失败。

**Step 5：提交测试基线**

```powershell
git add tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs tests/NLISSN.HostTests/Application/PipelineComponentTests.cs tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs
git commit -m "test: specify stage-specific rule contexts"
```

## Task 2：定义公共阶段接口和内部分析会话

**文件：**

- 创建：`src/NLISSN.Rule/AnalysisSession.cs`
- 创建：`src/NLISSN.Rule/StageRuleContexts.cs`
- 修改：`src/NLISSN.Rule/RuleExecutionContexts.cs`
- 删除：`src/NLISSN.Rule/RuleContext.cs`
- 创建：`src/NLISSN.Core/Properties/AssemblyInfo.cs`

**Step 1：收敛阶段接口**

将 `RuleExecutionContexts.cs` 保留为规则可见的接口声明。`IPropagationRuleContext` 变为：

```csharp
public interface IPropagationRuleContext : ISemanticRuleContext
{
  SyntaxNode Root { get; }
  CpgStructureViewQueryResult StructureViewQuery { get; }
  ResolvedCallFlow ResolveCallFlow(
    IInvocationOperation invocation,
    FlowSummaryEndpoint source,
    FlowSummaryEndpoint target);
}
```

`ILiftRuleContext` 将当前可空 `StructureView` 改为 `CpgStructureViewQueryResult StructureViewQuery`，保留 `AnalyzeIfStructure`、`TryFindContainingIf`、`FindLogicalHost` 与 `AnalyzeLoopStructure`。从 `IProposeRuleContext` 删除 `ResolveCallFlow`。保持 Mark 的现有专用查询能力，避免把阶段专用方法提升到 `ISemanticRuleContext`。

**Step 2：实现 `AnalysisSession`**

在 `AnalysisSession.cs` 将旧 `RuleContext` 的私有状态迁入：

```csharp
internal sealed class AnalysisSession
{
  internal AnalysisSession(
    CpgAnalysisContext analysisContext,
    IReadOnlyDictionary<string, string> options,
    AnalysisRuntime? runtime = null,
    MarkAnalysisSnapshot? markAnalysisSnapshot = null,
    AnalysisEvidenceCollector? evidence = null);

  internal IMarkRuleContext CreateMarkContext();
  internal IPropagationRuleContext CreatePropagationContext(IReadOnlyList<MarkRecord> inputMarks);
  internal ILiftRuleContext CreateLiftContext(
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks);
  internal IProposeRuleContext CreateProposeContext();
}
```

会话内部保留图绑定、目标名缓存、操作缓存、结构分析、选项读取、运行时和证据写入。其成员均为 `internal` 或 `private`；不提供返回原始 `CpgAnalysisContext`、`NLCPGGraph`、快照或证据收集器的公共成员。

**Step 3：实现四个适配器**

在 `StageRuleContexts.cs` 创建四个 `internal sealed` 类型。每个适配器只转发所属接口成员；不得实现其他阶段接口。Propagate/Lift 适配器构造时保存结构片段集合，并使用：

```csharp
private readonly Lazy<CpgStructureViewQueryResult> _structureViewQuery;

public CpgStructureViewQueryResult StructureViewQuery => _structureViewQuery.Value;
```

`Lazy` 的工厂调用 `AnalysisSession.QueryStructureView`，并沿用当前预算、关系 profile、方向和缓存范围。Flow Summary 方法转发到会话的内部解析和证据记录方法。

**Step 4：限制内部可见性**

在 `NLISSN.Core` 添加 `InternalsVisibleTo`，仅允许 `NLISSN.Application`、`RoslynDeletionPrototype.UnitTests`、`RoslynDeletionPrototype.ContractTests`、`RoslynDeletionPrototype.HostTests` 和 `RoslynDeletionPrototype.PerformanceTests` 访问会话与内部适配器。不得向 `NLISSN.Rules`、`NLISSN` 或基础设施程序集暴露该权限。

**Step 5：运行 Core 和架构测试**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ArchitectureBoundaryTests'
```

预期：Core 构建零错误；阶段隔离架构测试通过。

**Step 6：提交会话与 Context 契约**

```powershell
git add src/NLISSN.Rule/AnalysisSession.cs src/NLISSN.Rule/StageRuleContexts.cs src/NLISSN.Rule/RuleExecutionContexts.cs src/NLISSN.Core/Properties/AssemblyInfo.cs tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs
git add -u -- src/NLISSN.Rule/RuleContext.cs
git commit -m "refactor: split rule execution contexts by stage"
```

## Task 3：把执行、验证和决策路径迁移到 AnalysisSession

**文件：**

- 修改：`src/NLISSN.Application/Analysis/ApplicationService.cs`
- 修改：`src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs`
- 修改：`src/NLISSN.Core/Marking/MarkingEngine.cs`
- 修改：`src/NLISSN.Core/Propagation/PropagationEngine.cs`
- 修改：`src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- 修改：`src/NLISSN.Core/Decision/DecisionModel.cs`
- 修改：`src/NLISSN.Core/Validation/RuleBindingValidator.cs`
- 修改：`src/NLISSN.Core/Validation/DecisionBindingValidator.cs`（仅在其签名或调用点仍引用旧 Context 时）

**Step 1：从 Application 创建一次会话**

在 `ApplicationService` 的现有 `new RuleContext(...)` 位置创建 `AnalysisSession`。持有会话的内部分析记录改名为 `Session`，`ShouldSkipRewrite` 与 `--validate-bindings` 继续通过会话内部的选项读取方法判断。

**Step 2：在每个规则图节点创建阶段适配器**

更新 `RuleGraphAnalysisExecutor`：

```csharp
MarkingEngine.ExecuteRule(session, root, rule);
PropagationEngine.ExecuteRule(session, rule, inputMarks);
MarkLiftingEngine.ExecuteRule(session, rule, seedMarks, propagatedMarks, existingLiftedMarks);
rule.Propose(session.CreateProposeContext(), seedMarks, propagatedMarks, liftedMarks);
```

各执行引擎在调用规则前创建对应 Context。禁止继续把 `AnalysisSession` 直接作为规则方法实参。

**Step 3：迁移结构查询与证据写入**

删除 `PropagationEngine.BuildRuleContext`、`MarkLiftingEngine.BuildRuleContext` 和 `WithStructureView`。上下文工厂接收当前节点的输入 marks，延迟建立 `CpgStructureViewQueryResult`。

`MarkingEngine`、`PropagationEngine`、`MarkLiftingEngine` 和 `RuleGraphAnalysisExecutor` 继续在规则返回后调用会话的内部证据记录方法。`ResolveCallFlow` 在传播适配器中记录 `UsesSummary`；Propose 不拥有解析入口。

**Step 4：移除无效的旧 Context 表面**

在迁移完所有调用后，运行：

```powershell
rg -n -S '\bRuleContext\b|WithStructureView\(|context\.StructureView\b' src tests
```

预期：没有生产调用；测试仅可出现历史断言中的明确“旧类型已删除”文本。再检查没有规则实现向下转型阶段 Context：

```powershell
rg -n -S --glob '*.cs' 'as I(Mark|Propagation|Lift|Propose)RuleContext|\(I(Mark|Propagation|Lift|Propose)RuleContext\)' src/NLISSN.Rules
```

预期：无匹配。

**Step 5：运行最小行为测试**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~FlowSummaryDeletionSafetyTests|FullyQualifiedName~BindingValidatorTests'
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~CpgRelationQueryUnitTests'
```

预期：传播、提升、决策、结构查询与绑定验证行为通过；非完整结构查询仍保持保守处理。

**Step 6：提交执行路径迁移**

```powershell
git add src/NLISSN.Application/Analysis/ApplicationService.cs src/NLISSN.Application/Analysis/RuleGraphAnalysisExecutor.cs src/NLISSN.Core/Marking/MarkingEngine.cs src/NLISSN.Core/Propagation/PropagationEngine.cs src/NLISSN.Core/Lifting/MarkLiftingEngine.cs src/NLISSN.Core/Decision/DecisionModel.cs src/NLISSN.Core/Validation/RuleBindingValidator.cs src/NLISSN.Core/Validation/DecisionBindingValidator.cs tests/NLISSN.HostTests/Application/PipelineComponentTests.cs tests/NLISSN.HostTests/Decision/FlowSummaryDeletionSafetyTests.cs tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs
git commit -m "refactor: execute rules through stage contexts"
```

## Task 4：迁移全部测试工厂和直接引擎测试

**文件：**

- 修改：`tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/NLISSN.HostTests/Mark/LogicalConditionMarkAnalyzerTests.cs`
- 修改：`tests/NLISSN.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`
- 修改：`tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs`
- 修改：`tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs`
- 修改：`tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs`
- 修改：`tests/NLISSN.ContractTests/Cpg/NLCPGSliceQueryTests.cs`
- 修改：所有由 `rg -n -S '\bRuleContext\b' tests` 找到的剩余测试文件

**Step 1：统一测试会话工厂**

将每个本地 `CreateRuleContext`/`CreateContext` 辅助方法改为创建 `AnalysisSession`。通过受限 `InternalsVisibleTo` 保留测试对内部会话和直接引擎入口的访问；不新增公开测试工厂。

**Step 2：按阶段改写直接调用**

- Mark 测试继续调用 `MarkingEngine`，由引擎创建 `MarkRuleContext`。
- Propagate、Lift 和 Propose 测试继续传递显式 mark 列表，验证输出记录、语义标签、payload 和决定，不依赖 Context 具体类型。
- StructureViewBuilder 测试通过会话的内部查询入口验证缓存范围和状态；删除对 `WithStructureView` 的测试。
- `DecisionPolicy` 与 `RuleDecisionEngine` 的内部会话参数只用于运行时调度，保持 `DefaultDecisionPolicy` 对决策片段的现有结果。

**Step 3：运行测试工厂迁移验证**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MarkRuleRegistryCoverageTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~BindingValidatorTests'
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~StructureViewBuilderTests'
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~NLCPGSliceQueryTests|FullyQualifiedName~ArchitectureBoundaryTests'
```

预期：所有指定测试通过，且 `RuleContext` 搜索为空。

**Step 4：提交测试迁移**

```powershell
git add tests/NLISSN.HostTests/Application/PipelineComponentTests.cs tests/NLISSN.HostTests/Mark/LogicalConditionMarkAnalyzerTests.cs tests/NLISSN.HostTests/Mark/MarkRuleRegistryCoverageTests.cs tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs tests/NLISSN.UnitTests/Application/StructureViewBuilderTests.cs tests/NLISSN.ContractTests/Cpg/NLCPGSliceQueryTests.cs tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs
git commit -m "test: migrate rule tests to analysis session"
```

## Task 5：执行跨阶段等价验证并更新交接状态

**文件：**

- 修改：`progress.md`
- 修改：`feature_list.json`（仅更新 `flow-summary-2-execution` 的已验证命令与仍未完成的外部边界候选规则）

**Step 1：顺序构建受影响生产项目**

共享 `Build` 输出目录，因此必须顺序运行：

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\NLISSN.Core\NLISSN.Core.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Rules\NLISSN.Rules.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN.Application\NLISSN.Application.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
```

预期：四个构建均为零错误；记录现有无关警告，不将其归因于 Context 迁移。

**Step 2：运行阶段和目录等价测试**

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~RuleGraphCompilerTests|FullyQualifiedName~RuleStructureContractTests|FullyQualifiedName~FlowSummaryDeletionSafetyTests|FullyQualifiedName~DecisionEvidenceTests'
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~CpgExecutionMatrixTests'
```

随后运行现有 DOP 1/16 目录决策与 diff 对比测试。验收内容为：排序后的 Mark、传播 mark、Lift、`DecisionUnit`、最终决策、重写文本、diff、节点状态和确定性遥测一致。

**Step 3：运行文档与差异检查**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

预期：两个命令通过。若 harness 被迁移前已知路径问题阻塞，记录原始错误和已完成的独立构建/测试，不把阻塞写成通过。

**Step 4：更新状态并提交**

`progress.md` 只记录：`flow-summary-2-execution` 仍为 `in_progress`、阶段 Context 迁移已验证的范围、DOP 等价结果，以及尚未实现的外部边界删除候选规则。`feature_list.json` 仅追加实际通过的验证命令；不得把 feature 标为 `done`。

```powershell
git add progress.md feature_list.json
git commit -m "docs: record stage context verification"
```

## 完成门槛

- 生产规则运行时没有 `RuleContext` 类型、`WithStructureView` 调用或规则对其他阶段 Context 的转型。
- 每个规则图节点都接收独立阶段适配器；同一分析内的 CPG、运行时、快照和证据仍由一个 `AnalysisSession` 共享。
- 未读取结构视图的 Propagate/Lift 节点不执行结构查询；读取后查询只执行一次并保留非完整状态。
- Flow Summary 解析只可由 Propagate Context 调用，解析结果以传播 payload 进入规则图，Propose 不直接解析外部调用。
- 规则图合约、禁用节点、证据、决策合并、CLI 和 DOP 1/16 输出保持既有行为。
- 所有 Task 5 构建、测试和静态检查通过，或将独立验证结果与可复现的外部阻塞明确记录在 `progress.md`。
