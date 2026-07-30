using NLCPG.Contracts;
using NLCPG.Model;
using System.Runtime.CompilerServices;

namespace NLCPG.Persistence;

public static class CpgFrozenShardGraphReader
{
    private static readonly ConditionalWeakTable<CpgFrozenShard, NodeIndex> NodeIndexes = new();

    /// 仅还原遍历到 <paramref name="targetNodeId"/> 所需的记录。
    /// 分片负载仍由存储完整反序列化，但这避免了在有界切片查询期间为无关分片记录构建
    /// 索引图。
    public static CpgFrozenShardIncomingProjection ReadIncomingProjection(CpgFrozenShard shard, NodeId targetNodeId, IReadOnlySet<NLCPGEdgeKind> allowedEdgeKinds, int maxEdges)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentNullException.ThrowIfNull(allowedEdgeKinds);

        var nodeIndex = NodeIndexes.GetValue(shard, static source => new NodeIndex(source));
        var selectedEdges = new List<NLCPGEdge>();
        var requiredNodeIds = new HashSet<NodeId>();
        if (nodeIndex.TryGetByNodeId(targetNodeId, out var targetNode))
        {
            requiredNodeIds.Add(targetNodeId);
        }
        if (targetNode is not null &&
            CpgFrozenShardIncomingEdgeIndex.TryGet(shard, out var incomingEdgeOffsets, out var incomingEdgeIndexes))
        {
            for (var position = incomingEdgeOffsets[targetNode.LocalIndex];
                 position < incomingEdgeOffsets[targetNode.LocalIndex + 1];
                 position += 1)
            {
                var edge = shard.Edges[incomingEdgeIndexes[position]];
                var sourceNodeId = new NodeId(nodeIndex.GetByLocalIndex(edge.SourceLocalIndex).NodeId);
                if (!Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                    !allowedEdgeKinds.Contains(kind))
                {
                    continue;
                }

                selectedEdges.Add(new NLCPGEdge(
                  sourceNodeId,
                  targetNodeId,
                  kind,
                  ParseLabel(edge.Label, edge.FlowSummaryLabel),
                  edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
                  CreateCallSiteContext(edge)));
                requiredNodeIds.Add(sourceNodeId);
                requiredNodeIds.Add(targetNodeId);
                if (selectedEdges.Count >= maxEdges)
                {
                    break;
                }
            }
        }
        else
        {
            foreach (var edge in shard.Edges)
            {
                var sourceNodeId = new NodeId(nodeIndex.GetByLocalIndex(edge.SourceLocalIndex).NodeId);
                var targetId = new NodeId(nodeIndex.GetByLocalIndex(edge.TargetLocalIndex).NodeId);
                if (targetId != targetNodeId || !Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                    !allowedEdgeKinds.Contains(kind))
                {
                    continue;
                }

                selectedEdges.Add(new NLCPGEdge(
                  sourceNodeId,
                  targetId,
                  kind,
                  ParseLabel(edge.Label, edge.FlowSummaryLabel),
                  edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
                  CreateCallSiteContext(edge)));
                requiredNodeIds.Add(sourceNodeId);
                requiredNodeIds.Add(targetId);
                if (selectedEdges.Count >= maxEdges)
                {
                    break;
                }
            }
        }

        foreach (var edge in shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
        {
            var targetId = new NodeId(edge.TargetNodeId);
            if (targetId != targetNodeId || !Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                !allowedEdgeKinds.Contains(kind))
            {
                continue;
            }

            selectedEdges.Add(CreateBoundaryEdge(edge));
            requiredNodeIds.Add(new NodeId(edge.SourceNodeId));
            requiredNodeIds.Add(targetId);
            if (selectedEdges.Count >= maxEdges)
            {
                break;
            }
        }

        var nodes = requiredNodeIds
          .Select(nodeId => nodeIndex.TryGetByNodeId(nodeId, out var node) ? node : null)
          .Where(node => node is not null)
          .Select(node => node!)
          .OrderBy(node => node.LocalIndex)
          .Select(CreateNode)
          .ToDictionary(node => node.NodeId!.Value);
        return new CpgFrozenShardIncomingProjection(nodes, selectedEdges);
    }

