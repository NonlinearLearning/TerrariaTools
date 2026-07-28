# 传播阶段低风险局部性能优化执行提案 Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 在不调整阶段拓扑、`GroupKey`、DOP、结构视图契约或缓存边界的前提下，移除传播热路径中已经由源码确认的重复查找和重复分配。

**Architecture:** 仅在现有 `PropagationEngine` 和三个传播规则内部替换等价的局部实现。每个改动保留规则调用次序、同组可见性、产出顺序与最终去重键；不引入新的跨规则状态。

**Tech Stack:** .NET 10、C#、Roslyn、xUnit。

---

## 结论与边界

本提案选择三项局部等价优化：

1. 用组内语法节点键集合替代 `PropagationEngine.RunGroup` 中对 `groupMarks.Any(...)` 的重复线性查找。
2. 在两条逻辑条件传播规则的单次 `Propagate(...)` 调用内，只构造一次稳定的逗号分隔目标名。
3. 在两条局部符号引用传播规则中，对每个已标记定义只解析一次其可执行作用域。

这些改动不需要性能基准数据才可判断正确性：它们分别消除随 `groupMarks` 增长的重复比较、同一迭代内不变字符串的重复分配，以及同一源节点的重复祖先遍历。性能收益的大小留待后续独立测量，不改变本提案的实现门槛。

本提案明确排除下列工作：

- `RuleDefinitionPropagate` 增加 `RequiresStructureView` 等新契约，或取消 `BuildRuleContext(...)` / `StructureView` 构建；当前测试 `PropagationEngine_Run_BuildsRuleScopedStructureViewForEachRule` 锁定了传播规则可观察到该视图。
- 为 `IdentifierNameSyntax` 建立 compilation 级符号索引；这会改变 `AnalysisRuntime` 缓存内容、并发访问和 Roslyn 节点生命周期边界。
- 合并 `DEL-CLASS` / `DEL-SOBJ` 规则、改变组内规则注册顺序，或改变 `EnableGroupParallelism`、默认 DOP、调度器与输出收口位置。
- 修改 `PropagatedMarkRecord` 的最终去重键、payload、reason 文本或 rewrite / decision 代码。

## 必须保持的行为

1. 同一组规则仍按注册顺序执行；后续规则只能看到前序规则完整枚举结束后加入的新增 `MarkRecord`。
2. `groupMarks` 的成员资格仍只按 `(SpanStart, Span.Length, RawKind)` 判断；最终输出仍按 `(GroupKey, RuleId, SpanStart, Span.Length, RawKind)` 去重。因此不同 `RuleId` 在同一语法节点上产生的 payload 继续保留。
3. `PropagatedMarkRecord` 的产出和最终列表顺序不变；不能把最终 `DistinctBy(...)` 提前到规则产出路径。
4. `ClassSymbolReferencePropagationRule` 保留“对象创建定义、同一可执行作用域、且引用位于定义之后”的限制；`SObjectSymbolReferencePropagationRule` 保留现有的定义识别、同作用域和引用顺序语义。
5. DOP 1 与已存在的 group-parallel 路径产生相同的传播、决策和改写结果。

## 源码依据

| 位置 | 当前重复工作 | 局部替换 |
| --- | --- | --- |
| `src/NLISSN.Core/Propagation/PropagationEngine.cs:92-101` | 每个候选产出都扫描增长中的 `groupMarks`。 | 以同一节点键维护 `HashSet`，保留在单条规则枚举结束后才追加 `groupMarks` 的时机。 |
| `src/NLISSN.Rules/Propagate/TargetPropagationRules.cs:119, 209` | 每个候选 seed 都执行 `string.Join(",", targetNames)`。 | 每个规则调用在循环前构造一次 `targetNameList`。 |
| `src/NLISSN.Rules/Propagate/TypeSymbolReferencePropagationRule.cs:43-57` | 同一 source definition 的可执行作用域随每个候选引用重复遍历祖先。 | 构建 marked-symbol 映射时缓存 source scope。 |
| `src/NLISSN.Rules/Propagate/TargetPropagationRules.cs:306-320` | 与 delete-class 规则相同的 source-scope 重复遍历。 | 用同一局部模式缓存 source scope，不抽取跨规则公共层。 |

## Task 1: 锁定组内可见性和局部符号传播边界

**Files:**

- Modify: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: 添加组成员资格回归**

在现有 `PropagationEngine_Run_DeduplicatesSamePropagatedSpan` 与 `PropagationEngine_Run_ChainsIndependentRulesWithinSameGroupKey` 附近新增一个 fixture rule 对：

- 第一条规则对同一语法节点连续产生两条同 rule、同 span 的传播记录；
- 第二条同组规则读取输入 marks，并把该节点是否只出现一次写入可观察的下游传播结果。

断言最终结果保留第一条规则的一条记录，并且第二条规则只观察到一份新增 `MarkRecord`。这同时锁定“同一规则内延迟写入”与“跨规则可见一次”的现有契约。

**Step 2: 添加两类局部定义作用域回归**

为 `ClassSymbolReferencePropagationRule` 和 `SObjectSymbolReferencePropagationRule` 分别建立最小源代码夹具，覆盖：

- 同一方法内定义之后的引用被传播；
- 嵌套 local function 或 lambda 中同名变量不被传播；
- delete-class 的定义之前引用继续不被传播。

断言 `RuleId`、syntax span、source mark 与 reason 文本保持当前行为。测试只验证输出；不通过反射断言私有缓存字段。

