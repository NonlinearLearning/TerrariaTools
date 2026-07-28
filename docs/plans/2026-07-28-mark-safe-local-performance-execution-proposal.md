# Mark 阶段低风险局部性能优化执行提案 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `@executing-plans` to implement this plan task-by-task.

**Goal:** 在不改变 Mark 规则行为、候选顺序、语义结论、并发调度或目录执行拓扑的前提下，删除已确认的临时分配和无效计算。

**Architecture:** 只修改单文件 `RuleContext`、Mark snapshot 与冻结图查询的局部实现。每项独立提交、独立验证、可独立回滚；不新增跨文件缓存或执行阶段。

**Tech Stack:** .NET 10、C#、Roslyn、现有 `NLCPGGraph` 查询索引、xUnit。

---

## 范围和不可变边界

执行范围仅限四项：

1. 删除 atomic candidate 枚举中结果未被消费的结构分析。
2. 直接枚举方法声明，删除恒成立的定义结构 gate。
3. 单次遍历计算缓存的 Mark region 统计。
4. `RuleContext` 直接返回冻结图的按 kind 节点列表。

不执行以下工作：目录级或项目级 Mark 重构；`AtomicExpressionAnalyzer` 的候选边界和排序；`Lazy`/并发缓存；DeleteClass 语义匹配；逻辑条件 Propagate；规则注册顺序、默认 DOP、CPG 构建或新依赖。

必须保持：

- Mark 的 rule id、group key、syntax kind、span、primary graph node 与输出顺序完全一致。
- DOP 1 和现有 group-parallel 路径产生相同的 Mark/Propagate/Lift/Decision/Rewrite 结果。
- `GetGraphNodesByKind` 返回的 NodeId 顺序不变。
- 不创建跨文件状态。

## 已有测试归属

- `tests/RoslynDeletionPrototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`：atomic kind、稳定顺序、完整 pipeline 和编译结果。
- `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`：snapshot、Mark engine、`RuleContext`。
- `tests/RoslynDeletionPrototype.HostTests/Application/GraphAnalyzerTests.cs`：不可达方法的规则效果。
- `tests/RoslynDeletionPrototype.UnitTests/Application/StructureViewBuilderTests.cs`：冻结图查询索引。

测试验证可观察行为，不断言被删除的私有临时列表。

## Task 1：先锁定行为基线

**Files:**

- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`
- Modify: `tests/RoslynDeletionPrototype.UnitTests/Application/StructureViewBuilderTests.cs`

**Step 1: Write failing candidate enumeration tests**

新增 `EnumerateAllowedExpressions_AllAtomicKinds_ReturnsRequestedKindsInSourceOrder`。fixture 必须同时含 identifier、literal、member access、invocation、element access、conditional access 与 object creation；分别断言 single-kind 和 multi-kind 请求的 kind、span、顺序。

**Step 2: Write failing method, region, and graph query tests**

新增：

- `EnumerateMethodDeclarations_WithBlockAndExpressionBodies_ReturnsAllMethodsInSourceOrder`；
- `AnalyzeMarkRegion_ForDistinctAnchors_PreservesAnchorAndExactCounts`；
- `GetGraphNodesByKind_AfterFreeze_MatchesGraphNodeIndexOrder`。

region fixture 使用同一语句的两个 anchor，并断言 `NodeCount`、`ExpressionCount`、`StatementCount` 的具体值。

**Step 3: Run the tests before production edits**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~MarkRuleRegistryCoverageTests"
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~StructureViewBuilderTests"
```

Expected: 新旧测试全部通过。任何失败先修正测试基线，不能开始生产修改。

**Step 4: Commit**

```text
Lock Mark local optimization behavior before removing transient work
```

提交正文遵循 Lore trailers，并记录实际测试命令。

## Task 2：删除 atomic candidate 的无效结构分析

**Files:**

- Modify: `src/NLISSN.Core/Analysis/RuleSyntaxAnalysisHelpers.cs:10-26`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`

**Step 1: Confirm the precondition**

确认 `RuleContext.EnumerateAllowedExpressions` 传入的候选来自 `MarkAnalysisSnapshot.GetAtomicCandidates`，且 `AtomicExpressionAnalyzer` 的每一种产出均由当前 `TryAnalyzeExpression` 接受。若存在自定义候选调用点，本任务停止。

**Step 2: Apply the minimal implementation**

将循环收敛为保留 kind 过滤和原始枚举顺序：

```csharp
foreach (var expression in atomicCandidates ?? new AtomicExpressionAnalyzer().Analyze(root))
{
    if (allowedKinds.Contains(expression.Kind()))
    {
        yield return expression;
    }
}
```

删除只被该 gate 使用的 `TryAnalyzeExpression` 分支；不能删除仍被其他 helper 使用的 structure analyzer。

**Step 3: Verify and commit**

运行 Task 1 的 Host 测试，另运行：

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MarkingEngine_Run_SObjectRules_PreservesSeedMarksAcrossGroupParallelismAndReusesCachedOperation|FullyQualifiedName~Mark_DeleteSObjectRule"
```

