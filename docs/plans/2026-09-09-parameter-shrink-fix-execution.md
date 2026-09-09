# ParameterShrink Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 修复 ParameterShrink 的 10 个已知设计和实现缺陷，使参数收缩只在声明、所有绑定、契约和副作用都可证明安全时生成决定，并保持已有合法收缩场景的行为不变。

**Architecture:** 将参数收缩拆成“传播阶段收集并验证事实”和“提案阶段翻译已验证事实”两层。传播 payload 携带声明身份、参数集合、完整调用点覆盖状态、绑定类型、替换映射和失败原因；任何 dynamic、未知绑定、未覆盖的 method group/function pointer、外部 API 风险、契约冲突或副作用参数都产生 `Unknown/Skip`，提案层不再重新扫描 compilation。声明替换和调用点替换共享同一套安全事实，多个同类型目标参数合并成一个声明级收缩单元。

**Tech Stack:** C# / .NET 10, Roslyn `SemanticModel` and `IOperation`, xUnit, existing NLISSN Propagate/Propose pipeline, PowerShell test harness.

---

## 1. 范围和不变量

### 覆盖问题

| 编号 | 问题 | 计划任务 |
| --- | --- | --- |
| P1-1 | 删除参数后方法体、局部函数体或索引器体仍引用参数 | Task 2 |
| P1-2 | 普通位置参数路径漏掉重载冲突检查 | Task 2 |
| P1-3 | 泛型构造调用和构造索引器访问匹配失败 | Task 3 |
| P1-4 | 普通方法的 method group/function pointer 使用未扫描 | Task 3 |
| P1-5 | dynamic、未知绑定和外部调用者被当作不存在 | Task 3 |
| P1-6 | 隐式 interface 实现未排除 | Task 2 |
| P1-7 | 索引器缺少契约与重载冲突保护 | Task 2 |
| P1-8 | 删除调用实参时丢失副作用 | Task 4 |
| P2-1 | 同一声明的多个目标参数按声明节点去重 | Task 5 |
| P2-2 | Propagate 直接调用 Propose analyzer，且 Propose 重做绑定 | Task 5 |

### 必须保持的不变量

- 任何生成的声明替换都能在当前语法树和语义模型中找到被删除参数的全部实现引用；若参数仍被方法体、局部函数体、索引器 accessor、expression body、method group target 或 lambda body 使用，则跳过。
- 任何生成的调用点替换都来自传播阶段已验证的唯一绑定；未知、动态、歧义、外部不可见调用或 function pointer 使用不能被当作空集合。
- 普通方法、局部函数和索引器删除参数后不能产生重复签名，也不能破坏 implicit/explicit interface implementation、override、abstract、virtual 或其他成员契约。
- 删除实参只能丢弃已证明无副作用的表达式；无法证明纯性的表达式必须让整个收缩候选跳过。
- 同一声明的多个目标参数必须保留各自的参数索引和 provenance，并在一个声明级收缩单元中按降序同步删除参数和所有对应实参，不能发出互相冲突的部分替换。
- Propose 只消费 Propagate 生成的 typed payload 和 replacement facts，不再调用 compilation-wide scanner，也不重新推断调用点绑定。
- 现有命名参数、optional、params、扩展方法、委托、lambda 和普通合法位置参数场景保持现有输出；未知场景宁可跳过，不得生成不完整决定。

### 非范围

- 不扩大 ParameterShrink 的支持语法到未验证的 C# 新语法。
- 不尝试自动重写有副作用的实参为临时语句；本轮统一保守跳过。
- 不改变既有规则 ID、决策 action、规则图节点类型和最终 rewrite 格式，除非为携带参数集合和 skip reason 所必需。
- 不删除已有 `ParameterShrinkAnalyzer` 的外部测试入口；必要时将其改成对新 fact collector/rewrite factory 的兼容 facade。

## 2. Task 1: 建立缺陷回归测试和目标契约

**Files:**
- Modify: `tests/NLISSN.Testing/TestCodeSet/Performance/PerformanceSources.cs`
- Modify: `tests/NLISSN.Testing/TestCodeSet/Pipeline/PipelineSources.cs`
- Modify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- Modify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Create if the existing fixtures become too large: `tests/NLISSN.PerformanceTests/Performance/ParameterShrinkSafetyRegressionTests.cs`

