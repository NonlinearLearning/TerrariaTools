# Rules 术语定义

本文档解释规则注释里出现的专有概念，并直接结合当前代码实现和现有测试用例说明这些概念在项目里到底是什么意思。

对应代码主要来自：

- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Core\Models.cs`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Analysis\Roslyn\RoslynAnalysisEngine.cs`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Analysis\Roslyn\StatementAnalysisService.cs`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\MarkingRuleEngine.cs`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\StatementPropagationEngine.cs`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\src\Rules\BoundaryPromotionEngine.cs`

## 1. statement target

`statement target` 不是泛指“一条语句文本”，而是当前分析模型里的正式目标对象。

代码定义：

- `AnalysisTarget` 定义在 `Models.cs`
- 当 `AnalysisTarget.Target.TargetKind == TargetKind.Statement` 时，这个目标就是一个 `statement target`
- 这个目标的身份由 `PlanTarget` 记录，至少包含以下信息：
  - `DocumentPath`
  - `MemberId`
  - `TargetKind`
  - `SpanStart`
  - `SpanLength`
  - `DisplayText`

创建位置：

- `RoslynAnalysisEngine.CreateStatementTarget(...)`
- `RoslynAnalysisEngine.CreateInitializerTarget(...)`

一个 statement target 在规则层真正可用的信息，不只是文本，还包括：

- `DefinesSymbols`
- `UsesSymbols`
- `InvokedMemberIds`
- `StatementKind`
- `IsSanitizingAssignment`
- `IsObjectInitializerAssignment`
- `HasMarkedExpressionSeed`
- `MarkedExpressionKinds`
- `ScopeMode`
- `ScopeId`
- `ParentScopeId`

测试中的实际例子：

```csharp
// dome:delete
int count = 1;
int next = count;
```

在 `DirectiveSeedRule_MarksStatementWithDeleteDirective` 里，真正被直接标记为删除的是整条：

```csharp
int count = 1;
```

而不是单独的字面量 `1`。
同一个测试里，后面的：

```csharp
int next = count;
```

也是另一个独立的 statement target，只不过它不是 direct decision，而是在传播阶段命中。

## 2. statement kind

`statement kind` 是 statement target 的稳定类别标签，使用 `StatementKindRef` 表示。

代码定义：

- `Models.cs` 中的 `StatementKindRef`

分类入口：

- `RoslynAnalysisEngine.ClassifyStatementKind(...)`

当前稳定值包括：

- `Initializer`
- `Declaration`
- `Assignment`
- `If`
- `While`
- `For`
- `Return`
- `ObjectInitializerAssignment`
- `Unknown`

这个标签的作用不是完整替代 Roslyn 语法树，而是给规则层一个可测试、可比较的语句类别。

## 3. direct decision

`direct decision` 指规则直接产出的 `MarkDecision`，不是传播阶段推导出来的结果。

当前 direct decision 的主要来源：

- `DirectiveSeedRule`
- `ExpressionProjectionRule`
- 其他直接命中的 method/class 规则

在 `StatementPropagationEngine.Propagate(...)` 中，`seedDecisionsByTarget` 里的 decision 会被当成 direct decisions。

当前已经固定的边界：

- `expression-mark` 属于 direct decision
- `dataflow-propagation` 不属于 direct decision

这个区分会影响：

- 是否继续参与传播
- 是否允许进入 `boundary promotion`
- 报告里如何解释命中来源

## 4. 投影结果

`投影结果` 指表达式级命中被投影到其所属 statement 后形成的 direct decision。

当前直接对应：

- `ExpressionProjectionRule`
- `Reason.RuleId == "expression-mark"`

它不是 propagation 结果，而是 direct decision 的一种来源。

测试中的实际例子：

```csharp
// dome:delete
bool allowed = Run(value) && (value > 0);
return allowed;
```

在 `ExpressionProjectionRule_ProjectsDeleteToContainingStatement` 里，系统不会单独删除 `Run(value)` 这个 invocation expression，而是生成针对整条 statement 的 decision：

```csharp
bool allowed = Run(value) && (value > 0);
```

测试里锁定的就是：

- `decision.Target.DisplayText == "bool allowed = Run(value) && (value > 0);"`
- `decision.Reason.RuleId == "expression-mark"`

