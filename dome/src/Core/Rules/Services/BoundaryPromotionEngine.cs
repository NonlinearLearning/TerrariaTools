namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 负责把语句级决策提升为成员级决策。
/// </summary>
public sealed class BoundaryPromotionEngine
{
    private readonly MarkingRuleRegistry _registry;

    /// <summary>
    /// 初始化边界提升引擎。
    /// </summary>
    public BoundaryPromotionEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 根据当前决策集合执行边界提升。
    /// </summary>
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
