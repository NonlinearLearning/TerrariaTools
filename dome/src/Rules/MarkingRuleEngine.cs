namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

/// <summary>
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

/// <summary>
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
/// 指令种子规则。
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
public sealed class FunctionMarkingRule : IMethodRule
{
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
}

/// <summary>
/// 类标记规则。
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
        if (target.Target.TargetKind != TargetKind.Class)
        {
            yield break;
        }

        if (target.IsHighRisk ||
            context.Inheritance.IsInInheritanceChain(target.Target.MemberId.Value) ||
            context.References.HasReferences(target.Target.MemberId.Value))
        {
            yield break;
        }

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.Delete,
            "class-mark",
            "Class has no references and is not protected by inheritance.");
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
    public MarkingRuleRegistry(
        IEnumerable<ISeedRule> seedRules,
        IEnumerable<IExpressionProjectionRule> expressionProjectionRules,
        IEnumerable<IPropagationRule> propagationRules,
        IEnumerable<IProtectionRule> protectionRules,
        IEnumerable<IMethodRule> methodRules,
        IEnumerable<IClassRule> classRules)
    {
        SeedRules = seedRules.ToArray();
        ExpressionProjectionRules = expressionProjectionRules.ToArray();
        PropagationRules = propagationRules.ToArray();
        ProtectionRules = protectionRules.ToArray();
        MethodRules = methodRules.ToArray();
        ClassRules = classRules.ToArray();
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
            [new ClassMarkingRule()]);
}

/// <summary>
/// 标记规则引擎。
/// </summary>
public sealed class MarkingRuleEngine
{
    private readonly MarkingRuleRegistry _registry;

    /// <summary>
    /// 初始化标记规则引擎的新实例。
    /// </summary>
    /// <param name="registry">标记规则注册表。</param>
    public MarkingRuleEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 执行分析视图的标记规则。
    /// </summary>
    /// <param name="analysisView">分析视图。</param>
    /// <returns>标记决策集合。</returns>
    public IReadOnlyList<MarkDecision> Execute(AnalysisView analysisView)
    {
        return ExecuteCore(
            new AnalysisContext(
                analysisView,
                new NoOpInheritanceQueryService(),
                new NoOpReferenceQueryService()),
            includeMethodRules: false);
    }

    /// <summary>
    /// 执行分析上下文的标记规则。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <returns>标记决策集合。</returns>
    public IReadOnlyList<MarkDecision> Execute(AnalysisContext context)
    {
        return ExecuteCore(context, includeMethodRules: true);
    }

