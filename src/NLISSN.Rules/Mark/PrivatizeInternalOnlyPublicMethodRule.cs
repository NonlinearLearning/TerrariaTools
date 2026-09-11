using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// 命中只被同一类型内部调用的 public 方法，供后续改成 private。
[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.InternalOnlyPublicMethodPrivatization)]
public sealed class PrivatizeInternalOnlyPublicMethodRule : RuleDefinitionMark
{
    private static readonly RuleProducesContract InternalOnlyPublicMethodProduces =
      new(new[]
      {
        new RuleProducedSyntax(
          new[] { SyntaxKind.MethodDeclaration },
          InternalOnlyPublicMethodFacts.Marked)
      });


    public override string RuleId { get; } = "mark.privatize-internal-only-public-method";

    public override RuleProducesContract Produces => InternalOnlyPublicMethodProduces;


    public override string Name { get; } = "Match public methods only referenced inside their declaring type";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    // 仅标记只在声明类型内部被调用、且没有外部引用的 public 方法。
    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
        if (!IsEnabled(context))
        {
            yield break;
        }

        var candidates = BuildCandidateMap(context.SemanticModel.Compilation);
        var referenceFacts = BuildReferenceFacts(context.SemanticModel.Compilation, candidates.Keys);

        foreach (var method in context.EnumerateMethodDeclarations(root))
        {
            if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None)
                is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var canonicalMethod = Canonicalize(methodSymbol);
            if (!candidates.ContainsKey(canonicalMethod) ||
                !referenceFacts.TryGetValue(canonicalMethod, out var facts) ||
                facts.InternalReferenceCount == 0 ||
                facts.HasExternalReference)
            {
                continue;
            }

            yield return MarkRecordFactory.Create(
        RuleId,
        method,
        "Public method is referenced only from inside its declaring type.",
        factKind: InternalOnlyPublicMethodFacts.Marked);
        }
    }

    private static bool IsEnabled(IMarkRuleContext context)
    {
        return context.PrivatizeInternalOnlyPublicMethods;
    }

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> BuildCandidateMap(Compilation compilation)
    {
        var candidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(
          SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
                    !IsCandidate(methodSymbol) ||
                    !method.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword)))
                {
                    continue;
                }

                candidates[Canonicalize(methodSymbol)] = method;
            }
        }

        return candidates;
    }

    private static Dictionary<IMethodSymbol, ReferenceFacts> BuildReferenceFacts(Compilation compilation, IEnumerable<IMethodSymbol> candidates)
    {
        var candidateSet = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            candidateSet.Add(candidate);
        }

        var factsByMethod = new Dictionary<IMethodSymbol, ReferenceFacts>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidateSet)
        {
            factsByMethod[candidate] = new ReferenceFacts();
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (model.GetSymbolInfo(node, CancellationToken.None).Symbol is not IMethodSymbol referencedMethod)
                {
                    continue;
                }

                var canonicalReferencedMethod = Canonicalize(referencedMethod);
                if (!candidateSet.Contains(canonicalReferencedMethod))
                {
                    continue;
                }

                var containingMethodSyntax = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                var containingMethod = containingMethodSyntax is null
                  ? null
                  : model.GetDeclaredSymbol(containingMethodSyntax, CancellationToken.None) as IMethodSymbol;
                var facts = factsByMethod[canonicalReferencedMethod];
                if (containingMethod is not null &&
                    SymbolEqualityComparer.Default.Equals(
                      containingMethod.ContainingType,
                      canonicalReferencedMethod.ContainingType))
                {
                    facts.InternalReferenceCount++;
                }
                else
                {
                    facts.HasExternalReference = true;
                }
            }

            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation, CancellationToken.None).Symbol is not null ||
                    model.GetSymbolInfo(invocation, CancellationToken.None).CandidateSymbols.Length > 0 ||
                    !TryGetInvokedMethodName(invocation, out var methodName))
                {
                    continue;
                }

                MarkUnresolvedExternalReferences(
                  model,
                  invocation,
                  methodName,
                  candidateSet,
                  factsByMethod);
            }
        }

        return factsByMethod;
    }

    private static void MarkUnresolvedExternalReferences(
      SemanticModel model,
      InvocationExpressionSyntax invocation,
      string methodName,
      IReadOnlySet<IMethodSymbol> candidates,
      IReadOnlyDictionary<IMethodSymbol, ReferenceFacts> factsByMethod)
    {
        var containingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        var containingType = containingMethod is null
          ? null
          : model.GetDeclaredSymbol(containingMethod, CancellationToken.None)?.ContainingType;

        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) ||
                SymbolEqualityComparer.Default.Equals(containingType, candidate.ContainingType))
            {
                continue;
            }

            factsByMethod[candidate].HasExternalReference = true;
        }
    }

    private static bool TryGetInvokedMethodName(
      InvocationExpressionSyntax invocation,
      out string methodName)
    {
        methodName = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            _ => string.Empty
        };
        return methodName.Length > 0;
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
          method.DeclaredAccessibility == Accessibility.Public &&
          !method.IsAbstract &&
          !method.IsVirtual &&
          !method.IsOverride &&
          method.ExplicitInterfaceImplementations.Length == 0 &&
          !IsEntryPointShape(method);
    }

    private static bool IsEntryPointShape(IMethodSymbol method)
    {
        return string.Equals(method.Name, "Main", StringComparison.Ordinal) &&
          method.IsStatic;
    }

    private static IMethodSymbol Canonicalize(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }

    private sealed class ReferenceFacts
    {
        public int InternalReferenceCount { get; set; }

        public bool HasExternalReference { get; set; }
    }
}
