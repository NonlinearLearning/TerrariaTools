using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// 命中未被调用的接口成员对应的源码实现方法。
public sealed class ClearUnusedInterfaceImplementationRule : RuleDefinitionMark
{
  private static readonly RuleSemanticTag UnusedInterfaceImplementationSemanticTag = new("UnusedInterfaceImplementation");

  private static readonly RuleProducesContract UnusedInterfaceImplementationProduces =
    RuleStructureContractFactories.CreateDeclarationHostProduces(
      UnusedInterfaceImplementationSemanticTag);

  public override string CapabilityId { get; } = "mark.clear-unused-interface-implementation";

    public override string RuleId { get; } = "CLR-UNUSED-IFACE-IMPL-MARK-001";

  public override RuleProducesContract Produces => UnusedInterfaceImplementationProduces;


  public override string Name { get; } = "Match unused interface implementation methods";

  public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
    new[] { SyntaxKind.MethodDeclaration };

  // 找出既实现接口成员、又没有任何接口侧或实现侧引用的方法声明。
  public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
  {
    if (!IsEnabled(context))
    {
      yield break;
    }

    var profile = context.SymbolUsageProfile;
    foreach (var method in context.EnumerateMethodDeclarations(root))
    {
      if (context.SemanticModel.GetDeclaredSymbol(method, CancellationToken.None)
          is not IMethodSymbol methodSymbol)
      {
        continue;
      }

      if (!TryGetImplementedInterfaceMethods(profile, methodSymbol, out var implementedInterfaceMembers))
      {
        continue;
      }

      var implementationReferences = profile.GetReferences(Canonicalize(methodSymbol));
      if (implementationReferences.Status != UsageProfileStatus.Complete)
      {
        continue;
      }

      if (implementationReferences.Facts.Count > 0 ||
          implementedInterfaceMembers.Any(interfaceMethod =>
            profile.GetReferences(interfaceMethod).Facts.Count > 0))
      {
        continue;
      }

      yield return MarkRecordFactory.Create(
        RuleId,
        method,
        "Interface implementation is not referenced through its interface member or implementation method.",
        semanticTag: UnusedInterfaceImplementationSemanticTag);
    }
  }

  private static bool IsEnabled(RuleContext context)
  {
    return context.TryGetOption("clear-unused-interface-implementations", out var value) &&
      !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
  }

  private static bool TryGetImplementedInterfaceMethods(
    SymbolUsageProfile profile,
    IMethodSymbol method,
    out IReadOnlyList<IMethodSymbol> implementedInterfaceMethods)
  {
    implementedInterfaceMethods = Array.Empty<IMethodSymbol>();
    if (method.ContainingType is null || method.MethodKind != MethodKind.Ordinary)
    {
      return false;
    }

    var relations = profile.GetTypeRelations(method.ContainingType);
    if (relations.Status != UsageProfileStatus.Complete)
    {
      return false;
    }

    var candidates = new List<IMethodSymbol>();
    foreach (var interfaceType in relations.Facts
      .Where(fact => fact.Kind == TypeRelationKind.Interface)
      .Select(fact => fact.RelatedSymbol)
      .OfType<INamedTypeSymbol>())
    {
      foreach (var member in interfaceType.GetMembers().OfType<IMethodSymbol>())
      {
        if (member.MethodKind == MethodKind.Ordinary &&
            method.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
            SymbolEqualityComparer.Default.Equals(Canonicalize(method), Canonicalize(implementation)))
        {
          candidates.Add(Canonicalize(member));
        }
      }
    }

    implementedInterfaceMethods = candidates;
    return candidates.Count > 0;
  }

  private static IMethodSymbol Canonicalize(IMethodSymbol method)
  {
    return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
  }
}