### Step 1: 添加声明安全的红灯用例

为普通方法、局部函数和索引器各添加一个目标参数被 body 引用的 fixture：

```csharp
private int Apply(PlayerInput input, int frame) => input.GetHashCode() + frame;
```

断言对应 analyzer 不生成 plan，并覆盖 block body、expression body、indexer getter 三种 body 形状。

添加以下契约用例并断言 plan 被拒绝：

- 普通方法删除参数后与已有 overload 重复。
- implicit interface method implementation。
- explicit/implicit interface indexer implementation。
- override、abstract 或 virtual indexer。
- 删除参数后与现有 indexer 产生相同参数签名。

### Step 2: 添加调用覆盖和绑定用例

添加以下 fixture，所有用例都必须断言不会生成不完整 plan：

- generic method declaration plus `M<int>(...)` and inferred `M(...)` callsites。
- generic containing type plus constructed indexer access。
- ordinary method used as method group。
- ordinary method used through a function pointer where the language/runtime reference is available。
- dynamic receiver invocation mixed with one statically known invocation。
- public/protected method whose only consumer is outside the analyzed compilation boundary。
- ambiguous overload candidate and unresolved symbol。

对泛型场景同时断言已知调用点能被收集，避免用“整体跳过”掩盖符号匹配缺陷；对 unknown 场景断言 payload 的 coverage 状态为 `Unknown` 或 plan 为 false。

### Step 3: 添加副作用和多参数用例

添加：

- `Apply(LogAndReturnInput(), frame)`，断言不会删除目标实参。
- named/optional/mapped invocation 中的副作用实参，断言行为一致。
- 一个方法含两个 `PlayerInput` 参数，调用点同时传入两个参数，断言生成一个包含两个参数索引的声明级 payload，并一次性删除两个参数和两个实参。

### Step 4: 固化 Propagate/Propose 边界测试

在 `PipelineComponentTests.cs` 中增加传播测试，验证 payload 包含：

- 声明稳定身份；
- 目标参数索引集合；
- 每个参数的 mode；
- 每个调用点的唯一绑定和替换位置；
- `Complete` 或 `Unknown` coverage；
- skip reason/provenance。

Propose 测试使用同一 payload 直接生成 decisions，并通过扫描调用计数或测试替身证明提案阶段没有重新遍历 compilation。

### Step 5: 运行红灯测试

