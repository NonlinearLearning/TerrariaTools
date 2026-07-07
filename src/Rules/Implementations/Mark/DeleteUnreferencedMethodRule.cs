using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Marking;

namespace Rules;

/// <summary>
/// 在项目级 Compilation 内查找没有外部引用的普通私有方法声明。
/// </summary>
public sealed class DeleteUnreferencedMethodRule : RuleDefinitionMark
{
  public override string RuleId { get; } = DeleteUnreferencedMethodRuleIds.MarkRuleId;

  public override string GroupKey { get; } = DeleteUnreferencedMethodRuleIds.GroupKey;

  public override string Name { get; } = "Match unreferenced private method declarations";

  public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (!IsEnabled(context))
    {
      yield break;
    }

    var unreferencedMethods = FindUnreferencedCandidateMethods(context);
    foreach (var method in RuleAnalysisHelpers.EnumerateMethodDeclarations(
               root,
               context.AnalysisContext))
    {
      if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None)
          is not IMethodSymbol methodSymbol)
      {
        continue;
      }

      if (!unreferencedMethods.Contains(Canonicalize(methodSymbol)))
      {
        continue;
      }

      yield return new MarkRecord(
        RuleId,
        method,
        null,
        CreateMethodGraphNode(methodSymbol, method),
        "Private method has no references from methods that remain in the project.",
        GroupKey);
    }
  }

  private static bool IsEnabled(RuleContext context)
  {
    return context.TryGetOption("delete-unreferenced-methods", out var value) &&
      !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
  }

  private static HashSet<IMethodSymbol> FindUnreferencedCandidateMethods(RuleContext context)
  {
    var compilation = context.SemanticModel.Compilation;
    var candidates = BuildCandidateMethodMap(compilation);
    var references = BuildMethodReferenceIndex(compilation, candidates);
    return FindUnreferencedMethodsByDeletionIteration(candidates, references);
  }

  private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> BuildCandidateMethodMap(Compilation compilation)
  {
    var candidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(
      SymbolEqualityComparer.Default);

    foreach (var tree in compilation.SyntaxTrees)
    {
      var model = compilation.GetSemanticModel(tree);
      foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
      {
        if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
            !IsDeletionCandidate(symbol))
        {
          continue;
        }

        candidates[Canonicalize(symbol)] = method;
      }
    }

    return candidates;
  }

  private static MethodReferenceIndex BuildMethodReferenceIndex(Compilation compilation, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
  {
    var incomingCandidateCallers = CreateCandidateSetMap(candidates.Keys);
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

        incomingCandidateCallers[referencedMethod].Add(caller);
        candidateCallees[caller].Add(referencedMethod);
      }
    }

    return new MethodReferenceIndex(
      incomingCandidateCallers,
      candidateCallees,
      externallyReferencedMethods);
  }

  private static HashSet<IMethodSymbol> FindUnreferencedMethodsByDeletionIteration(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
  {
    var deletedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
    var pendingScan = new HashSet<IMethodSymbol>(
      candidates.Keys,
      SymbolEqualityComparer.Default);

    while (pendingScan.Count > 0)
    {
      var methodsDeletedThisRound = new List<IMethodSymbol>();
      foreach (var candidate in pendingScan)
      {
        if (deletedMethods.Contains(candidate) ||
            HasRemainingReferences(candidate, deletedMethods, references))
        {
          continue;
        }

        deletedMethods.Add(candidate);
        methodsDeletedThisRound.Add(candidate);
      }

      pendingScan.Clear();
      foreach (var deletedMethod in methodsDeletedThisRound)
      {
        foreach (var callee in references.CandidateCallees[deletedMethod])
        {
          if (!deletedMethods.Contains(callee))
          {
            pendingScan.Add(callee);
          }
        }
      }
    }

    var retainedMethods = FindExternallyReferencedClosure(candidates, references);
    foreach (var candidate in candidates.Keys)
    {
      if (!retainedMethods.Contains(candidate))
      {
        deletedMethods.Add(candidate);
      }
    }

    return deletedMethods;
  }

  private static bool HasRemainingReferences(IMethodSymbol method, IReadOnlySet<IMethodSymbol> deletedMethods, MethodReferenceIndex references)
  {
    if (references.ExternallyReferencedMethods.Contains(method))
    {
      return true;
    }

    return references.IncomingCandidateCallers[method]
      .Any(caller => !deletedMethods.Contains(caller));
  }

  private static HashSet<IMethodSymbol> FindExternallyReferencedClosure(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
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

  private static Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> CreateCandidateSetMap(IEnumerable<IMethodSymbol> candidates)
  {
    var map = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(
      SymbolEqualityComparer.Default);
    foreach (var candidate in candidates)
    {
      map[candidate] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    return map;
  }

  private static IMethodSymbol? GetContainingCandidateMethod(SemanticModel model, SyntaxNode node, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
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
    if (symbol is null)
    {
      return null;
    }

    return symbol switch
    {
      IMethodSymbol methodSymbol => Canonicalize(methodSymbol),
      _ => null
    };
  }

  private static bool IsDeletionCandidate(IMethodSymbol method)
  {
    return method.MethodKind == MethodKind.Ordinary &&
      method.DeclaredAccessibility == Accessibility.Private &&
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

  private static RoslynCpgNode CreateMethodGraphNode(IMethodSymbol methodSymbol, MethodDeclarationSyntax method)
  {
    return new RoslynCpgNode(
      Id: $"fast-method:{method.SyntaxTree.FilePath}:{method.SpanStart}:{method.Span.End}",
      Kind: RoslynCpgNodeKind.Method,
      DisplayKind: nameof(RoslynCpgNodeKind.Method),
      Name: methodSymbol.Name,
      FullName: methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
      Signature: methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
      FilePath: method.SyntaxTree.FilePath,
      SpanStart: method.SpanStart,
      SpanEnd: method.Span.End,
      Text: method.ToString());
  }

  private sealed record MethodReferenceIndex(
    IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> IncomingCandidateCallers,
    IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> CandidateCallees,
    IReadOnlySet<IMethodSymbol> ExternallyReferencedMethods);
}
