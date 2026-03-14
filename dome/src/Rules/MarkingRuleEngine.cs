namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

/// 种子规则接口。
/// </summary>
public interface ISeedRule
{
    /// <summary>
    /// 评估目标并生成标记决策。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisTarget target);
}

/// 传播规则接口。
/// </summary>
public interface IPropagationRule
{
    /// <summary>
    /// 检查是否可以传播。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <param name="usedSymbol">使用的符号。</param>
    /// <param name="sourceDecision">源决策。</param>
    /// <returns>如果可以传播则返回 true，否则返回 false。</returns>
    bool CanPropagate(AnalysisTarget target, SymbolRef usedSymbol, MarkDecision sourceDecision);
}

/// <summary>
/// 保护规则接口。
/// </summary>
public interface IProtectionRule
{
    /// <summary>
    /// 检查是否阻止标记。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>如果阻止则返回 true，否则返回 false。</returns>
    bool Blocks(AnalysisTarget target);
}

/// <summary>
/// 表达式投影规则接口。
/// </summary>
public interface IExpressionProjectionRule
{
    /// <summary>
    /// 评估目标并生成标记决策。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisTarget target);
}

/// <summary>
/// 方法规则接口。
/// </summary>
public interface IMethodRule
{
    /// <summary>
    /// 评估函数节点并生成标记决策。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="functionNode">函数节点引用。</param>
    /// <returns>标记决策集合。</returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode);
}

/// <summary>
/// 类规则接口。
/// </summary>
public interface IClassRule
{
    /// <summary>
    /// 评估分析目标并生成标记决策。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target);
}

/// <summary>
/// 边界提升规则接口。
/// </summary>
public interface IBoundaryPromotionRule
{
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target, MarkDecision decision);
}

/// <summary>
/// 语句作用域选择规则接口。
/// </summary>
public interface IStatementScopeRule
{
    StatementScopeMode SelectScopeMode(AnalysisContext context, AnalysisTarget seedTarget);
}

/// <summary>
/// 指令种子规则。
/// 规则族：ISeedRule。
/// 命中目标：携带 dome 指令的 statement target；控制流语句会统一为 controlflow-mark。
/// 直接产出：statement 级 MarkDecision，action 取自 directive，reason 为 directive rule id。
/// 传播：是，直接种子决策可沿 statement snapshot 的 use/def 边继续传播。
/// 阻断：sanitization、clean redefinition、protection rule、scope boundary。
/// 提升：是，direct statement delete 仍可进入 boundary promotion。
/// 最小测试：
/// - DirectiveSeedRule_MarksStatementWithDeleteDirective
/// - DirectiveSeedRule_DoesNotMarkStatementWithoutDirective
/// </summary>
public sealed class DirectiveSeedRule : ISeedRule
{
    /// <summary>
    /// 评估目标并生成标记决策。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisTarget target)
    {
        if (target.HasMarkedExpressionSeed && !IsControlFlowTarget(target.StatementKind))
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            var ruleId = IsControlFlowTarget(target.StatementKind)
                ? "controlflow-mark"
                : directive.RuleId;
            var reasonText = ruleId == "controlflow-mark"
                ? "Directive matched a control-flow target."
                : directive.ReasonText;

            yield return MarkDecision.ForTarget(
                target.Target,
                directive.ActionKind,
                ruleId,
                reasonText,
                directive.Payload);
        }
    }

    private static bool IsControlFlowTarget(StatementKindRef statementKind) =>
        statementKind is StatementKindRef.If or StatementKindRef.While or StatementKindRef.For or StatementKindRef.Return;
}

/// <summary>
/// 表达式投影规则。
/// </summary>
/// <summary>
/// 表达式投影规则。
/// 规则族：IExpressionProjectionRule。
/// 命中目标：带 directive 且 HasMarkedExpressionSeed 为 true 的非控制流 statement target。
/// 直接产出：statement 级 MarkDecision，reason 固定为 expression-mark。
/// 传播：是，投影结果按 direct decision 处理。
/// 阻断：高风险 target、对象初始化赋值、错误的 statement 归属、scope boundary。
/// 提升：是，投影产生的 statement delete 仍属于可提升的 direct delete。
/// 最小测试：
/// - ExpressionProjectionRule_ProjectsDeleteToContainingStatement
/// - ExpressionProjectionRule_DoesNotProjectAcrossDifferentStatement
/// </summary>
public sealed class ExpressionProjectionRule : IExpressionProjectionRule
{
    /// <summary>
    /// 评估目标并生成标记决策。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisTarget target)
    {
        if (!target.HasMarkedExpressionSeed ||
            target.Directives.Count == 0 ||
            target.Target.TargetKind != TargetKind.Statement ||
            target.StatementKind is StatementKindRef.If or StatementKindRef.While or StatementKindRef.For or StatementKindRef.Return ||
            target.IsHighRisk ||
            target.IsObjectInitializerAssignment)
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            yield return MarkDecision.ForTarget(
                target.Target,
                directive.ActionKind,
                "expression-mark",
                "Directive matched an expression-bearing statement and was projected to the statement target.",
                directive.Payload,
                relatedSymbolNames: target.MarkedExpressionKinds);
        }
    }
}

