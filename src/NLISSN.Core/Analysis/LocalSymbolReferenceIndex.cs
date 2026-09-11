using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// <summary>
/// Lazily materialized local and parameter references, partitioned by executable scope.
/// </summary>
public sealed class LocalSymbolReferenceIndex
{
    private readonly Dictionary<SyntaxNode, Dictionary<ISymbol, List<IdentifierNameSyntax>>> _referencesByScope =
      new(ReferenceEqualityComparer.Instance);

    public LocalSymbolReferenceIndex(SemanticModel semanticModel, SyntaxNode root)
    {
        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            IdentifierTraversalCount++;
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            {
                continue;
            }

            var scope = FindContainingExecutableScope(identifier);
            if (scope is null)
            {
                continue;
            }

            if (!_referencesByScope.TryGetValue(scope, out var referencesBySymbol))
            {
                referencesBySymbol = new Dictionary<ISymbol, List<IdentifierNameSyntax>>(SymbolEqualityComparer.Default);
                _referencesByScope.Add(scope, referencesBySymbol);
            }

            if (!referencesBySymbol.TryGetValue(symbol, out var references))
            {
                references = new List<IdentifierNameSyntax>();
                referencesBySymbol.Add(symbol, references);
            }

            references.Add(identifier);
        }

        foreach (var referencesBySymbol in _referencesByScope.Values)
        {
            foreach (var references in referencesBySymbol.Values)
            {
                references.Sort((left, right) => left.SpanStart.CompareTo(right.SpanStart));
            }
        }
    }

    public int IdentifierTraversalCount { get; }

    public IReadOnlyList<IdentifierNameSyntax> GetReferences(SyntaxNode sourceNode, ISymbol symbol)
    {
        var scope = FindContainingExecutableScope(sourceNode);
        return scope is not null &&
          _referencesByScope.TryGetValue(scope, out var referencesBySymbol) &&
          referencesBySymbol.TryGetValue(symbol, out var references)
            ? references
            : Array.Empty<IdentifierNameSyntax>();
    }

    private static SyntaxNode? FindContainingExecutableScope(SyntaxNode node)
    {
        return node.AncestorsAndSelf().FirstOrDefault(ancestor =>
          ancestor is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax);
    }
}
