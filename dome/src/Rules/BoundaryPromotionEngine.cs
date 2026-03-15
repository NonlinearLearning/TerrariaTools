namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Core;

/// <summary>
/// 边界提升引擎。
/// </summary>
public sealed class BoundaryPromotionEngine
{
    private readonly MarkingRuleRegistry _registry;

    /// <summary>
    /// 初始化边界提升引擎。
    /// </summary>
    /// <param name="registry">规则注册表。</param>
    public BoundaryPromotionEngine(MarkingRuleRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 根据当前语句级决策执行边界提升。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="currentDecisions">当前标记决策。</param>
    /// <param name="targetsByKey">按目标键索引的目标字典。</param>
    /// <returns>提升产生的决策集合。</returns>
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
