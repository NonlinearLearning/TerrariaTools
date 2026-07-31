using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

/// 分析编译中的普通私有方法，给出候选数量和没有外部引用的最终集合。
public sealed class UnreferencedMethodAnalysis
{
    private UnreferencedMethodAnalysis(
      int candidateMethodCount,
      IReadOnlySet<IMethodSymbol> unreferencedMethods)
    {
        CandidateMethodCount = candidateMethodCount;
        UnreferencedMethods = unreferencedMethods;
    }

    public int CandidateMethodCount { get; }

    public IReadOnlySet<IMethodSymbol> UnreferencedMethods { get; }

    public static UnreferencedMethodAnalysis Create(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var candidates = BuildCandidateMethodMap(compilation);
        var references = BuildMethodReferenceIndex(compilation, candidates);
        return new UnreferencedMethodAnalysis(
          candidates.Count,
          FindUnreferencedMethods(candidates, references));
    }

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> BuildCandidateMethodMap(
      Compilation compilation)
    {
        var candidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(
          SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
                    !IsCandidate(symbol))
                {
                    continue;
                }

                candidates[Canonicalize(symbol)] = method;
            }
        }

        return candidates;
    }

    private static MethodReferenceIndex BuildMethodReferenceIndex(
      Compilation compilation,
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var candidateCallees = CreateCandidateSetMap(candidates.Keys);
        var externallyReferencedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var referencedMethod = GetReferencedMethod(model, node);
                if (referencedMethod is null || !candidates.ContainsKey(referencedMethod))
                {
                    continue;
                }

                var caller = GetContainingCandidateMethod(model, node, candidates);
                if (caller is null)
                {
                    externallyReferencedMethods.Add(referencedMethod);
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(caller, referencedMethod))
                {
                    continue;
                }

                candidateCallees[caller].Add(referencedMethod);
            }
        }

        return new MethodReferenceIndex(
          candidateCallees,
          externallyReferencedMethods);
    }

    private static HashSet<IMethodSymbol> FindUnreferencedMethods(
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates,
      MethodReferenceIndex references)
    {
        var retainedMethods = FindExternallyReferencedClosure(candidates, references);
        return new HashSet<IMethodSymbol>(
          candidates.Keys.Where(candidate => !retainedMethods.Contains(candidate)),
          SymbolEqualityComparer.Default);
    }

    private static HashSet<IMethodSymbol> FindExternallyReferencedClosure(
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates,
      MethodReferenceIndex references)
    {
        var retained = new HashSet<IMethodSymbol>(
          references.ExternallyReferencedMethods,
          SymbolEqualityComparer.Default);
        var worklist = new Queue<IMethodSymbol>(retained);

        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            if (!candidates.ContainsKey(current))
            {
                continue;
            }

            foreach (var callee in references.CandidateCallees[current])
            {
                if (retained.Add(callee))
                {
                    worklist.Enqueue(callee);
                }
            }
        }

        return retained;
    }

    private static Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> CreateCandidateSetMap(
      IEnumerable<IMethodSymbol> candidates)
    {
        var map = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(
          SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            map[candidate] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        return map;
    }

    private static IMethodSymbol? GetContainingCandidateMethod(
      SemanticModel model,
      SyntaxNode node,
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var containingMethodSyntax = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethodSyntax is null ||
            model.GetDeclaredSymbol(containingMethodSyntax, CancellationToken.None)
              is not IMethodSymbol containingMethod)
        {
            return null;
        }

        var canonicalContainingMethod = Canonicalize(containingMethod);
        return candidates.ContainsKey(canonicalContainingMethod)
          ? canonicalContainingMethod
          : null;
    }

    private static IMethodSymbol? GetReferencedMethod(SemanticModel model, SyntaxNode node)
    {
        var symbol = model.GetSymbolInfo(node, CancellationToken.None).Symbol;
        return symbol is IMethodSymbol methodSymbol
          ? Canonicalize(methodSymbol)
          : null;
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
          method.DeclaredAccessibility == Accessibility.Private &&
          !method.IsOverride &&
          method.ExplicitInterfaceImplementations.Length == 0 &&
          !IsEntryPointShape(method);
    }

    private static bool IsEntryPointShape(IMethodSymbol method)
    {
        return string.Equals(method.Name, "Main", StringComparison.Ordinal) && method.IsStatic;
    }

    private static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }

    private sealed record MethodReferenceIndex(
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> CandidateCallees,
      IReadOnlySet<IMethodSymbol> ExternallyReferencedMethods);
}