/// <summary>
/// 清理传播规则。
/// </summary>
/// <summary>
/// 清洗传播规则。
/// 规则族：IPropagationRule。
/// 命中目标：传播遍历过程中被识别为 sanitizing assignment 的节点。
/// 直接产出：无；该规则只负责终止传播。
/// 传播：不适用；它不是传播源规则。
/// 阻断：任意 sanitizing assignment 都会终止经过该节点的传播。
/// 提升：否。
/// 最小测试：
/// - SanitizationPropagationRule_StopsPropagationAfterSanitizingAssignment
/// </summary>
public sealed class SanitizationPropagationRule : IPropagationRule
{
    /// <summary>
    /// 检查是否可以传播。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <param name="usedSymbol">使用的符号。</param>
    /// <param name="sourceDecision">源决策。</param>
    /// <returns>如果可以传播则返回 true，否则返回 false。</returns>
    public bool CanPropagate(AnalysisTarget target, SymbolRef usedSymbol, MarkDecision sourceDecision)
    {
        return !target.IsSanitizingAssignment;
    }
}

/// <summary>
/// 高风险保护规则。
/// </summary>
/// <summary>
/// 高风险保护规则。
/// 规则族：IProtectionRule。
/// 命中目标：被分析结果标记为高风险的 target。
/// 直接产出：无；该规则会同时阻止 direct mark 和 propagation。
/// 传播：否；被保护 target 同时是传播边界。
/// 阻断：一旦命中，当前 target 被跳过，taint 不得继续穿过该节点。
/// 提升：否。
/// 最小测试：
/// - HighRiskProtectionRule_BlocksPropagationIntoProtectedTarget
/// </summary>
public sealed class HighRiskProtectionRule : IProtectionRule
{
    /// <summary>
    /// 检查是否阻止标记。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>如果阻止则返回 true，否则返回 false。</returns>
    public bool Blocks(AnalysisTarget target) => target.IsHighRisk;
}

/// <summary>
/// 对象初始化器保护规则。
/// </summary>
/// <summary>
/// 对象初始化器保护规则。
/// 规则族：IProtectionRule。
/// 命中目标：被识别为 object initializer assignment 的 statement target。
/// 直接产出：无；该规则阻止对象初始化赋值被直接标记。
/// 传播：否；被保护的对象初始化语句同时是传播边界。
/// 阻断：一旦命中，当前 target 被跳过，taint 不得继续穿过该节点。
/// 提升：否。
/// 最小测试：
/// - ObjectInitializerProtectionRule_DoesNotMarkInitializerAssignment
/// </summary>
public sealed class ObjectInitializerProtectionRule : IProtectionRule
{
    /// <summary>
    /// 检查是否阻止标记。
    /// </summary>
    /// <param name="target">分析目标。</param>
    /// <returns>如果阻止则返回 true，否则返回 false。</returns>
    public bool Blocks(AnalysisTarget target) => target.IsObjectInitializerAssignment;
}

