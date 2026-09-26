using NLCPG.Builder.Streaming;
using NLCPG.Model;

namespace NLCPG.Persistence;

public static class CpgFrozenShardExporter
{
    internal static CpgFrozenShard ExportDescriptors(CpgShardLookup lookup, IReadOnlyList<CpgNodeDescriptor> descriptors, IReadOnlyList<CpgEdgeCandidate> edgeCandidates, DeterministicNodeIdTable allocation, ICollection<CpgFrozenBoundaryEdge> boundaryEdges, StringInterner stringInterner)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(edgeCandidates);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(boundaryEdges);
        ArgumentNullException.ThrowIfNull(stringInterner);

        var nodes = descriptors
          .Select(descriptor => (Descriptor: descriptor, NodeId: allocation.GetRequiredId(descriptor.Anchor)))
          .OrderBy(item => item.NodeId)
          .ToArray();
        if (nodes.Select(item => item.NodeId).Distinct().Count() != nodes.Length)
        {
            throw new InvalidOperationException("A frozen CPG shard cannot contain duplicate NodeIds.");
        }

        var localIndexes = nodes
          .Select((item, index) => (item.NodeId, LocalIndex: index))
          .ToDictionary(item => item.NodeId, item => item.LocalIndex);
        var edges = new List<CpgFrozenEdge>();
        var candidates = edgeCandidates
          .Select(candidate => (Candidate: candidate,
            SourceNodeId: allocation.GetRequiredId(candidate.SourceAnchor),
            TargetNodeId: allocation.GetRequiredId(candidate.TargetAnchor)))
          .OrderBy(item => item.SourceNodeId)
          .ThenBy(item => item.Candidate.Kind)
          .ThenBy(item => item.TargetNodeId)
          .ToArray();
        foreach (var item in candidates)
        {
            var sourceIsLocal = localIndexes.TryGetValue(item.SourceNodeId, out var sourceLocalIndex);
            var targetIsLocal = localIndexes.TryGetValue(item.TargetNodeId, out var targetLocalIndex);
            if (sourceIsLocal && targetIsLocal)
            {
                edges.Add(new CpgFrozenEdge(
                  sourceLocalIndex,
                  targetLocalIndex,
                  item.Candidate.Kind.ToString(),
                  item.Candidate.StructuredLabel?.StableKey,
                  item.Candidate.ContextId?.Value,
                  item.Candidate.CallSiteContext?.FilePath,
                  item.Candidate.CallSiteContext?.SpanStart,
                  item.Candidate.CallSiteContext?.SpanEnd,
                  item.Candidate.CallSiteContext?.DisplayName,
                  CpgFrozenFlowSummaryLabel.From(item.Candidate.StructuredLabel)));
            }
            else if (sourceIsLocal || targetIsLocal)
            {
                boundaryEdges.Add(new CpgFrozenBoundaryEdge(
                  item.SourceNodeId.Value,
                  item.TargetNodeId.Value,
                  item.Candidate.Kind.ToString(),
                  item.Candidate.StructuredLabel?.StableKey,
                  item.Candidate.ContextId?.Value,
                  item.Candidate.CallSiteContext?.FilePath,
                  item.Candidate.CallSiteContext?.SpanStart,
                  item.Candidate.CallSiteContext?.SpanEnd,
                  item.Candidate.CallSiteContext?.DisplayName,
                  CpgFrozenFlowSummaryLabel.From(item.Candidate.StructuredLabel)));
            }
        }

        var frozenNodes = nodes
          .Select((item, index) => ToFrozenNode(item.Descriptor, item.NodeId, index, stringInterner))
          .ToArray();
        var (incomingEdgeOffsets, incomingEdgeIndexes) = CpgFrozenShardIncomingEdgeIndex.Build(
          frozenNodes,
          edges);

