using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Runtime.CompilerServices;

namespace MinimalRoslynCpg.Persistence;

public static class CpgFrozenShardGraphReader
{
  private static readonly ConditionalWeakTable<CpgFrozenShard, NodeIndex> NodeIndexes = new();

  /// <summary>
  /// Restores only the records needed to traverse into <paramref name="targetNodeId"/>.
  /// The shard payload remains fully deserialized by the store, but this avoids building an
  /// indexed graph for unrelated shard records during a bounded slice query.
  /// </summary>
  public static CpgFrozenShardIncomingProjection ReadIncomingProjection(CpgFrozenShard shard, NodeId targetNodeId, IReadOnlySet<RoslynCpgEdgeKind> allowedEdgeKinds, int maxEdges)
  {
    ArgumentNullException.ThrowIfNull(shard);
    ArgumentNullException.ThrowIfNull(allowedEdgeKinds);

    var nodeIndex = NodeIndexes.GetValue(shard, static source => new NodeIndex(source));
    var selectedEdges = new List<RoslynCpgEdge>();
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
        if (!Enum.TryParse<RoslynCpgEdgeKind>(edge.Kind, out var kind) ||
            !allowedEdgeKinds.Contains(kind))
        {
          continue;
        }

        selectedEdges.Add(new RoslynCpgEdge(
          sourceNodeId,
          targetNodeId,
          kind,
          ParseLabel(edge.Label),
          edge.ContextId is null ? null : new RoslynCpgContextId(edge.ContextId),
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
        if (targetId != targetNodeId || !Enum.TryParse<RoslynCpgEdgeKind>(edge.Kind, out var kind) ||
            !allowedEdgeKinds.Contains(kind))
        {
          continue;
        }

        selectedEdges.Add(new RoslynCpgEdge(
          sourceNodeId,
          targetId,
          kind,
          ParseLabel(edge.Label),
          edge.ContextId is null ? null : new RoslynCpgContextId(edge.ContextId),
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
      if (targetId != targetNodeId || !Enum.TryParse<RoslynCpgEdgeKind>(edge.Kind, out var kind) ||
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

  internal static CpgFrozenShardGraphFacts ReadMutableFacts(IEnumerable<CpgFrozenShard> shards)
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

  public static RoslynCpgGraph ReadGraph(CpgFrozenShard shard)
  {
    ArgumentNullException.ThrowIfNull(shard);
    var nodes = shard.Nodes
      .OrderBy(node => node.LocalIndex)
      .Select(CreateNode)
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

  private static RoslynCpgNode CreateNode(CpgFrozenNode node)
  {
    var kind = Enum.Parse<RoslynCpgNodeKind>(node.Kind);
    return new RoslynCpgNode(
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

public sealed record CpgFrozenShardIncomingProjection(
  IReadOnlyDictionary<NodeId, RoslynCpgNode> Nodes,
  IReadOnlyList<RoslynCpgEdge> IncomingEdges);

internal sealed record CpgFrozenShardGraphFacts(
  IReadOnlyList<RoslynCpgNode> Nodes,
  IReadOnlyList<RoslynCpgEdge> Edges);