/// <summary>
/// 函数标记规则。
/// </summary>
/// <summary>
/// 函数标记规则。
/// 规则族：IMethodRule。
/// 命中目标：未被 override/interface 语义保护的私有方法。
/// 直接产出：
/// - 无引用的私有方法产出 method 级 Delete，reason 为 function-mark
/// - 已被引用但非 void 且空体的方法产出 method 级 AddReturn
/// 传播：不适用。
/// 依据：FunctionIndex、IInheritanceQueryService、IReferenceQueryService。
/// 阻断：override、interface implementation、已知框架入口保护、仍有引用。
/// 提升：否。
/// 最小测试：
/// - Execute_EmitsDeleteForUnreferencedPrivateMethod
/// - Execute_EmitsAddReturnForReferencedEmptyNonVoidMethod
/// - Execute_DoesNotDeleteOverrideMethod
/// - Execute_DoesNotDeleteEventSubscribedPrivateMethod
/// - Execute_DoesNotDeleteDelegateAssignedPrivateMethod
/// - Execute_DoesNotDeleteMethodCachedInDelegateDictionary
/// - Execute_DoesNotDeleteKnownFrameworkEntrypointMethod
/// - Execute_DoesNotDeleteKnownFrameworkShutdownMethod
/// - Execute_DoesNotDeleteKnownFrameworkDrawMethod
/// - Execute_DoesNotDeleteKnownFrameworkApplyPassMethod
/// - Execute_DeletesUnreferencedPrivateHelperInsideRegisteredType
/// - Execute_DeletesSameNamedMethodOutsideKnownFrameworkType
/// </summary>
public sealed class FunctionMarkingRule : IMethodRule
{
    private static readonly HashSet<string> KnownFrameworkEntrypointNames =
        new(StringComparer.Ordinal)
        {
            "Initialize",
            "Shutdown",
            "Connect",
            "Send",
            "Receive",
            "StartListening",
            "Apply",
            "ApplyPass",
            "TryDroppingItem",
            "CanDrop",
            "Find",
            "Perform",
            "Load",
            "OnBegin",
            "OnEnd",
            "OnActivate",
            "Draw",
            "Recalculate"
        };

    private static readonly string[] KnownFrameworkTypeMarkers =
    [
        "GenPass",
        "GenShape",
        "GenAction",
        "GenStructure",
        "NetSocialModule",
        "IItemDropRule",
        "ISocialModule"
    ];

    /// <summary>
    /// 评估函数节点并生成标记决策。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="functionNode">函数节点引用。</param>
    /// <returns>标记决策集合。</returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode)
    {
        if (functionNode.MemberKind != MemberKind.Method || !functionNode.IsPrivate)
        {
            yield break;
        }

        if (context.Inheritance.IsOverrideMember(functionNode.MemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(functionNode.MemberId.Value))
        {
            yield break;
        }

        if (IsKnownFrameworkEntrypoint(context, functionNode))
        {
            yield break;
        }

        var hasReferences = context.References.HasReferences(functionNode.MemberId.Value);
        if (!hasReferences)
        {
            yield return MarkDecision.ForTarget(
                ToMethodTarget(functionNode),
                PlanActionKind.Delete,
                "function-mark",
                "Method has no references and is not protected by inheritance or interface implementation.");
            yield break;
        }

        if (!functionNode.ReturnsVoid && functionNode.HasBody && !functionNode.HasStatements)
        {
            yield return MarkDecision.ForTarget(
                ToMethodTarget(functionNode),
                PlanActionKind.AddReturn,
                "function-mark",
                "Referenced non-void method has an empty body and requires a default return.",
                payload: DefaultValueFormatter.Format(functionNode.ReturnTypeDisplay));
        }
    }

    private static PlanTarget ToMethodTarget(FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            TargetKind.Method,
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName);

    private static bool IsKnownFrameworkEntrypoint(AnalysisContext context, FunctionNodeRef functionNode)
    {
        var methodName = TryGetMethodName(functionNode.MemberId.Value);
        if (string.IsNullOrEmpty(methodName) || !KnownFrameworkEntrypointNames.Contains(methodName))
        {
            return false;
        }

        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, functionNode.DeclaringTypeId, StringComparison.Ordinal) &&
            edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements &&
            KnownFrameworkTypeMarkers.Any(marker => edge.TargetTypeId.Contains(marker, StringComparison.Ordinal)));
    }

    private static string TryGetMethodName(string memberId)
    {
        var parameterStart = memberId.IndexOf('(');
        if (parameterStart <= 0)
        {
            return string.Empty;
        }

        var lastDot = memberId.LastIndexOf('.', parameterStart);
        if (lastDot < 0 || lastDot + 1 >= parameterStart)
        {
            return string.Empty;
        }

        return memberId.Substring(lastDot + 1, parameterStart - lastDot - 1);
    }
}

