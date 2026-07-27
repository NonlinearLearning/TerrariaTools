# Mark 规则注册表强制覆盖执行提案

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为默认注册的每条 `RuleDefinitionMark` 建立强制覆盖的逻辑验证，并证明每个正例可产出等价、可编译的最终重写产物。

**Architecture:** 测试通过 `RuleRegistry.CreateDefaultRules().Markers` 发现运行时实际启用的规则；显式场景注册表按 `RuleId` 提供正例、近似反例、期望 mark 节点和最终产物断言。注册规则集合与场景集合必须完全相等，新增规则没有测试场景时测试失败。每个正例复用同一份输入，依次验证 Mark 局部契约、全管线结果、直接重写/内存计划回放/持久化计划回放等价和 Roslyn 编译。

**Tech Stack:** .NET 10、xUnit、Microsoft.CodeAnalysis.CSharp、现有 `RuleRegistry`、` ApplicationService`、`PrototypeRewriter` 与 rewrite-plan artifact 服务。

---

## 范围和口径

- 默认规则由 [RuleRegistry.cs](../../src/Host/RuleRegistry.cs) 按 `Rules` 命名空间反射发现；测试不得维护另一份隐式规则清单。
- 当前预期覆盖 23 条 Mark 规则：16 条 DeleteSObject、3 条 DeleteClass、4 条独立规则（接口实现、不可达方法、未引用方法、内部 public 方法）。实现时以运行时注册表为准，不能把 23 写成硬断言。
- 每个场景的局部验证对象为 `(RuleId, 文件路径, span, SyntaxKind, 原文文本)`；只验证该规则自己的 `Mark(...)` 输出，避免其它规则混入结论。
- 每个正例的最终产物验证对象为：最终源码、`RewritePlanEdit` 集合、diff、Roslyn error diagnostics 和持久化 plan JSON 回放结果。
- 近似反例必须覆盖至少一种同名但不同符号、字符串/注释同文本、或不满足规则语义的构造。
- 本提案只新增测试和测试输入；不修改 Mark、传播、决策或重写生产逻辑，除非测试先证明公共契约缺陷。

## 建议文件边界

- Create: `tests/Roslyn Prototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`
- Modify: `tests/Roslyn Prototype.Testing/TestCodeSet/...` 下与 Mark 场景对应的输入资产；优先扩展现有 `SObjectExpressionSources`、DeleteClass 和方法规则资产，避免把长源码嵌入断言类。
- Modify: `tests/Roslyn Prototype.HostTests/Mark/MarkRuleEffectTests.cs`，仅保留其现有真实行为/diff 回归职责，删除与新注册表测试重复的局部断言。
- Optional Create: `tests/Roslyn Prototype.Testing/Mark/MarkRuleScenarioCatalog.cs`，只有当 HostTests 无法以内部可见性共享场景模型时创建；不要将测试框架代码放入生产项目。

## Task 1：建立规则发现和场景模型

**Files:**

- Create: `tests/Roslyn Prototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`

1. 建立不可变 `MarkRuleScenario`：`RuleId`、正例源码、反例源码、CLI/规则选项、期望 mark 集合、最终产物断言。
2. 从 `RuleRegistry.CreateDefaultRules().Markers` 获取规则，以 `RuleId` 建立集合。
3. 写第一个失败测试：运行时规则 `RuleId` 集合与场景 `RuleId` 集合完全相同；失败信息分别列出“未覆盖规则”和“已失效场景”。
4. 场景目录中的每条规则恰好一个主正例；同一规则需要多个语法形态时，将变体放入同一场景的 case 列表，避免伪造多个规则覆盖计数。
5. 先运行：

   ```powershell
   dotnet test .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MarkRuleRegistryCoverageTests"
   ```

   预期：场景未补齐时失败，并显示 RuleId 差集。

## Task 2：为每条 Mark 规则编写局部逻辑契约

**Files:**

- Modify: `tests/Roslyn Prototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`
- Modify: 对应 `TestCodeSet` 输入资产

