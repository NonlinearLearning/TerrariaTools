using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.MethodLinkage;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Revalidates unreachable method candidates against the compilation-level reachability proof.
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreachableMethodDeletion)]
public sealed class UnreachableMethodPropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleConsumesContract UnreachableMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreachableMethodFacts.Marked)
      });

    private static readonly RuleProducesContract UnreachableMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreachableMethodFacts.Propagated)
      });

    public override string RuleId { get; } = "propagate.unreachable-method";

    public override RuleConsumesContract Consumes => UnreachableMethodConsumes;

    public override RuleProducesContract Produces => UnreachableMethodProduces;

    public override string Name { get; } = "Revalidate unreachable method deletion candidates";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      IPropagationRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        var linkage = context.Runtime.GetOrCreateCompilationCache(
          context.SemanticModel.Compilation,
          static compilation => MethodLinkageAnalysis.Create(compilation));
        if (linkage.HasErrors)
        {
            yield break;
        }

        foreach (var seedMark in seedMarks)
        {
            if (seedMark.FactKind != UnreachableMethodFacts.Marked ||
                seedMark.SyntaxNode is not MethodDeclarationSyntax method ||
                context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
                !linkage.UnreachableMethods.Contains(Canonicalize(symbol)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                method,
                "Reachability proof still classifies this method as unreachable.",
                factKind: UnreachableMethodFacts.Propagated),
              seedMark,
              1);
        }
    }

    private static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }
}
