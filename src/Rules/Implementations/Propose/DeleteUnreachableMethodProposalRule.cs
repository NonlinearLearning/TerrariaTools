using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteUnreachableMethodProposalRule : RuleDefinitionPropose
{
    public override string RuleId { get; } = "DEL-DEAD-001";

    public override string Name { get; } = "Match unreachable methods by graph reachability proposal";

    public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
      Array.Empty<SyntaxKind>();

    public override IEnumerable<DecisionUnit> Propose(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> liftedMarks)
    {
        _ = context;
        _ = propagatedMarks;
        _ = liftedMarks;

        foreach (var seedMark in seedMarks)
        {
            yield return RuleAnalysisHelpers.CreateDeleteDecision(
              RuleId,
              seedMark.SyntaxNode,
              seedMark.Reason);
        }
    }
}