/// <summary>
/// 类标记规则。
/// </summary>
/// <summary>
/// 类标记规则。
/// 规则族：IClassRule。
/// 命中目标：未被继承、引用或高风险状态保护的 class target。
/// 直接产出：class 级 Delete，reason 为 class-mark。
/// 传播：不适用。
/// 依据：IInheritanceQueryService、IReferenceQueryService。
/// 阻断：高风险类型、位于继承链中、仍有引用、明确的注册/组合器持有。
/// 提升：否。
/// 最小测试：
/// - Execute_EmitsDeleteForUnreferencedNestedClass
/// - Execute_EmitsDeleteForUnreferencedTopLevelInternalClass
/// - Execute_DoesNotDeleteReferencedClass
/// - Execute_DoesNotDeletePublicTopLevelClass
/// - Execute_DoesNotDeleteClassRegisteredInStaticManager
/// - Execute_DoesNotDeleteClassRegisteredViaGenericRegister
/// - Execute_DoesNotDeleteClassRegisteredViaManagerIndexer
/// - Execute_DoesNotDeleteRuleNodeAddedToComposerChain
/// - Execute_DeletesTypeAddedOnlyToLocalTemporaryList
/// </summary>
public sealed class ClassMarkingRule : IClassRule
{
    /// <summary>
    /// 评估分析目标并生成标记决策。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="target">分析目标。</param>
    /// <returns>标记决策集合。</returns>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target)
    {
        var typeId = target.Target.MemberId.Value;
        if (target.Target.TargetKind != TargetKind.Class)
        {
            yield break;
        }

        if (target.IsHighRisk ||
            context.Inheritance.IsInInheritanceChain(typeId) ||
            context.References.HasReferences(typeId) ||
            IsKnownFrameworkType(context, typeId))
        {
            yield break;
        }

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.Delete,
            "class-mark",
            "Class has no references and is not protected by inheritance.");
    }

    private static bool IsKnownFrameworkType(AnalysisContext context, string typeId)
    {
        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, typeId, StringComparison.Ordinal) &&
            edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements &&
            (edge.TargetTypeId.Contains("GenPass", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenShape", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenAction", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenStructure", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("NetSocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("ISocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("IItemDropRule", StringComparison.Ordinal)));
    }
}

/// <summary>
/// 调用语句边界提升规则。
/// </summary>
/// <summary>
/// 调用边界提升规则。
/// 规则族：IBoundaryPromotionRule。
/// 命中目标：direct statement-level Delete，且该 statement 恰好调用一个私有方法。
/// 直接产出：method 级 Delete，reason 为 boundary-promotion。
/// 传播：否；该规则消费已有 statement decision，本身不产生传播。
/// 依据：InvokedMemberIds、FunctionIndex、IReferenceQueryService、IInheritanceQueryService。
/// 阻断：propagated delete、非私有或受保护的方法、仍有剩余引用、多调用 statement、已存在 method delete。
/// 提升：是，固定从 statement delete 提升到 method delete。
/// 最小测试：
/// - InvocationBoundaryPromotionRule_PromotesSingleStatementDeleteToMethodDelete
/// - InvocationBoundaryPromotionRule_DoesNotPromotePropagatedDelete
/// - BoundaryPromotionEngine_DoesNotDuplicateExistingMethodDelete
/// </summary>
public sealed class InvocationBoundaryPromotionRule : IBoundaryPromotionRule
{
    /// <summary>
    /// 评估是否进行边界提升。
    /// </summary>
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target, MarkDecision decision)
    {
        if (target.Target.TargetKind != TargetKind.Statement ||
            decision.Action.Kind != PlanActionKind.Delete ||
            string.Equals(decision.Reason.RuleId, "dataflow-propagation", StringComparison.Ordinal) ||
            target.InvokedMemberIds.Count != 1)
        {
            yield break;
        }

        var invokedMemberId = target.InvokedMemberIds[0];
        if (!context.FunctionIndex.NodesByMemberId.TryGetValue(invokedMemberId.Value, out var functionNode) ||
            functionNode.MemberKind != MemberKind.Method ||
            !functionNode.IsPrivate ||
            !functionNode.HasBody ||
            context.Inheritance.IsOverrideMember(invokedMemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(invokedMemberId.Value))
        {
            yield break;
        }

        var remainingReferences = context.References.GetReferencingFunctions(invokedMemberId.Value)
            .Select(memberId => memberId.Value)
            .ToHashSet(StringComparer.Ordinal);
        remainingReferences.Remove(target.Target.MemberId.Value);
        if (remainingReferences.Count > 0)
        {
            yield break;
        }

        yield return MarkDecision.ForTarget(
            new PlanTarget(
                functionNode.DocumentPath,
                functionNode.MemberId,
                functionNode.MemberKind,
                TargetKind.Method,
                functionNode.SpanStart,
                functionNode.SpanLength,
                functionNode.DisplayName),
            PlanActionKind.Delete,
            "boundary-promotion",
            "Invocation delete crossed the statement boundary and was promoted to a method delete candidate.",
            sourceTargetKey: target.Target.TargetKey,
            sourceTargetDisplayText: target.Target.DisplayText,
            sourceMemberId: target.Target.MemberId.Value,
            boundaryKind: BoundaryKind.Invocation,
            triggeredSymbolKeys: new[] { invokedMemberId.Value },
            relatedSymbolKeys: new[] { invokedMemberId.Value },
            relatedSymbolNames: new[] { functionNode.DisplayName });
    }
}

/// <summary>
/// 父块穿透作用域选择规则。
/// </summary>
/// <summary>
/// 父块穿透作用域规则。
/// 规则族：IStatementScopeRule。
/// 命中目标：读取了参数或父块 local，且满足父块穿透条件的 statement seed。
/// 直接产出：无；该规则只负责把 StatementScopeMode 从 MinimalBlock 切到 ParentBlockPiercing。
/// 传播：不适用；该规则只改变 statement snapshot 的可见范围。
/// 依据：StatementFactsIndex、UsesSymbols、ScopeId、ParentScopeId。
/// 阻断：高风险 target、对象初始化赋值、sanitizing assignment、缺失 scope 信息、跨 function boundary。
/// 提升：否。
/// 最小测试：
/// - ParentBlockPiercingScopeRule_ExpandsSnapshotWhenExplicitlyRequired
/// - Execute_DoesNotPropagateAcrossParentBlockByDefault
/// - Execute_UsesExplicitStatementScopeModeFromExecutionContext
/// </summary>
public sealed class ParentBlockPiercingScopeRule : IStatementScopeRule
{
    /// <summary>
    /// 选择语句作用域模式。
    /// </summary>
    public StatementScopeMode SelectScopeMode(AnalysisContext context, AnalysisTarget seedTarget)
    {
        if (seedTarget.Target.TargetKind != TargetKind.Statement ||
            seedTarget.IsHighRisk ||
            seedTarget.IsObjectInitializerAssignment ||
            seedTarget.IsSanitizingAssignment ||
            string.IsNullOrEmpty(seedTarget.ScopeId) ||
            string.IsNullOrEmpty(seedTarget.ParentScopeId) ||
            !context.StatementFacts.FactsByMemberId.TryGetValue(seedTarget.Target.MemberId.Value, out var bucket))
        {
            return StatementScopeMode.MinimalBlock;
        }

        var sameScopeDefinitions = bucket
            .Where(fact =>
                string.Equals(fact.ScopeId, seedTarget.ScopeId, StringComparison.Ordinal) &&
                fact.SpanStart < seedTarget.Target.SpanStart)
            .SelectMany(fact => fact.DefinesSymbols)
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in seedTarget.UsesSymbols)
        {
            if (symbol.DeclaringMemberId != seedTarget.Target.MemberId)
            {
                continue;
            }

            if (symbol.SymbolKind == SymbolKindRef.Parameter)
            {
                return StatementScopeMode.ParentBlockPiercing;
            }

            if (symbol.SymbolKind == SymbolKindRef.Local && !sameScopeDefinitions.Contains(symbol.SymbolKey))
            {
                return StatementScopeMode.ParentBlockPiercing;
            }
        }

        return StatementScopeMode.MinimalBlock;
    }
}

