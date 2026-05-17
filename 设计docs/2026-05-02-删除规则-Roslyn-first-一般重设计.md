# 删除规则 Roslyn-first 一般重设计

## 1. 这份文档负责什么

这份文档只回答一件事：

```text
如果删除规则系统的目标是先支持基础功能，
并尽量少造内部模型，应该怎样改成更贴近 Roslyn 的一般设计。
```

这份文档负责：

1. 给出第一期推荐保留的最小模型。
2. 给出建议删除或降级的过度设计模型。
3. 说明 Roslyn 现成对象分别承担什么职责。
4. 给出基础功能可落地的执行主线。

这份文档不负责：

1. 替代现有专项规则文档。
2. 定义完整外部规则包协议。
3. 定义完整产品化工作流。
4. 定义完整 CPG schema。

## 2. 总体结论

当前推荐方向是：

```text
规则层少造模型，优先直接依赖 Roslyn 事实；
图层继续保留 canonical CPG；
删除规则只在 Roslyn 与 CPG 之间补最少的结果对象。
```

再压缩成一句话：

```text
Roslyn 提供目标、定位、绑定与重写能力；
删除规则层只保留匹配、裁决、编辑这三类最小结果。
```

这意味着：

1. 不再把删除目标单独包装成厚重领域对象。
2. 不再把标记、传播、决策、计划都做成正式对象家族。
3. 不再先造三阶段行为接口，再让规则去适配框架。
4. 先让基础规则直接跑通，再看是否真的需要抽象升级。

## 3. 设计原则

### 3.1 Roslyn facts first

凡是 Roslyn 已经稳定提供的事实，优先直接使用：

1. 结构根对象：`SyntaxNode`
2. 节点种类：`SyntaxKind`
3. 文本区间：`TextSpan`
4. 重写跟踪：`SyntaxAnnotation`
5. 语义绑定：`SemanticModel`
6. 声明与引用身份：`ISymbol`
7. 归一化语义动作：`IOperation`

### 3.2 结果对象少而硬

规则层只保留真正跨步骤需要传递的结果对象。

前期建议只保留：

1. `RuleMetadata`
2. `RuleMatch`
3. `RuleDecision`
4. `RewriteEdit`

### 3.3 流程可以有，模型不要先膨胀

可以继续保留：

```text
命中 -> 扩展 -> 裁决 -> 重写
```

但不要求每个步骤都先有自己的正式对象族和接口族。

### 3.4 先支持基础功能，再抽象复用

第一期只要能稳定支持：

1. 删除 `s` 相关表达式
2. 必要时升级到语句删除
3. 删除不可达函数定义
4. 基于 Roslyn 回写源码

在这之前，不要为“未来可能存在的大型规则市场”提前建模。

## 4. 现有模型里建议删除或降级的部分

## 4.1 `DeletionTarget`

建议：从正式核心对象降级为轻量句柄，或直接取消。

原因：

1. `SyntaxKind`、`Span`、`TrackingAnnotation` 本来就是 Roslyn 事实。
2. `Code` 可由 `SyntaxNode.ToString()` 现取，不必常驻存储。
3. `Kind=Expression/Statement/Definition` 可由 Roslyn 节点家族直接判断。
4. `TargetId` 前期价值不高，维护成本高。

### 4.1.1 不再推荐的形状

```csharp
public sealed record DeletionTarget(
  string TargetId,
  DeletionTargetKind Kind,
  string FilePath,
  SyntaxKind SyntaxKind,
  TextSpan Span,
  string Code,
  SyntaxAnnotation TrackingAnnotation,
  IReadOnlyCollection<TargetRelation> Relations);
```

### 4.1.2 推荐替代

最小版：

```csharp
public sealed record SyntaxTarget(
  SyntaxNode Node,
  SyntaxAnnotation TrackingAnnotation);
```

如果必须脱离运行期节点：

```csharp
public sealed record SyntaxTargetHandle(
  string FilePath,
  TextSpan Span,
  SyntaxAnnotation TrackingAnnotation);
```

## 4.2 `TargetRelation`

建议：删除。

原因：

1. 第一阶段的父子、升级、派生关系大多可由 `Parent` 和 `Ancestors()` 现推。
2. “从表达式升级成语句”更像过程结果，不像需要常驻维护的关系模型。
3. 单独维护 `RelationId / SourceTargetId / DerivedTargetId` 会把简单升级问题重新做成关系图。

推荐替代：

直接在 `RuleDecision` 中保留：

1. `OriginalNode`
2. `FinalNode`
3. `Reason`

## 4.3 `Mark / PropagationTrace / DecisionCandidate / DecisionOutcome / DeletionPlan / RewriteAction`

建议：整体收缩成三类对象。

原因：

1. 这组对象把流程拆得过细。
2. 多数对象只是阶段中转容器。
3. `TextSpan` 已能稳定表达编辑范围，不必再转成行列型动作对象。
4. 第一阶段的核心问题是删哪里、升不升级、怎么改树，不是做完整审计平台。

推荐收缩后只保留：

