using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 管理最小 Roslyn CPG 的内存节点集和边集。
/// </summary>
public sealed class RoslynCpgGraph
{
    private readonly Dictionary<StableNodeAnchor, RoslynCpgNode> _mutableNodesByAnchor = new();
    private readonly Dictionary<NodeId, RoslynCpgNode> _nodesByNodeId = new();
    private readonly HashSet<RoslynCpgEdge> _edges = new();
    private readonly HashSet<PendingEdge> _pendingEdges = new();
    private readonly Dictionary<string, string> _sourceByPath = new(StringComparer.Ordinal);
    private readonly StringInterner _identityInterner = new();
    private RoslynCpgGraphIndex? _queryIndex;
    private RoslynCpgFreezeTelemetry _freezeTelemetry = RoslynCpgFreezeTelemetry.CreateDefault();

    public IReadOnlyCollection<RoslynCpgNode> Nodes =>
      _queryIndex is null ? _mutableNodesByAnchor.Values : _nodesByNodeId.Values;

    public IReadOnlyCollection<RoslynCpgEdge> Edges => _edges;

    internal int CurrentEdgeCount => _queryIndex is null ? _pendingEdges.Count : _edges.Count;

    internal IReadOnlyCollection<PendingEdge> PendingEdges => _pendingEdges;

    public bool HasQueryIndex => _queryIndex is not null;

    public string GraphSnapshotVersion => RequireQueryIndex().SnapshotVersion;

    /// <summary>
    /// 新增节点；如果同稳定锚点已存在则直接返回已有节点。
    /// </summary>
    public RoslynCpgNode AddNode(RoslynCpgNode node)
    {
        EnsureMutable();
        var materializedNode = MaterializeCompatibilityIdentity(node);
        var stableAnchor = materializedNode.StableAnchor!.Value;
        if (!_mutableNodesByAnchor.TryGetValue(stableAnchor, out var existing))
        {
            _mutableNodesByAnchor[stableAnchor] = materializedNode;
            return materializedNode;
        }

        return existing;
    }

    /// <summary>
    /// 在确保端点节点已注册后，补上一条带类型边。
    /// </summary>
    public void AddEdge(
        RoslynCpgNode source,
        RoslynCpgNode target,
        RoslynCpgEdgeKind kind,
        RoslynCpgEdgeLabel? structuredLabel = null,
        RoslynCpgContextId? contextId = null,
        RoslynCpgCallSiteContext? callSiteContext = null)
    {
        EnsureMutable();
        var materializedSource = AddNode(source);
        var materializedTarget = AddNode(target);
        _pendingEdges.Add(new PendingEdge(
          materializedSource,
          materializedTarget,
          kind,
          structuredLabel,
          contextId,
          callSiteContext));
    }

    /// <summary>
    /// 返回指定节点种类的全部节点。
    /// </summary>
    public IEnumerable<RoslynCpgNode> NodesByKind(RoslynCpgNodeKind kind)
    {
        return _queryIndex is null
            ? _mutableNodesByAnchor.Values.Where(node => node.Kind == kind)
            : GetNodes(kind);
    }

    public RoslynCpgNode? GetNode(NodeId nodeId)
    {
        return _nodesByNodeId.TryGetValue(nodeId, out var node) ? node : null;
    }

