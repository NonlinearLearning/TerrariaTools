using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NLCPG.Contracts;

namespace NLCPG.Model;

/// 保存图冻结后可供只读查询使用的确定性边索引。
internal sealed class NLCPGGraphIndex
{
    private sealed class EdgeIndexAccumulator
    {
        public Dictionary<NodeId, List<NLCPGEdge>> OutgoingByNodeId { get; } = new();

        public Dictionary<NodeId, List<NLCPGEdge>> IncomingByNodeId { get; } = new();

        public Dictionary<(NodeId NodeId, NLCPGEdgeKind Kind), List<NLCPGEdge>> OutgoingByNodeAndKind { get; } = new();

        public Dictionary<(NodeId NodeId, NLCPGEdgeKind Kind), List<NLCPGEdge>> IncomingByNodeAndKind { get; } = new();

        public Dictionary<NLCPGEdgeKind, List<NLCPGEdge>> EdgesByKind { get; } = new();
    }

    private NLCPGGraphIndex(IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> outgoingByNodeId, IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> incomingByNodeId, IReadOnlyDictionary<(NodeId NodeId, NLCPGEdgeKind Kind), IReadOnlyList<NLCPGEdge>> outgoingByNodeAndKind, IReadOnlyDictionary<(NodeId NodeId, NLCPGEdgeKind Kind), IReadOnlyList<NLCPGEdge>> incomingByNodeAndKind, IReadOnlyDictionary<NLCPGEdgeKind, IReadOnlyList<NLCPGEdge>> edgesByKind, IReadOnlyDictionary<NLCPGNodeKind, IReadOnlyList<NLCPGNode>> nodesByKind, IReadOnlyDictionary<string, IReadOnlyList<NLCPGNode>> nodesByFilePath, string snapshotVersion)
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

    public IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> OutgoingByNodeId { get; }

    public IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> IncomingByNodeId { get; }

    public IReadOnlyDictionary<(NodeId NodeId, NLCPGEdgeKind Kind), IReadOnlyList<NLCPGEdge>> OutgoingByNodeAndKind { get; }

    public IReadOnlyDictionary<(NodeId NodeId, NLCPGEdgeKind Kind), IReadOnlyList<NLCPGEdge>> IncomingByNodeAndKind { get; }

    public IReadOnlyDictionary<NLCPGEdgeKind, IReadOnlyList<NLCPGEdge>> EdgesByKind { get; }