    private IReadOnlyList<MarkDecision> ExecuteCore(AnalysisContext context, bool includeMethodRules)
    {
        var seedDecisionsByTarget = new Dictionary<string, List<MarkDecision>>(StringComparer.Ordinal);

        foreach (var target in context.View.Targets)
        {
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

        var finalDecisions = new List<MarkDecision>();

        foreach (var memberTargets in context.View.Targets
                     .GroupBy(target => $"{target.Target.DocumentPath}|{target.Target.MemberId.Value}", StringComparer.Ordinal)
                     .Select(group => group.OrderBy(target => target.Target.SpanStart).ToArray()))
        {
            var taintedSymbols = new Dictionary<string, MarkDecision>(StringComparer.Ordinal);

            foreach (var target in memberTargets)
            {
                if (IsProtected(target))
                {
                    foreach (var definedSymbol in target.DefinesSymbols)
                    {
                        taintedSymbols.Remove(definedSymbol.SymbolKey);
                    }

                    continue;
                }

                IReadOnlyList<MarkDecision> directDecisions = seedDecisionsByTarget.TryGetValue(target.Target.TargetKey, out var seeds)
                    ? seeds
                    : Array.Empty<MarkDecision>();

                var emitted = new List<MarkDecision>(directDecisions);

                if (emitted.Count == 0)
                {
                    var propagatedByAction = new Dictionary<PlanActionKind, (MarkDecision SourceDecision, List<SymbolRef> Symbols)>();

                    foreach (var usedSymbol in target.UsesSymbols)
                    {
                        if (!taintedSymbols.TryGetValue(usedSymbol.SymbolKey, out var sourceDecision))
                        {
                            continue;
                        }

                        if (_registry.PropagationRules.Any(rule => !rule.CanPropagate(target, usedSymbol, sourceDecision)))
                        {
                            continue;
                        }

                        if (!propagatedByAction.TryGetValue(sourceDecision.Action.Kind, out var propagation))
                        {
                            propagation = (sourceDecision, new List<SymbolRef>());
                            propagatedByAction[sourceDecision.Action.Kind] = propagation;
                        }

                        if (propagation.Symbols.All(symbol => !string.Equals(symbol.SymbolKey, usedSymbol.SymbolKey, StringComparison.Ordinal)))
                        {
                            propagation.Symbols.Add(usedSymbol);
                        }
                    }

                    foreach (var propagation in propagatedByAction.Values)
                    {
                        var evidence = new PropagationEvidence(
                            propagation.Symbols.Select(symbol => symbol.SymbolKey).ToArray(),
                            propagation.Symbols.Select(symbol => symbol.DisplayName).Distinct(StringComparer.Ordinal).ToArray());
                        var chain = AppendPropagationChain(
                            propagation.SourceDecision,
                            target.Target,
                            evidence);

                        emitted.Add(MarkDecision.ForTarget(
                            target.Target,
                            propagation.SourceDecision.Action.Kind,
                            "dataflow-propagation",
                            "Propagated through a use/def dependency.",
                            propagation.SourceDecision.Action.Payload,
                            propagation.SourceDecision.Target.TargetKey,
                            propagation.SourceDecision.Target.DisplayText,
                            evidence.RelatedSymbolKeys,
                            evidence.RelatedSymbolNames,
                            chain: chain));
                    }
                }

                finalDecisions.AddRange(emitted);

                foreach (var definedSymbol in target.DefinesSymbols)
                {
                    var sourceDecision = emitted.FirstOrDefault();
                    if (sourceDecision != null)
                    {
                        taintedSymbols[definedSymbol.SymbolKey] = sourceDecision;
                    }
                    else
                    {
                        taintedSymbols.Remove(definedSymbol.SymbolKey);
                    }
                }
            }
        }

        if (includeMethodRules)
        {
            foreach (var functionNode in context.View.FunctionGraph.Nodes)
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

    private static PropagationChain AppendPropagationChain(
        MarkDecision sourceDecision,
        PlanTarget target,
        PropagationEvidence evidence)
    {
        var existingHops = sourceDecision.Chain?.Hops ?? Array.Empty<PropagationHop>();
        var rootTargetKey = sourceDecision.Chain?.RootTargetKey ?? sourceDecision.Target.TargetKey;
        var rootTargetDisplayText = sourceDecision.Chain?.RootTargetDisplayText ?? sourceDecision.Target.DisplayText;
        var newHop = new PropagationHop(
            sourceDecision.Target.TargetKey,
            sourceDecision.Target.DisplayText,
            target.TargetKey,
            target.DisplayText,
            "dataflow-propagation",
            sourceDecision.Action.Kind,
            evidence);

        return new PropagationChain(
            rootTargetKey,
            rootTargetDisplayText,
            existingHops.Concat([newHop]).ToArray());
    }
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

/// <summary>
/// 无操作继承查询服务。
/// </summary>
internal sealed class NoOpInheritanceQueryService : IInheritanceQueryService
{
    public bool IsOverrideMember(string memberId) => false;

    public bool ImplementsInterfaceMember(string memberId) => false;

    public bool IsInInheritanceChain(string typeId) => false;
}

/// <summary>
/// 无操作引用查询服务。
/// </summary>
internal sealed class NoOpReferenceQueryService : IReferenceQueryService
{
    public bool HasReferences(string symbolOrMemberId) => false;

    public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => Array.Empty<MemberId>();

    public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => Array.Empty<string>();
}