/// <summary>
/// 标记规则注册表。
/// </summary>
public sealed class MarkingRuleRegistry
{
    /// <summary>
    /// 初始化标记规则注册表的新实例。
    /// </summary>
    /// <param name="seedRules">种子规则。</param>
    /// <param name="expressionProjectionRules">表达式投影规则。</param>
    /// <param name="propagationRules">传播规则。</param>
    /// <param name="protectionRules">保护规则。</param>
    /// <param name="methodRules">方法规则。</param>
    /// <param name="classRules">类规则。</param>
    /// <param name="boundaryPromotionRules">边界提升规则。</param>
    public MarkingRuleRegistry(
        IEnumerable<ISeedRule> seedRules,
        IEnumerable<IExpressionProjectionRule> expressionProjectionRules,
        IEnumerable<IPropagationRule> propagationRules,
        IEnumerable<IProtectionRule> protectionRules,
        IEnumerable<IMethodRule> methodRules,
        IEnumerable<IClassRule> classRules,
        IEnumerable<IBoundaryPromotionRule> boundaryPromotionRules,
        IEnumerable<IStatementScopeRule> statementScopeRules)
    {
        SeedRules = seedRules.ToArray();
        ExpressionProjectionRules = expressionProjectionRules.ToArray();
        PropagationRules = propagationRules.ToArray();
        ProtectionRules = protectionRules.ToArray();
        MethodRules = methodRules.ToArray();
        ClassRules = classRules.ToArray();
        BoundaryPromotionRules = boundaryPromotionRules.ToArray();
        StatementScopeRules = statementScopeRules.ToArray();
    }

