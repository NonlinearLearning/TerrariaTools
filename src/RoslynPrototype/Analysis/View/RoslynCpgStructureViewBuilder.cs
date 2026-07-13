using Microsoft.CodeAnalysis;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace RoslynPrototype.Analysis;

/// <summary>
/// 从主 CPG 图中复制与一个或多个代码片段相关的局部视图。
/// </summary>
public sealed class RoslynCpgStructureViewBuilder
{
    private static readonly ConditionalWeakTable<RoslynCpgGraph, GraphCache> GraphCaches = new();
    private static readonly ConditionalWeakTable<CpgAnalysisContext, AnalysisRunCache> RunCaches = new();

    /// <summary>
    /// 为单个代码片段构建局部 CPG 视图。
    /// </summary>
    public RoslynCpgStructureView Build(SyntaxNode root, CpgAnalysisContext context)
    {
        return Build(new SyntaxNode[] { root }, context);
    }

    public RoslynCpgStructureView Build(
      SyntaxNode root,
      CpgAnalysisContext context,
      string cacheScopeKey)
    {
        return Build(new SyntaxNode[] { root }, context, cacheScopeKey);
    }

    /// <summary>
    /// 为多个离散代码片段构建同一份局部 CPG 视图。
    /// </summary>
    public RoslynCpgStructureView Build(IReadOnlyCollection<SyntaxNode> fragments, CpgAnalysisContext context)
    {
        return Build(fragments, context, null);
    }

