using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class LogicalExpressionProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.LogicalProposalRuleId;

    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    public override string Name { get; } = "Match s-rooted logical expression reductions";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.LogicalConflictNodeKinds;

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks)
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
