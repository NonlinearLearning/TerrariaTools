using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis.MethodLinkage;

internal static class MethodLinkageGraphBuilder
{
    internal static MethodLinkageGraph Build(Compilation compilation)
    {
        var methods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var privateCandidates = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, CancellationToken.None) is not IMethodSymbol method)
                {
                    continue;
                }

                var canonicalMethod = Canonicalize(method);
                methods.Add(canonicalMethod);
                if (IsPrivateCandidate(canonicalMethod))
                {
                    privateCandidates.Add(canonicalMethod);
                }
            }
        }

        var edges = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(
          SymbolEqualityComparer.Default);
        foreach (var method in methods)
        {
            edges.Add(method, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        }
        var externalRoots = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                RecordReference(model, invocation.Expression, methods, privateCandidates, edges, externalRoots);
            }

            foreach (var methodGroup in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (methodGroup.Parent is InvocationExpressionSyntax invocation && invocation.Expression == methodGroup)
                {
                    continue;
                }

                RecordReference(model, methodGroup, methods, privateCandidates, edges, externalRoots);
            }
        }

        return new MethodLinkageGraph(
          compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
          FindEntryPoint(compilation, methods) is IMethodSymbol entryPoint
            ? Canonicalize(entryPoint)
            : null,
          methods,
          privateCandidates,
          edges,
          externalRoots);
    }

    private static void RecordReference(
      SemanticModel model,
      SyntaxNode reference,
      IReadOnlySet<IMethodSymbol> methods,
      IReadOnlySet<IMethodSymbol> privateCandidates,
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> edges,
      ISet<IMethodSymbol> externalRoots)
    {
        var symbolInfo = model.GetSymbolInfo(reference, CancellationToken.None);
        var target = symbolInfo.Symbol as IMethodSymbol;
        if (target is not null)
        {
            RecordResolvedReference(model, reference, Canonicalize(target), methods, privateCandidates, edges, externalRoots);
            return;
        }

        foreach (var candidate in symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Select(Canonicalize))
        {
            if (privateCandidates.Contains(candidate))
            {
                externalRoots.Add(candidate);
            }
        }
    }

    private static void RecordResolvedReference(
      SemanticModel model,
      SyntaxNode reference,
      IMethodSymbol target,
      IReadOnlySet<IMethodSymbol> methods,
      IReadOnlySet<IMethodSymbol> privateCandidates,
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> edges,
      ISet<IMethodSymbol> externalRoots)
    {
        if (!methods.Contains(target))
        {
            return;
        }

        if (IsNestedExecutableBoundary(reference))
        {
            if (privateCandidates.Contains(target))
            {
                externalRoots.Add(target);
            }

            return;
        }

        var containingDeclaration = reference.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        var caller = containingDeclaration is null
          ? null
          : model.GetDeclaredSymbol(containingDeclaration, CancellationToken.None) as IMethodSymbol;
        if (caller is null || !methods.Contains(Canonicalize(caller)))
        {
            if (privateCandidates.Contains(target))
            {
                externalRoots.Add(target);
            }

            return;
        }

        var canonicalCaller = Canonicalize(caller);
        edges[canonicalCaller].Add(target);
        if (!privateCandidates.Contains(canonicalCaller) && privateCandidates.Contains(target))
        {
            externalRoots.Add(target);
        }
    }

    private static bool IsNestedExecutableBoundary(SyntaxNode reference)
    {
        return reference.Ancestors().Any(ancestor =>
          ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
    }

    private static bool IsPrivateCandidate(IMethodSymbol method)
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

    private static IMethodSymbol? FindEntryPoint(
      Compilation compilation,
      IEnumerable<IMethodSymbol> methods)
    {
        var entryPoint = compilation.WithOptions(
            compilation.Options.WithOutputKind(OutputKind.ConsoleApplication))
          .GetEntryPoint(CancellationToken.None);
        if (entryPoint is not null &&
            methods.Contains(Canonicalize(entryPoint), SymbolEqualityComparer.Default))
        {
            return Canonicalize(entryPoint);
        }

        var entryCandidates = methods.Where(IsEntryPointShape).Take(2).ToArray();
        return entryCandidates.Length == 1 ? entryCandidates[0] : null;
    }

    internal static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }
}

internal sealed record MethodLinkageGraph(
  bool HasErrors,
  IMethodSymbol? EntryPoint,
  IReadOnlySet<IMethodSymbol> Methods,
  IReadOnlySet<IMethodSymbol> PrivateCandidates,
  IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> Edges,
  IReadOnlySet<IMethodSymbol> ExternalRoots);
