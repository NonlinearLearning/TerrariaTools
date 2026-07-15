using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 保存图冻结后可供只读查询使用的确定性边索引。
/// </summary>
internal sealed class RoslynCpgGraphIndex
{
    private RoslynCpgGraphIndex(
        IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> outgoingByNodeId,
        IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> incomingByNodeId,
        IReadOnlyDictionary<(string NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> outgoingByNodeAndKind,
        IReadOnlyDictionary<(string NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> incomingByNodeAndKind,
        IReadOnlyDictionary<RoslynCpgEdgeKind, IReadOnlyList<RoslynCpgEdge>> edgesByKind,
        IReadOnlyDictionary<RoslynCpgNodeKind, IReadOnlyList<RoslynCpgNode>> nodesByKind,
        IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgNode>> nodesByFilePath,
        string snapshotVersion)
    {
        OutgoingByNodeId = outgoingByNodeId;
        IncomingByNodeId = incomingByNodeId;
        OutgoingByNodeAndKind = outgoingByNodeAndKind;
        IncomingByNodeAndKind = incomingByNodeAndKind;
        EdgesByKind = edgesByKind;
        NodesByKind = nodesByKind;
        NodesByFilePath = nodesByFilePath;
        SnapshotVersion = snapshotVersion;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> OutgoingByNodeId { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> IncomingByNodeId { get; }

    public IReadOnlyDictionary<(string NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> OutgoingByNodeAndKind { get; }

    public IReadOnlyDictionary<(string NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> IncomingByNodeAndKind { get; }

    public IReadOnlyDictionary<RoslynCpgEdgeKind, IReadOnlyList<RoslynCpgEdge>> EdgesByKind { get; }

    public IReadOnlyDictionary<RoslynCpgNodeKind, IReadOnlyList<RoslynCpgNode>> NodesByKind { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgNode>> NodesByFilePath { get; }

    public string SnapshotVersion { get; }

    public static RoslynCpgGraphIndex Create(
        IEnumerable<RoslynCpgNode> nodes,
        IEnumerable<RoslynCpgEdge> edges)
    {
        var orderedEdges = edges.OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Label, StringComparer.Ordinal)
            .ThenBy(edge => edge.ContextId, StringComparer.Ordinal)
            .ToArray();
        var orderedNodes = nodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
        var snapshotVersion = string.Join("|", orderedNodes.Select(node => node.Id)) + "#" +
            string.Join("|", orderedEdges.Select(edge => $"{edge.SourceId}>{edge.Kind}>{edge.TargetId}>{edge.Label}>{edge.ContextId}"));
        return new RoslynCpgGraphIndex(
            CreateAdjacency(orderedEdges, useOutgoingEdges: true),
            CreateAdjacency(orderedEdges, useOutgoingEdges: false),
            CreateKindAdjacency(orderedEdges, useOutgoingEdges: true),
            CreateKindAdjacency(orderedEdges, useOutgoingEdges: false),
            orderedEdges.GroupBy(edge => edge.Kind)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgEdge>)group.ToArray()),
            orderedNodes
                .GroupBy(node => node.Kind)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgNode>)group.ToArray()),
            orderedNodes.Where(node => !string.IsNullOrEmpty(node.FilePath) && node.SpanStart.HasValue && node.SpanEnd.HasValue)
                .GroupBy(node => node.FilePath!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<RoslynCpgNode>)group
                        .OrderBy(node => node.SpanStart)
                        .ThenBy(node => node.SpanEnd)
                        .ThenBy(node => node.Id, StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal),
            snapshotVersion);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> CreateAdjacency(
        IEnumerable<RoslynCpgEdge> edges,
        bool useOutgoingEdges)
    {
        return edges.GroupBy(edge => useOutgoingEdges ? edge.SourceId : edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgEdge>)group.ToArray(), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<(string NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> CreateKindAdjacency(
        IEnumerable<RoslynCpgEdge> edges,
        bool useOutgoingEdges)
    {
        return edges.GroupBy(
                edge => (useOutgoingEdges ? edge.SourceId : edge.TargetId, edge.Kind))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgEdge>)group.ToArray());
    }
}