    // 合并多个冻结分片及其边界边，重建完整冻结图。
    public static NLCPGGraph ReadGraph(IEnumerable<CpgFrozenShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        var nodes = new Dictionary<NodeId, NLCPGNode>();
        var edges = new HashSet<NLCPGEdge>();
        var orderedShards = shards
          .OrderBy(shard => shard.Lookup.Fragment.Kind, StringComparer.Ordinal)
          .ThenBy(shard => shard.Lookup.Fragment.SpanStart)
          .ThenBy(shard => shard.Lookup.Fragment.SpanLength)
          .ToArray();
        foreach (var shard in orderedShards)
        {
            var graph = ReadGraph(shard);
            foreach (var node in graph.Nodes)
            {
                nodes.TryAdd(node.NodeId!.Value, node);
            }

            edges.UnionWith(graph.Edges);
        }

        foreach (var boundaryEdge in orderedShards
          .SelectMany(shard => shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId))
        {
            var sourceNodeId = new NodeId(boundaryEdge.SourceNodeId);
            var targetNodeId = new NodeId(boundaryEdge.TargetNodeId);
            if (!nodes.ContainsKey(sourceNodeId) || !nodes.ContainsKey(targetNodeId))
            {
                throw new InvalidDataException("A CPG boundary edge references a node that was not restored.");
            }

            edges.Add(CreateBoundaryEdge(boundaryEdge));
        }

        return NLCPGGraph.CreateFrozen(nodes.Values, edges);
    }

    internal static CpgFrozenShardGraphFacts ReadMutableFacts(IEnumerable<CpgFrozenShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        var nodes = new Dictionary<NodeId, NLCPGNode>();
        var edges = new HashSet<NLCPGEdge>();
        var orderedShards = shards
          .OrderBy(shard => shard.Lookup.Fragment.Kind, StringComparer.Ordinal)
          .ThenBy(shard => shard.Lookup.Fragment.SpanStart)
          .ThenBy(shard => shard.Lookup.Fragment.SpanLength)
          .ToArray();
        foreach (var shard in orderedShards)
        {
            var graph = ReadGraph(shard);
            foreach (var node in graph.Nodes)
            {
                if (!nodes.TryAdd(node.NodeId!.Value, node) && nodes[node.NodeId.Value] != node)
                {
                    throw new InvalidDataException("The CPG shards contain conflicting nodes with the same NodeId.");
                }
            }

            edges.UnionWith(graph.Edges);
        }

        foreach (var boundaryEdge in orderedShards
          .SelectMany(shard => shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId))
        {
            var sourceNodeId = new NodeId(boundaryEdge.SourceNodeId);
            var targetNodeId = new NodeId(boundaryEdge.TargetNodeId);
            if (!nodes.ContainsKey(sourceNodeId) || !nodes.ContainsKey(targetNodeId))
            {
                throw new InvalidDataException("A CPG boundary edge references a node that was not restored.");
            }

            edges.Add(CreateBoundaryEdge(boundaryEdge));
        }

        return new CpgFrozenShardGraphFacts(nodes.Values.ToArray(), edges.ToArray());
    }

    // 从单个冻结分片恢复一张只读图。
    public static NLCPGGraph ReadGraph(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var nodes = shard.Nodes
          .OrderBy(node => node.LocalIndex)
          .Select(CreateNode)
          .ToArray();
        var nodeIdsByLocalIndex = shard.Nodes.ToDictionary(node => node.LocalIndex, node => new NodeId(node.NodeId));
        var edges = shard.Edges.Select(edge => new NLCPGEdge(
          nodeIdsByLocalIndex[edge.SourceLocalIndex],
          nodeIdsByLocalIndex[edge.TargetLocalIndex],
          Enum.Parse<NLCPGEdgeKind>(edge.Kind),
          ParseLabel(edge.Label, edge.FlowSummaryLabel),
          edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
          CreateCallSiteContext(edge))).ToArray();
        return NLCPGGraph.CreateFrozen(nodes, edges);
    }

    // 读取单个分片记录的全部边界边。
    public static IReadOnlyList<NLCPGEdge> ReadBoundaryEdges(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return (shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId)
          .Select(CreateBoundaryEdge)
          .ToArray();
    }

