using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DefaultDeleteProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.DefaultDeleteProposalRuleId;

    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    public override string Name { get; } = "Match s-rooted default delete decisions";

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> DecisionConflictNodeKinds =>
      DeleteSObjectProposalHelpers.DefaultConflictNodeKinds;

    public override IReadOnlyList<Microsoft.CodeAnalysis.CSharp.SyntaxKind> MergeableNodeKinds =>
      DeleteSObjectProposalHelpers.MergeableNodeKinds;

    public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;

        foreach (var (mark, sourceMark) in DeleteSObjectProposalHelpers.EnumerateActiveDerivedMarks(
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(mark))
            {
                continue;
            }

            yield return RuleAnalysisHelpers.CreateDeleteDecision(
              RuleId,
              mark.SyntaxNode,
              mark.Reason,
              sourceMark.SyntaxNode);
        }

        foreach (var seedMark in DeleteSObjectProposalHelpers.EnumerateUncoveredSeedMarks(
                     seedMarks,
                     propagatedMarks,
                     liftedMarks))
        {
            if (IsHandledBySpecializedRule(seedMark))
            {
                continue;
            }

            yield return RuleAnalysisHelpers.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }

    private static bool IsHandledBySpecializedRule(MarkRecord mark)
    {
        var kind = (Microsoft.CodeAnalysis.CSharp.SyntaxKind)mark.SyntaxNode.RawKind;
        return DeleteSObjectProposalHelpers.LogicalConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.IfConflictNodeKinds.Contains(kind) ||
          DeleteSObjectProposalHelpers.ControlConflictNodeKinds.Contains(kind) ||
          mark.SyntaxNode is ElseClauseSyntax;
    }
}