同一组测试里的反例：

```csharp
// dome:delete
bool allowed = Run(value) && (value > 0);
bool fallback = Check(value) && (value < 10);
```

在 `ExpressionProjectionRule_DoesNotProjectAcrossDifferentStatement` 里，只会投影第一条，不会把第二条一起投影进去。这说明“投影结果”有非常明确的 statement 归属边界。

## 5. statement snapshot

`statement snapshot` 是传播阶段真正使用的局部 statement 图快照，不是整个项目的全量语句图。

代码定义：

- `StatementGraphSnapshot` 定义在 `Models.cs`

字段包括：

- `SeedTargetKey`
- `ScopeMode`
- `BoundaryMemberId`
- `Nodes`
- `Edges`

生成入口：

- `IStatementAnalysisService.Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode)`
- 默认实现是 `StatementAnalysisService.Analyze(...)`

它的语义是：

- 以某个 statement seed 为中心
- 在给定 scope 内
- 抽出当前传播真正可见的节点和边

测试中的实际例子：

```csharp
public void Update(int seed)
{
    int parent = seed;
    {
        // dome:delete
        int child = parent;
        int next = child;
    }
}
```

在 `ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired` 里，传播不是直接扫描整个方法，而是先构建一个 statement snapshot。
如果 scope 是 `ParentBlockPiercing`，snapshot 会把父块里的相关节点一起纳入；如果 scope 是 `MinimalBlock`，可见范围就更小。

## 6. propagation

`propagation` 指已有 decision 沿 use/def 关系扩散到其他 statement target。

实现位置：

- `StatementPropagationEngine.Propagate(...)`

传播结果的显式标识：

- `Reason.RuleId == "dataflow-propagation"`

传播依赖的核心事实：

- `UsesSymbols`
- `DefinesSymbols`
- `StatementGraphSnapshot.Nodes`
- `StatementGraphSnapshot.Edges`
- `IPropagationRule.CanPropagate(...)`

测试中的实际例子：

```csharp
// dome:delete
int count = 1;
int next = count;
```

在 `DirectiveSeedRule_MarksStatementWithDeleteDirective` 里：

- `int count = 1;` 是 direct decision
- `int next = count;` 读取了被污染的 `count`
- 所以第二条语句得到：
  - `Reason.RuleId == "dataflow-propagation"`
  - 非空的 `PropagationChain`

也就是说，propagation 在当前项目里就是“已有删除意图沿 use/def 依赖继续扩散”。

## 7. sanitization

`sanitization` 在当前代码里不是抽象概念，而是已经被提取成布尔事实的 statement 属性。

代码事实字段：

- `AnalysisTarget.IsSanitizingAssignment`

提取位置：

- `RoslynAnalysisEngine.IsSanitizingNode(...)`

当前判定方式：

- 语句是赋值或初始化
- 右值存在
- 右值不再依赖当前跟踪的符号

规则层语义：

- `SanitizationPropagationRule.CanPropagate(...)` 对 sanitizing target 返回 `false`
- 这意味着 taint 不允许继续通过该 target 形成新的传播结果

测试中的实际例子：

```csharp
// dome:delete
int count = 1;
int next = count;
next = 0;
int final = next;
```

在 `SanitizationPropagationRule_StopsPropagationAfterSanitizingAssignment` 里：

- `int next = count;` 会命中传播
- `next = 0;` 会被识别成 `IsSanitizingAssignment == true`
- 所以后面的：

```csharp
int final = next;
```

不会再命中

因此，这里的 `sanitization` 可以精确写成：

> 一个定义语句把值改写成不再依赖当前被跟踪符号的干净值，因此已有传播在这里被截断。

## 8. clean redefinition

`clean redefinition` 目前不是一个单独的类型，而是传播实现中的行为结果。

它在 `StatementPropagationEngine.Propagate(...)` 中体现为：

- 某条 statement 定义了符号
- 该 statement 没有发出 direct decision
- 该 statement 也没有发出 propagated decision
- 那么该符号会从 `taintedSymbols` 中被移除

测试中的实际例子：

```csharp
// dome:delete
int count = 1;
count = 2;
int next = count;
```

在 `Propagation_StopsAfterCleanRedefinition` 里：

