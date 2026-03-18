namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed class MarkingRuleEngine : IMarkDecisionBuilder
{
    private readonly MarkingRuleRegistry _registry;
    private readonly StatementPropagationEngine _statementPropagationEngine;
    private readonly BoundaryPromotionEngine _boundaryPromotionEngine;

    public MarkingRuleEngine(
        MarkingRuleRegistry registry,
        StatementPropagationEngine? statementPropagationEngine = null,
        BoundaryPromotionEngine? boundaryPromotionEngine = null)
    {
        _registry = registry;
        _statementPropagationEngine = statementPropagationEngine ?? new StatementPropagationEngine(registry);
        _boundaryPromotionEngine = boundaryPromotionEngine ?? new BoundaryPromotionEngine(registry);
    }

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

    private bool IsProtected(ModelAnalysis.AnalysisTarget target) =>
        _registry.ProtectionRules.Any(rule => rule.Blocks(target));
}

internal static class DefaultValueFormatter
{
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
