using Domain.Propagation;
using Domain.Analysis;
using Domain.Rules;

namespace Logic.Propagation;

/// <summary>
/// 传播构造器。
/// </summary>
public sealed class ImpactPropagator : IImpactPropagator
{

    public PropagationResolution Propagate(PropagationBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ChangeCandidate candidate = input.Candidate ?? ChangeCandidate.Create(
            input.RuleTargetId,
            input.RuleCode,
            input.TargetName,
            input.CandidateKind,
            input.PrimaryReason,
            input.ScenarioTags.FirstOrDefault());

        SliceBoundary sliceBoundary = new(
            input.BoundaryName,
            input.SliceDirection,
            input.MaxDepth,
            input.IncludeExternalReferences);

        List<PropagationFactReference> factReferences = new();
        IReadOnlyCollection<string> propagationTargets = ResolveTargets(input, factReferences);
        candidate.ApplyPropagation(
            sliceBoundary,
            input.AdditionalReasons,
            input.ScenarioTags.Skip(1),
            propagationTargets.Select(Domain.Common.TargetName.Create).ToArray(),
            input.Snapshot is null ? "传播阶段识别到联动目标。" : "传播阶段基于分析事实识别到联动目标。");

        return new PropagationResolution
        {
            Candidate = candidate,
            SliceBoundary = sliceBoundary,
            FactReferences = factReferences,
        };
    }

    private static IReadOnlyCollection<string> ResolveTargets(
        PropagationBuildInput input,
        ICollection<PropagationFactReference> factReferences)
    {
        if (input.Snapshot is null)
        {
            return input.PropagationTargets;
        }

        Dictionary<string, string> nodeNames = input.Snapshot.Nodes.ToDictionary(static node => node.NodeId, static node => node.DisplayName);
        List<string> resolvedTargets = new();
        string candidateTargetName = input.Candidate?.TargetName ?? input.TargetName;

        foreach (CpgCall call in input.Snapshot.Calls)
        {
            nodeNames.TryGetValue(call.FromNodeId, out string? fromNodeName);
            nodeNames.TryGetValue(call.ToNodeId, out string? toNodeName);

            bool matchesSource = input.Candidate?.MatchesTarget(fromNodeName) == true ||
                input.Candidate?.MatchesTarget(call.TargetSymbol) == true ||
                string.Equals(fromNodeName, candidateTargetName, StringComparison.Ordinal) ||
                string.Equals(call.TargetSymbol, candidateTargetName, StringComparison.Ordinal);
            if (!matchesSource || string.IsNullOrWhiteSpace(toNodeName))
            {
                continue;
            }

            if (!resolvedTargets.Contains(toNodeName, StringComparer.Ordinal))
            {
                resolvedTargets.Add(toNodeName);
                factReferences.Add(new PropagationFactReference(call.FromNodeId, call.ToNodeId, "Call"));
            }
        }

        foreach (CpgFlow flow in input.Snapshot.Flows)
        {
            nodeNames.TryGetValue(flow.FromNodeId, out string? fromNodeName);
            nodeNames.TryGetValue(flow.ToNodeId, out string? toNodeName);
            bool matchesSource = input.Candidate?.MatchesTarget(fromNodeName) == true ||
                string.Equals(fromNodeName, candidateTargetName, StringComparison.Ordinal);
            if (!matchesSource || string.IsNullOrWhiteSpace(toNodeName))
            {
                continue;
            }

            if (!resolvedTargets.Contains(toNodeName, StringComparer.Ordinal))
            {
                resolvedTargets.Add(toNodeName);
                factReferences.Add(new PropagationFactReference(flow.FromNodeId, flow.ToNodeId, "Flow"));
            }
        }

        return resolvedTargets.Count > 0 ? resolvedTargets : input.PropagationTargets;
    }
}
