namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 计划期引用归零预测分析器。
/// </summary>
public sealed class ReferenceZeroPredictionAnalyzer
{
    /// <summary>
    /// 根据当前计划预测将变为零引用的方法。
    /// </summary>
    /// <param name="snapshot">分析快照。</param>
    /// <param name="services">分析服务。</param>
    /// <param name="executionContext">规则执行上下文。</param>
    /// <param name="decisions">当前已有的标记决策。</param>
    /// <returns>预测的标记决策列表。</returns>
    public IReadOnlyList<MarkDecision> Predict(
        AnalysisSnapshot snapshot,
        AnalysisServices services,
        RuleExecutionContext executionContext,
        IReadOnlyList<MarkDecision> decisions)
    {
        var deletedCallSites = decisions
            .Where(decision => decision.Target.TargetKind == TargetKind.Statement && decision.Action.Kind == PlanActionKind.Delete)
            .Select(decision => new
            {
                Decision = decision,
                Target = snapshot.View.Targets.FirstOrDefault(target => target.Target.TargetKey == decision.Target.TargetKey)
            })
            .Where(item => item.Target != null && item.Target.InvokedMemberIds.Count == 1)
            .ToArray();

        if (deletedCallSites.Length == 0)
        {
            return Array.Empty<MarkDecision>();
        }

        var existingMethodDeletes = decisions
            .Where(decision => decision.Target.TargetKind == TargetKind.Method && decision.Action.Kind == PlanActionKind.Delete)
            .Select(decision => decision.Target.MemberId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var decisionsByInvokedMethod = deletedCallSites
            .GroupBy(item => item.Target!.InvokedMemberIds[0].Value, StringComparer.Ordinal);
        var predicted = new List<MarkDecision>();

        foreach (var group in decisionsByInvokedMethod)
        {
            var functionSnapshot = services.FunctionGraphs.GetSnapshot(
                FunctionGraphRequests.ExpandedMembersCalls(
                    new[] { new MemberId(group.Key) },
                    executionContext.Requester,
                    executionContext.Reason ?? "Reference-zero prediction"));
            var functionNodes = functionSnapshot.Graph.Nodes
                .ToDictionary(node => node.MemberId.Value, StringComparer.Ordinal);
            if (existingMethodDeletes.Contains(group.Key) ||
                !functionNodes.TryGetValue(group.Key, out var functionNode))
            {
                continue;
            }

            if (functionNode.MemberKind != MemberKind.Method ||
                !functionNode.IsPrivate ||
                !functionNode.HasBody)
            {
                continue;
            }

            if (services.Inheritance.IsOverrideMember(group.Key) ||
                services.Inheritance.ImplementsInterfaceMember(group.Key))
            {
                continue;
            }

            var remainingReferences = services.References.GetReferencingFunctions(group.Key)
                .Select(memberId => memberId.Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var referencingMemberId in group.Select(item => item.Decision.Target.MemberId.Value).Distinct(StringComparer.Ordinal))
            {
                remainingReferences.Remove(referencingMemberId);
            }

            if (remainingReferences.Count > 0)
            {
                continue;
            }

            var sourceDecision = group
                .OrderBy(item => item.Decision.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Decision.Target.SpanStart)
                .First()
                .Decision;

            predicted.Add(MarkDecision.ForTarget(
                new PlanTarget(
                    functionNode.DocumentPath,
                    functionNode.MemberId,
                    functionNode.MemberKind,
                    TargetKind.Method,
                    functionNode.SpanStart,
                    functionNode.SpanLength,
                    functionNode.DisplayName),
                PlanActionKind.Delete,
                "reference-zero-prediction",
                "All call references are scheduled for deletion in the current plan.",
                sourceTargetKey: sourceDecision.Target.TargetKey,
                sourceTargetDisplayText: sourceDecision.Target.DisplayText));
        }

        return predicted;
    }

    /// <summary>
    /// 根据当前计划预测将变为零引用的方法。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="decisions">当前已有的标记决策。</param>
    /// <returns>预测的标记决策列表。</returns>
    public IReadOnlyList<MarkDecision> Predict(AnalysisContext context, IReadOnlyList<MarkDecision> decisions) =>
        Predict(
            context.Snapshot,
            context.Services,
            new RuleExecutionContext(
                "ReferenceZeroPredictionAnalyzer",
                null,
                StatementScopeMode.MinimalBlock,
                CancellationToken.None,
                "Compatibility facade"),
            decisions);
}
