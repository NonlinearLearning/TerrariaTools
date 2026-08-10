using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NLCPG.Contracts;

namespace NLCPG.Model;

/// 保存图冻结后可供只读查询使用的确定性边索引。
internal sealed class NLCPGGraphIndex
{
    private sealed class CsrEdgeTable
    {
        internal CsrEdgeTable(NLCPGEdge[] edges, int[] offsets, Dictionary<NodeId, int> nodeOrdinals, int kindWidth)
        {
            Edges = edges;
            Offsets = offsets;
            NodeOrdinals = nodeOrdinals;
            KindWidth = kindWidth;
        }

        internal NLCPGEdge[] Edges { get; }

        internal int[] Offsets { get; }

        internal Dictionary<NodeId, int> NodeOrdinals { get; }

        internal int KindWidth { get; }

        internal IReadOnlyList<NLCPGEdge> Get(NodeId nodeId)
        {
            return TryGetBucket(nodeId, kind: null, out var offset, out var count)
                ? new ArraySegment<NLCPGEdge>(Edges, offset, count)
                : Array.Empty<NLCPGEdge>();
        }

        internal IReadOnlyList<NLCPGEdge> Get(NodeId nodeId, NLCPGEdgeKind kind)
        {
            return TryGetBucket(nodeId, kind, out var offset, out var count)
                ? new ArraySegment<NLCPGEdge>(Edges, offset, count)
                : Array.Empty<NLCPGEdge>();
        }

        private bool TryGetBucket(NodeId nodeId, NLCPGEdgeKind? kind, out int offset, out int count)
        {
            if (!NodeOrdinals.TryGetValue(nodeId, out var nodeOrdinal))
            {
                offset = 0;
                count = 0;
                return false;
            }

            var bucketOrdinal = kind.HasValue
                ? checked((nodeOrdinal * KindWidth) + (int)kind.Value)
                : nodeOrdinal;
            if ((uint)bucketOrdinal >= (uint)(Offsets.Length - 1))
            {
                offset = 0;
                count = 0;
                return false;
            }

            offset = Offsets[bucketOrdinal];
            count = Offsets[bucketOrdinal + 1] - offset;
            return count > 0;
        }
    }

    private NLCPGGraphIndex(
      IReadOnlyList<NLCPGNode> orderedNodes,
      IReadOnlyList<NLCPGEdge> orderedEdges,
      Dictionary<NodeId, int> nodeOrdinals,
      CsrEdgeTable outgoing,
      CsrEdgeTable incoming,
      CsrEdgeTable outgoingByKind,
      CsrEdgeTable incomingByKind,
      NLCPGEdge[] edgesByKind,
      int[] edgesByKindOffsets,
      IReadOnlyDictionary<NLCPGNodeKind, IReadOnlyList<NLCPGNode>> nodesByKind,
      IReadOnlyDictionary<string, IReadOnlyList<NLCPGNode>> nodesByFilePath,
      string snapshotVersion)
    {
        OrderedNodes = orderedNodes;
        OrderedEdges = orderedEdges;
        NodeOrdinals = nodeOrdinals;
        Outgoing = outgoing;
        Incoming = incoming;
        OutgoingByKind = outgoingByKind;
        IncomingByKind = incomingByKind;
        EdgesByKindBuffer = edgesByKind;
        EdgesByKindOffsets = edgesByKindOffsets;
        NodesByKind = nodesByKind;
        NodesByFilePath = nodesByFilePath;
        SnapshotVersion = snapshotVersion;
    }

    internal IReadOnlyList<NLCPGNode> OrderedNodes { get; }

    internal IReadOnlyList<NLCPGEdge> OrderedEdges { get; }

    private Dictionary<NodeId, int> NodeOrdinals { get; }

    private CsrEdgeTable Outgoing { get; }

    private CsrEdgeTable Incoming { get; }

    private CsrEdgeTable OutgoingByKind { get; }

    private CsrEdgeTable IncomingByKind { get; }

    private NLCPGEdge[] EdgesByKindBuffer { get; }

    private int[] EdgesByKindOffsets { get; }

    internal IReadOnlyDictionary<NLCPGNodeKind, IReadOnlyList<NLCPGNode>> NodesByKind { get; }

    internal IReadOnlyDictionary<string, IReadOnlyList<NLCPGNode>> NodesByFilePath { get; }

    internal string SnapshotVersion { get; }

