using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// Allocates the string-backed parts of stable anchors for one build identity scope.
/// </summary>
public sealed class StableNodeIdentityFactory
{
  private readonly StringInterner _interner = new();

  public StableNodeAnchor GetStableAnchor(RoslynCpgNode node)
  {
    ArgumentNullException.ThrowIfNull(node);
    return node.StableAnchor ?? StableNodeAnchor.CreateFallback(
      node,
      _interner,
      MapStableNodeRole(node.Kind));
  }

  private static StableNodeRole MapStableNodeRole(RoslynCpgNodeKind kind)
  {
    return kind switch
    {
      RoslynCpgNodeKind.SyntaxNode => StableNodeRole.SyntaxNode,
      RoslynCpgNodeKind.SyntaxToken => StableNodeRole.SyntaxToken,
      RoslynCpgNodeKind.Operation => StableNodeRole.Operation,
      RoslynCpgNodeKind.Reference => StableNodeRole.Reference,
      RoslynCpgNodeKind.TypeRef => StableNodeRole.TypeReference,
      RoslynCpgNodeKind.TypeDecl => StableNodeRole.TypeDeclaration,
      RoslynCpgNodeKind.Method => StableNodeRole.Method,
      RoslynCpgNodeKind.MethodParameter => StableNodeRole.MethodParameter,
      RoslynCpgNodeKind.MethodReturn => StableNodeRole.MethodReturn,
      RoslynCpgNodeKind.MethodEntry => StableNodeRole.MethodEntry,
      RoslynCpgNodeKind.MethodExit => StableNodeRole.MethodExit,
      RoslynCpgNodeKind.CallSite => StableNodeRole.CallSite,
      RoslynCpgNodeKind.MemberAccess => StableNodeRole.MemberAccess,
      RoslynCpgNodeKind.SymbolMethod or
      RoslynCpgNodeKind.SymbolParameter or
      RoslynCpgNodeKind.SymbolLocal or
      RoslynCpgNodeKind.SymbolField or
      RoslynCpgNodeKind.SymbolProperty or
      RoslynCpgNodeKind.SymbolType or
      RoslynCpgNodeKind.SymbolUnknown => StableNodeRole.Symbol,
      _ => StableNodeRole.None,
    };
  }
}