    public void RegisterSource(string filePath, string source)
    {
        EnsureMutable();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _sourceByPath[filePath] = source;

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(fullPath, filePath, StringComparison.Ordinal))
        {
            _sourceByPath[fullPath] = source;
        }
    }

    public string GetDisplayText(RoslynCpgNode node)
    {
        if (node.Text is not null)
        {
            return node.Text;
        }

        if (TryResolveSourceSlice(node, out var sourceText))
        {
            return sourceText;
        }

        return node.FullName ?? node.Name ?? node.DisplayKind;
    }

    public RoslynCpgFreezeTelemetry FreezeQueryIndex()
    {
        if (_queryIndex is not null)
        {
            return _freezeTelemetry;
        }

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var assignTelemetry = AssignDeterministicNodeIds();
        var indexBuildResult = RoslynCpgGraphIndex.Create(_nodesByNodeId.Values, _edges);
        _queryIndex = indexBuildResult.Index;
        totalStopwatch.Stop();
        var indexTelemetry = indexBuildResult.Telemetry;
        _freezeTelemetry = new RoslynCpgFreezeTelemetry(
          TotalElapsedMilliseconds: totalStopwatch.ElapsedMilliseconds,
          AssignDeterministicNodeIdsElapsedMilliseconds: assignTelemetry.AssignDeterministicNodeIdsElapsedMilliseconds,
          CreateAnchorsElapsedMilliseconds: assignTelemetry.CreateAnchorsElapsedMilliseconds,
          CreateNodeIdTableElapsedMilliseconds: assignTelemetry.CreateNodeIdTableElapsedMilliseconds,
          RemapNodesElapsedMilliseconds: assignTelemetry.RemapNodesElapsedMilliseconds,
          RemapEdgesElapsedMilliseconds: assignTelemetry.RemapEdgesElapsedMilliseconds,
          BuildQueryIndexElapsedMilliseconds: indexTelemetry.BuildQueryIndexElapsedMilliseconds,
          PopulateEdgeIndexBucketsElapsedMilliseconds: indexTelemetry.PopulateEdgeIndexBucketsElapsedMilliseconds,
          OrderEdgesElapsedMilliseconds: indexTelemetry.OrderEdgesElapsedMilliseconds,
          OrderNodesElapsedMilliseconds: indexTelemetry.OrderNodesElapsedMilliseconds,
          SnapshotHashElapsedMilliseconds: indexTelemetry.SnapshotHashElapsedMilliseconds,
          BuildAdjacencyElapsedMilliseconds: indexTelemetry.BuildAdjacencyElapsedMilliseconds,
          BuildKindAdjacencyElapsedMilliseconds: indexTelemetry.BuildKindAdjacencyElapsedMilliseconds,
          BuildEdgeKindIndexElapsedMilliseconds: indexTelemetry.BuildEdgeKindIndexElapsedMilliseconds,
          BuildNodeKindIndexElapsedMilliseconds: indexTelemetry.BuildNodeKindIndexElapsedMilliseconds,
          BuildFilePathIndexElapsedMilliseconds: indexTelemetry.BuildFilePathIndexElapsedMilliseconds,
          NodeCount: indexTelemetry.NodeCount,
          EdgeCount: indexTelemetry.EdgeCount,
          DistinctAnchorCount: assignTelemetry.DistinctAnchorCount);
        return _freezeTelemetry;
    }

    public IReadOnlyList<RoslynCpgEdge> GetOutgoingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: true);
    }

    public IReadOnlyList<RoslynCpgEdge> GetIncomingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: false);
    }

    public IReadOnlyList<RoslynCpgEdge> GetIncomingEdges(NodeId nodeId, RoslynCpgEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.IncomingByNodeAndKind.TryGetValue((nodeId, kind), out var edges)
            ? edges
            : Array.Empty<RoslynCpgEdge>();
    }

    public IReadOnlyList<RoslynCpgEdge> GetOutgoingEdges(NodeId nodeId, RoslynCpgEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.OutgoingByNodeAndKind.TryGetValue((nodeId, kind), out var edges)
            ? edges
            : Array.Empty<RoslynCpgEdge>();
    }

    public IReadOnlyList<RoslynCpgEdge> GetEdges(RoslynCpgEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.EdgesByKind.TryGetValue(kind, out var edges) ? edges : Array.Empty<RoslynCpgEdge>();
    }

    public IReadOnlyList<RoslynCpgNode> GetNodes(RoslynCpgNodeKind kind)
    {
        var index = RequireQueryIndex();
        return index.NodesByKind.TryGetValue(kind, out var nodes) ? nodes : Array.Empty<RoslynCpgNode>();
    }

    public IReadOnlyList<RoslynCpgNode> GetSymbolReferences(NodeId symbolNodeId)
    {
        return GetIncomingEdges(symbolNodeId, RoslynCpgEdgeKind.Ref)
            .Select(edge => _nodesByNodeId[edge.SourceNodeId])
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    public IReadOnlyList<RoslynCpgNode> GetMethodOwnedCallSites(NodeId methodNodeId)
    {
        return GetOutgoingEdges(methodNodeId, RoslynCpgEdgeKind.ContainsSymbol)
            .Select(edge => _nodesByNodeId[edge.TargetNodeId])
            .Where(node => node.Kind == RoslynCpgNodeKind.CallSite)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    public IReadOnlyList<RoslynCpgNode> GetNodesInFileSpan(string filePath, int start, int end)
    {
        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The span must be a valid half-open interval.");
        }

        var index = RequireQueryIndex();
        if (!index.NodesByFilePath.TryGetValue(filePath, out var nodes))
        {
            return Array.Empty<RoslynCpgNode>();
        }

        return nodes.Where(node => node.SpanStart >= start && node.SpanEnd <= end)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    public int GetEdgeMaskId(IReadOnlySet<RoslynCpgEdgeKind> edgeKinds)
    {
        ArgumentNullException.ThrowIfNull(edgeKinds);
        var hash = new HashCode();
        foreach (var kind in edgeKinds.OrderBy(kind => kind))
        {
            hash.Add((int)kind);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// 返回指定节点直接控制的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> Controls(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.ControlDependence)
            .ToArray();
    }

    /// <summary>
    /// 返回直接控制指定节点的关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> ControlledBy(NodeId nodeId)
    {
        return GetIncomingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.ControlDependence)
            .ToArray();
    }

    /// <summary>
    /// 返回指定节点支配的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> Dominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.Dominates)
            .ToArray();
    }

    /// <summary>
    /// 返回指定节点后支配的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> PostDominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.PostDominates)
            .ToArray();
    }

    /// <summary>
    /// 围绕指定锚点提取一个 hop 有界的局部视图。
    /// </summary>
    public RoslynCpgLocalView ExtractLocalView(NodeId anchorNodeId, int hops, RoslynCpgViewDirection direction = RoslynCpgViewDirection.Both, IReadOnlyCollection<RoslynCpgEdgeKind>? edgeKinds = null)
    {
        if (hops < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hops), "Hops must be non-negative.");
        }

        _ = RequireQueryIndex();
        if (!_nodesByNodeId.TryGetValue(anchorNodeId, out var anchor))
        {
            throw new ArgumentException($"Unknown anchor node id: {anchorNodeId}", nameof(anchorNodeId));
        }

        var allowedKinds = edgeKinds is null ? null : new HashSet<RoslynCpgEdgeKind>(edgeKinds);
        var index = RequireQueryIndex();
        var visitedNodeIds = new HashSet<NodeId> { anchorNodeId };
        var frontierNodeIds = new HashSet<NodeId> { anchorNodeId };

        for (var depth = 0; depth < hops; depth += 1)
        {
            var nextFrontierNodeIds = new HashSet<NodeId>();
            foreach (var nodeId in frontierNodeIds)
            {
                ExpandFrom(nodeId, direction, index.OutgoingByNodeId, index.IncomingByNodeId, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
            }

            frontierNodeIds = nextFrontierNodeIds;
            if (frontierNodeIds.Count == 0)
            {
                break;
            }
        }

        var localNodes = visitedNodeIds
          .Select(nodeId => _nodesByNodeId[nodeId])
          .OrderBy(node => node.NodeId)
          .ToArray();
        var localEdges = index.EdgesByKind.Values.SelectMany(edges => edges)
          .Where(edge =>
            (allowedKinds is null || allowedKinds.Contains(edge.Kind)) &&
            visitedNodeIds.Contains(edge.SourceNodeId) &&
            visitedNodeIds.Contains(edge.TargetNodeId))
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind)
          .ThenBy(edge => edge.TargetNodeId)
          .ToArray();
        return new RoslynCpgLocalView(anchor, hops, localNodes, localEdges);
    }

    /// <summary>
    /// 按请求方向扩展一个 BFS 前沿节点。
    /// </summary>
    private static void ExpandFrom(NodeId nodeId, RoslynCpgViewDirection direction, IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> outgoingEdges, IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> incomingEdges, HashSet<RoslynCpgEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        if (direction is RoslynCpgViewDirection.Both or RoslynCpgViewDirection.Outgoing)
        {
            ExpandNeighbors(nodeId, outgoingEdges, useOutgoingTarget: true, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }

        if (direction is RoslynCpgViewDirection.Both or RoslynCpgViewDirection.Incoming)
        {
            ExpandNeighbors(nodeId, incomingEdges, useOutgoingTarget: false, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }
    }

    /// <summary>
    /// 将尚未访问的相邻节点加入下一轮 BFS 前沿。
    /// </summary>
    private static void ExpandNeighbors(NodeId nodeId, IReadOnlyDictionary<NodeId, IReadOnlyList<RoslynCpgEdge>> adjacency, bool useOutgoingTarget, HashSet<RoslynCpgEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        if (!adjacency.TryGetValue(nodeId, out var edges))
        {
            return;
        }

        foreach (var edge in edges)
        {
            if (allowedKinds is not null && !allowedKinds.Contains(edge.Kind))
            {
                continue;
            }

            var neighborId = useOutgoingTarget ? edge.TargetNodeId : edge.SourceNodeId;
            if (visitedNodeIds.Add(neighborId))
            {
                nextFrontierNodeIds.Add(neighborId);
            }
        }
    }

    private IReadOnlyList<RoslynCpgEdge> GetAdjacency(NodeId nodeId, bool useOutgoingEdges)
    {
        var index = RequireQueryIndex();
        var adjacency = useOutgoingEdges ? index.OutgoingByNodeId : index.IncomingByNodeId;
        return adjacency.TryGetValue(nodeId, out var edges) ? edges : Array.Empty<RoslynCpgEdge>();
    }

    private RoslynCpgGraphIndex RequireQueryIndex()
    {
        return _queryIndex ?? throw new InvalidOperationException("The graph query index is unavailable until the graph has been frozen.");
    }

    private bool TryResolveSourceSlice(RoslynCpgNode node, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(node.FilePath) ||
            !node.SpanStart.HasValue ||
            !node.SpanEnd.HasValue)
        {
            return false;
        }

        if (!TryGetSource(node.FilePath, out var source))
        {
            return false;
        }

        var start = node.SpanStart.Value;
        var end = node.SpanEnd.Value;
        if (start < 0 || end < start || end > source.Length)
        {
            return false;
        }

        text = source[start..end];
        return true;
    }

    private bool TryGetSource(string filePath, out string source)
    {
        if (_sourceByPath.TryGetValue(filePath, out source!))
        {
            return true;
        }

        var fullPath = Path.GetFullPath(filePath);
        return _sourceByPath.TryGetValue(fullPath, out source!);
    }

    private RoslynCpgNode MaterializeCompatibilityIdentity(RoslynCpgNode node)
    {
        if (node.NodeId.HasValue && node.StableAnchor.HasValue)
        {
            return node;
        }

        var role = MapStableNodeRole(node.Kind);
        var stableAnchor = node.StableAnchor ?? StableNodeAnchor.CreateFallback(node, _identityInterner, role);
        return node with
        {
            NodeId = node.NodeId,
            StableAnchor = stableAnchor,
        };
    }

    private RoslynCpgFreezeTelemetry AssignDeterministicNodeIds()
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var createAnchorsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var anchoredNodes = _mutableNodesByAnchor.Values
          .Select(node =>
          {
              var anchor = node.StableAnchor ?? StableNodeAnchor.CreateFallback(node, _identityInterner, MapStableNodeRole(node.Kind));
              return (Node: node, Anchor: anchor);
          })
          .ToArray();
        createAnchorsStopwatch.Stop();
        var createNodeIdTableStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var nodeIdTable = DeterministicNodeIdTable.Create(anchoredNodes.Select(entry => entry.Anchor));
        createNodeIdTableStopwatch.Stop();
        var remapNodesStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var remappedNodes = anchoredNodes
          .Select(entry =>
          {
              if (!nodeIdTable.TryGetNodeId(entry.Anchor, out var nodeId))
              {
                  throw new InvalidOperationException($"Failed to resolve deterministic NodeId for '{DescribeNode(entry.Node)}'.");
              }

              return entry.Node with
              {
                  NodeId = nodeId,
                  StableAnchor = entry.Anchor,
              };
          })
          .ToDictionary(node => node.StableAnchor!.Value);
        remapNodesStopwatch.Stop();
        var remapEdgesStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var remappedEdges = _pendingEdges
          .Select(edge =>
          {
              var sourceNode = remappedNodes[edge.SourceNode.StableAnchor!.Value];
              var targetNode = remappedNodes[edge.TargetNode.StableAnchor!.Value];
              return new RoslynCpgEdge(
                sourceNode.NodeId!.Value,
                targetNode.NodeId!.Value,
                edge.Kind,
                edge.StructuredLabel,
                edge.ContextId,
                edge.CallSiteContext);
          })
          .ToArray();
        remapEdgesStopwatch.Stop();

        _mutableNodesByAnchor.Clear();
        _nodesByNodeId.Clear();
        foreach (var node in remappedNodes.Values)
        {
            _nodesByNodeId[node.NodeId!.Value] = node;
        }

        _edges.Clear();
        foreach (var edge in remappedEdges)
        {
            _edges.Add(edge);
        }
        totalStopwatch.Stop();
        return new RoslynCpgFreezeTelemetry(
          TotalElapsedMilliseconds: totalStopwatch.ElapsedMilliseconds,
          AssignDeterministicNodeIdsElapsedMilliseconds: totalStopwatch.ElapsedMilliseconds,
          CreateAnchorsElapsedMilliseconds: createAnchorsStopwatch.ElapsedMilliseconds,
          CreateNodeIdTableElapsedMilliseconds: createNodeIdTableStopwatch.ElapsedMilliseconds,
          RemapNodesElapsedMilliseconds: remapNodesStopwatch.ElapsedMilliseconds,
          RemapEdgesElapsedMilliseconds: remapEdgesStopwatch.ElapsedMilliseconds,
          BuildQueryIndexElapsedMilliseconds: 0,
          PopulateEdgeIndexBucketsElapsedMilliseconds: 0,
          OrderEdgesElapsedMilliseconds: 0,
          OrderNodesElapsedMilliseconds: 0,
          SnapshotHashElapsedMilliseconds: 0,
          BuildAdjacencyElapsedMilliseconds: 0,
          BuildKindAdjacencyElapsedMilliseconds: 0,
          BuildEdgeKindIndexElapsedMilliseconds: 0,
          BuildNodeKindIndexElapsedMilliseconds: 0,
          BuildFilePathIndexElapsedMilliseconds: 0,
          NodeCount: remappedNodes.Count,
          EdgeCount: remappedEdges.Length,
          DistinctAnchorCount: nodeIdTable.Count);
    }

    private static string DescribeNode(RoslynCpgNode node)
    {
        return node.FullName ??
          node.Name ??
          $"{node.Kind}:{node.FilePath}:{node.SpanStart}:{node.SpanEnd}";
    }

    private static StableNodeRole MapStableNodeRole(RoslynCpgNodeKind kind)
    {
        return kind switch
        {
            RoslynCpgNodeKind.SyntaxNode => StableNodeRole.SyntaxNode,
            RoslynCpgNodeKind.SyntaxToken => StableNodeRole.SyntaxToken,
            RoslynCpgNodeKind.Operation => StableNodeRole.Operation,
            RoslynCpgNodeKind.Reference => StableNodeRole.Reference,
            RoslynCpgNodeKind.TypeRef => StableNodeRole.TypeReference,
            RoslynCpgNodeKind.TypeDecl => StableNodeRole.TypeDeclaration,
            RoslynCpgNodeKind.Method => StableNodeRole.Method,
            RoslynCpgNodeKind.MethodParameter => StableNodeRole.MethodParameter,
            RoslynCpgNodeKind.MethodReturn => StableNodeRole.MethodReturn,
            RoslynCpgNodeKind.MethodEntry => StableNodeRole.MethodEntry,
            RoslynCpgNodeKind.MethodExit => StableNodeRole.MethodExit,
            RoslynCpgNodeKind.CallSite => StableNodeRole.CallSite,
            RoslynCpgNodeKind.MemberAccess => StableNodeRole.MemberAccess,
            RoslynCpgNodeKind.SymbolMethod or
            RoslynCpgNodeKind.SymbolParameter or
            RoslynCpgNodeKind.SymbolLocal or
            RoslynCpgNodeKind.SymbolField or
            RoslynCpgNodeKind.SymbolProperty or
            RoslynCpgNodeKind.SymbolType or
            RoslynCpgNodeKind.SymbolUnknown => StableNodeRole.Symbol,
            _ => StableNodeRole.None,
        };
    }

    private void EnsureMutable()
    {
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException("The graph is frozen and cannot be mutated.");
        }
    }

    internal sealed record PendingEdge(
      RoslynCpgNode SourceNode,
      RoslynCpgNode TargetNode,
      RoslynCpgEdgeKind Kind,
      RoslynCpgEdgeLabel? StructuredLabel,
      RoslynCpgContextId? ContextId,
      RoslynCpgCallSiteContext? CallSiteContext);
}
