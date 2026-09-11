using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Promotes a revalidated unreferenced private method into a declaration deletion candidate.
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreferencedMethodDeletion)]
public sealed class UnreferencedMethodLiftingRule : RuleDefinitionLift
{
    private static readonly RuleConsumesContract UnreferencedMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          UnreferencedMethodFacts.Propagated)
      });

    private static readonly RuleProducesContract UnreferencedMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          UnreferencedMethodFacts.Lifted)
      });

    public override string RuleId { get; } = "lift.unreferenced-method";

    public override RuleConsumesContract Consumes => UnreferencedMethodConsumes;

    public override RuleProducesContract Produces => UnreferencedMethodProduces;

    public override string Name { get; } = "Lift unreferenced methods into declaration deletion candidates";

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
            if (propagatedMark.RuleId != "propagate.unreferenced-method" ||
                propagatedMark.Mark.FactKind != UnreferencedMethodFacts.Propagated ||
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
                factKind: UnreferencedMethodFacts.Lifted),
              propagatedMark.SourceMark,
              propagatedMark.Depth,
              StructuralKind.MethodDeletion,
              new MethodDeletionLiftPayload(
                MethodDeletionKind.Unreferenced,
                method.Identifier.ValueText,
                propagatedMark.SourceMark.Reason));
        }
    }
}
