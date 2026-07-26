using Microsoft.CodeAnalysis.CSharp;
using Deletion.Core.Decision;
using Deletion.Core.Lifting;
using Deletion.Core.Marking;
using Deletion.Core.Propagation;

namespace Deletion.Rules;

public sealed class UnreferencedMethodProposalRule : RuleDefinitionPropose
{
  public override string CapabilityId { get; } = "propose.unreferenced-method";

    public override string RuleId { get; } = "DEL-UNREF-METHOD-PROP-001";

  public override string GroupKey { get; } = "DEL-UNREF-METHOD";

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
      yield return DeleteDecisionFactory.CreateDeleteDecision(
        RuleId,
        seedMark.SyntaxNode,
        seedMark.Reason);
    }
  }
}
