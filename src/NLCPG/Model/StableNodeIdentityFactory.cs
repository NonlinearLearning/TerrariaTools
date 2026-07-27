using NLCPG.Contracts;

namespace NLCPG.Model;

/// 为一个构建标识作用域分配稳定锚点中由字符串支持的部分。
public sealed class StableNodeIdentityFactory
{
    private readonly StringInterner _interner = new();

    // 返回节点现有锚点，或按当前互斥字符串表生成一个后备锚点。
    public StableNodeAnchor GetStableAnchor(NLCPGNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.StableAnchor ?? StableNodeAnchor.CreateFallback(node, _interner, MapStableNodeRole(node.Kind));
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