提交：

```text
Avoid discarded structure analysis while enumerating atomic Mark candidates
```

## Task 3：删除方法枚举的恒成立 gate

**Files:**

- Modify: `src/NLISSN.Core/Analysis/RuleSyntaxAnalysisHelpers.cs:29-40`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/GraphAnalyzerTests.cs`

**Step 1: Apply the minimal implementation**

令 `EnumerateMethodDeclarations` 直接 yield `root.DescendantNodes().OfType<MethodDeclarationSyntax>()`。不纳入 constructor 或 local function，不改变 Roslyn 遍历顺序。

**Step 2: Verify all consumers**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~GraphAnalyzerTests|FullyQualifiedName~AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods|FullyQualifiedName~AnalyzeFromArgs_ForDirectoryClearUnusedInterfaceImplementations|FullyQualifiedName~AnalyzeFromArgs_ForDirectoryPrivatizeInternalOnlyPublicMethods"
```

**Step 3: Commit**

```text
Remove unobserved definition analysis from Mark method enumeration
```

## Task 4：单次计算 snapshot region 统计

**Files:**

- Modify: `src/NLISSN.Core/Analysis/MarkAnalysisSnapshot.cs:220-229`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: Preserve region semantics**

不得改动 `MarkRegionAnalyzer.ResolveRegionNode`、`GetMarkRegion` 的 `RegionNode` cache key、`MarkCodeRegion` 字段或 `TextSpan`。

**Step 2: Apply the minimal implementation**

在 `regionNode.DescendantNodesAndSelf()` 的单次 foreach 内累计节点、表达式和语句数；直接构造原有 `MarkRegionFacts`，不再物化 descendants 列表。

**Step 3: Verify and commit**

```powershell
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MarkAnalysisSnapshot_ReusesRegionFactsForDistinctAnchorsInOneStatement|FullyQualifiedName~AnalyzeMarkRegion_ForDistinctAnchors_PreservesAnchorAndExactCounts|FullyQualifiedName~MarkRuleRegistryCoverageTests"
```

提交：

```text
Count cached Mark region facts without materializing descendants
```

## Task 5：取消冻结图节点列表复制

**Files:**

- Modify: `src/NLISSN.Core/Pipeline/RuleContext.cs:186-189`
- Test: `tests/RoslynDeletionPrototype.UnitTests/Application/StructureViewBuilderTests.cs`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/GraphAnalyzerTests.cs`

**Step 1: Apply the minimal implementation**

让 `GetGraphNodesByKind` 直接返回 `_analysisContext.Graph.GetNodes(kind)`。不能改动 `NLCPGGraph.NodesByKind` 的 mutable-graph 分支，也不能向调用方提供可写集合。

**Step 2: Verify and commit**

运行 Task 1 的 graph query test 和不可达方法测试。

```text
Reuse frozen graph node kind lists in Mark rule context
```

## Task 6：最终验证和交付

**Files:**

- Verify: `docs/plans/2026-07-28-mark-safe-local-performance-execution-proposal.md`
- Verify: Task 1-5 的生产和测试文件

**Step 1: Run owning suites sequentially**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~StructureViewBuilderTests"
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~MarkRuleRegistryCoverageTests|FullyQualifiedName~GraphAnalyzerTests"
```

**Step 2: Run repository checks**

```powershell
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

当前基线中，`check-harness-consistency.ps1` 因缺失 `src/RoslynPrototype/Program.cs` 失败；它不属于本提案范围。执行时必须重新运行并记录结果。若仍为同一错误，继续完成 focused suites 与 `git diff --check`，但最终报告必须明确“已完成代码与 focused 回归验证，仓库级 harness 仍被既有文件目录不一致阻塞”。不得把该错误归类为本提案回归。

**Step 3: Review the final diff**

确认 diff 只涉及 Task 1-5 列出的生产、测试和本计划文件；保留用户已有未提交修改。

## 完成条件和回滚

- 四项优化的 focused suites 与 `git diff --check` 均通过。
- Mark、后续 pipeline、改写结果和编译诊断无差异。
- 每项修改独立提交、独立可回滚。
- `check-harness-consistency.ps1` 必须被重新执行；若其既有缺失文件问题未先由独立任务修复，不能宣称取得仓库级 harness 验收。

任何 Mark span、kind、顺序、graph binding、规则效果、改写结果或编译诊断变化，都立即回滚对应单项提交；不得通过改测试期望、禁用规则或降低并行度掩盖差异。
