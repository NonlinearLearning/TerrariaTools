using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 管理最小 Roslyn CPG 的内存节点集和边集。
/// </summary>
public sealed class RoslynCpgGraph
{
    private readonly Dictionary<string, RoslynCpgNode> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<RoslynCpgEdge> _edges = new();

    public IReadOnlyCollection<RoslynCpgNode> Nodes => _nodes.Values;

    public IReadOnlyCollection<RoslynCpgEdge> Edges => _edges;

    /// <summary>
    /// 新增节点；如果同 id 已存在则直接返回已有节点。
    /// </summary>
    public RoslynCpgNode AddNode(RoslynCpgNode node)
    {
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
    public void AddEdge(
      RoslynCpgNode source,
      RoslynCpgNode target,
      RoslynCpgEdgeKind kind,
      string? label = null)
    {
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

    /// <summary>
    /// 围绕指定锚点提取一个 hop 有界的局部视图。
    /// </summary>
    public RoslynCpgLocalView ExtractLocalView(
      string anchorNodeId,
      int hops,
      RoslynCpgViewDirection direction = RoslynCpgViewDirection.Both,
      IReadOnlyCollection<RoslynCpgEdgeKind>? edgeKinds = null)
    {
        if (hops < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hops), "Hops must be non-negative.");
        }

        if (!_nodes.TryGetValue(anchorNodeId, out var anchor))
        {
            throw new ArgumentException($"Unknown anchor node id: {anchorNodeId}", nameof(anchorNodeId));
        }

        var allowedKinds = edgeKinds is null
          ? null
          : new HashSet<RoslynCpgEdgeKind>(edgeKinds);
        // 两个方向的邻接表只建一次，后续 BFS 扩展就不必重复扫描全量边。
        var outgoingEdges = BuildAdjacencyMap(useOutgoingEdges: true, allowedKinds);
        var incomingEdges = BuildAdjacencyMap(useOutgoingEdges: false, allowedKinds);
        var visitedNodeIds = new HashSet<string>(StringComparer.Ordinal) { anchorNodeId };
        var frontierNodeIds = new HashSet<string>(StringComparer.Ordinal) { anchorNodeId };

        for (var depth = 0; depth < hops; depth += 1)
        {
            var nextFrontierNodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nodeId in frontierNodeIds)
            {
                ExpandFrom(nodeId, direction, outgoingEdges, incomingEdges, visitedNodeIds, nextFrontierNodeIds);
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
        var localEdges = _edges
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
    /// 按源或目标 id 组织邻接表，供局部视图遍历使用。
    /// </summary>
    private Dictionary<string, List<RoslynCpgEdge>> BuildAdjacencyMap(
      bool useOutgoingEdges,
      HashSet<RoslynCpgEdgeKind>? allowedKinds)
    {
        var adjacency = new Dictionary<string, List<RoslynCpgEdge>>(StringComparer.Ordinal);
        foreach (var edge in _edges)
        {
            if (allowedKinds is not null && !allowedKinds.Contains(edge.Kind))
            {
                continue;
            }

            var key = useOutgoingEdges ? edge.SourceId : edge.TargetId;
            if (!adjacency.TryGetValue(key, out var list))
            {
                list = new List<RoslynCpgEdge>();
                adjacency[key] = list;
            }

            list.Add(edge);
        }

        return adjacency;
    }

    /// <summary>
    /// 按请求方向扩展一个 BFS 前沿节点。
    /// </summary>
    private static void ExpandFrom(
      string nodeId,
      RoslynCpgViewDirection direction,
      IReadOnlyDictionary<string, List<RoslynCpgEdge>> outgoingEdges,
      IReadOnlyDictionary<string, List<RoslynCpgEdge>> incomingEdges,
      ISet<string> visitedNodeIds,
      ISet<string> nextFrontierNodeIds)
    {
        if (direction is RoslynCpgViewDirection.Both or RoslynCpgViewDirection.Outgoing)
        {
            ExpandNeighbors(nodeId, outgoingEdges, useOutgoingTarget: true, visitedNodeIds, nextFrontierNodeIds);
        }

        if (direction is RoslynCpgViewDirection.Both or RoslynCpgViewDirection.Incoming)
        {
            ExpandNeighbors(nodeId, incomingEdges, useOutgoingTarget: false, visitedNodeIds, nextFrontierNodeIds);
        }
    }

    /// <summary>
    /// 将尚未访问的相邻节点加入下一轮 BFS 前沿。
    /// </summary>
    private static void ExpandNeighbors(
      string nodeId,
      IReadOnlyDictionary<string, List<RoslynCpgEdge>> adjacency,
      bool useOutgoingTarget,
      ISet<string> visitedNodeIds,
      ISet<string> nextFrontierNodeIds)
    {
        if (!adjacency.TryGetValue(nodeId, out var edges))
        {
            return;
        }

        foreach (var edge in edges)
        {
            var neighborId = useOutgoingTarget ? edge.TargetId : edge.SourceId;
            if (visitedNodeIds.Add(neighborId))
            {
                nextFrontierNodeIds.Add(neighborId);
            }
        }
    }
}
