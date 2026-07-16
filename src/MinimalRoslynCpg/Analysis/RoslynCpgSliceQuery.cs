using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// Performs bounded, deterministic reverse traversal over a frozen CPG index.
/// </summary>
public sealed class RoslynCpgSliceQuery
{
    private readonly RoslynCpgGraph _graph;
    private readonly Dictionary<QueryKey, RoslynCpgSliceResult> _cache = new();

    public RoslynCpgSliceQuery(RoslynCpgGraph graph)
    {
        _graph = graph;
    }

    public RoslynCpgSliceResult QueryBackward(NodeId sinkNodeId, RoslynCpgSliceQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var sinkNode = _graph.GetNode(sinkNodeId);
        if (sinkNode is null)
        {
            throw new ArgumentException($"Unknown sink node id: {sinkNodeId}", nameof(sinkNodeId));
        }

        var queryKey = QueryKey.Create(_graph, sinkNodeId, options);
        if (_cache.TryGetValue(queryKey, out var cached))
        {
            return cached with
            {
                Telemetry = cached.Telemetry! with { CacheHitCount = 1, CacheMissCount = 0 },
            };
        }

        var queue = new Queue<SliceState>();
        queue.Enqueue(new SliceState(sinkNodeId, Parent: null, options.MaxHops, options.MaxCallDepth, CallStack: string.Empty));
        var visitedStates = new HashSet<(NodeId NodeId, int RemainingHops, int RemainingCallDepth, string CallStack)>
        {
            (sinkNodeId, options.MaxHops, options.MaxCallDepth, string.Empty),
        };
        var paths = new List<RoslynCpgSlicePath>();
        var visitedNodeIds = new HashSet<NodeId> { sinkNodeId };
        long visitedEdgeCount = 0;
        long interproceduralBridgeExpansionCount = 0;
        var maxObservedCallDepth = 0;
        string? truncationReason = null;

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var incoming = GetAllowedIncomingEdges(state.NodeId, options.AllowedEdgeKinds);
            var callerFrames = incoming
                .Where(edge => edge.Kind == RoslynCpgEdgeKind.InterproceduralDataFlow)
                .Select(GetCallSiteFrame)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (callerFrames > options.MaxCallerFanout)
            {
                truncationReason ??= "maxCallerFanout";
                break;
            }
            foreach (var edge in incoming)
            {
                if (visitedEdgeCount >= options.MaxVisitedEdges)
                {
                    truncationReason ??= "maxVisitedEdges";
                    break;
                }

                visitedEdgeCount += 1;
                if (state.RemainingHops == 0)
                {
                    continue;
                }

                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                if (ContainsNode(state, edge.SourceNodeId))
                {
                    continue;
                }

                var remainingCallDepth = state.RemainingCallDepth;
                var callStack = state.CallStack;
                if (edge.Kind == RoslynCpgEdgeKind.InterproceduralDataFlow)
                {
                    if (remainingCallDepth == 0)
                    {
                        truncationReason ??= "maxCallDepth";
                        continue;
                    }

                    var callSiteFrame = GetCallSiteFrame(edge);
                    if (ContainsCallSiteFrame(callStack, callSiteFrame))
                    {
                        truncationReason ??= "callStackCycle";
                        continue;
                    }

                    remainingCallDepth -= 1;
                    interproceduralBridgeExpansionCount += 1;
                    maxObservedCallDepth = Math.Max(maxObservedCallDepth, options.MaxCallDepth - remainingCallDepth);
                    callStack = string.IsNullOrEmpty(callStack) ? callSiteFrame : $"{callStack}\u001f{callSiteFrame}";
                }

                var next = new SliceState(
                    edge.SourceNodeId,
                    state,
                    state.RemainingHops - 1,
                    remainingCallDepth,
                    callStack);
                if (!visitedStates.Add((next.NodeId, next.RemainingHops, next.RemainingCallDepth, next.CallStack)))
                {
                    continue;
                }

                if (visitedNodeIds.Count >= options.MaxVisitedNodes)
                {
                    truncationReason ??= "maxVisitedNodes";
                    break;
                }

                visitedNodeIds.Add(next.NodeId);
                queue.Enqueue(next);
            }

            if (truncationReason is "maxVisitedEdges" or "maxPaths" or "maxVisitedNodes")
            {
                break;
            }

            if (incoming.Count == 0)
            {
                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                AddPath(state, sinkNodeId, paths);
                if (paths.Count >= options.MaxDefinitions)
                {
                    truncationReason ??= "maxDefinitions";
                    break;
                }

                continue;
            }

            if (state.RemainingHops == 0)
            {
                if (paths.Count >= options.MaxPaths)
                {
                    truncationReason ??= "maxPaths";
                    break;
                }

                AddPath(state, sinkNodeId, paths);
                truncationReason ??= "maxHops";
                continue;
            }

        }

