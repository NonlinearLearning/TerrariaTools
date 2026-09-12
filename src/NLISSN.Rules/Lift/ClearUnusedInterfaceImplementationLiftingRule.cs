using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Promotes an unused interface implementation into a body-replacement fact.
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.UnusedInterfaceImplementationCleanup)]
public sealed class ClearUnusedInterfaceImplementationLiftingRule : RuleDefinitionLift
{
public override string RuleId { get; } = "lift.clear-unused-interface-implementation";
  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnusedInterfaceImplementationFacts.Propagated)
  });
  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnusedInterfaceImplementationFacts.Lifted)
  });
  public override string Name { get; } = "Lift unused interface implementation facts into cleanup decisions";
  public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } = new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
  {
    _ = context;
    _ = seedMarks;
    foreach (var propagatedMark in propagatedMarks)
    {
      if (propagatedMark.Mark.FactKind != UnusedInterfaceImplementationFacts.Propagated ||
          propagatedMark.Mark.SyntaxNode is not MethodDeclarationSyntax method)
      {
        continue;
      }

      yield return new LiftedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(RuleId, method, "Unused interface implementation is approved for body cleanup.", factKind: UnusedInterfaceImplementationFacts.Lifted),
        propagatedMark.SourceMark,
        propagatedMark.Depth);
    }
  }
}
