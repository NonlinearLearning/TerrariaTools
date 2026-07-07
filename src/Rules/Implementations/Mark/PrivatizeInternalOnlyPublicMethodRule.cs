using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;

namespace Rules;

/// <summary>
/// 命中只被同一类型内部调用的 public 方法，供后续改成 private。
/// </summary>
public sealed class PrivatizeInternalOnlyPublicMethodRule : RuleDefinitionMark
{
  public override string RuleId { get; } = PrivatizeInternalOnlyPublicMethodRuleIds.MarkRuleId;

  public override string GroupKey { get; } = PrivatizeInternalOnlyPublicMethodRuleIds.GroupKey;

  public override string Name { get; } = "Match public methods only referenced inside their declaring type";

  public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (!IsEnabled(context))
    {
      yield break;
    }

    var candidates = BuildCandidateMap(context.SemanticModel.Compilation);
    var referenceFacts = BuildReferenceFacts(context.SemanticModel.Compilation, candidates.Keys);

    foreach (var method in RuleAnalysisHelpers.EnumerateMethodDeclarations(
               root,
               context.AnalysisContext))
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

      yield return RuleAnalysisHelpers.CreateMark(
        RuleId,
        method,
        "Public method is referenced only from inside its declaring type.");
    }
  }

  private static bool IsEnabled(RuleContext context)
  {
    return context.TryGetOption("privatize-internal-only-public-methods", out var value) &&
      !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
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
    }

    return factsByMethod;
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
