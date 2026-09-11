using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Promotes a revalidated unreachable method into a declaration deletion candidate.
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreachableMethodDeletion)]
public sealed class UnreachableMethodLiftingRule : RuleDefinitionLift
{
    private static readonly RuleConsumesContract UnreachableMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          UnreachableMethodFacts.Propagated)
      });

    private static readonly RuleProducesContract UnreachableMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          UnreachableMethodFacts.Lifted)
      });

    public override string RuleId { get; } = "lift.unreachable-method";

    public override RuleConsumesContract Consumes => UnreachableMethodConsumes;

    public override RuleProducesContract Produces => UnreachableMethodProduces;

    public override string Name { get; } = "Lift unreachable methods into declaration deletion candidates";

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
            if (propagatedMark.RuleId != "propagate.unreachable-method" ||
                propagatedMark.Mark.FactKind != UnreachableMethodFacts.Propagated ||
                propagatedMark.Mark.SyntaxNode is not MethodDeclarationSyntax method ||
                method.Parent is not TypeDeclarationSyntax)
            {
                continue;
            }

            yield return new LiftedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                method,
                propagatedMark.SourceMark.Reason,
                factKind: UnreachableMethodFacts.Lifted),
              propagatedMark.SourceMark,
              propagatedMark.Depth,
              StructuralKind.MethodDeletion,
              new MethodDeletionLiftPayload(
                MethodDeletionKind.Unreachable,
                method.Identifier.ValueText,
                propagatedMark.SourceMark.Reason));
        }
    }
}
