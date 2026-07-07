using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;

namespace Rules;

/// <summary>
/// 命中未被调用的接口成员对应的源码实现方法。
/// </summary>
public sealed class ClearUnusedInterfaceImplementationRule : RuleDefinitionMark
{
  public override string RuleId { get; } = ClearUnusedInterfaceImplementationRuleIds.MarkRuleId;

  public override string GroupKey { get; } = ClearUnusedInterfaceImplementationRuleIds.GroupKey;

  public override string Name { get; } = "Match unused interface implementation methods";

  public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (!IsEnabled(context))
    {
      yield break;
    }

    var compilation = context.SemanticModel.Compilation;
    var implementations = BuildInterfaceImplementations(compilation);
    if (implementations.Count == 0)
    {
      yield break;
    }

    var referencedMethods = FindReferencedMethods(compilation);
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
      if (!implementations.TryGetValue(canonicalMethod, out var implementedInterfaceMembers))
      {
        continue;
      }

      if (referencedMethods.Contains(canonicalMethod) ||
          implementedInterfaceMembers.Any(referencedMethods.Contains))
      {
        continue;
      }

      yield return RuleAnalysisHelpers.CreateMark(
        RuleId,
        method,
        "Interface implementation is not referenced through its interface member or implementation method.");
    }
  }

  private static bool IsEnabled(RuleContext context)
  {
    return context.TryGetOption("clear-unused-interface-implementations", out var value) &&
      !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
  }

  private static Dictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>> BuildInterfaceImplementations(Compilation compilation)
  {
    var implementations = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(
      SymbolEqualityComparer.Default);

    foreach (var tree in compilation.SyntaxTrees)
    {
      var model = compilation.GetSemanticModel(tree);
      foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
      {
        if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol methodSymbol ||
            methodSymbol.ContainingType is null ||
            methodSymbol.MethodKind != MethodKind.Ordinary)
        {
          continue;
        }

        var canonicalMethod = Canonicalize(methodSymbol);
        foreach (var interfaceMethod in EnumerateInterfaceMethods(methodSymbol.ContainingType))
        {
          var implementation = methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMethod);
          if (implementation is not IMethodSymbol implementationMethod ||
              !SymbolEqualityComparer.Default.Equals(canonicalMethod, Canonicalize(implementationMethod)))
          {
            continue;
          }

          if (!implementations.TryGetValue(canonicalMethod, out var interfaceMembers))
          {
            interfaceMembers = new List<IMethodSymbol>();
            implementations[canonicalMethod] = interfaceMembers;
          }

          interfaceMembers.Add(Canonicalize(interfaceMethod));
        }
      }
    }

    var result = new Dictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>>(
      SymbolEqualityComparer.Default);
    foreach (var (method, interfaceMembers) in implementations)
    {
      result[method] = interfaceMembers;
    }

    return result;
  }

  private static IEnumerable<IMethodSymbol> EnumerateInterfaceMethods(INamedTypeSymbol type)
  {
    foreach (var interfaceType in type.AllInterfaces)
    {
      foreach (var member in interfaceType.GetMembers().OfType<IMethodSymbol>())
      {
        if (member.MethodKind == MethodKind.Ordinary)
        {
          yield return member;
        }
      }
    }
  }

  private static HashSet<IMethodSymbol> FindReferencedMethods(Compilation compilation)
  {
    var referencedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

    foreach (var tree in compilation.SyntaxTrees)
    {
      var model = compilation.GetSemanticModel(tree);
      foreach (var node in tree.GetRoot().DescendantNodes())
      {
        if (model.GetSymbolInfo(node, CancellationToken.None).Symbol is IMethodSymbol methodSymbol)
        {
          referencedMethods.Add(Canonicalize(methodSymbol));
        }
      }
    }

    return referencedMethods;
  }

  private static IMethodSymbol Canonicalize(IMethodSymbol method)
  {
    return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
  }
}