    /// <summary>
    /// 获取种子规则。
    /// </summary>
    public IReadOnlyList<ISeedRule> SeedRules { get; }

    /// <summary>
    /// 获取表达式投影规则。
    /// </summary>
    public IReadOnlyList<IExpressionProjectionRule> ExpressionProjectionRules { get; }

    /// <summary>
    /// 获取传播规则。
    /// </summary>
    public IReadOnlyList<IPropagationRule> PropagationRules { get; }

    /// <summary>
    /// 获取保护规则。
    /// </summary>
    public IReadOnlyList<IProtectionRule> ProtectionRules { get; }

    /// <summary>
    /// 获取方法规则。
    /// </summary>
    public IReadOnlyList<IMethodRule> MethodRules { get; }

    /// <summary>
    /// 获取类规则。
    /// </summary>
    public IReadOnlyList<IClassRule> ClassRules { get; }

    /// <summary>
    /// 获取边界提升规则。
    /// </summary>
    public IReadOnlyList<IBoundaryPromotionRule> BoundaryPromotionRules { get; }

    /// <summary>
    /// 获取语句作用域规则。
    /// </summary>
    public IReadOnlyList<IStatementScopeRule> StatementScopeRules { get; }

    /// <summary>
    /// 创建默认规则注册表。
    /// </summary>
    /// <returns>默认规则注册表。</returns>
    public static MarkingRuleRegistry CreateDefault() =>
        new(
            [new DirectiveSeedRule()],
            [new ExpressionProjectionRule()],
            [new SanitizationPropagationRule()],
            [new HighRiskProtectionRule(), new ObjectInitializerProtectionRule()],
            [new FunctionMarkingRule()],
            [new ClassMarkingRule()],
            [new InvocationBoundaryPromotionRule()],
            [new ParentBlockPiercingScopeRule()]);
}

/// <summary>
/// 标记规则引擎。
/// </summary>
public sealed class MarkingRuleEngine
{
    private readonly MarkingRuleRegistry _registry;
    private readonly StatementPropagationEngine _statementPropagationEngine;
    private readonly BoundaryPromotionEngine _boundaryPromotionEngine;
    private readonly RuleCompatibilityAdapter _compatibilityAdapter;

    /// <summary>
    /// 初始化标记规则引擎的新实例。
    /// </summary>
    /// <param name="registry">标记规则注册表。</param>
    public MarkingRuleEngine(
        MarkingRuleRegistry registry,
        StatementPropagationEngine? statementPropagationEngine = null,
        BoundaryPromotionEngine? boundaryPromotionEngine = null,
        RuleCompatibilityAdapter? compatibilityAdapter = null)
    {
        _registry = registry;
        _statementPropagationEngine = statementPropagationEngine ?? new StatementPropagationEngine(registry);
        _boundaryPromotionEngine = boundaryPromotionEngine ?? new BoundaryPromotionEngine(registry);
        _compatibilityAdapter = compatibilityAdapter ?? new RuleCompatibilityAdapter();
    }

    /// <summary>
    /// 执行分析视图的标记规则。
    /// </summary>
    /// <param name="analysisView">分析视图。</param>
    /// <returns>标记决策集合。</returns>
    public IReadOnlyList<MarkDecision> Execute(AnalysisView analysisView)
    {
        return ExecuteCore(
            _compatibilityAdapter.CreateContext(analysisView),
            _compatibilityAdapter.CreateExecutionContext("AnalysisView compatibility execution"),
            includeMethodRules: false);
    }