Run:

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink|FullyQualifiedName~PropagationEngine_Run_DeclarationMethodParameterUsageRule"
```

Expected: 新增边界测试在当前实现下失败，已有合法场景保持通过。若新测试未能复现对应缺陷，先修正 fixture 的语义绑定，再进入实现阶段。

### Step 6: Commit

```powershell
git add tests/NLISSN.Testing/TestCodeSet/Performance tests/NLISSN.Testing/TestCodeSet/Pipeline tests/NLISSN.PerformanceTests/Performance tests/NLISSN.HostTests/Application/PipelineComponentTests.cs
git commit -m "test: lock parameter shrink safety boundaries"
```

## 3. Task 2: 集中实现声明、契约和签名安全证明

**Files:**
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `src/NLISSN.Rules/Propose/Support/MethodProposalSafety.cs`
- Create: `src/NLISSN.Rules/Propagate/ParameterShrink/ParameterShrinkSafety.cs`
- Modify: `src/NLISSN.Core/Propagation/DeclarationStructuredPropagationPayloads.cs` if the safety result is part of the shared payload
- Test: `tests/NLISSN.PerformanceTests/Performance/ParameterShrinkSafetyRegressionTests.cs`

### Step 1: 定义统一安全结果

在 `ParameterShrinkSafety.cs` 中定义内部 typed result，而不是让各个 `TryBuild*Plan` 只返回 bool：

- `ParameterShrinkCoverage`: `Complete`、`Unknown`、`Conflict`、`Unsupported`。
- `ParameterShrinkSkipReason`: body reference、interface contract、override contract、overload conflict、indexer conflict、unknown binding、side effect 等稳定枚举值。
- `ParameterShrinkTargetFact`: declaration syntax key、parameter symbol identity、source parameter index、mode、body proof。

所有 analyzer 入口先取得 safety result；除 `Complete` 外不得创建 plan。Propose 层只读取结果，不用字符串猜测失败原因。

### Step 2: 增加 body 引用证明

实现 `HasParameterReference(SemanticModel, SyntaxNode owner, IParameterSymbol parameter)`：

- 遍历 method body、local function body、indexer accessor body 和 expression body 的 operation tree。
- 只接受绑定到目标 `IParameterSymbol` 的引用；同名局部变量或 lambda 参数不能误判。
- 将 method group target 的 body 和 local function body 纳入同一检查。
- 在构造 `replacementMethod`、`replacementLocalFunction`、`replacementIndexer` 之前执行证明。

保留现有纯语法 `TryBuildReplacement*` 方法作为最后一步的 syntax builder，但禁止任何 plan builder 在未通过语义引用检查时直接调用它。

### Step 3: 修正方法成员契约判断

扩展 `MethodProposalSafety`，输入 declaration syntax 和 `IMethodSymbol`，统一检查：

- `ExplicitInterfaceImplementations`；
- containing type 的 interfaces 中是否存在该方法的 implicit implementation；
- `IsOverride`、`IsAbstract`、`IsVirtual`、`IsExtern`、partial/extern 相关形状；
- 方法是否处于允许的分析 scope。

不要仅凭 `ExplicitInterfaceSpecifier` 或 modifier token 判断接口契约。

### Step 4: 修正索引器契约和索引器重载冲突

为 `IPropertySymbol` 增加与方法契约等价的 interface/override/abstract/virtual 检查，并实现 `HasConflictingReplacementIndexer`：

- 删除目标参数后构造 replacement parameter signature；
- 比较同一 containing type 中的 indexer 参数数量、类型和 ref kind；
- 排除当前 indexer 本身；
- 在普通和 named indexer plan 两条路径都调用该检查。

`TryResolveIndexerParameter` 仍可排除接口声明本身，但不能把它当作完整的契约安全检查。

### Step 5: 补上普通位置方法的 overload guard

在 `TryBuildMethodPlan` 和所有共享该路径的 extension/method-group target builder 中，先调用 `HasConflictingReplacementOverload`，再创建 replacement method。将检查放到共享 helper，避免 named/optional/params 与 positional 路径行为分叉。

### Step 6: 运行声明安全测试

Run:

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrinkSafetyRegressionTests"
```

Expected: body reference、interface、indexer conflict、ordinary overload conflict 测试 PASS；现有 named/optional/params/indexer 正常场景继续 PASS。

### Step 7: Commit

```powershell
git add src/NLISSN.Rules/Propagate/ParameterShrink src/NLISSN.Rules/Propose/ParameterShrink src/NLISSN.Rules/Propose/Support/MethodProposalSafety.cs src/NLISSN.Core/Propagation/DeclarationStructuredPropagationPayloads.cs tests/NLISSN.PerformanceTests/Performance/ParameterShrinkSafetyRegressionTests.cs
git commit -m "fix: prove parameter shrink declaration safety"
```

## 4. Task 3: 修正符号归一化和完整调用点覆盖

**Files:**
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Create or move: `src/NLISSN.Rules/Propagate/ParameterShrink/ParameterShrinkFactCollector.cs`
- Modify: `src/NLISSN.Core/Propagation/DeclarationStructuredPropagationPayloads.cs`
- Test: `tests/NLISSN.Testing/TestCodeSet/Performance/PerformanceSources.cs`
- Test: `tests/NLISSN.PerformanceTests/Performance/ParameterShrinkSafetyRegressionTests.cs`

### Step 1: 统一方法和属性符号比较

让 direct invocation collector 与 mapped invocation collector 使用同一套 lookup：

- `methodSymbol`、`ReducedFrom`、`OriginalDefinition` 和 reduced original 都进入候选集合；
- 目标匹配使用已有 `MethodMatchesInvocationTarget` 语义，而不是单纯 `SymbolEqualityComparer.Default.Equals`；
- 每个语法节点只保留一次 binding，避免同一个 constructed generic invocation 因多个 lookup key 重复计数；
- indexer/property collector 增加等价的 original-definition comparison。

普通、named、optional、params、extension 和 delegate invocation-chain 路径都必须走同一 helper，不能只修其中一个 collector。