    public RoslynCpgStructureView Build(
      IReadOnlyCollection<SyntaxNode> fragments,
      CpgAnalysisContext context,
      string? cacheScopeKey)
    {
        if (fragments.Count == 0)
        {
            throw new ArgumentException("At least one syntax fragment is required.", nameof(fragments));
        }

        var fragmentList = fragments.ToList();
        var runCache = RunCaches.GetValue(context, static _ => new AnalysisRunCache());
        var cacheKey = BuildFragmentSetKey(fragmentList, cacheScopeKey);
        if (runCache.TryGetView(cacheKey, out var cachedView))
        {
            return cachedView;
        }

        var graphCache = GraphCaches.GetValue(
            context.Graph,
            static graph => new GraphCache(graph));
        var fragmentNodeSets = fragmentList
            .Select(fragment => ResolveGraphNodesInside(graphCache, fragment))
            .ToList();
        var selectedNodeIds = fragmentNodeSets
            .SelectMany(nodes => nodes.Select(node => node.Id))
            .ToHashSet(StringComparer.Ordinal);
        if (selectedNodeIds.Count == 0)
        {
            throw new InvalidOperationException("None of the syntax fragments are bound to graph nodes.");
        }

        var selectedEdges = new HashSet<RoslynCpgEdge>();
        AddShortestConnectingPaths(graphCache, fragmentNodeSets, selectedNodeIds, selectedEdges);

        foreach (var edge in graphCache.Edges)
        {
            if (selectedNodeIds.Contains(edge.SourceId) && selectedNodeIds.Contains(edge.TargetId))
            {
                selectedEdges.Add(edge);
            }
        }

        var nodes = context.Graph.Nodes
            .Where(node => selectedNodeIds.Contains(node.Id))
            .OrderBy(node => node.SpanStart ?? int.MaxValue)
            .ThenBy(node => node.SpanEnd ?? int.MaxValue)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToList();
        var edges = selectedEdges
            .Where(edge => selectedNodeIds.Contains(edge.SourceId) && selectedNodeIds.Contains(edge.TargetId))
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(edge => edge.Label, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToList();
        var view = new RoslynCpgStructureView(SelectRootNode(fragmentList[0], nodes), nodes, edges);
        runCache.RememberView(cacheKey, view);
        return view;
    }

    /// <summary>
    /// 解析代码片段语法树范围内已经存在于主图中的节点。
    /// </summary>
    private static IReadOnlyList<RoslynCpgNode> ResolveGraphNodesInside(GraphCache graphCache, SyntaxNode fragment)
    {
        var filePath = fragment.SyntaxTree.FilePath ?? string.Empty;
        if (!graphCache.NodesByFilePath.TryGetValue(filePath, out var nodes))
        {
            return Array.Empty<RoslynCpgNode>();
        }

        return nodes
            .Where(node =>
                node.SpanStart >= fragment.SpanStart &&
                node.SpanEnd <= fragment.Span.End)
            .ToList();
    }

    /// <summary>
    /// 用主图的全边无向最短路径连接各个离散片段节点集合。
    /// </summary>
    private static void AddShortestConnectingPaths(GraphCache graphCache, IReadOnlyList<IReadOnlyList<RoslynCpgNode>> fragmentNodeSets, ISet<string> selectedNodeIds, ISet<RoslynCpgEdge> selectedEdges)
    {
        if (fragmentNodeSets.Count < 2)
        {
            return;
        }

        for (var leftIndex = 0; leftIndex < fragmentNodeSets.Count; leftIndex += 1)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < fragmentNodeSets.Count; rightIndex += 1)
            {
                var path = FindShortestPath(
                    graphCache.UndirectedAdjacency,
                    fragmentNodeSets[leftIndex].Select(node => node.Id).ToHashSet(StringComparer.Ordinal),
                    fragmentNodeSets[rightIndex].Select(node => node.Id).ToHashSet(StringComparer.Ordinal));
                if (path is null)
                {
                    continue;
                }

                foreach (var edge in path)
                {
                    selectedNodeIds.Add(edge.SourceId);
                    selectedNodeIds.Add(edge.TargetId);
                    selectedEdges.Add(edge);
                }
            }
        }
    }

    private static string BuildFragmentSetKey(
      IReadOnlyList<SyntaxNode> fragments,
      string? cacheScopeKey)
    {
        var fragmentKey = string.Join(
            "|",
            fragments
                .Select(fragment =>
                    $"{fragment.SyntaxTree.FilePath}:{fragment.SpanStart}:{fragment.Span.Length}:{fragment.RawKind}"));
        return string.IsNullOrWhiteSpace(cacheScopeKey)
          ? fragmentKey
          : $"{cacheScopeKey}|{fragmentKey}";
    }

    /// <summary>
    /// 将主图所有边转换成无向邻接表，最短链搜索不区分边方向。
    /// </summary>
    private static Dictionary<string, List<(string NeighborId, RoslynCpgEdge Edge)>> BuildUndirectedAdjacency(IEnumerable<RoslynCpgEdge> edges)
    {
        var adjacency = new Dictionary<string, List<(string NeighborId, RoslynCpgEdge Edge)>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            AddNeighbor(adjacency, edge.SourceId, edge.TargetId, edge);
            AddNeighbor(adjacency, edge.TargetId, edge.SourceId, edge);
        }

        return adjacency;
    }

    /// <summary>
    /// 在无向图上寻找从任一源节点到任一目标节点的最短边链。
    /// </summary>
    private static IReadOnlyList<RoslynCpgEdge>? FindShortestPath(IReadOnlyDictionary<string, List<(string NeighborId, RoslynCpgEdge Edge)>> adjacency, ISet<string> sourceNodeIds, ISet<string> targetNodeIds)
    {
        if (sourceNodeIds.Count == 0 || targetNodeIds.Count == 0)
        {
            return null;
        }

        if (sourceNodeIds.Overlaps(targetNodeIds))
        {
            return Array.Empty<RoslynCpgEdge>();
        }

        var queue = new Queue<string>(sourceNodeIds);
        var visited = sourceNodeIds.ToHashSet(StringComparer.Ordinal);
        var previous = new Dictionary<string, (string PreviousId, RoslynCpgEdge Edge)>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!adjacency.TryGetValue(currentId, out var neighbors))
            {
                continue;
            }

            foreach (var (neighborId, edge) in neighbors)
            {
                if (!visited.Add(neighborId))
                {
                    continue;
                }

                previous[neighborId] = (currentId, edge);
                if (targetNodeIds.Contains(neighborId))
                {
                    return ReconstructPath(previous, neighborId);
                }

                queue.Enqueue(neighborId);
            }
        }

        return null;
    }

    /// <summary>
    /// 从 BFS 前驱表还原最短路径上的原始主图边。
    /// </summary>
    private static IReadOnlyList<RoslynCpgEdge> ReconstructPath(IReadOnlyDictionary<string, (string PreviousId, RoslynCpgEdge Edge)> previous, string targetNodeId)
    {
        var path = new List<RoslynCpgEdge>();
        var currentId = targetNodeId;
        while (previous.TryGetValue(currentId, out var step))
        {
            path.Add(step.Edge);
            currentId = step.PreviousId;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// 向无向邻接表追加一个方向的邻接项。
    /// </summary>
    private static void AddNeighbor(IDictionary<string, List<(string NeighborId, RoslynCpgEdge Edge)>> adjacency, string sourceId, string targetId, RoslynCpgEdge edge)
    {
        if (!adjacency.TryGetValue(sourceId, out var neighbors))
        {
            neighbors = new List<(string NeighborId, RoslynCpgEdge Edge)>();
            adjacency[sourceId] = neighbors;
        }

        neighbors.Add((targetId, edge));
    }

    /// <summary>
    /// 为兼容现有视图契约，优先选第一个片段对应的精确主图节点作为 Root。
    /// </summary>
    private static RoslynCpgNode SelectRootNode(SyntaxNode firstFragment, IReadOnlyList<RoslynCpgNode> nodes)
    {
        return nodes
            .Where(node =>
                node.SpanStart == firstFragment.SpanStart &&
                node.SpanEnd == firstFragment.Span.End)
            .OrderBy(node => node.Kind == RoslynCpgNodeKind.SyntaxNode ? 0 : 1)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? nodes.First();
    }

    private sealed class AnalysisRunCache
    {
        private readonly ConcurrentDictionary<string, RoslynCpgStructureView> _views = new(StringComparer.Ordinal);

        public bool TryGetView(string cacheKey, out RoslynCpgStructureView view)
        {
            return _views.TryGetValue(cacheKey, out view!);
        }

        public void RememberView(string cacheKey, RoslynCpgStructureView view)
        {
            _views.TryAdd(cacheKey, view);
        }
    }

    private sealed class GraphCache
    {
        public GraphCache(RoslynCpgGraph graph)
        {
            Edges = graph.Edges.ToList();
            NodesByFilePath = graph.Nodes
                .GroupBy(node => node.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<RoslynCpgNode>)group
                        .OrderBy(node => node.SpanStart ?? int.MaxValue)
                        .ThenBy(node => node.SpanEnd ?? int.MaxValue)
                        .ThenBy(node => node.Id, StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.Ordinal);
            UndirectedAdjacency = BuildUndirectedAdjacency(Edges);
        }

        public IReadOnlyList<RoslynCpgEdge> Edges { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<RoslynCpgNode>> NodesByFilePath { get; }

        public IReadOnlyDictionary<string, List<(string NeighborId, RoslynCpgEdge Edge)>> UndirectedAdjacency { get; }
    }
}