    // 按确定性顺序冻结节点和边，并生成连续数组查询索引与快照版本。
    internal static NLCPGGraphIndex Create(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges)
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
        var nodeOrdinals = orderedNodes
            .Select((node, ordinal) => (node.NodeId!.Value, ordinal))
            .ToDictionary(entry => entry.Value, entry => entry.ordinal);
        var kindWidth = Enum.GetValues<NLCPGEdgeKind>().Select(kind => (int)kind).DefaultIfEmpty(0).Max() + 1;
        var snapshotVersion = CreateSnapshotVersion(orderedNodes, orderedEdges);
        var outgoing = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.SourceNodeId, kindWidth, groupByKind: false);
        var incoming = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.TargetNodeId, kindWidth, groupByKind: false);
        var outgoingByKind = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.SourceNodeId, kindWidth, groupByKind: true);
        var incomingByKind = BuildCsr(orderedEdges, nodeOrdinals, edge => edge.TargetNodeId, kindWidth, groupByKind: true);
        var (edgesByKind, edgesByKindOffsets) = BuildKindBuffer(orderedEdges, kindWidth);
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
            orderedNodes,
            orderedEdges,
            nodeOrdinals,
            outgoing,
            incoming,
            outgoingByKind,
            incomingByKind,
            edgesByKind,
            edgesByKindOffsets,
            nodesByKind,
            nodesByFilePath,
            snapshotVersion);
    }

    internal IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId)
    {
        return Outgoing.Get(nodeId);
    }

    internal IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId)
    {
        return Incoming.Get(nodeId);
    }

    internal IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        return OutgoingByKind.Get(nodeId, kind);
    }

    internal IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        return IncomingByKind.Get(nodeId, kind);
    }

    internal IReadOnlyList<NLCPGEdge> GetEdges(NLCPGEdgeKind kind)
    {
        var kindOrdinal = (int)kind;
        if ((uint)kindOrdinal >= (uint)(EdgesByKindOffsets.Length - 1))
        {
            return Array.Empty<NLCPGEdge>();
        }

        var offset = EdgesByKindOffsets[kindOrdinal];
        var count = EdgesByKindOffsets[kindOrdinal + 1] - offset;
        return count == 0
            ? Array.Empty<NLCPGEdge>()
            : new ArraySegment<NLCPGEdge>(EdgesByKindBuffer, offset, count);
    }

    internal bool TryGetNodesByFilePath(string filePath, out IReadOnlyList<NLCPGNode> nodes)
    {
        return NodesByFilePath.TryGetValue(filePath, out nodes!);
    }

    private static CsrEdgeTable BuildCsr(
      IReadOnlyList<NLCPGEdge> orderedEdges,
      Dictionary<NodeId, int> nodeOrdinals,
      Func<NLCPGEdge, NodeId> endpointSelector,
      int kindWidth,
      bool groupByKind)
    {
        var bucketCount = groupByKind
            ? checked(nodeOrdinals.Count * kindWidth)
            : nodeOrdinals.Count;
        var offsets = new int[bucketCount + 1];
        foreach (var edge in orderedEdges)
        {
            var endpointOrdinal = nodeOrdinals[endpointSelector(edge)];
            var bucketOrdinal = groupByKind
                ? checked((endpointOrdinal * kindWidth) + (int)edge.Kind)
                : endpointOrdinal;
            offsets[bucketOrdinal + 1] += 1;
        }

        for (var index = 1; index < offsets.Length; index += 1)
        {
            offsets[index] += offsets[index - 1];
        }

        var positions = offsets[..^1].ToArray();
        var adjacency = new NLCPGEdge[orderedEdges.Count];
        foreach (var edge in orderedEdges)
        {
            var endpointOrdinal = nodeOrdinals[endpointSelector(edge)];
            var bucketOrdinal = groupByKind
                ? checked((endpointOrdinal * kindWidth) + (int)edge.Kind)
                : endpointOrdinal;
            adjacency[positions[bucketOrdinal]] = edge;
            positions[bucketOrdinal] += 1;
        }

        return new CsrEdgeTable(adjacency, offsets, nodeOrdinals, groupByKind ? kindWidth : 1);
    }

    private static (NLCPGEdge[] Edges, int[] Offsets) BuildKindBuffer(IReadOnlyList<NLCPGEdge> orderedEdges, int kindWidth)
    {
        var offsets = new int[kindWidth + 1];
        foreach (var edge in orderedEdges)
        {
            offsets[(int)edge.Kind + 1] += 1;
        }

        for (var index = 1; index < offsets.Length; index += 1)
        {
            offsets[index] += offsets[index - 1];
        }

        var positions = offsets[..^1].ToArray();
        var edgesByKind = new NLCPGEdge[orderedEdges.Count];
        foreach (var edge in orderedEdges)
        {
            var kindOrdinal = (int)edge.Kind;
            edgesByKind[positions[kindOrdinal]] = edge;
            positions[kindOrdinal] += 1;
        }

        return (edgesByKind, offsets);
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