```csharp
public sealed record RuleMatch(
  string RuleId,
  SyntaxNode Node,
  string Reason,
  int Depth = 0);

public sealed record RuleDecision(
  SyntaxNode OriginalNode,
  SyntaxNode FinalNode,
  DecisionActionKind Action,
  string Reason,
  string? WinningRuleId);

public sealed record RewriteEdit(
  SyntaxTree Tree,
  TextSpan Span,
  string ReplacementText);
```

## 4.4 `RuleMarkingSpec / RulePropagationSpec / RuleDecisionSpec`

建议：第一期不保留三套独立 `Spec`。

原因：

1. 它们把本来可直接写在规则里的静态约束拆成三层对象。
2. `RequiredGraphCapabilities` 现在还是字符串集合，抽象收益不高。
3. 规则数量少时，这种拆法会放大样板代码。
4. 真实执行逻辑最终仍会回到规则实现本身。

推荐替代：

```csharp
public sealed record RuleMetadata(
  string RuleId,
  string Name,
  bool EnabledByDefault);

[Flags]
public enum GraphCapability
{
  None = 0,
  Ast = 1,
  SourceLocation = 2,
  CallGraph = 4,
  ReachingDef = 8,
  SemanticBinding = 16
}
```

## 4.5 `IMarkBehavior / IPropagationBehavior / IDecisionBehavior`

建议：第一期不拆。

原因：

1. 它们偏框架设计。
2. 基础规则少时，单接口更直接。
3. 规则先做成可运行逻辑，比先接到三阶段引擎上更重要。

推荐替代：

```csharp
public interface IDeletionRule
{
  RuleMetadata Metadata { get; }
  GraphCapability RequiredCapabilities { get; }
  IEnumerable<RuleMatch> Evaluate(RuleContext context, SyntaxNode root);
}
```

如果后续真的出现阶段复用需求，再从这个单接口里拆出子能力。

## 4.6 `MarkMatchResult / PropagationCheckResult / DecisionCheckResult`

建议：删除这组三阶段返回值对象。

原因：

1. 三者字段高度重复。
2. 大量信息只是 `Reason + DerivedTarget + bool` 的不同变体。
3. 第一期完全可以直接返回 `RuleMatch`、`RuleDecision` 或空集合。

## 5. 建议保留的最小模型

## 5.1 `RuleMetadata`

```csharp
public sealed record RuleMetadata(
  string RuleId,
  string Name,
  bool EnabledByDefault);
```

作用：

1. 规则注册键。
2. 日志与调试锚点。
3. 宿主级启停控制。

前期不强制带：

1. `Version`
2. `Category`
3. `Description`

这些都可以后移到文档或宿主元数据字典。

## 5.2 `RuleContext`

```csharp
public sealed class RuleContext
{
  public NewJoernGraph Graph { get; }
  public SemanticModel SemanticModel { get; }
  public SyntaxNode Root { get; }
  public IReadOnlyDictionary<string, string> Options { get; }

  public RuleContext(
    NewJoernGraph graph,
    SemanticModel semanticModel,
    SyntaxNode root,
    IReadOnlyDictionary<string, string> options)
  {
    Graph = graph;
    SemanticModel = semanticModel;
    Root = root;
    Options = options;
  }
}
```

作用：

1. 统一传入图事实。
2. 统一传入 Roslyn 语义能力。
3. 统一传入当前源码根节点。

## 5.3 `RuleMatch`

```csharp
public sealed record RuleMatch(
  string RuleId,
  SyntaxNode Node,
  string Reason,
  int Depth = 0);
```

作用：

1. 表达一条规则命中了哪个语法节点。
2. 支撑简单传播深度。
3. 保留最小解释文本。

## 5.4 `RuleDecision`

```csharp
public sealed record RuleDecision(
  SyntaxNode OriginalNode,
  SyntaxNode FinalNode,
  DecisionActionKind Action,
  string Reason,
  string? WinningRuleId);
```

作用：

1. 表达删还是跳过。
2. 表达是否从原始命中升级到更大父节点。
3. 作为重写阶段直接输入。

## 5.5 `RewriteEdit`

```csharp
public sealed record RewriteEdit(
  SyntaxTree Tree,
  TextSpan Span,
  string ReplacementText);
```

作用：

1. 把最终裁决落到稳定文本区间。
2. 作为编辑器层或 patch 层输入。

如果后续完全改走 `SyntaxEditor` / `DocumentEditor`，连这层也可以继续变薄。

## 6. Roslyn 对齐后的职责分配

## 6.1 Syntax 层负责什么

Syntax 层直接负责：

1. 目标节点是谁。
2. 目标的父结构是什么。
3. 目标属于表达式、语句还是成员声明。
4. 目标在源码中的位置。

直接依赖：

1. `SyntaxNode`
2. `ExpressionSyntax`
3. `StatementSyntax`
4. `MemberDeclarationSyntax`
5. `SyntaxKind`
6. `TextSpan`

## 6.2 Semantic 层负责什么

Semantic 层直接负责：