### Step 2: 扫描 method group 和 function pointer

扩展 `TreeScan` 的 binding 构建：

- 继续收集 `InvocationExpressionSyntax`；
- 从 operation tree 收集 `IMethodReferenceOperation`/method-group conversion；
- 收集 function pointer address-of/reference operation（若当前 Roslyn API 版本支持）；
- 记录使用形状和目标参数映射，不把 method group 当作普通 invocation。

如果 operation tree 无法唯一解析目标方法，生成 `Unknown` binding；不能继续使用“没有 binding 就说明没有使用”的语义。

### Step 3: 建立 unknown 和分析 scope 规则

新增 `CallsiteCoverageFact`，至少包含 `Complete`、`UnknownDynamic`、`UnknownOverload`、`UnknownFunctionPointer`、`OutsideCompilation`。

- `GetSymbolInfo` 没有唯一方法、候选符号多于一个或 operation 为 dynamic 时，保留 unknown fact；
- `requireCallsites` 只允许检查“至少一个已知调用点”，不能代替 complete coverage；
- non-private/public/protected 方法只有在调用上下文明确声明为 closed-world 时才可继续；默认分析 scope 不足时返回 `OutsideCompilation`；
- private/local 方法也不能因为 `requireCallsites=false` 而忽略 method group、dynamic 或未知绑定；
- skip reason 进入 payload，供 proposal evidence 和测试断言。

### Step 4: 运行绑定覆盖测试

Run:

```powershell
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrinkSafetyRegressionTests|FullyQualifiedName~PerformanceOptimizationRegressionTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink|FullyQualifiedName~PropagationEngine_Run_DeclarationMethodParameterUsageRule"
```

Expected: generic constructed calls/accesses被正确纳入 complete coverage；method group、function pointer、dynamic、ambiguous 和 closed-world 之外的 public usage 被拒绝或明确标记 unknown；已有 extension/delegate tests 不回归。

### Step 5: Commit

```powershell
git add src/NLISSN.Rules/Propose/ParameterShrink src/NLISSN.Rules/Propagate/ParameterShrink src/NLISSN.Core/Propagation tests/NLISSN.Testing/TestCodeSet/Performance tests/NLISSN.PerformanceTests/Performance
git commit -m "fix: cover parameter shrink bindings conservatively"
```

## 5. Task 4: 防止调用点副作用丢失

**Files:**
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/MethodParameterUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/LocalFunctionAndIndexerUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/DelegateAndExtensionUsage.cs`
- Test: `tests/NLISSN.Testing/TestCodeSet/Performance/PerformanceSources.cs`
- Test: `tests/NLISSN.PerformanceTests/Performance/ParameterShrinkSafetyRegressionTests.cs`

### Step 1: 定义保守的纯表达式判断

实现共享 `IsSafeToDropArgument(IOperation argument)`，只接受没有用户代码执行的表达式，例如 literal、`null`、`default`、`nameof` 和已经验证的简单局部读取。以下形状默认不安全：

- invocation、object creation、assignment、increment/decrement；
- property/indexer access 或可能调用 getter 的 member access；
- conditional/coalesce containing unsafe branches；
- await、anonymous function invocation、dynamic expression；
- `ref`/`out`/`in` 或其他 unsupported parameter shape。

判定结果写入 callsite fact；一个目标参数只要有一个 unsafe argument，整个声明级计划跳过，不能只跳过单个调用点而继续删声明。

### Step 2: 接入所有实参删除路径

将纯性检查接入：

- `TryBuildReplacementInvocation`；
- `TryBuildNamedArgumentReplacementInvocation`；
- `TryBuildOptionalReplacementInvocation`；
- `TryBuildMappedInvocationReplacement`；
- `TryBuildNamedArgumentReplacementElementAccess` 和 positional element access。

Propagate 阶段应先收集并证明，Propose 阶段只应用已验证的 argument span/index；不要在 Propose 阶段遇到失败后静默 `continue`。

### Step 3: 验证副作用语义

添加 `LogAndReturnInput()`、property getter、constructor 和 dynamic argument fixtures。断言这些场景没有 declaration decision 和 invocation decision；literal/null/default 场景仍能正常收缩。

### Step 4: Commit

```powershell
git add src/NLISSN.Rules/Propose/ParameterShrink src/NLISSN.Rules/Propose/Parameters tests/NLISSN.Testing/TestCodeSet/Performance tests/NLISSN.PerformanceTests/Performance
git commit -m "fix: preserve argument side effects during shrink"
```

## 6. Task 5: 重构 payload、去重和 Propagate/Propose 边界

**Files:**
- Modify: `src/NLISSN.Core/Propagation/DeclarationStructuredPropagationPayloads.cs`
- Modify: `src/NLISSN.Rules/Propagate/MethodParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/LocalFunctionParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/IndexerParameterUsagePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/ExtensionMethodMappedCallsitePropagationRule.cs`
- Modify: `src/NLISSN.Rules/Propagate/DelegateUsageClassificationPropagationRule.cs` where it consumes the same analyzer path
- Modify: `src/NLISSN.Rules/Propose/Parameters/MethodParameterUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/LocalFunctionAndIndexerUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/Parameters/DelegateAndExtensionUsage.cs`
- Modify: `src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs`
- Modify: `src/NLISSN.Core/Validation/RuleBindingValidator.cs` and `src/NLISSN.Rules/Propose/Support/ProposalFacts.cs` if payload shape changes require validation updates
- Test: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`

