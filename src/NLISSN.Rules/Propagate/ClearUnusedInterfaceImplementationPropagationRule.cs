using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Carries an unused interface implementation into the visibility cleanup handoff.
public sealed class ClearUnusedInterfaceImplementationPropagationRule : RuleDefinitionPropagate
{
public override string RuleId { get; } = "propagate.clear-unused-interface-implementation";
  public override RuleConsumesContract Consumes { get; } = new(new[]
  {
    new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnusedInterfaceImplementationFacts.Marked)
  });
  public override RuleProducesContract Produces { get; } = new(new[]
  {
    new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnusedInterfaceImplementationFacts.Propagated)
  });
  public override string Name { get; } = "Propagate unused interface implementation facts";
  public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } = new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
  {
    _ = context;
    foreach (var seedMark in seedMarks)
    {
      if (seedMark.FactKind != UnusedInterfaceImplementationFacts.Marked ||
          seedMark.SyntaxNode is not MethodDeclarationSyntax method)
      {
        continue;
      }

      yield return new PropagatedMarkRecord(
        RuleId,
        MarkRecordFactory.Create(RuleId, method, "Unused interface implementation is ready for lifting.", factKind: UnusedInterfaceImplementationFacts.Propagated),
        seedMark,
        1);
    }
  }
}
