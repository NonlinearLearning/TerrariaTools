using TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Core.Planning;

/// <summary>
/// 负责把规则决策编译为可执行的审计计划。
/// </summary>
public static class AuditPlanCompiler
{
    /// <summary>
    /// 将规则决策集合编译为计划结果。
    /// </summary>
    public static PlanCompilationResult Compile(PlanMetadata metadata, IEnumerable<ModelRules.MarkDecision> decisions)
    {
        var normalized = NormalizeDeleteDecisions(decisions).ToArray();
        var grouped = normalized.GroupBy(decision => decision.TargetKey, StringComparer.Ordinal).ToArray();
        var conflicts = new List<PlanConflict>();
        var changes = new List<PlannedChange>();

        foreach (var group in grouped)
        {
            var items = group.ToArray();
            var kinds = items.Select(item => item.Action.Kind).Distinct().ToArray();

            if (kinds.Length > 1)
            {
                conflicts.Add(new PlanConflict(
                    "MultipleActionsForTarget",
                    items[0].Target,
                    items[0].Locator,
                    kinds,
                    "Target has more than one planned action and no resolver is configured."));
                continue;
            }

            var preferred = items
                .OrderBy(item => item.Reason.Origin == DecisionOrigin.Propagation ? 1 : 0)
                .ThenBy(item => item.Reason.SourceTargetKey is null ? 0 : 1)
                .First();

            changes.Add(new PlannedChange(
                changes.Count,
                preferred.Target,
                preferred.Locator,
                preferred.Action,
                ProjectReason(preferred.Reason),
                ProjectChain(preferred.Chain)));
        }

        if (conflicts.Count > 0)
        {
            return PlanCompilationResult.Failure("Unresolved conflicts detected while compiling the plan.", conflicts);
        }

        var orderedChanges = changes
            .OrderBy(change => change.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Target.MemberId.Value, StringComparer.Ordinal)
            .ThenBy(change => change.Locator.SpanStart)
            .Select((change, index) => change with { ExecutionOrder = index })
            .ToArray();

        return PlanCompilationResult.Success(new AuditPlan(metadata, orderedChanges, conflicts));
    }

    /// <summary>
    /// 清理已经被更大删除动作覆盖的冗余决策。
    /// </summary>
    private static IEnumerable<ModelRules.MarkDecision> NormalizeDeleteDecisions(IEnumerable<ModelRules.MarkDecision> decisions)
    {
        var items = decisions.ToArray();
        var deletedClasses = items
            .Where(item => item.Target.TargetKind == TargetKind.Class && item.Action.Kind == PlanActionKind.Delete)
            .Select(item => $"{item.Target.DocumentPath}|{item.Target.MemberId.Value}")
            .ToArray();
        var deletedMethods = items
            .Where(item => item.Target.TargetKind == TargetKind.Method && item.Action.Kind == PlanActionKind.Delete)
            .Select(item => $"{item.Target.DocumentPath}|{item.Target.MemberId.Value}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var decision in items)
        {
            if (decision.Target.TargetKind != TargetKind.Class &&
                deletedClasses.Any(classKey => IsCoveredByDeletedClass(decision.Target, classKey)))
            {
                continue;
            }

            if (decision.Target.TargetKind == TargetKind.Statement &&
                deletedMethods.Contains($"{decision.Target.DocumentPath}|{decision.Target.MemberId.Value}"))
            {
                continue;
            }

            yield return decision;
        }
    }

    /// <summary>
    /// 将规则层原因对象投影为规划层原因对象。
    /// </summary>
    private static PlanReason ProjectReason(ModelRules.PlanReason reason) =>
        new(
            reason.RuleId,
            reason.ReasonText,
            reason.SourceTargetKey,
            reason.SourceTargetDisplayText,
            reason.RelatedSymbolKeys,
            reason.RelatedSymbolNames,
            reason.Severity,
            reason.SourceMemberId,
            reason.BoundaryKind,
            reason.TriggeredSymbolKeys,
            reason.Origin,
            reason.Category);

    /// <summary>
    /// 将规则层传播链投影为规划层传播链。
    /// </summary>
    private static PropagationChain? ProjectChain(ModelRules.PropagationChain? chain)
    {
        if (chain is null)
        {
            return null;
        }

        return new PropagationChain(
            chain.RootTargetKey,
            chain.RootTargetDisplayText,
            chain.Hops.Select(ProjectHop).ToArray());
    }

    /// <summary>
    /// 将规则层传播跳转投影为规划层传播跳转。
    /// </summary>
    private static PropagationHop ProjectHop(ModelRules.PropagationHop hop) =>
        new(
            hop.FromTargetKey,
            hop.FromTargetDisplayText,
            hop.ToTargetKey,
            hop.ToTargetDisplayText,
            hop.RuleId,
            hop.ActionKind,
            ProjectEvidence(hop.Evidence));

    /// <summary>
    /// 将规则层传播证据投影为规划层传播证据。
    /// </summary>
    private static PropagationEvidence ProjectEvidence(ModelRules.PropagationEvidence evidence) =>
        new(evidence.RelatedSymbolKeys, evidence.RelatedSymbolNames);

    /// <summary>
    /// 判断目标是否已被类级删除动作完全覆盖。
    /// </summary>
    private static bool IsCoveredByDeletedClass(TargetIdentity target, string classKey)
    {
        var separator = classKey.IndexOf('|');
        if (separator < 0)
        {
            return false;
        }

        var documentPath = classKey[..separator];
        var classId = classKey[(separator + 1)..];
        return string.Equals(target.DocumentPath, documentPath, StringComparison.Ordinal) &&
               (string.Equals(target.MemberId.Value, classId, StringComparison.Ordinal) ||
                target.MemberId.Value.StartsWith($"{classId}.", StringComparison.Ordinal));
    }
}