        return new CpgFrozenShard(
          lookup,
          frozenNodes,
          edges,
          Array.Empty<CpgSymbolLocation>(),
          IncomingEdgeOffsets: incomingEdgeOffsets,
          IncomingEdgeIndexes: incomingEdgeIndexes);
    }

    // 从冻结后的图导出一个分片，可选限制导出的节点集合。
    public static CpgFrozenShard Export(NLCPGGraph graph, CpgShardLookup lookup, IReadOnlySet<NodeId>? includedNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(lookup);
        return Export(Prepare(graph), lookup, includedNodeIds);
    }

    internal static CpgFrozenGraphProjection Prepare(NLCPGGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.HasQueryIndex)
        {
            throw new InvalidOperationException("A CPG graph must be frozen before it can be projected for shard export.");
        }

        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId)
          .ToArray();
        var nodesByNodeId = nodes.ToDictionary(node => node.NodeId!.Value);
        var outgoingEdgesByNodeId = new Dictionary<NodeId, IReadOnlyList<NLCPGEdge>>(nodes.Length);
        foreach (var node in nodes)
        {
            outgoingEdgesByNodeId[node.NodeId!.Value] = graph.GetOutgoingEdges(node.NodeId!.Value).ToArray();
        }
        return new CpgFrozenGraphProjection(graph.StringTable, nodes, nodesByNodeId, outgoingEdgesByNodeId);
    }

    internal static CpgFrozenShard Export(CpgFrozenGraphProjection projection, CpgShardLookup lookup, IReadOnlySet<NodeId>? includedNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(lookup);

        var nodes = includedNodeIds is null
          ? projection.Nodes
          : includedNodeIds
            .Where(projection.NodesByNodeId.ContainsKey)
            .Select(nodeId => projection.NodesByNodeId[nodeId])
            .OrderBy(node => node.NodeId)
            .ToArray();
        var localIndexes = nodes
          .Select((node, index) => (NodeId: node.NodeId!.Value, LocalIndex: index))
          .ToDictionary(item => item.NodeId, item => item.LocalIndex);
        var frozenNodes = nodes.Select((node, index) => ToFrozenNode(node, index, projection.StringInterner)).ToArray();
        var frozenEdges = new List<CpgFrozenEdge>();
        foreach (var sourceNode in nodes)
        {
            foreach (var edge in projection.OutgoingEdgesByNodeId[sourceNode.NodeId!.Value])
            {
                if (!localIndexes.TryGetValue(edge.TargetNodeId, out var targetLocalIndex))
                {
                    continue;
                }

                frozenEdges.Add(new CpgFrozenEdge(
                  localIndexes[edge.SourceNodeId],
                  targetLocalIndex,
                  edge.Kind.ToString(),
                  edge.StructuredLabel?.StableKey,
                  edge.ContextId?.Value,
                  edge.CallSiteContext?.FilePath,
                  edge.CallSiteContext?.SpanStart,
                  edge.CallSiteContext?.SpanEnd,
                  edge.CallSiteContext?.DisplayName,
                  CpgFrozenFlowSummaryLabel.From(edge.StructuredLabel)));
            }
        }

        var (incomingEdgeOffsets, incomingEdgeIndexes) = CpgFrozenShardIncomingEdgeIndex.Build(
          frozenNodes,
          frozenEdges);
        return new CpgFrozenShard(
          lookup,
          frozenNodes,
          frozenEdges,
          Array.Empty<CpgSymbolLocation>(),
          IncomingEdgeOffsets: incomingEdgeOffsets,
          IncomingEdgeIndexes: incomingEdgeIndexes);
    }

    private static CpgFrozenNode ToFrozenNode(NLCPGNode node, int localIndex, StringInterner stringInterner)
    {
        return new CpgFrozenNode(
          localIndex,
          node.NodeId!.Value.Value,
          node.Kind.ToString(),
          Resolve(stringInterner, node.FilePathId),
          node.SpanStart,
          node.SpanEnd,
          node.Kind.ToString(),
          Resolve(stringInterner, node.NameId),
          Resolve(stringInterner, node.FullNameId),
          Resolve(stringInterner, node.SignatureId),
          node.IsImplicit,
          node.StableAnchor?.FilePathId ?? 0,
          node.StableAnchor?.SpanStart ?? -1,
          node.StableAnchor?.SpanEnd ?? -1,
          (int)(node.StableAnchor?.Role ?? StableNodeRole.None),
          node.StableAnchor?.Ordinal ?? 0,
          node.StableAnchor?.ExtraKeyId ?? 0);
    }

    private static CpgFrozenNode ToFrozenNode(CpgNodeDescriptor descriptor, NodeId nodeId, int localIndex, StringInterner stringInterner)
    {
        return new CpgFrozenNode(
          localIndex,
          nodeId.Value,
          descriptor.Kind.ToString(),
          Resolve(stringInterner, descriptor.FilePathId),
          descriptor.SpanStart,
          descriptor.SpanEnd,
          descriptor.Kind.ToString(),
          Resolve(stringInterner, descriptor.NameId),
          Resolve(stringInterner, descriptor.FullNameId),
          Resolve(stringInterner, descriptor.SignatureId),
          descriptor.IsImplicit,
          descriptor.Anchor.FilePathId,
          descriptor.Anchor.SpanStart,
          descriptor.Anchor.SpanEnd,
          (int)descriptor.Anchor.Role,
          descriptor.Anchor.Ordinal,
          descriptor.Anchor.ExtraKeyId);
    }

    private static string? Resolve(StringInterner stringInterner, uint id)
    {
        return stringInterner.TryResolve(id, out var text) ? text : null;
    }
}

internal sealed class CpgFrozenGraphProjection
{
    internal CpgFrozenGraphProjection(
      StringInterner stringInterner,
      IReadOnlyList<NLCPGNode> nodes,
      IReadOnlyDictionary<NodeId, NLCPGNode> nodesByNodeId,
      IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> outgoingEdgesByNodeId)
    {
        StringInterner = stringInterner;
        Nodes = nodes;
        NodesByNodeId = nodesByNodeId;
        OutgoingEdgesByNodeId = outgoingEdgesByNodeId;
    }

    internal IReadOnlyList<NLCPGNode> Nodes { get; }

    internal StringInterner StringInterner { get; }

    internal IReadOnlyDictionary<NodeId, NLCPGNode> NodesByNodeId { get; }

    internal IReadOnlyDictionary<NodeId, IReadOnlyList<NLCPGEdge>> OutgoingEdgesByNodeId { get; }
}
