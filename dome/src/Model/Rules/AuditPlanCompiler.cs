using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Model.Planning;

public static class AuditPlanCompiler
{
    public static PlanCompilationResult Compile(PlanMetadata metadata, IEnumerable<MarkDecision> decisions)
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
                preferred.Reason,
                preferred.Chain));
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