### Step 1: 把共享 payload 收敛为传播事实

在 Core propagation payload 中引入不依赖 Propose namespace 的事实类型：

- declaration identity and syntax node;
- `IReadOnlyList<ParameterShrinkTargetFact>`，每项含 parameter index、mode、symbol key 和 body proof；
- `IReadOnlyList<ParameterCallsiteFact>`，每项含调用/访问节点、绑定 kind、目标参数索引、要删除的 argument span/index 和 side-effect proof；
- `CallsiteCoverageFact`；
- `SkipReason` 和来源 mark/span。

不要让 Core payload 引用 `InvocationRewrite`、`ElementAccessRewrite` 等位于 Propose 的类型；若需要 replacement syntax，新增位于 Core/Propagation 的结构化 fact 类型。

### Step 2: 修复多目标参数的 identity 和聚合

将去重键从单一声明节点改为 declaration identity 加目标参数集合：

- 同一声明中的两个目标参数不能相互覆盖；
- 同一调用点因删除两个参数只保留一份聚合 fact；
- 参数和实参按原始索引降序删除，避免前一个删除改变后一个索引；
- declaration decision 只生成一次，且 replacement 同时删除全部已证明目标参数；
- 如果多个参数的 mode、coverage 或安全状态无法合并，则整个声明级候选跳过并记录原因，不能退化成只删其中一个。

对应更新 `MethodParameterUsagePayload`、`LocalFunctionParameterUsagePayload`、`IndexerParameterUsagePayload` 和 extension payload 的 singular `Parameter/ParameterIndex` 消费点。

### Step 3: 把 compilation-wide 扫描移出 Propose

将现有 analyzer 中的扫描、symbol lookup、调用点收集和 coverage proof 提取到 `ParameterShrinkFactCollector` 或同层 Propagate helper：

- `MethodParameterUsagePropagationRule`、local function、indexer 和 extension propagation 只调用 collector；
- collector 返回完整 typed facts 或明确 skip reason；
- Propose helper 只按 payload 读取 replacement facts 并创建 `DecisionUnit`；
- 删除 Propose helper 中通过 `compilation.GetSemanticModel` 重新解析参数、调用 operation 和 symbol 的路径；
- 删除遇到 replacement 失败就静默 `continue` 的行为，失败必须在传播阶段体现为 non-actionable payload。

### Step 4: 保持 evidence 和 fact key 稳定

更新 `PropagationFactKey`/相关 validator，使同一 declaration + parameter set 的 payload 能稳定去重，而不同参数集合不会被吞掉。确保 `RuleEvidenceOrigin`、source mark、文件路径和 span 仍可追溯。

### Step 5: 运行边界和 payload 测试

Run:

```powershell
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink|FullyQualifiedName~PropagationEngine_Run_DeclarationMethodParameterUsageRule|FullyQualifiedName~PropagationEngine_Run_DeclarationLocalFunctionParameterUsageRule"
```

