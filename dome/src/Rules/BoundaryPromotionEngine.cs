namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

public sealed class BoundaryPromotionEngine
{
    private readonly MarkingRuleRegistry _registry;

    public BoundaryPromotionEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<MarkDecision> Promote(
        AnalysisContext context,
        IReadOnlyList<MarkDecision> currentDecisions,
        IReadOnlyDictionary<string, AnalysisTarget> targetsByKey)
    {
        var promoted = new List<MarkDecision>();
        var existingMethodDeletes = currentDecisions
            .Where(decision => decision.Target.TargetKind == TargetKind.Method && decision.Action.Kind == PlanActionKind.Delete)
            .Select(decision => decision.Target.MemberId.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var decision in currentDecisions.Where(decision => decision.Target.TargetKind == TargetKind.Statement))
        {
            if (!targetsByKey.TryGetValue(decision.Target.TargetKey, out var target))
            {
                continue;
            }

            foreach (var rule in _registry.BoundaryPromotionRules)
            {
                foreach (var promotedDecision in rule.Evaluate(context, target, decision))
                {
                    if (existingMethodDeletes.Add(promotedDecision.Target.MemberId.Value))
                    {
                        promoted.Add(promotedDecision);
                    }
                }
            }
        }

        return promoted;
    }
}
