using NLCPG.Contracts;

namespace NLCPG.Model;

/// 为一个构建标识作用域分配稳定锚点中由字符串支持的部分。
public sealed class StableNodeIdentityFactory
{
    private readonly StringInterner _interner = new();

    // 返回节点现有锚点，或按节点字符串表标识生成一个后备锚点。
    public StableNodeAnchor GetStableAnchor(NLCPGNode node)
    {
        return node.StableAnchor ?? StableNodeAnchor.CreateFallback(node, MapStableNodeRole(node.Kind));
    }

    // 使用稳定身份专用的字符串表，避免 graph-owned ID 的分配顺序影响 NodeId。
    internal StableNodeAnchor GetStableAnchor(
      NLCPGNode node,
      StringInterner graphStringInterner,
      string? stableIdentityText = null)
    {
        ArgumentNullException.ThrowIfNull(graphStringInterner);
        if (node.StableAnchor is { } existing)
        {
            return existing;
        }

        var filePath = graphStringInterner.TryResolve(node.FilePathId, out var resolvedFilePath)
          ? resolvedFilePath
          : string.Empty;
        var extraKey = stableIdentityText ?? ResolveExtraKey(node, graphStringInterner);
        return new StableNodeAnchor(
          node.Kind,
          _interner.Intern(filePath),
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          MapStableNodeRole(node.Kind),
          0,
          _interner.Intern(extraKey));
    }

    internal bool TryResolveStableIdentityText(NLCPGNode node, out string? text)
    {
        if (node.StableAnchor is { } anchor && _interner.TryResolve(anchor.ExtraKeyId, out text))
        {
            return true;
        }

        text = null;
        return false;
    }

    private static string ResolveExtraKey(NLCPGNode node, StringInterner graphStringInterner)
    {
        if (node.FullNameId != 0 && graphStringInterner.TryResolve(node.FullNameId, out var fullName))
        {
            return fullName!;
        }

        if (node.SignatureId != 0 && graphStringInterner.TryResolve(node.SignatureId, out var signature))
        {
            return signature!;
        }

        if (node.NameId != 0 && graphStringInterner.TryResolve(node.NameId, out var name))
        {
            return name!;
        }

        return node.Kind.ToString();
    }

    private static StableNodeRole MapStableNodeRole(NLCPGNodeKind kind)
    {
        return kind switch
        {
            NLCPGNodeKind.SyntaxNode => StableNodeRole.SyntaxNode,
            NLCPGNodeKind.SyntaxToken => StableNodeRole.SyntaxToken,
            NLCPGNodeKind.Operation => StableNodeRole.Operation,
            NLCPGNodeKind.Reference => StableNodeRole.Reference,
            NLCPGNodeKind.TypeRef => StableNodeRole.TypeReference,
            NLCPGNodeKind.TypeDecl => StableNodeRole.TypeDeclaration,
            NLCPGNodeKind.Method => StableNodeRole.Method,
            NLCPGNodeKind.MethodParameter => StableNodeRole.MethodParameter,
            NLCPGNodeKind.MethodReturn => StableNodeRole.MethodReturn,
            NLCPGNodeKind.MethodEntry => StableNodeRole.MethodEntry,
            NLCPGNodeKind.MethodExit => StableNodeRole.MethodExit,
            NLCPGNodeKind.CallSite => StableNodeRole.CallSite,
            NLCPGNodeKind.MemberAccess => StableNodeRole.MemberAccess,
            NLCPGNodeKind.SymbolMethod or
            NLCPGNodeKind.SymbolParameter or
            NLCPGNodeKind.SymbolLocal or
            NLCPGNodeKind.SymbolField or
            NLCPGNodeKind.SymbolProperty or
            NLCPGNodeKind.SymbolType or
            NLCPGNodeKind.SymbolUnknown => StableNodeRole.Symbol,
            _ => StableNodeRole.None,
        };
    }
}