Expected: 两个相同目标参数都出现在同一个聚合 payload；Propose 输出一个声明替换和对应调用点替换；payload 的 skip reason、coverage、source span 可被验证；没有第二次 compilation-wide 扫描。

### Step 6: Commit

```powershell
git add src/NLISSN.Core/Propagation src/NLISSN.Core/Validation src/NLISSN.Rules/Propagate src/NLISSN.Rules/Propose tests/NLISSN.HostTests/Application/PipelineComponentTests.cs
git commit -m "refactor: make parameter shrink facts propagation-owned"
```

## 7. Task 6: 完整回归、确定性和文档收口

**Files:**
- Modify if behavior wording changed: `设计docs/目前设计/proposal-fact-extraction.md`
- Modify if implementation boundary changed: `设计docs/目前设计/testing-strategy.md`
- Verify: `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- Verify: `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`
- Verify: `tests/NLISSN.Testing/TestCodeSet/Performance/PerformanceSources.cs`

### Step 1: 运行最小 owning tests

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~ParameterShrink|FullyQualifiedName~PropagationEngine_Run_DeclarationMethodParameterUsageRule|FullyQualifiedName~PropagationEngine_Run_DeclarationLocalFunctionParameterUsageRule"
```

Expected: 新增安全测试和原有参数收缩测试全部通过，失败数为 0。

### Step 2: 运行 DOP 确定性验证

对普通方法、generic method、indexer 和多目标参数 fixture 分别使用 DOP 1、2、16，比较：

- payload fact signature；
- decision ordering and replacement text；
- rewritten source；
- final edit list。

Expected: 三个 DOP 下上述输出字节级一致，unknown/skip reason 也一致。

### Step 3: 运行相关测试 tier

```powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Host
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Performance
```

Expected: Fast、Host、Performance tier 均通过；如果环境再次出现编译器文件锁，先按错误记录使用单进程/隔离输出目录重跑，不把锁错误当作功能通过。

### Step 4: 验证最终重写结果可编译

对每个成功收缩 fixture 使用现有 host rewrite 流程重新构造 compilation，断言没有 error diagnostics。对每个 skip fixture 断言原始 source 没有被修改。

### Step 5: 更新设计说明

在 `proposal-fact-extraction.md` 中补充 ParameterShrink 的事实流：

```text
target parameter mark
  -> propagation-owned declaration/callsite facts
  -> coverage and safety proof
  -> grouped parameter shrink payload
  -> propose-only decision translation
  -> rewrite and compile validation
```

明确 unknown binding、外部 API、method group/function pointer 和副作用表达式的默认行为是 `Skip`，并说明多目标参数按声明级 payload 聚合。

### Step 6: 静态检查和最终验收

```powershell
git diff --check
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git status --short
```

Expected: 无 whitespace error，harness consistency 通过，除计划中明确的源码/测试/设计文档外没有生成临时文件。

### Step 7: Commit

```powershell
git add src tests 设计docs/目前设计/proposal-fact-extraction.md 设计docs/目前设计/testing-strategy.md
git commit -m "fix: harden parameter shrink analysis"
```

## 8. 完成条件

- [ ] 10 个问题各有对应回归测试，且每个测试在修复前能稳定复现问题。
- [ ] 方法、局部函数、索引器和 method-group target 的 body 引用不会生成无效声明。
- [ ] 普通方法、索引器和多目标参数不会产生重复签名或互相冲突的 rewrite。
- [ ] generic constructed invocation/indexer access 被正确匹配并按语法位置去重。
- [ ] method group、function pointer、dynamic、歧义绑定和 closed-world 之外的外部调用都不会被静默忽略。
- [ ] implicit/explicit interface implementation、override、abstract、virtual 和相关索引器契约默认跳过。
- [ ] 有副作用的删除实参默认跳过；纯 literal/null/default 场景继续成功。
- [ ] payload 由 Propagate 负责收集和证明，Propose 不再执行 compilation-wide 扫描或重新绑定。
- [ ] DOP 1、2、16 下事实、决定、重写和 skip reason 稳定一致。
- [ ] Performance、Host、Fast、Host tier、Performance tier 以及 harness consistency 全部通过。
- [ ] `proposal-fact-extraction.md` 与实际 ParameterShrink 分层和 unknown policy 一致。