        var orderedPaths = paths
            .DistinctBy(path => string.Join("\u001f", path.NodeIds.Select(nodeId => nodeId.Value)))
            .OrderBy(path => path.SourceNodeId)
            .ThenBy(path => path.SinkNodeId)
            .ThenBy(path => string.Join("\u001f", path.NodeIds.Select(nodeId => nodeId.Value)), StringComparer.Ordinal)
            .ToArray();
        var result = new RoslynCpgSliceResult(
            orderedPaths,
            truncationReason is not null,
            truncationReason,
            visitedNodeIds.Count,
            visitedEdgeCount,
            new RoslynCpgQueryTelemetry(
                visitedNodeIds.Count,
                visitedEdgeCount,
                orderedPaths.Length,
                truncationReason is not null,
                truncationReason,
                CacheHitCount: 0,
                CacheMissCount: 1,
                MaxObservedCallDepth: maxObservedCallDepth,
                InterproceduralBridgeExpansionCount: interproceduralBridgeExpansionCount,
                CacheCapacityBypassCount: _cache.Count >= options.MaxCachedStates ? 1 : 0));
        if (_cache.Count < options.MaxCachedStates && !result.WasTruncated)
        {
            _cache[queryKey] = result;
        }

        return result;
    }

    private static void AddPath(SliceState state, NodeId sinkNodeId, ICollection<RoslynCpgSlicePath> paths)
    {
        var nodeIds = EnumeratePathNodeIds(state).ToArray();
        paths.Add(new RoslynCpgSlicePath(
            state.NodeId,
            sinkNodeId,
            nodeIds));
    }

    private static IEnumerable<NodeId> EnumeratePathNodeIds(SliceState state)
    {
        for (var current = state; current is not null; current = current.Parent)
        {
            yield return current.NodeId;
        }
    }

    private static bool ContainsNode(SliceState state, NodeId nodeId)
    {
        for (var current = state; current is not null; current = current.Parent)
        {
            if (current.NodeId == nodeId)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateOptions(RoslynCpgSliceQueryOptions options)
    {
        if (options.AllowedEdgeKinds.Count == 0)
        {
            throw new ArgumentException("At least one edge kind is required.", nameof(options));
        }

        if (options.MaxHops < 0 || options.MaxPaths <= 0 || options.MaxDefinitions <= 0 ||
            options.MaxCallDepth < 0 || options.MaxVisitedNodes <= 0 ||
            options.MaxVisitedEdges <= 0 || options.MaxCachedStates < 0 || options.MaxCallerFanout <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Slice budgets must be non-negative, with positive path and definition limits.");
        }
    }

    private sealed record SliceState(
        NodeId NodeId,
        SliceState? Parent,
        int RemainingHops,
        int RemainingCallDepth,
        string CallStack);

    private static string GetCallSiteFrame(RoslynCpgEdge edge)
    {
        if (edge.CallSiteContext is { } callSiteContext)
        {
            return callSiteContext.ToContextId().Value;
        }

        if (edge.ContextId is { } contextId &&
            !string.IsNullOrEmpty(contextId.Value))
        {
            return contextId.Value;
        }

        return $"edge:{edge.SourceNodeId}>{edge.TargetNodeId}";
    }

    private static bool ContainsCallSiteFrame(string callStack, string frame)
    {
        return callStack.Split('\u001f', StringSplitOptions.RemoveEmptyEntries)
            .Contains(frame, StringComparer.Ordinal);
    }

    private IReadOnlyList<RoslynCpgEdge> GetAllowedIncomingEdges(
        NodeId nodeId,
        IReadOnlySet<RoslynCpgEdgeKind> allowedKinds)
    {
        var edges = new List<RoslynCpgEdge>();
        foreach (var edgeKind in allowedKinds.OrderBy(kind => kind))
        {
            edges.AddRange(_graph.GetIncomingEdges(nodeId, edgeKind));
        }

        edges.Sort(static (left, right) =>
        {
            var sourceComparison = left.SourceNodeId.CompareTo(right.SourceNodeId);
            return sourceComparison != 0 ? sourceComparison : left.Kind.CompareTo(right.Kind);
        });
        return edges;
    }

    private sealed record QueryKey(
        uint SinkNodeId,
        string GraphSnapshotVersion,
        int EdgeMaskId,
        string EdgeKinds,
        int MaxHops,
        int MaxPaths,
        int MaxDefinitions,
        int MaxCallDepth,
        int MaxVisitedNodes,
        int MaxVisitedEdges,
        int MaxCachedStates,
        int MaxCallerFanout)
    {
        public static QueryKey Create(RoslynCpgGraph graph, NodeId sinkNodeId, RoslynCpgSliceQueryOptions options)
        {
            var edgeKinds = string.Join(",", options.AllowedEdgeKinds.OrderBy(kind => kind));
            return new QueryKey(
                sinkNodeId.Value,
                graph.GraphSnapshotVersion,
                graph.GetEdgeMaskId(options.AllowedEdgeKinds),
                edgeKinds,
                options.MaxHops,
                options.MaxPaths,
                options.MaxDefinitions,
                options.MaxCallDepth,
                options.MaxVisitedNodes,
                options.MaxVisitedEdges,
                options.MaxCachedStates,
                options.MaxCallerFanout);
        }
    }
}
