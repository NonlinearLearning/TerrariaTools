using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// <summary>
/// Carries a verified internal-only public method into the stage handoff port.
/// </summary>
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.InternalOnlyPublicMethodPrivatization)]
public sealed class PrivatizeInternalOnlyPublicMethodPropagationRule : RuleDefinitionPropagate
{

  public override string RuleId { get; } = "propagate.privatize-internal-only-public-method";

  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(
      new[] { SyntaxKind.MethodDeclaration },
      InternalOnlyPublicMethodFacts.Marked)
  });

  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(
      new[] { SyntaxKind.MethodDeclaration },
      InternalOnlyPublicMethodFacts.Propagated)
  });

  public override string Name { get; } =
    "Propagate verified internal-only public methods to the visibility handoff";

  public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<PropagatedMarkRecord> Propagate(
    IPropagationRuleContext context,
    IReadOnlyList<MarkRecord> seedMarks)
  {
    _ = context;
    foreach (var seedMark in seedMarks)
    {
      if (seedMark.FactKind != InternalOnlyPublicMethodFacts.Marked ||
          seedMark.SyntaxNode is not MethodDeclarationSyntax method)
      {
        continue;
      }

      yield return new PropagatedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(
          RuleId,
          method,
          "Verified internal-only public method is ready for visibility lifting.",
          factKind: InternalOnlyPublicMethodFacts.Propagated),
        seedMark,
        1);
    }
  }
}