1. 标识符到底绑定到谁。
2. 表达式类型是什么。
3. 调用绑定到哪个方法。
4. 可用的 `AnalyzeDataFlow` / `AnalyzeControlFlow` 入口。

直接依赖：

1. `SemanticModel`
2. `ISymbol`
3. `GetSymbolInfo`
4. `GetTypeInfo`
5. `AnalyzeDataFlow`
6. `AnalyzeControlFlow`

## 6.3 Operation 层负责什么

Operation 层直接负责：

1. 统一不同语法写法下的语义动作。
2. 识别 invocation、assignment、field/property/index access。
3. 给基础删除规则提供更稳定的动作视图。

直接依赖：

1. `IOperation`
2. `IInvocationOperation`
3. `IAssignmentOperation`
4. `IFieldReferenceOperation`
5. `IPropertyReferenceOperation`

## 6.4 Rewrite 层负责什么

Rewrite 层直接负责：

1. 用 `SyntaxAnnotation` 稳定跟踪节点。
2. 用 `ReplaceNode`、`RemoveNode` 或 `SyntaxEditor` 改树。
3. 在必要时用 `SyntaxFactory` 造替代节点。

优先工具：

1. `SyntaxNodeExtensions.ReplaceNode`
2. `SyntaxEditor`
3. `DocumentEditor`
4. `SyntaxFactory`

## 7. 基础功能的推荐执行主线

## 7.1 删除 `s` 相关代码

推荐主线：

```text
图上命中候选
  -> 回到 SyntaxNode 根
  -> 用 SemanticModel / IOperation 确认它确实以 s 为根
  -> 产出 RuleMatch
  -> 判断局部替换是否安全
  -> 不安全时升级到父 StatementSyntax
  -> 产出 RuleDecision
  -> 用 SyntaxEditor / ReplaceNode 回写
```

这里不需要：

1. `DeletionTarget`
2. `TargetRelation`
3. `PropagationTrace`
4. `DeletionPlan`

## 7.2 删除不可达函数

推荐主线：

```text
CallGraph 求 METHOD 可达性
  -> 找到不可达方法节点
  -> 映射回 MethodDeclarationSyntax
  -> 产出 RuleMatch
  -> 直接产出 RuleDecision(MethodDeclarationSyntax)
  -> 删除完整方法定义
```

这里的删除单位已经天然是 `MethodDeclarationSyntax`，没必要再包一层 `Definition target`。

## 8. 推荐实现顺序

### 第 1 步：先把规则结果对象收缩

只保留：

1. `RuleMetadata`
2. `RuleContext`
3. `RuleMatch`
4. `RuleDecision`
5. `RewriteEdit`

### 第 2 步：让规则直接吃 Roslyn 句柄

所有规则内部优先直接处理：

1. `SyntaxNode`
2. `SemanticModel`
3. `IOperation`

### 第 3 步：先跑通两个基础场景

优先做：

1. `s` 相关表达式删除
2. 不可达函数定义删除

### 第 4 步：最后才判断是否需要阶段拆分

只有在下面情况同时出现时，才考虑重新引入分阶段接口：

1. 规则数量明显增多。
2. 多条规则共享复杂传播逻辑。
3. 决策阶段开始出现稳定的跨规则复用。

## 9. 与现有文档的关系

这份文档是“一般重设计建议”，不是对旧文档的逐段替换。

它给出的核心裁剪结论是：

1. 保留 CPG 主图。
2. 删除规则层尽量直接依赖 Roslyn。
3. 只保留最小结果对象。
4. 不把基础功能提前框架化。

## 10. 参考依据

本地依据：

1. `设计docs/2026-04-29-内部模型总览.md`
2. `设计docs/2026-04-29-规则对象模型-CSharp-设计稿.md`
3. `设计docs/2026-04-30-从joern收缩到简化CPG实现指南.md`

官方资料：

1. Roslyn Syntax API：
   - <https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis>
2. `SemanticModel`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.semanticmodel>
   - <https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-semantics>
   - <https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis>
3. `IOperation`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.ioperation?view=roslyn-dotnet-5.0.0>
4. `SyntaxAnnotation`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxannotation?view=roslyn-dotnet-5.0.0>
5. `TextSpan`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.text.textspan?view=roslyn-dotnet-5.0.0>
6. `SyntaxEditor`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.editing.syntaxeditor?view=roslyn-dotnet-5.0.0>
7. `DocumentEditor`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.editing.documenteditor?view=roslyn-dotnet-5.0.0>
8. `SyntaxFactory`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntaxfactory?view=roslyn-dotnet-5.0.0>
9. `ReplaceNode`：
   - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxnodeextensions.replacenode?view=roslyn-dotnet-5.0.0>

## 11. 最终建议

如果目标是“先做出基础可用的删除规则”，当前最合理的一般路线是：

```text
图分析继续走 CPG，
目标识别、语义确认、源码回写尽量直接靠 Roslyn，
规则层只保留最小命中与裁决结果。
```

一句话收口：

```text
少造删除规则内部模型，
把复杂度压回 Roslyn 现成对象和少量结果对象，
让第一期先把基础功能跑通。
```
