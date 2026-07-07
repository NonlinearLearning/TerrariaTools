using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteUnreferencedMethodProposalRule : RuleDefinitionPropose
{
  public override string RuleId { get; } = DeleteUnreferencedMethodRuleIds.ProposalRuleId;

  public override string GroupKey { get; } = DeleteUnreferencedMethodRuleIds.GroupKey;

  public override string Name { get; } = "Delete unreferenced private method declarations";

  public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
    Array.Empty<SyntaxKind>();

  public override IEnumerable<DecisionUnit> Propose(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
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
