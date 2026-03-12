namespace TerrariaTools.Dome.Plan;

using TerrariaTools.Dome.Core;

public static class AuditPlanCompiler
{
    public static PlanCompilationResult Compile(PlanMetadata metadata, IEnumerable<MarkDecision> decisions)
    {
        var grouped = decisions.GroupBy(decision => decision.Target.TargetKey, StringComparer.Ordinal).ToArray();
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
}
