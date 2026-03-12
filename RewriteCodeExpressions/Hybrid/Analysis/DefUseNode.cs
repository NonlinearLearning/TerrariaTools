using Microsoft.CodeAnalysis;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

public enum DefUseNodeKind
{
    Definition,
    Use
}

public sealed class DefUseNode
{
    public DefUseNode(ISymbol symbol, SyntaxNode syntax, DefUseNodeKind kind)
    {
        Symbol = symbol;
        Syntax = syntax;
        Kind = kind;
    }

    public ISymbol Symbol { get; }
    public SyntaxNode Syntax { get; }
    public DefUseNodeKind Kind { get; }
    public string DisplayName => Symbol.ToDisplayString();
}

