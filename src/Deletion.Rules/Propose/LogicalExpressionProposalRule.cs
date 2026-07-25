using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Deletion.Core.Decision;
using Deletion.Core.Lifting;
using Deletion.Core.Marking;
using Deletion.Core.Propagation;

namespace Deletion.Rules;

public sealed class LogicalExpressionProposalRule : RuleDefinitionPropose
{
    public override string CapabilityId { get; } = "propose.logical-expression";

    public override string RuleId { get; } = "DEL-SOBJ-PROPOSE-LOGIC-001";

    public override string GroupKey { get; } = "DEL-SOBJ";

    public override string Name { get; } = "Match s-rooted logical expression reductions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.LogicalConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = seedMarks;
        _ = liftedMarks;

        foreach (var payload in DeleteSObjectProposalHelpers.EnumerateLogicalHostPayloads(
                     propagatedMarks))
        {
            var replacementNode = DeleteSObjectProposalHelpers.BuildLogicalReplacement(
              payload);
            if (replacementNode is not null)
            {
                yield return DeleteSObjectProposalHelpers.CreateLogicalReplaceDecision(
                  RuleId,
                  payload.Host,
                  replacementNode);
            }
        }
    }
}
