using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// <summary>
/// Promotes a visibility handoff into the fact consumed by the replacement rule.
/// </summary>
public sealed class PrivatizeInternalOnlyPublicMethodLiftingRule : RuleDefinitionLift
{

  public override string RuleId { get; } = "lift.privatize-internal-only-public-method";

  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(
      new[] { SyntaxKind.MethodDeclaration },
      InternalOnlyPublicMethodFacts.Propagated)
  });

  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(
      new[] { SyntaxKind.MethodDeclaration },
      InternalOnlyPublicMethodFacts.Lifted)
  });

  public override string Name { get; } =
    "Lift verified internal-only public methods into visibility decisions";

  public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<LiftedMarkRecord> Lift(
    ILiftRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
  {
    _ = context;
    _ = seedMarks;
    foreach (var propagatedMark in propagatedMarks)
    {
      if (propagatedMark.RuleId != "propagate.privatize-internal-only-public-method" ||
          propagatedMark.Mark.FactKind != InternalOnlyPublicMethodFacts.Propagated ||
          propagatedMark.Mark.SyntaxNode is not MethodDeclarationSyntax method)
      {
        continue;
      }

      yield return new LiftedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(
          RuleId,
          method,
          "Internal-only public method is approved for accessibility replacement.",
          factKind: InternalOnlyPublicMethodFacts.Lifted),
        propagatedMark.SourceMark,
        propagatedMark.Depth);
    }
  }
}
