namespace TerrariaTools.Dome.Plan;

using TerrariaTools.Dome.Core;

/// <summary>
/// 审计计划编译器。
/// </summary>
public static class AuditPlanCompiler
{
    /// <summary>
    /// 编译审计计划。
    /// </summary>
    /// <param name="metadata">计划元数据。</param>
    /// <param name="decisions">标记决策集合。</param>
    /// <returns>计划编译结果。</returns>
    public static PlanCompilationResult Compile(PlanMetadata metadata, IEnumerable<MarkDecision> decisions)
    {
        var normalized = NormalizeDeleteDecisions(decisions).ToArray();
        var grouped = normalized.GroupBy(decision => decision.Target.TargetKey, StringComparer.Ordinal).ToArray();
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
                    kinds,
                    "Target has more than one planned action and no resolver is configured."));
                continue;
            }

            var preferred = items
                .OrderBy(item => item.Reason.RuleId == "dataflow-propagation" ? 1 : 0)
                .ThenBy(item => item.Reason.SourceTargetKey is null ? 0 : 1)
                .First();

            changes.Add(new PlannedChange(changes.Count, preferred.Target, preferred.Action, preferred.Reason, preferred.Chain));
        }

        if (conflicts.Count > 0)
        {
            return PlanCompilationResult.Failure("Unresolved conflicts detected while compiling the plan.", conflicts);
        }

        var orderedChanges = changes
            .OrderBy(change => change.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.Target.MemberId.Value, StringComparer.Ordinal)
            .ThenBy(change => change.Target.SpanStart)
            .Select((change, index) => change with { ExecutionOrder = index })
            .ToArray();

        return PlanCompilationResult.Success(new AuditPlan(metadata, orderedChanges, conflicts));
    }

    /// <summary>
    /// 规范化删除决策。
    /// </summary>
    /// <param name="decisions">标记决策集合。</param>
    /// <returns>规范化后的标记决策集合。</returns>
    private static IEnumerable<MarkDecision> NormalizeDeleteDecisions(IEnumerable<MarkDecision> decisions)
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
    /// 检查目标是否被已删除的类覆盖。
    /// </summary>
    /// <param name="target">计划目标。</param>
    /// <param name="classKey">类键。</param>
    /// <returns>如果被覆盖则返回 true，否则返回 false。</returns>
    private static bool IsCoveredByDeletedClass(PlanTarget target, string classKey)
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