1. 对每个场景只实例化其 `RuleId` 对应的规则，调用 `Mark(context, root)`。
2. 断言输出的节点键集合完全等于期望集合：`SyntaxKind`、`SpanStart`、`Span.Length`、`ToString()` 和 `RuleId`。
3. 断言所有节点均属于 `rule.AllowedMarkNodeKinds`。
4. 断言同一 `(span, rawKind)` 仅出现一次，且结果按 `SpanStart` 升序、同起点按跨度降序稳定排列。
5. 对反例重复调用，断言空集合；反例不得因字符串、注释、遮蔽变量或相邻类型名产生 mark。
6. 为 DeleteSObject 的 16 条原子/声明规则至少覆盖：标识符、`this`、`base`、声明左值、数值/字符串/bool/null、成员/成员绑定、调用、显式/隐式构造、元素访问、条件访问。
7. 为 DeleteClass 三条规则分别覆盖：类声明、符号引用表达式、类型语法；为其余四条规则覆盖各自的 semantic predicate 与拒绝条件。
8. 运行同一 focused filter；预期全部通过，且覆盖测试发现的每条规则。

## Task 3：验证每条正例的完整最终产物

**Files:**

- Modify: `tests/Roslyn Prototype.HostTests/Mark/MarkRuleRegistryCoverageTests.cs`

1. 对每个正例使用默认完整管线运行：`Mark -> Propagate -> Lift -> Decide -> Rewrite`。
2. 捕获直接重写结果，同时捕获 plan 并执行内存回放；将 plan 写为 `RewritePlanFile` JSON、读回后执行持久化计划回放。
3. 断言三条路径的最终源码、编辑操作和 diff 相同。
4. 对最终源码创建 Roslyn compilation，断言 error diagnostics 为空；场景资产必须自包含所需的最小类型，不能依赖外部 Terraria fixture。
5. 断言场景指定的目标结构消失或被预期替换，且不相关声明/语句保留。
6. 对无 rewrite 的合法 Mark 场景，显式断言空 plan 和原文保持不变；不把“未产生产物”当作测试漏过。
7. 运行 focused filter；预期每条正例同时完成局部、最终产物、三路径和编译验证。

## Task 4：补齐计划边界和目录产物

**Files:**

- Modify: `tests/Roslyn Prototype.ContractTests/Rewrite/DiffModelTests.cs`
- Modify: `tests/Roslyn Prototype.HostTests/Rewrite/RewritePlanPersistenceTests.cs`

1. 保持现有重叠、失效文本、越界 span、重复操作测试；增加与 Mark 场景目录计划兼容的断言，不将同一错误重复在每个 Mark 规则 case 中。
2. 选择至少一个覆盖多规则/多文件的场景目录，捕获目录计划、回放并 Roslyn 编译全部回放后的文件。
3. 断言 manifest 文件集、每文件 hash 和回放文件集一致；缺失文件、已改原文和部分重叠编辑仍被拒绝。
4. 运行 Contract 与 Host focused tests；预期两者通过。

## Task 5：完成验证与提交

按顺序运行，避免共享输出目录的并发竞争：

```powershell
dotnet build .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MarkRuleRegistryCoverageTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests"
dotnet test .\tests\Roslyn Prototype.ContractTests\Roslyn Prototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiffModelTests"
dotnet test .\tests\Roslyn Prototype.HostTests\Roslyn Prototype.HostTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RewritePlanPersistenceTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

完成条件：

- 每条运行时注册 Mark 规则都有且只有一个场景入口；集合差异为零。
- 每个场景同时具有命中与拒绝断言，且局部 mark 结果精确、稳定、无重复。
- 每个可重写正例的三条路径最终产物等价，并通过 Roslyn 编译。
- 目录计划回放验证 manifest、文本完整性和全文件编译。
- 没有生产代码改动，除非失败测试证实公共行为错误；该例外必须单独记录原因和回归测试。

提交时使用 Lore commit 格式，明确记录测试覆盖的运行时注册表契约、拒绝的“只做快照”方案、验证命令及未覆盖风险。

## 风险和边界

- `RuleRegistry` 是反射注册；只检查类型数量会漏掉 RuleId 重复或场景错配，必须以唯一 `RuleId` 做双向集合比较。
- 单条规则局部测试不能代替全管线验证；全管线测试也不能代替局部精确 mark 断言，两层都保留。
- 真实 Terraria 103 文件 fixture 用于性能和覆盖量统计，不适合作为逐样本编译输入，因为它脱离原项目引用。最终产物编译测试必须使用自包含最小源。
- 新规则加入后，测试预期先失败是设计目标；补场景后才能恢复通过。