- 第一条语句让 `count` 进入 taint
- 第二条语句：

```csharp
count = 2;
```

重新定义了 `count`
- 但这条语句自己没有变成 delete decision

于是旧 taint 在这里被清空，后面的：

```csharp
int next = count;
```

不会再命中传播

所以当前代码里 `clean redefinition` 的精确定义是：

> 重新定义了被污染符号，但该定义语句本身没有产生 decision，因此旧 taint 被清除。

## 9. protection rule

`protection rule` 是 Rules 层的一类正式规则，不是泛泛的“保护性判断”。

接口定义：

- `IProtectionRule`

当前默认实现：

- `HighRiskProtectionRule`
- `ObjectInitializerProtectionRule`

执行位置：

- `MarkingRuleEngine` 会在 direct decision 阶段跳过被保护 target
- `StatementPropagationEngine.IsProtected(...)` 会在传播阶段检测 protection

当前已经收口的正式语义：

> protection rule 不只是“当前节点不命中”，而是“当前节点既不命中，也作为传播边界”。

对应到传播代码就是：

- 命中 protection 后，`taintedSymbols.Clear()`
- 然后直接 `continue`

测试中的实际例子有两类。

第一类是高风险目标：

```csharp
public interface IPlayer
{
    void Update();
}

public class Player : IPlayer
{
    public void Update()
    {
        // dome:delete
        Run();
    }

    private void Run() { }
}
```

在 `HighRiskProtectionRule_BlocksPropagationIntoProtectedTarget` 里，因为 `Update()` 是接口实现成员，所以当前 target 会被视为受保护目标，最终 `decisions` 为空。

第二类是对象初始化器边界：

```csharp
// dome:delete
int count = seed;
var item = new Item { Value = count };
int next = count;
```

在 `Propagate_StopsAtProtectedObjectInitializerBoundary` 里：

- `var item = new Item { Value = count };` 会触发 `ObjectInitializerProtectionRule`
- 当前实现会在这里清空 taint
- 所以后面的：

```csharp
int next = count;
```

不会再继续命中传播

这说明当前项目里的 protection rule 已经是明确的传播边界。

## 10. high-risk target

`high-risk target` 对应 `AnalysisTarget.IsHighRisk == true`。

它的来源主要有两类：

- 成员级高风险，例如抽象、公开、泛型等不适合直接删除的成员
- `object initializer assignment` 这种被主动提升风险等级的 statement

在 `CreateStatementTarget(...)` 中，`IsHighRisk` 会因为：

- `IsHighRiskMember(memberSymbol)`
- 或 `isObjectInitializerAssignment`

而被置为 `true`。

当前默认规则里，`HighRiskProtectionRule` 会直接把 high-risk target 当作 protection target。

## 11. object initializer assignment

`object initializer assignment` 不是“所有初始化语句”，而是特指对象创建时带初始化器的本地声明。

判定位置：

- `RoslynAnalysisEngine.IsObjectInitializerAssignment(...)`

当前实现只把下面这一类识别为 object initializer assignment：

- `LocalDeclarationStatementSyntax`
- 其初始化值是 `ObjectCreationExpressionSyntax`
- 且这个对象创建表达式带 `Initializer`

测试中的实际例子：

```csharp
// dome:delete
var item = new Item { Value = seed };
```

在 `ObjectInitializerProtectionRule_DoesNotMarkInitializerAssignment` 里，这条语句即使带显式 directive，也不会产生 decision。
这说明当前项目里 object initializer assignment 不是普通 statement target，而是被专门保护的高风险目标。

## 12. scope mode

`scope mode` 指 statement snapshot 的构建范围模式。

定义位置：

- `StatementScopeMode`

当前稳定值：

- `MinimalBlock`
- `ParentBlockPiercing`

选择入口：

- `StatementPropagationEngine.ResolveScopeMode(...)`

优先级已经固定为：

1. `RuleExecutionContext.StatementScopeMode` 显式指定且不是 `MinimalBlock`
2. 否则尝试 `IStatementScopeRule.SelectScopeMode(...)`
3. 最后回落到 `MinimalBlock`

测试中的实际例子：

在 `ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired` 里，测试没有只看结果，还专门注入了 `RecordingStatementAnalysisService`，断言：

