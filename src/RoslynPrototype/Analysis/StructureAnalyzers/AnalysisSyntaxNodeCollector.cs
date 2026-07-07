using Microsoft.CodeAnalysis;

namespace RoslynPrototype.Analysis;

internal static class AnalysisSyntaxNodeCollector
{
    /// <summary>
    /// 按源码位置整理一个结构分析器命中的语法节点。
    /// </summary>
    public static IReadOnlyList<SyntaxNode> BuildAffectedSyntaxTree(SyntaxNode root, IEnumerable<SyntaxNode?> nodes)
    {
        return nodes
            .Where(node => node is not null)
            .Select(node => node!)
            .Where(node => root.Span.Contains(node.Span))
            .Distinct()
            .OrderBy(node => node.SpanStart)
            .ThenBy(node => node.Span.Length)
            .ToList();
    }

    /// <summary>
    /// 添加可选语法节点，避免每个分析器重复空值判断。
    /// </summary>
    public static void AddIfNotNull(ICollection<SyntaxNode> nodes, SyntaxNode? node)
    {
        if (node is not null)
        {
            nodes.Add(node);
        }
    }
}
