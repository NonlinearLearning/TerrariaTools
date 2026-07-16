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
            .SelectMany(nodes => nodes.Select(node => node.NodeId))
            .OfType<NodeId>()
            .ToHashSet();
        if (selectedNodeIds.Count == 0)
        {
            throw new InvalidOperationException("None of the syntax fragments are bound to graph nodes.");
        }

        var selectedEdges = new HashSet<RoslynCpgEdge>();
        AddShortestConnectingPaths(graphCache, fragmentNodeSets, selectedNodeIds, selectedEdges);

        AddContainedEdges(graphCache, selectedNodeIds, selectedEdges);

        var nodes = context.Graph.Nodes
            .Where(node => node.NodeId.HasValue && selectedNodeIds.Contains(node.NodeId.Value))
            .OrderBy(node => node.SpanStart ?? int.MaxValue)
            .ThenBy(node => node.SpanEnd ?? int.MaxValue)
            .ThenBy(node => node.NodeId)
            .ToList();
        var edges = selectedEdges
            .Where(edge =>
                selectedNodeIds.Contains(edge.SourceNodeId) &&
                selectedNodeIds.Contains(edge.TargetNodeId))
            .OrderBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId)
            .ToList();
        var view = new RoslynCpgStructureView(SelectRootNode(fragmentList[0], nodes), nodes, edges);
        runCache.RememberView(cacheKey, view);
        return view;
    }

    public static RoslynCpgStructureViewCacheTelemetry GetCacheTelemetry(CpgAnalysisContext context)
    {
        return RunCaches.TryGetValue(context, out var runCache)
          ? runCache.CreateTelemetry()
          : RoslynCpgStructureViewCacheTelemetry.CreateDefault();
    }

    /// <summary>
    /// 解析代码片段语法树范围内已经存在于主图中的节点。
    /// </summary>
    private static IReadOnlyList<RoslynCpgNode> ResolveGraphNodesInside(GraphCache graphCache, SyntaxNode fragment)
    {
        var filePath = fragment.SyntaxTree.FilePath ?? string.Empty;
        if (string.IsNullOrEmpty(filePath))
        {
            return graphCache.Graph.Nodes
                .Where(node =>
                    string.IsNullOrEmpty(node.FilePath) &&
                    node.SpanStart >= fragment.SpanStart &&
                    node.SpanEnd <= fragment.Span.End)
                .OrderBy(node => node.SpanStart ?? int.MaxValue)
                .ThenBy(node => node.SpanEnd ?? int.MaxValue)
                .ThenBy(node => node.NodeId)
                .ThenBy(node => node.FullName, StringComparer.Ordinal)
                .ToList();
        }

        return graphCache.Graph
            .GetNodesInFileSpan(filePath, fragment.SpanStart, fragment.Span.End)
            .ToList();
    }

    /// <summary>
    /// 用主图的全边无向最短路径连接各个离散片段节点集合。
    /// </summary>
    private static void AddShortestConnectingPaths(GraphCache graphCache, IReadOnlyList<IReadOnlyList<RoslynCpgNode>> fragmentNodeSets, ISet<NodeId> selectedNodeIds, ISet<RoslynCpgEdge> selectedEdges)
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
                    graphCache,
                    fragmentNodeSets[leftIndex].Select(node => node.NodeId).OfType<NodeId>().ToHashSet(),
                    fragmentNodeSets[rightIndex].Select(node => node.NodeId).OfType<NodeId>().ToHashSet());
                if (path is null)
                {
                    continue;
                }

                foreach (var edge in path)
                {
                    selectedNodeIds.Add(edge.SourceNodeId);
                    selectedNodeIds.Add(edge.TargetNodeId);
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
    /// 在无向图上寻找从任一源节点到任一目标节点的最短边链。
    /// </summary>
    private static IReadOnlyList<RoslynCpgEdge>? FindShortestPath(GraphCache graphCache, ISet<NodeId> sourceNodeIds, ISet<NodeId> targetNodeIds)
    {
        if (sourceNodeIds.Count == 0 || targetNodeIds.Count == 0)
        {
            return null;
        }

        if (sourceNodeIds.Overlaps(targetNodeIds))
        {
            return Array.Empty<RoslynCpgEdge>();
        }

        var queue = new Queue<NodeId>(sourceNodeIds);
        var visited = sourceNodeIds.ToHashSet();
        var previous = new Dictionary<NodeId, (NodeId PreviousId, RoslynCpgEdge Edge)>();
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var (neighborId, edge) in graphCache.GetUndirectedNeighbors(currentId))
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
    private static IReadOnlyList<RoslynCpgEdge> ReconstructPath(IReadOnlyDictionary<NodeId, (NodeId PreviousId, RoslynCpgEdge Edge)> previous, NodeId targetNodeId)
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
    /// 将当前选中节点集合之间的现有主图边加入视图。
    /// </summary>
    private static void AddContainedEdges(GraphCache graphCache, IReadOnlySet<NodeId> selectedNodeIds, ISet<RoslynCpgEdge> selectedEdges)
    {
        foreach (var nodeId in selectedNodeIds)
        {
            foreach (var edge in graphCache.Graph.GetOutgoingEdges(nodeId))
            {
                if (selectedNodeIds.Contains(edge.TargetNodeId))
                {
                    selectedEdges.Add(edge);
                }
            }
        }
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
            .ThenBy(node => node.NodeId)
            .FirstOrDefault()
            ?? nodes.First();
    }

    private sealed class AnalysisRunCache
    {
        private readonly ConcurrentDictionary<string, RoslynCpgStructureView> _views = new(StringComparer.Ordinal);
        private long _hitCount;
        private long _missCount;
        private int _maxCachedViewCount;

        public bool TryGetView(string cacheKey, out RoslynCpgStructureView view)
        {
            var hit = _views.TryGetValue(cacheKey, out view!);
            if (hit)
            {
                Interlocked.Increment(ref _hitCount);
            }
            else
            {
                Interlocked.Increment(ref _missCount);
            }

            return hit;
        }

        public void RememberView(string cacheKey, RoslynCpgStructureView view)
        {
            if (_views.TryAdd(cacheKey, view))
            {
                UpdateMaxCachedViewCount(_views.Count);
            }
        }

        public RoslynCpgStructureViewCacheTelemetry CreateTelemetry()
        {
            var hitCount = Volatile.Read(ref _hitCount);
            var missCount = Volatile.Read(ref _missCount);
            var requestCount = hitCount + missCount;
            return new RoslynCpgStructureViewCacheTelemetry(
              RequestCount: requestCount,
              CacheHitCount: hitCount,
              CacheMissCount: missCount,
              UniqueFragmentSetCount: _views.Count,
              MaxCachedViewCount: Volatile.Read(ref _maxCachedViewCount),
              CacheHitRate: requestCount == 0 ? 0 : (double)hitCount / requestCount);
        }

        private void UpdateMaxCachedViewCount(int currentCount)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxCachedViewCount);
                if (currentCount <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxCachedViewCount, currentCount, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class GraphCache
    {
        public GraphCache(RoslynCpgGraph graph)
        {
            graph.FreezeQueryIndex();
            Graph = graph;
            _undirectedNeighborsByNodeId = new ConcurrentDictionary<NodeId, IReadOnlyList<(NodeId NeighborId, RoslynCpgEdge Edge)>>();
        }

        private readonly ConcurrentDictionary<NodeId, IReadOnlyList<(NodeId NeighborId, RoslynCpgEdge Edge)>> _undirectedNeighborsByNodeId;

        public RoslynCpgGraph Graph { get; }

        public IReadOnlyList<(NodeId NeighborId, RoslynCpgEdge Edge)> GetUndirectedNeighbors(NodeId nodeId)
        {
            return _undirectedNeighborsByNodeId.GetOrAdd(nodeId, BuildUndirectedNeighbors);
        }

        private IReadOnlyList<(NodeId NeighborId, RoslynCpgEdge Edge)> BuildUndirectedNeighbors(NodeId nodeId)
        {
            var neighbors = new List<(NodeId NeighborId, RoslynCpgEdge Edge)>();
            neighbors.AddRange(Graph.GetOutgoingEdges(nodeId)
                .Select(edge => (edge.TargetNodeId, edge)));
            neighbors.AddRange(Graph.GetIncomingEdges(nodeId)
                .Select(edge => (edge.SourceNodeId, edge)));
            return neighbors;
        }
    }
}

public sealed record RoslynCpgStructureViewCacheTelemetry(
  long RequestCount,
  long CacheHitCount,
  long CacheMissCount,
  int UniqueFragmentSetCount,
  int MaxCachedViewCount,
  double CacheHitRate)
{
    public static RoslynCpgStructureViewCacheTelemetry CreateDefault()
    {
        return new RoslynCpgStructureViewCacheTelemetry(0, 0, 0, 0, 0, 0);
    }
}
