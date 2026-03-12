using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// 基于词法嵌套的作用域实现。
/// </summary>
public sealed class LexicalScope : IScope
{
    private readonly HashSet<string> _symbols = new(StringComparer.Ordinal);

    public LexicalScope(IScope? parent)
    {
        Parent = parent;
    }

    public IScope? Parent { get; }

    public bool IsDefined(string symbolName)
    {
        if (_symbols.Contains(symbolName))
        {
            return true;
        }

        return Parent?.IsDefined(symbolName) == true;
    }

    public void Declare(string symbolName)
    {
        if (!string.IsNullOrWhiteSpace(symbolName))
        {
            _symbols.Add(symbolName);
        }
    }
}