    public IReadOnlyDictionary<NLCPGNodeKind, IReadOnlyList<NLCPGNode>> NodesByKind { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<NLCPGNode>> NodesByFilePath { get; }

    public string SnapshotVersion { get; }

    // 按确定性顺序冻结节点和边，并生成查询索引与快照版本。
    public static NLCPGGraphIndex Create(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges)
    {
        var orderedEdges = edges.OrderBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.TargetNodeId)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.ContextId?.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.CallSiteContext?.FilePath, StringComparer.Ordinal)
            .ThenBy(edge => edge.CallSiteContext?.SpanStart)
            .ThenBy(edge => edge.CallSiteContext?.SpanEnd)
            .ThenBy(edge => edge.CallSiteContext?.DisplayName, StringComparer.Ordinal)
            .ToArray();
        var orderedNodes = nodes.OrderBy(node => node.NodeId).ToArray();
        var snapshotVersion = CreateSnapshotVersion(orderedNodes, orderedEdges);
        var edgeIndexes = BuildEdgeIndexes(orderedEdges);
        var outgoingByNodeId = FreezeEdgeLists(edgeIndexes.OutgoingByNodeId);
        var incomingByNodeId = FreezeEdgeLists(edgeIndexes.IncomingByNodeId);
        var outgoingByNodeAndKind = FreezeEdgeLists(edgeIndexes.OutgoingByNodeAndKind);
        var incomingByNodeAndKind = FreezeEdgeLists(edgeIndexes.IncomingByNodeAndKind);
        var edgesByKind = FreezeEdgeLists(edgeIndexes.EdgesByKind);
        var nodesByKind = orderedNodes
            .GroupBy(node => node.Kind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NLCPGNode>)group
                    .OrderBy(node => node.NodeId)
                    .ToArray());
        var nodesByFilePath = orderedNodes
            .Where(node => !string.IsNullOrEmpty(node.FilePath) && node.SpanStart.HasValue && node.SpanEnd.HasValue)
            .GroupBy(node => node.FilePath!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NLCPGNode>)group
                    .OrderBy(node => node.SpanStart)
                    .ThenBy(node => node.SpanEnd)
                    .ThenBy(node => node.NodeId)
                    .ToArray(),
                StringComparer.Ordinal);
        return new NLCPGGraphIndex(
            outgoingByNodeId,
            incomingByNodeId,
            outgoingByNodeAndKind,
            incomingByNodeAndKind,
            edgesByKind,
            nodesByKind,
            nodesByFilePath,
            snapshotVersion);
    }

    private static string CreateSnapshotVersion(IReadOnlyList<NLCPGNode> orderedNodes, IReadOnlyList<NLCPGEdge> orderedEdges)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, orderedNodes.Count);
        foreach (var node in orderedNodes)
        {
            AppendUInt32(hash, node.NodeId?.Value ?? 0);
            AppendInt32(hash, (int)node.Kind);
            AppendUInt32(hash, node.StableAnchor?.FilePathId ?? 0);
            AppendInt32(hash, node.StableAnchor?.SpanStart ?? -1);
            AppendInt32(hash, node.StableAnchor?.SpanEnd ?? -1);
            AppendInt32(hash, (int)(node.StableAnchor?.Role ?? StableNodeRole.None));
            AppendInt32(hash, node.StableAnchor?.Ordinal ?? 0);
            AppendUInt32(hash, node.StableAnchor?.ExtraKeyId ?? 0);
        }

        AppendInt32(hash, orderedEdges.Count);
        foreach (var edge in orderedEdges)
        {
            AppendUInt32(hash, edge.SourceNodeId.Value);
            AppendInt32(hash, (int)edge.Kind);
            AppendUInt32(hash, edge.TargetNodeId.Value);
            AppendString(hash, edge.StructuredLabel?.StableKey);
            AppendString(hash, edge.ContextId?.Value);
            AppendString(hash, edge.CallSiteContext?.FilePath);
            AppendInt32(hash, edge.CallSiteContext?.SpanStart ?? -1);
            AppendInt32(hash, edge.CallSiteContext?.SpanEnd ?? -1);
            AppendString(hash, edge.CallSiteContext?.DisplayName);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static EdgeIndexAccumulator BuildEdgeIndexes(IReadOnlyList<NLCPGEdge> orderedEdges)
    {
        var accumulator = new EdgeIndexAccumulator();
        foreach (var edge in orderedEdges)
        {
            AddEdge(accumulator.OutgoingByNodeId, edge.SourceNodeId, edge);
            AddEdge(accumulator.IncomingByNodeId, edge.TargetNodeId, edge);
            AddEdge(accumulator.OutgoingByNodeAndKind, (edge.SourceNodeId, edge.Kind), edge);
            AddEdge(accumulator.IncomingByNodeAndKind, (edge.TargetNodeId, edge.Kind), edge);
            AddEdge(accumulator.EdgesByKind, edge.Kind, edge);
        }

        return accumulator;
    }

    private static IReadOnlyDictionary<TKey, IReadOnlyList<NLCPGEdge>> FreezeEdgeLists<TKey>(Dictionary<TKey, List<NLCPGEdge>> source)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, IReadOnlyList<NLCPGEdge>>(source.Count);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value.ToArray();
        }

        return result;
    }

    private static void AddEdge<TKey>(Dictionary<TKey, List<NLCPGEdge>> index, TKey key, NLCPGEdge edge)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var edges))
        {
            edges = new List<NLCPGEdge>();
            index[key] = edges;
        }

        edges.Add(edge);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendString(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            AppendInt32(hash, -1);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        AppendInt32(hash, byteCount);
        if (byteCount == 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
    }
}