**Step 3: 运行失败/通过基线**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

Expected: 新增测试先能在现状下通过，作为等价重构基线；若基线失败，停止后续生产修改，先确认当前语义或夹具假设。

**Step 4: Commit**

```text
Lock propagation local-optimization behavior
```

提交说明记录同组可见性、symbol scope 和最终去重键是不可变约束。

## Task 2: 将组内成员资格查询收敛为 O(1)

**Files:**

- Modify: `src/NLISSN.Core/Propagation/PropagationEngine.cs:72-108`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: 在 `RunGroup` 初始化节点键集合**

保留现有局部键的三个字段，并以 `groupSeedMarks` 初始化：

```csharp
var groupMarks = new List<MarkRecord>(groupSeedMarks);
var groupMarkKeys = groupMarks
  .Select(mark => (mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length, mark.SyntaxNode.RawKind))
  .ToHashSet();
```

键只用于同组可见集合，不能混入 `RuleId`、`GroupKey`、`Payload` 或 `Reason`。

**Step 2: 只替换成员资格判断**

在 `producedMarks` 的第二个循环中用 `groupMarkKeys.Add(...)` 判断首次出现；返回 `false` 时继续，返回 `true` 时才执行现有的 `groupMarks.Add(producedMark.Mark)`。

必须保留两个循环的边界。禁止在第一个 `foreach (var propagatedMark ...)` 中把产物直接写入 `groupMarks`，以免同一条规则读取自身刚产出的 mark。

**Step 3: 验证**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~BoundedRuleStageScheduler"
```

Expected: 全部通过。第二条命令只覆盖已存在的调度顺序/取消保护，不把本任务变成 DOP 调参。

**Step 4: Commit**

```text
Avoid repeated group-mark scans during propagation
```

## Task 3: 移除逻辑规则内的不变字符串重复构造

**Files:**

- Modify: `src/NLISSN.Rules/Propagate/TargetPropagationRules.cs:101-161`
- Modify: `src/NLISSN.Rules/Propagate/TargetPropagationRules.cs:191-251`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: 在每条规则的循环前构造目标名**

保留各自已有的 target-name 解析和空列表早退。在早退之后增加：

```csharp
var targetNameList = string.Join(",", targetNames);
```

将循环内两处 `string.Join(",", targetNames)` 替换为该局部变量。不要改用新缓存，也不要改动 `ParseTargetNames(...)` 的排序、去重或大小写行为。

**Step 2: 锁定多目标名逻辑结果**

扩展现有 `PropagationEngine_Run_DeleteSObjectLogicalOperandGroupRule_ProducesStructuredPayload` 夹具，使 `target-name` 包含两个名称，并断言逻辑 host、removable/surviving payload 和 reason 与改动前相同。再运行现有完整 logical-propagation fixture，确保弱逻辑宿主规则仍会在 payload 不可构造时回退。

**Step 3: 验证并提交**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

```text
Reuse logical propagation target names within each rule run
```

## Task 4: 缓存局部符号传播源端作用域

**Files:**

- Modify: `src/NLISSN.Rules/Propagate/TypeSymbolReferencePropagationRule.cs:37-119`
- Modify: `src/NLISSN.Rules/Propagate/TargetPropagationRules.cs:277-372`
- Test: `tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**Step 1: 让 marked-symbol 映射携带不可变 source facts**

在每个规则的私有实现内，以私有 record 或 value tuple 保存：

```csharp
(MarkRecord SourceMark, SyntaxNode? ExecutableScope)
```

构建映射时，对 source mark 调用一次现有 `FindContainingExecutableScope(...)`。若 scope 为 `null`，仍保留该 entry；引用比较时按现有语义拒绝它。

**Step 2: 在引用循环中只解析 reference scope**

保留 `GetSymbolInfo(reference)`、`TryGetValue(...)`、原有 source-order 判断和 `knownKeys` 的检查顺序。将 `IsSameScope(sourceMark.SyntaxNode, reference)` 替换为：

```csharp
var referenceScope = FindContainingExecutableScope(reference);
if (markedDefinition.ExecutableScope is null ||
    !ReferenceEquals(markedDefinition.ExecutableScope, referenceScope))
{
    continue;
}
```

不得建立全树 identifier 索引，不得把 `ISymbol` 比较替换为名称比较，也不得跨两个规则抽取新的共享服务。

**Step 3: 验证并提交**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests"
```

```text
Reuse source executable scopes during local reference propagation
```

## 完成验证与回滚

在所有任务完成后，顺序执行：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName!~Execute_TerrariaCodeSet"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

当前 `init.ps1` 仍固定查找已不存在的 `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`，会在任何构建前失败；`scripts/check-harness-consistency.ps1` 也仍查找已不存在的 `src/RoslynPrototype/Program.cs`。这两项 harness 路径漂移不属于本提案，验证时以以上实际项目命令替代，并在独立任务中修复脚本。`Execute_TerrariaCodeSet` 依赖仓库外部的 `D:\lodes\TR` 输入，只能在明确 opt-in 的环境中单独运行，不能作为常规 `dotnet test` 的验收项。

每个任务均可独立回滚。若任一测试显示同组前序可见性、symbol scope、最终传播集合、payload、decision 或 rewrite 发生变化，立即回滚对应任务；不以拆组、放宽 scope、减少 fixture 或修改 DOP 掩盖回归。
