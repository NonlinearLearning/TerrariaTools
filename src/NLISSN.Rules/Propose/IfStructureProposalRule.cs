using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Deletion.Core.Decision;
using Deletion.Core.Lifting;
using Deletion.Core.Marking;
using Deletion.Core.Propagation;

namespace Deletion.Rules;

public sealed class IfStructureProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.if-structure";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-IF-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted if/elseif/else structure decisions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.IfConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;
        var consumedKeys = new HashSet<(int Start, int Length, int RawKind)>();

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateIfStructureCompletionPayloads(
                     propagatedMarks))
        {
            var decisionNode = DeleteSObjectProposalHelpers.GetIfStructureDecisionNode(payload);

            if (consumedKeys.Contains(DeleteSObjectProposalHelpers.BuildNodeKey(decisionNode)))
            {
                continue;
            }

            if (DeleteSObjectProposalHelpers.TryBuildIfStructureDecisionFromMark(
                    RuleId,
                    payload,
                    out var decision,
                    out var consumedNodes) &&
                decision is not null)
            {
                foreach (var node in consumedNodes)
                {
                    consumedKeys.Add(DeleteSObjectProposalHelpers.BuildNodeKey(node));
                }

                yield return decision;
            }
        }
    }
}
