namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// Predicts method deletes when all remaining call sites are already scheduled for deletion.
/// </summary>
public sealed class ReferenceZeroPredictionAnalyzer : ApplicationAbstractions.IReferenceZeroPredictionAnalyzer
{
    IReadOnlyList<CoreRules.MarkDecision> ApplicationAbstractions.IReferenceZeroPredictionAnalyzer.Predict(
        CoreAnalysis.AnalysisContext context,
        IReadOnlyList<CoreRules.MarkDecision> decisions)
    {
        var statementTargetsByDecisionKey = context.View.Targets
            .Where(static target => target.Target.TargetKind == CoreCommon.TargetKind.Statement)
            .ToDictionary(
                static target => BuildDecisionKey(target.Target, target.Locator),
                static target => target,
                StringComparer.Ordinal);

        var deletedCallSites = decisions
            .Where(static decision =>
                decision.Target.TargetKind == CoreCommon.TargetKind.Statement &&
                decision.Action.Kind == CoreCommon.PlanActionKind.Delete)
            .Select(decision => new
            {
                Decision = decision,
                Target = statementTargetsByDecisionKey.GetValueOrDefault(decision.TargetKey)
            })
            .Where(static item => item.Target is not null && item.Target.InvokedMemberIds.Count == 1)
            .ToArray();

        if (deletedCallSites.Length == 0)
        {
            return Array.Empty<CoreRules.MarkDecision>();
        }

        var existingMethodDeletes = decisions
            .Where(static decision =>
                decision.Target.TargetKind == CoreCommon.TargetKind.Method &&
                decision.Action.Kind == CoreCommon.PlanActionKind.Delete)
            .Select(static decision => decision.Target.MemberId.Value)
            .ToHashSet(StringComparer.Ordinal);

        var predicted = new List<CoreRules.MarkDecision>();
        foreach (var group in deletedCallSites.GroupBy(
                     static item => item.Target!.InvokedMemberIds[0].Value,
                     StringComparer.Ordinal))
        {
            if (existingMethodDeletes.Contains(group.Key) ||
                !context.FunctionIndex.NodesByMemberId.TryGetValue(group.Key, out var functionNode))
            {
                continue;
            }

            if (functionNode.MemberKind != CoreCommon.MemberKind.Method ||
                !functionNode.IsPrivate ||
                !functionNode.HasBody ||
                context.Inheritance.IsOverrideMember(group.Key) ||
                context.Inheritance.ImplementsInterfaceMember(group.Key))
            {
                continue;
            }

            var remainingReferences = context.References.GetReferencingFunctions(group.Key)
                .Select(static memberId => memberId.Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var referencingMemberId in group
                         .Select(static item => item.Decision.Target.MemberId.Value)
                         .Distinct(StringComparer.Ordinal))
            {
                remainingReferences.Remove(referencingMemberId);
            }

            if (remainingReferences.Count > 0)
            {
                continue;
            }

            var sourceDecision = group
                .OrderBy(static item => item.Decision.Target.DocumentPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Decision.Locator.SpanStart)
                .First()
                .Decision;

            predicted.Add(
                new CoreRules.MarkDecision(
                    new CoreCommon.TargetIdentity(
                        functionNode.DocumentPath,
                        functionNode.MemberId,
                        functionNode.MemberKind,
                        CoreCommon.TargetKind.Method),
                    new CoreCommon.TargetLocator(
                        functionNode.SpanStart,
                        functionNode.SpanLength,
                        functionNode.DisplayName,
                        new CoreCommon.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
                    new CoreCommon.PlanAction(CoreCommon.PlanActionKind.Delete, sourceDecision.Action.Payload),
                    new CoreRules.PlanReason(
                        "reference-zero-prediction",
                        "All remaining call references are already scheduled for deletion.",
                        sourceDecision.TargetKey,
                        sourceDecision.Locator.DisplayText,
                        [group.Key],
                        [functionNode.DisplayName],
                        SourceMemberId: sourceDecision.Target.MemberId.Value,
                        TriggeredSymbolKeys: [group.Key],
                        Origin: CoreCommon.DecisionOrigin.Prediction,
                        Category: CoreCommon.DecisionCategory.Delete)));
        }

        return predicted;
    }

    private static string BuildDecisionKey(CoreCommon.TargetIdentity target, CoreCommon.TargetLocator locator) =>
        $"{target.IdentityKey}|{locator.EffectiveResolutionKey.SpanStart}|{locator.EffectiveResolutionKey.SpanLength}";
}
