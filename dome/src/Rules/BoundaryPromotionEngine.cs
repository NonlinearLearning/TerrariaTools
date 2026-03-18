namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed class BoundaryPromotionEngine
{
    private readonly MarkingRuleRegistry _registry;

    public BoundaryPromotionEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<ModelRules.MarkDecision> Promote(
        ModelAnalysis.AnalysisContext context,
        IReadOnlyList<ModelRules.MarkDecision> currentDecisions,
        IReadOnlyDictionary<string, ModelAnalysis.AnalysisTarget> targetsByKey)
    {
        var promoted = new List<ModelRules.MarkDecision>();
        var existingMethodDeletes = currentDecisions
            .Where(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Method &&
                               decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete)
            .Select(decision => decision.Target.MemberId.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var decision in currentDecisions.Where(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Statement))
        {
            if (!targetsByKey.TryGetValue(decision.TargetKey, out var target))
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