    private static NLCPGEdgeLabel? ParseLabel(string? label, CpgFrozenFlowSummaryLabel? flowSummaryLabel)
    {
        if (flowSummaryLabel is not null)
        {
            return flowSummaryLabel.ToEdgeLabel();
        }

        if (label is null)
        {
            return null;
        }

        const string bridgePrefix = "interprocedural-bridge:";
        const string relationPrefix = "decision-relation:";
        if (label.StartsWith(bridgePrefix, StringComparison.Ordinal))
        {
            return NLCPGEdgeLabel.ForInterproceduralBridge(
              Enum.Parse<NLCPGInterproceduralBridgeKind>(label[bridgePrefix.Length..]));
        }

        if (label.StartsWith(relationPrefix, StringComparison.Ordinal))
        {
            return NLCPGEdgeLabel.ForDecisionRelation(
              Enum.Parse<NLCPGDecisionRelationKind>(label[relationPrefix.Length..]));
        }

        throw new InvalidDataException("The CPG shard contains an unknown structured edge label.");
    }

    private sealed class NodeIndex
    {
        private readonly Dictionary<uint, CpgFrozenNode> _nodesByNodeId = new();
        private readonly Dictionary<int, CpgFrozenNode> _nodesByLocalIndex = new();

        internal NodeIndex(CpgFrozenShard shard)
        {
            foreach (var node in shard.Nodes)
            {
                if (!_nodesByNodeId.TryAdd(node.NodeId, node) ||
                    !_nodesByLocalIndex.TryAdd(node.LocalIndex, node))
                {
                    throw new InvalidDataException("The CPG shard contains duplicate local nodes.");
                }
            }
        }

        internal bool TryGetByNodeId(NodeId nodeId, out CpgFrozenNode? node)
        {
            return _nodesByNodeId.TryGetValue(nodeId.Value, out node);
        }

        internal CpgFrozenNode GetByLocalIndex(int localIndex)
        {
            if (_nodesByLocalIndex.TryGetValue(localIndex, out var node))
            {
                return node;
            }

            throw new InvalidDataException("The CPG shard contains an orphan local edge endpoint.");
        }
    }

    private static NLCPGNode CreateNode(CpgFrozenNode node)
    {
        var kind = Enum.Parse<NLCPGNodeKind>(node.Kind);
        return new NLCPGNode(
          kind,
          node.DisplayKind,
          node.Name,
          node.FullName,
          node.Signature,
          FilePath: node.FilePath,
          SpanStart: node.SpanStart,
          SpanEnd: node.SpanEnd,
          IsImplicit: node.IsImplicit,
          NodeId: new NodeId(node.NodeId),
          StableAnchor: new StableNodeAnchor(
            kind, node.StableFilePathId, node.StableSpanStart,
            node.StableSpanEnd, (StableNodeRole)node.StableRole, node.StableOrdinal, node.StableExtraKeyId));
    }

    private static NLCPGCallSiteContext? CreateCallSiteContext(CpgFrozenEdge edge)
    {
        if (edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
            !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null)
        {
            return null;
        }

        return new NLCPGCallSiteContext(
          edge.CallSiteFilePath,
          edge.CallSiteSpanStart.Value,
          edge.CallSiteSpanEnd.Value,
          edge.CallSiteDisplayName);
    }

    private static NLCPGEdge CreateBoundaryEdge(CpgFrozenBoundaryEdge edge)
    {
        NLCPGCallSiteContext? callSiteContext = edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
          !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null
          ? null
          : new NLCPGCallSiteContext(
            edge.CallSiteFilePath,
            edge.CallSiteSpanStart.Value,
            edge.CallSiteSpanEnd.Value,
            edge.CallSiteDisplayName);
        return new NLCPGEdge(
          new NodeId(edge.SourceNodeId),
          new NodeId(edge.TargetNodeId),
          Enum.Parse<NLCPGEdgeKind>(edge.Kind),
          ParseLabel(edge.Label, edge.FlowSummaryLabel),
          edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
          callSiteContext);
    }
}

public sealed record CpgFrozenShardIncomingProjection(
  IReadOnlyDictionary<NodeId, NLCPGNode> Nodes,
  IReadOnlyList<NLCPGEdge> IncomingEdges);

internal sealed record CpgFrozenShardGraphFacts(
  IReadOnlyList<NLCPGNode> Nodes,
  IReadOnlyList<NLCPGEdge> Edges);
