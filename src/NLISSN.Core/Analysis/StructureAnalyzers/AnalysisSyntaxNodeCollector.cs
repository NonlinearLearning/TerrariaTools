using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis;

internal static class AnalysisSyntaxNodeCollector
{
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

    public static void AddIfNotNull(ICollection<SyntaxNode> nodes, SyntaxNode? node)
    {
        if (node is not null)
        {
            nodes.Add(node);
        }
    }
}
