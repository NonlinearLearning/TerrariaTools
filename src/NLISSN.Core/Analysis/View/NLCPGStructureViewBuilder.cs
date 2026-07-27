using Microsoft.CodeAnalysis;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NLISSN.Core.Analysis;

/// 从主 CPG 图中复制与一个或多个代码片段相关的局部视图。
public sealed class NLCPGStructureViewBuilder
{
    private static readonly ConditionalWeakTable<NLCPGGraph, GraphCache> GraphCaches = new();
    private static readonly ConditionalWeakTable<CpgAnalysisContext, AnalysisRunCache> RunCaches = new();

    // 为单个语法根节点构建局部结构视图，复用统一的片段集合入口。
    public NLCPGStructureView Build(SyntaxNode root, CpgAnalysisContext context)
    {
        return Build(new SyntaxNode[] { root }, context);
    }

    // 为单个语法根节点构建局部结构视图，并把结果绑定到指定缓存作用域。
    public NLCPGStructureView Build(SyntaxNode root, CpgAnalysisContext context, string cacheScopeKey)
    {
        return Build(new SyntaxNode[] { root }, context, cacheScopeKey);
    }

    // 为多个语法片段构建局部结构视图，自动推导片段集合缓存键。
    public NLCPGStructureView Build(IReadOnlyCollection<SyntaxNode> fragments, CpgAnalysisContext context)
    {
        return Build(fragments, context, null);
    }

    // 为多个语法片段裁剪最小连通子图，并按作用域键缓存视图结果。
    public NLCPGStructureView Build(IReadOnlyCollection<SyntaxNode> fragments, CpgAnalysisContext context, string? cacheScopeKey)
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

        var selectedEdges = new HashSet<NLCPGEdge>();
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
        var view = new NLCPGStructureView(SelectRootNode(fragmentList[0], nodes), nodes, edges);
        runCache.RememberView(cacheKey, view);
        return view;
    }

    private static IReadOnlyList<NLCPGNode> ResolveGraphNodesInside(GraphCache graphCache, SyntaxNode fragment)
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

    private static void AddShortestConnectingPaths(GraphCache graphCache, IReadOnlyList<IReadOnlyList<NLCPGNode>> fragmentNodeSets, ISet<NodeId> selectedNodeIds, ISet<NLCPGEdge> selectedEdges)
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

    private static string BuildFragmentSetKey(IReadOnlyList<SyntaxNode> fragments, string? cacheScopeKey)
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

    private static IReadOnlyList<NLCPGEdge>? FindShortestPath(GraphCache graphCache, ISet<NodeId> sourceNodeIds, ISet<NodeId> targetNodeIds)
    {
        if (sourceNodeIds.Count == 0 || targetNodeIds.Count == 0)
        {
            return null;
        }

        if (sourceNodeIds.Overlaps(targetNodeIds))
        {
            return Array.Empty<NLCPGEdge>();
        }

        var queue = new Queue<NodeId>(sourceNodeIds);
        var visited = sourceNodeIds.ToHashSet();
        var previous = new Dictionary<NodeId, (NodeId PreviousId, NLCPGEdge Edge)>();
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

    private static IReadOnlyList<NLCPGEdge> ReconstructPath(IReadOnlyDictionary<NodeId, (NodeId PreviousId, NLCPGEdge Edge)> previous, NodeId targetNodeId)
    {
        var path = new List<NLCPGEdge>();
        var currentId = targetNodeId;
        while (previous.TryGetValue(currentId, out var step))
        {
            path.Add(step.Edge);
            currentId = step.PreviousId;
        }

        path.Reverse();
        return path;
    }

    private static void AddContainedEdges(GraphCache graphCache, IReadOnlySet<NodeId> selectedNodeIds, ISet<NLCPGEdge> selectedEdges)
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

    private static NLCPGNode SelectRootNode(SyntaxNode firstFragment, IReadOnlyList<NLCPGNode> nodes)
    {
        return nodes
            .Where(node =>
                node.SpanStart == firstFragment.SpanStart &&
                node.SpanEnd == firstFragment.Span.End)
            .OrderBy(node => node.Kind == NLCPGNodeKind.SyntaxNode ? 0 : 1)
            .ThenBy(node => node.NodeId)
            .FirstOrDefault()
            ?? nodes.First();
    }

    private sealed class AnalysisRunCache
    {
        private readonly ConcurrentDictionary<string, NLCPGStructureView> _views = new(StringComparer.Ordinal);

        public bool TryGetView(string cacheKey, out NLCPGStructureView view)
        {
            return _views.TryGetValue(cacheKey, out view!);
        }

        public void RememberView(string cacheKey, NLCPGStructureView view)
        {
            _views.TryAdd(cacheKey, view);
        }
    }

    private sealed class GraphCache
    {
        public GraphCache(NLCPGGraph graph)
        {
            graph.FreezeQueryIndex();
            Graph = graph;
            _undirectedNeighborsByNodeId = new ConcurrentDictionary<NodeId, IReadOnlyList<(NodeId NeighborId, NLCPGEdge Edge)>>();
        }

        private readonly ConcurrentDictionary<NodeId, IReadOnlyList<(NodeId NeighborId, NLCPGEdge Edge)>> _undirectedNeighborsByNodeId;

        public NLCPGGraph Graph { get; }

        public IReadOnlyList<(NodeId NeighborId, NLCPGEdge Edge)> GetUndirectedNeighbors(NodeId nodeId)
        {
            return _undirectedNeighborsByNodeId.GetOrAdd(nodeId, BuildUndirectedNeighbors);
        }

        private IReadOnlyList<(NodeId NeighborId, NLCPGEdge Edge)> BuildUndirectedNeighbors(NodeId nodeId)
        {
            var neighbors = new List<(NodeId NeighborId, NLCPGEdge Edge)>();
            neighbors.AddRange(Graph.GetOutgoingEdges(nodeId)
                .Select(edge => (edge.TargetNodeId, edge)));
            neighbors.AddRange(Graph.GetIncomingEdges(nodeId)
                .Select(edge => (edge.SourceNodeId, edge)));
            return neighbors;
        }
    }
}
