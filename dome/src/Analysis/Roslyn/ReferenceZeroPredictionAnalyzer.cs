namespace TerrariaTools.Dome.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed partial class ReferenceZeroPredictionAnalyzer : ApplicationAbstractions.IReferenceZeroPredictionAnalyzer
{
    IReadOnlyList<ModelRules.MarkDecision> ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict(
        ModelAnalysis.AnalysisExecutionSnapshot snapshot,
        ModelAnalysis.AnalysisServices services,
        ModelRules.RuleExecutionContext executionContext,
        IReadOnlyList<ModelRules.MarkDecision> decisions)
    {
        var deletedCallSites = decisions
            .Where(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Statement && decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete)
            .Select(decision => new
            {
                Decision = decision,
                Target = snapshot.View.Targets.FirstOrDefault(target =>
                    string.Equals(
                        $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}",
                        decision.TargetKey,
                        StringComparison.Ordinal))
            })
            .Where(item => item.Target != null && item.Target.InvokedMemberIds.Count == 1)
            .ToArray();

        if (deletedCallSites.Length == 0)
        {
            return Array.Empty<ModelRules.MarkDecision>();
        }

        var existingMethodDeletes = decisions
            .Where(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Method && decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete)
            .Select(decision => decision.Target.MemberId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var decisionsByInvokedMethod = deletedCallSites
            .GroupBy(item => item.Target!.InvokedMemberIds[0].Value, StringComparer.Ordinal);
        var predicted = new List<ModelRules.MarkDecision>();

        foreach (var group in decisionsByInvokedMethod)
        {
            var functionSnapshot = services.FunctionGraphs.GetSnapshot(
                ModelAnalysis.FunctionGraphRequests.ExpandedMembersCalls(
                    new[] { new ModelPrimitives.MemberId(group.Key) },
                    executionContext.Requester,
                    executionContext.Reason ?? "Reference-zero prediction"));
            var functionNodes = functionSnapshot.Graph.Nodes
                .ToDictionary(node => node.MemberId.Value, StringComparer.Ordinal);
            if (existingMethodDeletes.Contains(group.Key) ||
                !functionNodes.TryGetValue(group.Key, out var functionNode))
            {
                continue;
            }

            if (functionNode.MemberKind != ModelPrimitives.MemberKind.Method ||
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
                .ThenBy(item => item.Decision.Locator.SpanStart)
                .First()
                .Decision;

            predicted.Add(new ModelRules.MarkDecision(
                new ModelPrimitives.TargetIdentity(
                    functionNode.DocumentPath,
                    functionNode.MemberId,
                    functionNode.MemberKind,
                    ModelPrimitives.TargetKind.Method),
                new ModelPrimitives.TargetLocator(
                    functionNode.SpanStart,
                    functionNode.SpanLength,
                    functionNode.DisplayName,
                    new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
                new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason(
                    "reference-zero-prediction",
                    "All call references are scheduled for deletion in the current plan.",
                    sourceDecision.TargetKey,
                    sourceDecision.Locator.DisplayText,
                    Origin: ModelPrimitives.DecisionOrigin.Prediction)));
        }

        return predicted;
    }

    IReadOnlyList<ModelRules.MarkDecision> ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict(
        ModelAnalysis.AnalysisContext context,
        IReadOnlyList<ModelRules.MarkDecision> decisions) =>
        ((ApplicationAbstractions.IReferenceZeroPredictionAnalyzer)this).Predict(
            context.Snapshot,
            context.Services,
            new ModelRules.RuleExecutionContext(
                "ReferenceZeroPredictionAnalyzer",
                null,
                ModelPrimitives.StatementScopeMode.MinimalBlock,
                CancellationToken.None,
                "Compatibility facade"),
            decisions);
}
