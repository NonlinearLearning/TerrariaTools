using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.MethodLinkage;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// Revalidates unreferenced private method candidates against the compilation-level linkage proof.
[global::NLISSN.Core.Pipeline.RuleRegistration(
    global::NLISSN.Core.Pipeline.RuleFeature.UnreferencedMethodDeletion)]
public sealed class UnreferencedMethodPropagationRule : RuleDefinitionPropagate
{
    private static readonly RuleConsumesContract UnreferencedMethodConsumes =
      new(new[]
      {
        new RuleConsumedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreferencedMethodFacts.Marked)
      });

    private static readonly RuleProducesContract UnreferencedMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(new[] { SyntaxKind.MethodDeclaration }, UnreferencedMethodFacts.Propagated)
      });

    public override string RuleId { get; } = "propagate.unreferenced-method";

    public override RuleConsumesContract Consumes => UnreferencedMethodConsumes;

    public override RuleProducesContract Produces => UnreferencedMethodProduces;

    public override string Name { get; } = "Revalidate unreferenced method deletion candidates";

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
            if (seedMark.FactKind != UnreferencedMethodFacts.Marked ||
                seedMark.SyntaxNode is not MethodDeclarationSyntax method ||
                context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
                !linkage.UnreferencedPrivateMethods.Contains(Canonicalize(symbol)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                method,
                "Reference closure still proves that this private method is unreferenced.",
                factKind: UnreferencedMethodFacts.Propagated),
              seedMark,
              1);
        }
    }

    private static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }
}