- `Analyze(...)` 被调用时的 `ScopeMode == StatementScopeMode.ParentBlockPiercing`

所以 `scope mode` 在当前代码里不是说明性文字，而是真正传给 `StatementAnalysisService.Analyze(...)` 的执行参数。

## 13. scope boundary

`scope boundary` 在当前实现里不是一个单独对象，而是 statement snapshot 的可见边界。

边界由两部分共同决定：

- `StatementScopeMode`
- `StatementAnalysisService` 实际纳入 snapshot 的节点集合

对应实现：

- `CollectMinimalBlock(...)`
- `CollectParentPiercing(...)`

因此，当前项目里的 `scope boundary` 可以定义为：

> 某个 statement 不在当前 `StatementGraphSnapshot.Nodes` 中时，它就位于本次传播的 scope boundary 之外，当前传播不能看到它，也不能通过它继续传播。

测试中的实际例子：

```csharp
public void Update(int seed)
{
    int parent = seed;
    {
        // dome:delete
        int child = parent;
    }
}
```

配套的两个测试把 boundary 说得很清楚：

- 在 `Execute_DoesNotPropagateAcrossParentBlockByDefault` 里，默认 `MinimalBlock` 下，传播不会自动把父块里的相关节点一起纳入
- 在 `ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired` 里，显式指定 `ParentBlockPiercing` 后，statement snapshot 才会扩大到父块

所以 `scope boundary` 不是抽象的“距离远近”，而是当前 snapshot 真实纳入了哪些 statement target。

## 14. boundary promotion

`boundary promotion` 指 statement-level delete 被提升为 method-level delete。

实现位置：

- `BoundaryPromotionEngine`
- `InvocationBoundaryPromotionRule`

当前已经固定的语义：

- 只消费 direct statement delete
- 不消费 `dataflow-propagation` 产生的 propagated delete

测试中的实际例子也有一正一反。

正向样例：

```csharp
// dome:delete
fun2(value);
```

在 `InvocationBoundaryPromotionRule_PromotesSingleStatementDeleteToMethodDelete` 里，这条 direct statement delete 会被提升为：

- `TargetKind.Method`
- `Sample.Player.fun2(int)`

反向样例：

```csharp
int count = value;
fun2(count);
```

在 `InvocationBoundaryPromotionRule_DoesNotPromotePropagatedDelete` 里，测试手工构造了一个：

- `Reason.RuleId == "dataflow-propagation"`

的 statement delete，再交给 `BoundaryPromotionEngine`。结果必须为空。
这就把 boundary promotion 的正式边界锁定为：

> propagated delete 不能参与 promotion。

## 15. function-mark / class-mark

这两个词在当前规则体系里都不是泛指“标记函数/类”，而是具体规则发出的删除候选。

对应规则：

- `FunctionMarkingRule`
- `ClassMarkingRule`

当前作用：

- `FunctionMarkingRule` 决定某个方法 target 是否进入删除候选
- `ClassMarkingRule` 决定某个类 target 是否进入删除候选

它们依赖的不是 statement 数据流，而是更偏全局的结构事实，例如：

- `FunctionIndex`
- `ReferenceQueryService`
- `InheritanceQueryService`
- 类型图

因此，当文档写“某个结构不进入 function-mark 删除候选”时，真正意思是：

> 它不会被 `FunctionMarkingRule` 判定为可删除的方法目标。

## 16. 写规则注释时推荐的表达方式

为了让规则注释和当前实现保持一致，建议在源码注释里尽量使用下面这些表达：

- 写 `statement target` 时，默认指 `TargetKind.Statement` 的 `AnalysisTarget`
- 写 `sanitization` 时，默认指 `IsSanitizingAssignment == true`
- 写 `clean redefinition` 时，默认指“定义了符号但未发出 decision，导致 taint 被清空”
- 写 `protection rule` 时，默认指 `IProtectionRule`，且当前语义包含“切断传播”
- 写 `scope boundary` 时，默认指当前 `StatementGraphSnapshot` 之外的不可见边界
- 写 `direct decision` 时，默认排除 `dataflow-propagation`
- 写 `boundary promotion` 时，默认指 statement delete 到 method delete 的提升

这样规则注释、测试名和实现语义才能保持一致，不会把抽象术语和当前代码行为混在一起。
