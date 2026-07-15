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
        IReadOnlyDictionary<RoslynCpgEdgeKind, IReadOnlyList<RoslynCpgEdge>> edgesByKind)
    {
        OutgoingByNodeId = outgoingByNodeId;
        IncomingByNodeId = incomingByNodeId;
        EdgesByKind = edgesByKind;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> OutgoingByNodeId { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> IncomingByNodeId { get; }

    public IReadOnlyDictionary<RoslynCpgEdgeKind, IReadOnlyList<RoslynCpgEdge>> EdgesByKind { get; }

    public static RoslynCpgGraphIndex Create(IEnumerable<RoslynCpgEdge> edges)
    {
        var orderedEdges = edges.OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Label, StringComparer.Ordinal)
            .ToArray();
        return new RoslynCpgGraphIndex(
            CreateAdjacency(orderedEdges, useOutgoingEdges: true),
            CreateAdjacency(orderedEdges, useOutgoingEdges: false),
            orderedEdges.GroupBy(edge => edge.Kind)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgEdge>)group.ToArray()));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> CreateAdjacency(
        IEnumerable<RoslynCpgEdge> edges,
        bool useOutgoingEdges)
    {
        return edges.GroupBy(edge => useOutgoingEdges ? edge.SourceId : edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RoslynCpgEdge>)group.ToArray(), StringComparer.Ordinal);
    }
}
