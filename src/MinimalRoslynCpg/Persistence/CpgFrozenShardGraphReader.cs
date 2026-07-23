using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Persistence;

public static class CpgFrozenShardGraphReader
{
  public static RoslynCpgGraph ReadGraph(IEnumerable<CpgFrozenShard> shards)
  {
    ArgumentNullException.ThrowIfNull(shards);
    var nodes = new Dictionary<NodeId, RoslynCpgNode>();
    var edges = new HashSet<RoslynCpgEdge>();
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

    return RoslynCpgGraph.CreateFrozen(nodes.Values, edges);
  }

  public static RoslynCpgGraph ReadGraph(CpgFrozenShard shard)
  {
    ArgumentNullException.ThrowIfNull(shard);
    var nodes = shard.Nodes
      .OrderBy(node => node.LocalIndex)
      .Select(node => new RoslynCpgNode(
        Enum.Parse<RoslynCpgNodeKind>(node.Kind),
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
          Enum.Parse<RoslynCpgNodeKind>(node.Kind), node.StableFilePathId, node.StableSpanStart,
          node.StableSpanEnd, (StableNodeRole)node.StableRole, node.StableOrdinal, node.StableExtraKeyId)))
      .ToArray();
    var nodeIdsByLocalIndex = shard.Nodes.ToDictionary(node => node.LocalIndex, node => new NodeId(node.NodeId));
    var edges = shard.Edges.Select(edge => new RoslynCpgEdge(
      nodeIdsByLocalIndex[edge.SourceLocalIndex],
      nodeIdsByLocalIndex[edge.TargetLocalIndex],
      Enum.Parse<RoslynCpgEdgeKind>(edge.Kind),
      ParseLabel(edge.Label),
      edge.ContextId is null ? null : new RoslynCpgContextId(edge.ContextId),
      CreateCallSiteContext(edge))).ToArray();
    return RoslynCpgGraph.CreateFrozen(nodes, edges);
  }

  public static IReadOnlyList<RoslynCpgEdge> ReadBoundaryEdges(CpgFrozenShard shard)
  {
    ArgumentNullException.ThrowIfNull(shard);
    return (shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
      .OrderBy(edge => edge.SourceNodeId)
      .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
      .ThenBy(edge => edge.TargetNodeId)
      .Select(CreateBoundaryEdge)
      .ToArray();
  }

  private static RoslynCpgEdgeLabel? ParseLabel(string? label)
  {
    if (label is null)
    {
      return null;
    }

    const string bridgePrefix = "interprocedural-bridge:";
    const string relationPrefix = "decision-relation:";
    if (label.StartsWith(bridgePrefix, StringComparison.Ordinal))
    {
      return RoslynCpgEdgeLabel.ForInterproceduralBridge(
        Enum.Parse<RoslynCpgInterproceduralBridgeKind>(label[bridgePrefix.Length..]));
    }

    if (label.StartsWith(relationPrefix, StringComparison.Ordinal))
    {
      return RoslynCpgEdgeLabel.ForDecisionRelation(
        Enum.Parse<RoslynCpgDecisionRelationKind>(label[relationPrefix.Length..]));
    }

    throw new InvalidDataException("The CPG shard contains an unknown structured edge label.");
  }

  private static RoslynCpgCallSiteContext? CreateCallSiteContext(CpgFrozenEdge edge)
  {
    if (edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
        !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null)
    {
      return null;
    }

    return new RoslynCpgCallSiteContext(
      edge.CallSiteFilePath,
      edge.CallSiteSpanStart.Value,
      edge.CallSiteSpanEnd.Value,
      edge.CallSiteDisplayName);
  }

  private static RoslynCpgEdge CreateBoundaryEdge(CpgFrozenBoundaryEdge edge)
  {
    RoslynCpgCallSiteContext? callSiteContext = edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
      !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null
      ? null
      : new RoslynCpgCallSiteContext(
        edge.CallSiteFilePath,
        edge.CallSiteSpanStart.Value,
        edge.CallSiteSpanEnd.Value,
        edge.CallSiteDisplayName);
    return new RoslynCpgEdge(
      new NodeId(edge.SourceNodeId),
      new NodeId(edge.TargetNodeId),
      Enum.Parse<RoslynCpgEdgeKind>(edge.Kind),
      ParseLabel(edge.Label),
      edge.ContextId is null ? null : new RoslynCpgContextId(edge.ContextId),
      callSiteContext);
  }
}
