using MinimalRoslynCpg.Builder.Streaming;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Persistence;

public static class CpgFrozenShardExporter
{
  internal static CpgFrozenShard ExportDescriptors(
    CpgShardLookup lookup,
    IReadOnlyList<CpgNodeDescriptor> descriptors,
    IReadOnlyList<CpgEdgeCandidate> edgeCandidates,
    DeterministicNodeIdTable allocation,
    ICollection<CpgFrozenBoundaryEdge> boundaryEdges)
  {
    ArgumentNullException.ThrowIfNull(lookup);
    ArgumentNullException.ThrowIfNull(descriptors);
    ArgumentNullException.ThrowIfNull(edgeCandidates);
    ArgumentNullException.ThrowIfNull(allocation);
    ArgumentNullException.ThrowIfNull(boundaryEdges);

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
          item.Candidate.CallSiteContext?.DisplayName));
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
          item.Candidate.CallSiteContext?.DisplayName));
      }
    }

    return new CpgFrozenShard(
      lookup,
      nodes.Select((item, index) => ToFrozenNode(item.Descriptor, item.NodeId, index)).ToArray(),
      edges,
      Array.Empty<CpgSymbolLocation>());
  }

  public static CpgFrozenShard Export(
    RoslynCpgGraph graph,
    CpgShardLookup lookup,
    IReadOnlySet<NodeId>? includedNodeIds = null)
  {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(lookup);
    if (!graph.HasQueryIndex)
    {
      throw new InvalidOperationException("A CPG graph must be frozen before it can be exported as a shard.");
    }

    var nodes = graph.Nodes
      .Where(node => includedNodeIds is null || includedNodeIds.Contains(node.NodeId!.Value))
      .OrderBy(node => node.NodeId)
      .Select((node, index) => (Node: node, LocalIndex: index))
      .ToArray();
    var localIndexes = nodes.ToDictionary(item => item.Node.NodeId!.Value, item => item.LocalIndex);
    return new CpgFrozenShard(
      lookup,
      nodes.Select(item => ToFrozenNode(item.Node, item.LocalIndex)).ToArray(),
      graph.Edges
        .Where(edge => localIndexes.ContainsKey(edge.SourceNodeId) && localIndexes.ContainsKey(edge.TargetNodeId))
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind)
        .ThenBy(edge => edge.TargetNodeId)
        .Select(edge => new CpgFrozenEdge(
          localIndexes[edge.SourceNodeId],
          localIndexes[edge.TargetNodeId],
          edge.Kind.ToString(),
          edge.StructuredLabel?.StableKey,
          edge.ContextId?.Value,
          edge.CallSiteContext?.FilePath,
          edge.CallSiteContext?.SpanStart,
          edge.CallSiteContext?.SpanEnd,
          edge.CallSiteContext?.DisplayName))
        .ToArray(),
      Array.Empty<CpgSymbolLocation>());
  }

  private static CpgFrozenNode ToFrozenNode(RoslynCpgNode node, int localIndex)
  {
    return new CpgFrozenNode(
      localIndex,
      node.NodeId!.Value.Value,
      node.Kind.ToString(),
      node.FilePath,
      node.SpanStart,
      node.SpanEnd,
      node.DisplayKind,
      node.Name,
      node.FullName,
      node.Signature,
      node.IsImplicit,
      node.StableAnchor?.FilePathId ?? 0,
      node.StableAnchor?.SpanStart ?? -1,
      node.StableAnchor?.SpanEnd ?? -1,
      (int)(node.StableAnchor?.Role ?? StableNodeRole.None),
      node.StableAnchor?.Ordinal ?? 0,
      node.StableAnchor?.ExtraKeyId ?? 0);
  }

  private static CpgFrozenNode ToFrozenNode(
    CpgNodeDescriptor descriptor,
    NodeId nodeId,
    int localIndex)
  {
    return new CpgFrozenNode(
      localIndex,
      nodeId.Value,
      descriptor.Kind.ToString(),
      descriptor.FilePath,
      descriptor.SpanStart,
      descriptor.SpanEnd,
      descriptor.DisplayKind,
      descriptor.Name,
      descriptor.FullName,
      descriptor.Signature,
      descriptor.IsImplicit,
      descriptor.Anchor.FilePathId,
      descriptor.Anchor.SpanStart,
      descriptor.Anchor.SpanEnd,
      (int)descriptor.Anchor.Role,
      descriptor.Anchor.Ordinal,
      descriptor.Anchor.ExtraKeyId);
  }
}
