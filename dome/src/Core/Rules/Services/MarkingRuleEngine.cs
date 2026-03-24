namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 统一执行种子、传播、边界提升与成员级规则，生成最终决策集合。
/// </summary>
public sealed class MarkingRuleEngine : IMarkDecisionBuilder
{
    private readonly MarkingRuleRegistry _registry;
    private readonly StatementPropagationEngine _statementPropagationEngine;
    private readonly BoundaryPromotionEngine _boundaryPromotionEngine;

    /// <summary>
    /// 初始化标记规则引擎。
    /// </summary>
    public MarkingRuleEngine(
        MarkingRuleRegistry registry,
        StatementPropagationEngine? statementPropagationEngine = null,
        BoundaryPromotionEngine? boundaryPromotionEngine = null)
    {
        _registry = registry;
        _statementPropagationEngine = statementPropagationEngine ?? new StatementPropagationEngine(registry);
        _boundaryPromotionEngine = boundaryPromotionEngine ?? new BoundaryPromotionEngine(registry);
    }

    /// <summary>
    /// 基于分析上下文构建完整决策集合。
    /// </summary>
    public IReadOnlyList<ModelRules.MarkDecision> BuildDecisions(ModelAnalysis.AnalysisContext context, CancellationToken cancellationToken)
    {
        return ExecuteCore(
            context,
            new ModelRules.RuleExecutionContext(
                "MarkingRuleEngine",
                null,
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                cancellationToken,
                "AnalysisContext execution"),
            includeMethodRules: true);
    }

    /// <summary>
    /// 执行核心规则评估流程。
    /// </summary>
    private IReadOnlyList<ModelRules.MarkDecision> ExecuteCore(
        ModelAnalysis.AnalysisContext context,
        ModelRules.RuleExecutionContext executionContext,
        bool includeMethodRules)
    {
        var seedDecisionsByTarget = new Dictionary<string, List<ModelRules.MarkDecision>>(StringComparer.Ordinal);
        var targetsByKey = context.View.Targets.ToDictionary(
            target => $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}",
            StringComparer.Ordinal);

        foreach (var target in context.View.Targets)
        {
            if (executionContext.SeedTarget != null &&
                target.Target.IdentityKey != executionContext.SeedTarget.IdentityKey)
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
                    if (!seedDecisionsByTarget.TryGetValue(decision.TargetKey, out var list))
                    {
                        list = [];
                        seedDecisionsByTarget[decision.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }

            foreach (var rule in _registry.ExpressionProjectionRules)
            {
                foreach (var decision in rule.Evaluate(target))
                {
                    if (!seedDecisionsByTarget.TryGetValue(decision.TargetKey, out var list))
                    {
                        list = [];
                        seedDecisionsByTarget[decision.TargetKey] = list;
                    }

                    list.Add(decision);
                }
            }
        }

        var seedDecisionView = seedDecisionsByTarget.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ModelRules.MarkDecision>)pair.Value,
            StringComparer.Ordinal);
        var finalDecisions = seedDecisionsByTarget.Values.SelectMany(list => list).ToList();

        foreach (var seedGroup in seedDecisionsByTarget)
        {
            if (!targetsByKey.TryGetValue(seedGroup.Key, out var seedTarget) ||
                seedTarget.Target.TargetKind != ModelPrimitives.TargetKind.Statement ||
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

        finalDecisions.AddRange(
            _boundaryPromotionEngine.Promote(
                context,
                finalDecisions,
                targetsByKey));

        if (includeMethodRules)
        {
            foreach (var functionNode in context.FunctionIndex.NodesByMemberId.Values.OrderBy(node => node.MemberId.Value, StringComparer.Ordinal))
            {
                foreach (var rule in _registry.MethodRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, functionNode));
                }
            }

            foreach (var target in context.View.Targets.Where(target => target.Target.TargetKind is ModelPrimitives.TargetKind.Field or ModelPrimitives.TargetKind.Property))
            {
                foreach (var rule in _registry.MemberTargetRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, target));
                }
            }

            foreach (var target in context.View.Targets.Where(target => target.Target.TargetKind == ModelPrimitives.TargetKind.Class))
            {
                foreach (var rule in _registry.ClassRules)
                {
                    finalDecisions.AddRange(rule.Evaluate(context, target));
                }
            }
        }

        return finalDecisions
            .GroupBy(decision => $"{decision.TargetKey}|{decision.Action.Kind}|{decision.Reason.RuleId}|{decision.Reason.SourceTargetKey}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// 判断目标是否被保护规则拦截。
    /// </summary>
    private bool IsProtected(ModelAnalysis.AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));
}

/// <summary>
/// 为需要补默认返回值的场景生成对应文本。
/// </summary>
internal static class DefaultValueFormatter
{
    /// <summary>
    /// 根据返回类型格式化默认返回值文本。
    /// </summary>
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

    /// <summary>
    /// 判断返回类型是否可按引用类型语义处理。
    /// </summary>
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
