using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 管理最小 Roslyn CPG 的内存节点集和边集。
/// </summary>
public sealed class RoslynCpgGraph
{
    private readonly Dictionary<string, RoslynCpgNode> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<RoslynCpgEdge> _edges = new();
    private RoslynCpgGraphIndex? _queryIndex;

    public IReadOnlyCollection<RoslynCpgNode> Nodes => _nodes.Values;

    public IReadOnlyCollection<RoslynCpgEdge> Edges => _edges;

    public bool HasQueryIndex => _queryIndex is not null;

    /// <summary>
    /// 新增节点；如果同 id 已存在则直接返回已有节点。
    /// </summary>
    public RoslynCpgNode AddNode(RoslynCpgNode node)
    {
        EnsureMutable();
        if (!_nodes.TryGetValue(node.Id, out var existing))
        {
            _nodes[node.Id] = node;
            return node;
        }

        return existing;
    }

    /// <summary>
    /// 在确保端点节点已注册后，补上一条带类型边。
    /// </summary>
    public void AddEdge(RoslynCpgNode source, RoslynCpgNode target, RoslynCpgEdgeKind kind, string? label = null)
    {
        EnsureMutable();
        AddNode(source);
        AddNode(target);
        _edges.Add(new RoslynCpgEdge(source.Id, target.Id, kind, label));
    }

    /// <summary>
    /// 返回指定节点种类的全部节点。
    /// </summary>
    public IEnumerable<RoslynCpgNode> NodesByKind(RoslynCpgNodeKind kind)
    {
        return _nodes.Values.Where(node => node.Kind == kind);
    }

    /// <summary>
    /// 按稳定图 id 查找节点。
    /// </summary>
    public RoslynCpgNode? GetNode(string nodeId)
    {
        return _nodes.TryGetValue(nodeId, out var node) ? node : null;
    }

    public void FreezeQueryIndex()
    {
        if (_queryIndex is not null)
        {
            return;
        }

        _queryIndex = RoslynCpgGraphIndex.Create(_edges);
    }

    public IReadOnlyList<RoslynCpgEdge> GetOutgoingEdges(string nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: true);
    }

    public IReadOnlyList<RoslynCpgEdge> GetIncomingEdges(string nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: false);
    }

    public IReadOnlyList<RoslynCpgEdge> GetEdges(RoslynCpgEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.EdgesByKind.TryGetValue(kind, out var edges) ? edges : Array.Empty<RoslynCpgEdge>();
    }

    /// <summary>
    /// 返回指定节点直接控制的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> Controls(string nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.ControlDependence)
            .ToArray();
    }

    /// <summary>
    /// 返回直接控制指定节点的关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> ControlledBy(string nodeId)
    {
        return GetIncomingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.ControlDependence)
            .ToArray();
    }

    /// <summary>
    /// 返回指定节点支配的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> Dominates(string nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.Dominates)
            .ToArray();
    }

    /// <summary>
    /// 返回指定节点后支配的节点关系。
    /// </summary>
    public IReadOnlyList<RoslynCpgEdge> PostDominates(string nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == RoslynCpgEdgeKind.PostDominates)
            .ToArray();
    }

    /// <summary>
    /// 围绕指定锚点提取一个 hop 有界的局部视图。
    /// </summary>
    public RoslynCpgLocalView ExtractLocalView(string anchorNodeId, int hops, RoslynCpgViewDirection direction = RoslynCpgViewDirection.Both, IReadOnlyCollection<RoslynCpgEdgeKind>? edgeKinds = null)
    {
        if (hops < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hops), "Hops must be non-negative.");
        }

        if (!_nodes.TryGetValue(anchorNodeId, out var anchor))
        {
            throw new ArgumentException($"Unknown anchor node id: {anchorNodeId}", nameof(anchorNodeId));
        }

        var allowedKinds = edgeKinds is null ? null : new HashSet<RoslynCpgEdgeKind>(edgeKinds);
        var index = RequireQueryIndex();
        var visitedNodeIds = new HashSet<string>(StringComparer.Ordinal) { anchorNodeId };
        var frontierNodeIds = new HashSet<string>(StringComparer.Ordinal) { anchorNodeId };

        for (var depth = 0; depth < hops; depth += 1)
        {
            var nextFrontierNodeIds = new HashSet<string>(StringComparer.Ordinal);
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
          .Select(nodeId => _nodes[nodeId])
          .OrderBy(node => node.Id, StringComparer.Ordinal)
          .ToArray();
        var localEdges = index.EdgesByKind.Values.SelectMany(edges => edges)
          .Where(edge =>
            (allowedKinds is null || allowedKinds.Contains(edge.Kind)) &&
            visitedNodeIds.Contains(edge.SourceId) &&
            visitedNodeIds.Contains(edge.TargetId))
          .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
          .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
          .ToArray();
        return new RoslynCpgLocalView(anchor, hops, localNodes, localEdges);
    }

    /// <summary>
    /// 按请求方向扩展一个 BFS 前沿节点。
    /// </summary>
    private static void ExpandFrom(string nodeId, RoslynCpgViewDirection direction, IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> outgoingEdges, IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> incomingEdges, HashSet<RoslynCpgEdgeKind>? allowedKinds, ISet<string> visitedNodeIds, ISet<string> nextFrontierNodeIds)
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
    private static void ExpandNeighbors(string nodeId, IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgEdge>> adjacency, bool useOutgoingTarget, HashSet<RoslynCpgEdgeKind>? allowedKinds, ISet<string> visitedNodeIds, ISet<string> nextFrontierNodeIds)
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

            var neighborId = useOutgoingTarget ? edge.TargetId : edge.SourceId;
            if (visitedNodeIds.Add(neighborId))
            {
                nextFrontierNodeIds.Add(neighborId);
            }
        }
    }

    private IReadOnlyList<RoslynCpgEdge> GetAdjacency(string nodeId, bool useOutgoingEdges)
    {
        var index = RequireQueryIndex();
        var adjacency = useOutgoingEdges ? index.OutgoingByNodeId : index.IncomingByNodeId;
        return adjacency.TryGetValue(nodeId, out var edges) ? edges : Array.Empty<RoslynCpgEdge>();
    }

    private RoslynCpgGraphIndex RequireQueryIndex()
    {
        return _queryIndex ?? throw new InvalidOperationException("The graph query index is unavailable until the graph has been frozen.");
    }

    private void EnsureMutable()
    {
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException("The graph is frozen and cannot be mutated.");
        }
    }
}
