using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// 收集结构视图节点和边，并尽量复用主图中已存在的 CPG 节点。
/// </summary>
internal sealed class RoslynCpgStructureViewWriter
{
    private readonly CpgAnalysisContext _context;
    private readonly Dictionary<string, RoslynCpgNode> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<RoslynCpgEdge> _edges = new();

    /// <summary>
    /// 使用当前分析上下文创建一个局部结构视图 writer。
    /// </summary>
    public RoslynCpgStructureViewWriter(CpgAnalysisContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 为语法节点获取一个视图节点，优先绑定主图中同跨度的现有节点。
    /// </summary>
    public RoslynCpgNode GetOrCreateNode(SyntaxNode syntaxNode, RoslynCpgNodeKind kind, string role)
    {
        var existingNode = ResolvePrimaryGraphNode(syntaxNode);
        if (existingNode is not null)
        {
            _nodes.TryAdd(existingNode.Id, existingNode);
            return existingNode;
        }

        var node = new RoslynCpgNode(
            Id: $"structure:{BuildNodeKey(syntaxNode)}:{role}",
            Kind: kind,
            DisplayKind: syntaxNode.Kind().ToString(),
            Name: role,
            FullName: BuildNodeKey(syntaxNode),
            FilePath: syntaxNode.SyntaxTree.FilePath,
            SpanStart: syntaxNode.SpanStart,
            SpanEnd: syntaxNode.Span.End,
            Text: syntaxNode.ToString());
        _nodes.TryAdd(node.Id, node);
        return node;
    }

    /// <summary>
    /// 向当前结构视图追加一条边，并确保端点节点已被纳入视图。
    /// </summary>
    public void AddEdge(
        RoslynCpgNode source,
        RoslynCpgNode target,
        RoslynCpgEdgeKind kind,
        string? label)
    {
        _nodes.TryAdd(source.Id, source);
        _nodes.TryAdd(target.Id, target);
        _edges.Add(new RoslynCpgEdge(source.Id, target.Id, kind, label));
    }

    /// <summary>
    /// 按稳定顺序生成最终结构视图。
    /// </summary>
    public RoslynCpgStructureView Build(RoslynCpgNode root)
    {
        return new RoslynCpgStructureView(
            root,
            _nodes.Values
                .OrderBy(node => node.SpanStart ?? int.MaxValue)
                .ThenBy(node => node.SpanEnd ?? int.MaxValue)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToList(),
            _edges
                .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(edge => edge.Label, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// 从主图中解析与当前语法节点最匹配的已有 CPG 节点。
    /// </summary>
    private RoslynCpgNode? ResolvePrimaryGraphNode(SyntaxNode syntaxNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return _context.Graph.Nodes
            .Where(node =>
                !node.IsImplicit &&
                string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
                node.SpanStart == syntaxNode.SpanStart &&
                node.SpanEnd == syntaxNode.Span.End)
                .OrderBy(GetBindingPriority)
            .FirstOrDefault();
    }

    /// <summary>
    /// 当同一语法跨度对应多个节点时，定义复用顺序。
    /// </summary>
    private static int GetBindingPriority(RoslynCpgNode node)
    {
        return node.Kind switch
        {
            RoslynCpgNodeKind.Method => 0,
            RoslynCpgNodeKind.MethodParameter => 1,
            RoslynCpgNodeKind.CallSite => 2,
            RoslynCpgNodeKind.MemberAccess => 3,
            RoslynCpgNodeKind.Reference => 4,
            RoslynCpgNodeKind.Operation => 5,
            RoslynCpgNodeKind.OpInvocation => 6,
            RoslynCpgNodeKind.OpBinary => 7,
            RoslynCpgNodeKind.OpAssignment => 8,
            RoslynCpgNodeKind.SyntaxNode => 9,
            _ => 10
        };
    }

    /// <summary>
    /// 为视图节点生成稳定键，避免仅按文本去重。
    /// </summary>
    private static string BuildNodeKey(SyntaxNode node)
    {
        return $"{node.SyntaxTree?.FilePath}|{node.Span.Start}|{node.Span.End}|{node.RawKind}";
    }
}
