using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// Pass 1 产出：待重写节点计划。
/// </summary>
public sealed class RewritePlan
{
    private readonly List<RewritePlanItem> _items = new();

    public IReadOnlyList<RewritePlanItem> Items => _items;

    public void Add(SyntaxNode node, IRule rule)
    {
        _items.Add(new RewritePlanItem(node, rule));
    }
}

/// <summary>
/// 待重写节点及其匹配规则。
/// </summary>
public sealed record RewritePlanItem(SyntaxNode Node, IRule Rule);
