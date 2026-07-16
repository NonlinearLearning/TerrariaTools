using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 保存图冻结后可供只读查询使用的确定性边索引。
/// </summary>
internal sealed class RoslynCpgGraphIndex
{
    internal sealed record BuildResult(
        RoslynCpgGraphIndex Index,
        RoslynCpgFreezeTelemetry Telemetry);

    private sealed class EdgeIndexAccumulator
    {
        public Dictionary<NodeId, List<RoslynCpgEdge>> OutgoingByNodeId { get; } = new();

        public Dictionary<NodeId, List<RoslynCpgEdge>> IncomingByNodeId { get; } = new();

        public Dictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), List<RoslynCpgEdge>> OutgoingByNodeAndKind { get; } = new();

        public Dictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), List<RoslynCpgEdge>> IncomingByNodeAndKind { get; } = new();

        public Dictionary<RoslynCpgEdgeKind, List<RoslynCpgEdge>> EdgesByKind { get; } = new();
    }

    private RoslynCpgGraphIndex(
        IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> outgoingByNodeId,
        IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> incomingByNodeId,
        IReadOnlyDictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> outgoingByNodeAndKind,
        IReadOnlyDictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> incomingByNodeAndKind,
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

    public IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> OutgoingByNodeId { get; }

    public IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> IncomingByNodeId { get; }

    public IReadOnlyDictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> OutgoingByNodeAndKind { get; }

    public IReadOnlyDictionary<(NodeId NodeId, RoslynCpgEdgeKind Kind), IReadOnlyList<RoslynCpgEdge>> IncomingByNodeAndKind { get; }

    public IReadOnlyDictionary<RoslynCpgEdgeKind, IReadOnlyList<RoslynCpgEdge>> EdgesByKind { get; }

    public IReadOnlyDictionary<RoslynCpgNodeKind, IReadOnlyList<RoslynCpgNode>> NodesByKind { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgNode>> NodesByFilePath { get; }

    public string SnapshotVersion { get; }

    public static BuildResult Create(
        IEnumerable<RoslynCpgNode> nodes,
        IEnumerable<RoslynCpgEdge> edges)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var orderEdgesStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
        orderEdgesStopwatch.Stop();
        var orderNodesStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var orderedNodes = nodes.OrderBy(node => node.NodeId).ToArray();
        orderNodesStopwatch.Stop();
        var snapshotHashStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var snapshotVersion = CreateSnapshotVersion(orderedNodes, orderedEdges);
        snapshotHashStopwatch.Stop();
        var populateEdgeBucketsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var edgeIndexes = BuildEdgeIndexes(orderedEdges);
        populateEdgeBucketsStopwatch.Stop();
        var adjacencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outgoingByNodeId = FreezeEdgeLists(edgeIndexes.OutgoingByNodeId);
        var incomingByNodeId = FreezeEdgeLists(edgeIndexes.IncomingByNodeId);
        adjacencyStopwatch.Stop();
        var kindAdjacencyStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outgoingByNodeAndKind = FreezeEdgeLists(edgeIndexes.OutgoingByNodeAndKind);
        var incomingByNodeAndKind = FreezeEdgeLists(edgeIndexes.IncomingByNodeAndKind);
        kindAdjacencyStopwatch.Stop();
        var edgeKindIndexStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var edgesByKind = FreezeEdgeLists(edgeIndexes.EdgesByKind);
        edgeKindIndexStopwatch.Stop();
        var nodeKindIndexStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var nodesByKind = orderedNodes
            .GroupBy(node => node.Kind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoslynCpgNode>)group
                    .OrderBy(node => node.NodeId)
                    .ToArray());
        nodeKindIndexStopwatch.Stop();
        var filePathIndexStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var nodesByFilePath = orderedNodes
            .Where(node => !string.IsNullOrEmpty(node.FilePath) && node.SpanStart.HasValue && node.SpanEnd.HasValue)
            .GroupBy(node => node.FilePath!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoslynCpgNode>)group
                    .OrderBy(node => node.SpanStart)
                    .ThenBy(node => node.SpanEnd)
                    .ThenBy(node => node.NodeId)
                    .ToArray(),
                StringComparer.Ordinal);
        filePathIndexStopwatch.Stop();
        totalStopwatch.Stop();
        var index = new RoslynCpgGraphIndex(
            outgoingByNodeId,
            incomingByNodeId,
            outgoingByNodeAndKind,
            incomingByNodeAndKind,
            edgesByKind,
            nodesByKind,
            nodesByFilePath,
            snapshotVersion);
        var telemetry = new RoslynCpgFreezeTelemetry(
            TotalElapsedMilliseconds: totalStopwatch.ElapsedMilliseconds,
            AssignDeterministicNodeIdsElapsedMilliseconds: 0,
            CreateAnchorsElapsedMilliseconds: 0,
            CreateNodeIdTableElapsedMilliseconds: 0,
            RemapNodesElapsedMilliseconds: 0,
            RemapEdgesElapsedMilliseconds: 0,
            BuildQueryIndexElapsedMilliseconds: totalStopwatch.ElapsedMilliseconds,
            PopulateEdgeIndexBucketsElapsedMilliseconds: populateEdgeBucketsStopwatch.ElapsedMilliseconds,
            OrderEdgesElapsedMilliseconds: orderEdgesStopwatch.ElapsedMilliseconds,
            OrderNodesElapsedMilliseconds: orderNodesStopwatch.ElapsedMilliseconds,
            SnapshotHashElapsedMilliseconds: snapshotHashStopwatch.ElapsedMilliseconds,
            BuildAdjacencyElapsedMilliseconds: adjacencyStopwatch.ElapsedMilliseconds,
            BuildKindAdjacencyElapsedMilliseconds: kindAdjacencyStopwatch.ElapsedMilliseconds,
            BuildEdgeKindIndexElapsedMilliseconds: edgeKindIndexStopwatch.ElapsedMilliseconds,
            BuildNodeKindIndexElapsedMilliseconds: nodeKindIndexStopwatch.ElapsedMilliseconds,
            BuildFilePathIndexElapsedMilliseconds: filePathIndexStopwatch.ElapsedMilliseconds,
            NodeCount: orderedNodes.Length,
            EdgeCount: orderedEdges.Length,
            DistinctAnchorCount: 0);
        return new BuildResult(index, telemetry);
    }

    private static string CreateSnapshotVersion(
        IReadOnlyList<RoslynCpgNode> orderedNodes,
        IReadOnlyList<RoslynCpgEdge> orderedEdges)
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

    private static EdgeIndexAccumulator BuildEdgeIndexes(IReadOnlyList<RoslynCpgEdge> orderedEdges)
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

    private static IReadOnlyDictionary<TKey, IReadOnlyList<RoslynCpgEdge>> FreezeEdgeLists<TKey>(
        Dictionary<TKey, List<RoslynCpgEdge>> source)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, IReadOnlyList<RoslynCpgEdge>>(source.Count);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value.ToArray();
        }

        return result;
    }

    private static void AddEdge<TKey>(
        Dictionary<TKey, List<RoslynCpgEdge>> index,
        TKey key,
        RoslynCpgEdge edge)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var edges))
        {
            edges = new List<RoslynCpgEdge>();
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