    public IReadOnlyList<MarkDecision> Execute(
        AnalysisSnapshot snapshot,
        AnalysisServices services,
        RuleExecutionContext executionContext)
    {
        return ExecuteCore(
            AnalysisContext.Create(snapshot, services),
            executionContext,
            includeMethodRules: true);
    }

    /// <summary>
    /// 执行分析上下文的标记规则。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <returns>标记决策集合。</returns>
    public IReadOnlyList<MarkDecision> Execute(AnalysisContext context)
    {
        return ExecuteCore(
            context,
            _compatibilityAdapter.CreateExecutionContext("AnalysisContext compatibility execution"),
            includeMethodRules: true);
    }

    private IReadOnlyList<MarkDecision> ExecuteCore(
        AnalysisContext context,
        RuleExecutionContext executionContext,
        bool includeMethodRules)
    {
        var seedDecisionsByTarget = new Dictionary<string, List<MarkDecision>>(StringComparer.Ordinal);
        var targetsByKey = context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal);

        foreach (var target in context.View.Targets)
        {
            if (executionContext.SeedTarget != null &&
                target.Target.TargetKey != executionContext.SeedTarget.TargetKey)
            {
                continue;
            }

            if (IsProtected(target))
            {
                continue;
            }

            foreach (var rule in _registry.SeedRules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.Target.TargetKey, out var list))
                    {
                        list = new List<MarkDecision>();
                        seedDecisionsByTarget[decision.Target.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }

            foreach (var rule in _registry.ExpressionProjectionRules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.Target.TargetKey, out var list))
                    {
                        list = new List<MarkDecision>();
                        seedDecisionsByTarget[decision.Target.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }
        }

        // Propagation only reads direct seed decisions; build the read-only view once
        // for the whole execution to avoid per-seed map copies.
        var seedDecisionView = seedDecisionsByTarget.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MarkDecision>)pair.Value,
            StringComparer.Ordinal);

        var finalDecisions = seedDecisionsByTarget.Values.SelectMany(list => list).ToList();
        foreach (var seedGroup in seedDecisionsByTarget)
        {
            if (!targetsByKey.TryGetValue(seedGroup.Key, out var seedTarget) ||
                seedTarget.Target.TargetKind != TargetKind.Statement ||
                IsProtected(seedTarget))
            {
                continue;
            }

            finalDecisions.AddRange(
                _statementPropagationEngine.Propagate(
                    context,
                    executionContext,
                    seedTarget,
                    seedDecisionView));
        }

        finalDecisions.AddRange(_boundaryPromotionEngine.Promote(context, finalDecisions, targetsByKey));

        if (includeMethodRules)
        {
            foreach (var functionNode in context.FunctionIndex.NodesByMemberId.Values.OrderBy(node => node.MemberId.Value, StringComparer.Ordinal))
            {
                foreach (var rule in _registry.MethodRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, functionNode));
                }
            }

            foreach (var target in context.View.Targets.Where(target => target.Target.TargetKind == TargetKind.Class))
            {
                foreach (var rule in _registry.ClassRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, target));
                }
            }
        }

        return finalDecisions
            .GroupBy(decision => $"{decision.Target.TargetKey}|{decision.Action.Kind}|{decision.Reason.RuleId}|{decision.Reason.SourceTargetKey}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private bool IsProtected(AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));

}

/// <summary>
/// 默认值格式化器。
/// </summary>
internal static class DefaultValueFormatter
{
    /// <summary>
    /// 格式化默认值。
    /// </summary>
    /// <param name="returnTypeDisplay">返回类型显示名称。</param>
    /// <returns>格式化后的默认值字符串。</returns>
    public static string Format(string returnTypeDisplay) =>
        returnTypeDisplay switch
        {
            "bool" => "false",
            "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "decimal" or "float" or "double" => "0",
            "char" => "'\\0'",
            "void" => string.Empty,
            var value when value.EndsWith("?", StringComparison.Ordinal) => "null",
            _ when IsReferenceTypeLike(returnTypeDisplay) => "null",
            _ => "default"
        };

    private static bool IsReferenceTypeLike(string returnTypeDisplay)
    {
        if (string.Equals(returnTypeDisplay, "string", StringComparison.Ordinal))
        {
            return true;
        }

        return returnTypeDisplay.Contains('.', StringComparison.Ordinal) ||
               returnTypeDisplay.Contains('<', StringComparison.Ordinal);
    }
}
