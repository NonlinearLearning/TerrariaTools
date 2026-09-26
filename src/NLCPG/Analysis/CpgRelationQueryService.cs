using NLCPG.Contracts;
using NLCPG.Model;
using System.Diagnostics;

namespace NLCPG.Analysis;

/// Executes reviewed relation profiles against a frozen in-memory CPG.
public sealed class CpgRelationQueryService : ICpgRelationQueryService
{
    private readonly NLCPGGraph _graph;
    private readonly NLCPGCapability _availableCapabilities;
    private readonly Dictionary<QueryKey, CpgRelationQueryResult> _cache = new();

    public CpgRelationQueryService(
        NLCPGGraph graph,
        NLCPGCapability availableCapabilities = NLCPGCapability.All)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _availableCapabilities = availableCapabilities;
        _graph.FreezeQueryIndex();
    }

    public CpgRelationQueryResult Query(CpgRelationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);
        var requiredCapabilities = CpgRelationProfiles.GetRequiredCapabilities(query.Profile) |
            query.RequiredCapabilities;
        if ((_availableCapabilities & requiredCapabilities) != requiredCapabilities)
        {
            return CreateUnavailableResult(requiredCapabilities);
        }

        var key = QueryKey.Create(_graph, query, requiredCapabilities);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached with
            {
                WasCacheHit = true,
                Metrics = new CpgRelationQueryMetrics(0, 0, 0, CacheHitCount: 1),
            };
        }

        var executionStopwatch = Stopwatch.StartNew();
        var result = Execute(query, requiredCapabilities);
        executionStopwatch.Stop();
        result = result with
        {
            Metrics = (result.Metrics ?? new CpgRelationQueryMetrics(0, 0, 0, 0)) with
            {
                QueryExecutionElapsedMilliseconds = executionStopwatch.ElapsedMilliseconds,
            },
        };
        if (result.Status is CpgQueryStatus.Complete or CpgQueryStatus.Disconnected &&
            _cache.Count < query.Budget.MaxCachedStates)
        {
            _cache[key] = result;
        }

        return result;
    }

    public Task<CpgRelationQueryResult> QueryAsync(
        CpgRelationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Query(query));
    }

    private CpgRelationQueryResult Execute(CpgRelationQuery query, NLCPGCapability requiredCapabilities)
    {
        var sourceNodes = ResolveNodes(query.Source);
        var targetNodeIds = query.Target is null
            ? null
            : ResolveNodes(query.Target).Select(node => node.NodeId!.Value).ToHashSet();
        if (sourceNodes.Count == 0 || (query.Target is not null && targetNodeIds!.Count == 0))
        {
            return new CpgRelationQueryResult(
                sourceNodes,
                Array.Empty<NLCPGEdge>(),
                Array.Empty<CpgRelationPath>(),
                CpgQueryStatus.Disconnected,
                null,
                Array.Empty<CpgShardUnavailableResult>(),
                requiredCapabilities,
                WasCacheHit: false,
                VisitedNodeCount: sourceNodes.Count,
                VisitedEdgeCount: 0,
                LoadedShardBytes: 0);
        }

        var allowedKinds = CpgRelationProfiles.GetAllowedEdgeKinds(query.Profile);
        var queue = new Queue<QueryState>();
        var visitedStates = new HashSet<(NodeId NodeId, int Hops, int RemainingCallDepth, string CallStack)>();
        var selectedNodeIds = new HashSet<NodeId>();
        foreach (var sourceNode in sourceNodes)
        {
            var sourceNodeId = sourceNode.NodeId!.Value;
            queue.Enqueue(new QueryState(sourceNodeId, sourceNodeId, null, null, 0, query.Budget.MaxCallDepth, string.Empty));
            visitedStates.Add((sourceNodeId, 0, query.Budget.MaxCallDepth, string.Empty));
            selectedNodeIds.Add(sourceNodeId);
        }

        var selectedEdges = new HashSet<NLCPGEdge>();
        var paths = new List<CpgRelationPath>();
        long visitedEdgeCount = 0;
        string? truncationReason = null;
        var stablePathLimitReached = false;
        while (queue.Count > 0 && truncationReason is null && !stablePathLimitReached)
        {
            var state = queue.Dequeue();
            if (targetNodeIds?.Contains(state.NodeId) == true && state.Parent is not null)
            {
                paths.Add(CreatePath(state));
                if (paths.Count >= query.Budget.MaxPaths || paths.Count >= query.Budget.MaxDefinitions)
                {
                    // A caller that asks for one stable path does not ask us to enumerate alternatives.
                    stablePathLimitReached = true;
                }

                continue;
            }

            if (state.Hops >= query.Budget.MaxHops)
            {
                continue;
            }

            var adjacentEdges = GetAdjacentEdges(state.NodeId, query.Direction, allowedKinds).ToArray();
            var callerFrames = adjacentEdges
                .Where(edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
                .Select(GetCallSiteFrame)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (callerFrames > query.Budget.MaxCallerFanout)
            {
                truncationReason = "maxCallerFanout";
                break;
            }

            foreach (var edge in adjacentEdges)
            {
                if (visitedEdgeCount >= query.Budget.MaxVisitedEdges)
                {
                    truncationReason = "maxVisitedEdges";
                    break;
                }

                visitedEdgeCount += 1;
                var nextNodeId = GetNextNodeId(state.NodeId, edge, query.Direction);
                var remainingCallDepth = state.RemainingCallDepth;
                var callStack = state.CallStack;
                if (edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow)
                {
                    if (remainingCallDepth == 0)
                    {
                        truncationReason = "maxCallDepth";
                        continue;
                    }

                    var callSiteFrame = GetCallSiteFrame(edge);
                    if (ContainsCallSiteFrame(callStack, callSiteFrame))
                    {
                        truncationReason = "callStackCycle";
                        continue;
                    }

                    remainingCallDepth -= 1;
                    callStack = string.IsNullOrEmpty(callStack) ? callSiteFrame : $"{callStack}\u001f{callSiteFrame}";
                }

                if (!visitedStates.Add((nextNodeId, state.Hops + 1, remainingCallDepth, callStack)))
                {
                    continue;
                }

                if (selectedNodeIds.Count >= query.Budget.MaxVisitedNodes)
                {
                    truncationReason = "maxVisitedNodes";
                    break;
                }

                selectedNodeIds.Add(nextNodeId);
                selectedEdges.Add(edge);
                queue.Enqueue(new QueryState(nextNodeId, state.SourceNodeId, state, edge, state.Hops + 1, remainingCallDepth, callStack));
            }
        }

        var nodes = selectedNodeIds
            .Select(nodeId => _graph.GetNode(nodeId))
            .Where(node => node.HasValue)
            .Select(node => node.GetValueOrDefault())
            .OrderBy(node => node.NodeId)
            .ToArray();
        var edges = selectedEdges
            .OrderBy(edge => edge.Kind)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.TargetNodeId)
            .ToArray();
        var materializationStopwatch = Stopwatch.StartNew();
        var orderedPaths = paths
            .DistinctBy(path => string.Join("\u001f", path.NodeIds))
            .OrderBy(path => path.SourceNodeId)
            .ThenBy(path => path.TargetNodeId)
            .ThenBy(path => string.Join("\u001f", path.NodeIds), StringComparer.Ordinal)
            .ToArray();
        materializationStopwatch.Stop();
        var status = truncationReason is not null
            ? CpgQueryStatus.Truncated
            : targetNodeIds is not null && orderedPaths.Length == 0
                ? CpgQueryStatus.Disconnected
                : CpgQueryStatus.Complete;
        return new CpgRelationQueryResult(
            nodes,
            edges,
            orderedPaths,
            status,
            truncationReason,
            Array.Empty<CpgShardUnavailableResult>(),
            requiredCapabilities,
            WasCacheHit: false,
            selectedNodeIds.Count,
            visitedEdgeCount,
            LoadedShardBytes: 0,
            Metrics: new CpgRelationQueryMetrics(0, materializationStopwatch.ElapsedMilliseconds, 0, 0));
    }

    private IReadOnlyList<NLCPGNode> ResolveNodes(CpgNodeSelector selector)
    {
        return _graph.Nodes
            .Where(node => node.NodeId.HasValue && selector.Matches(_graph, node))
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    private IEnumerable<NLCPGEdge> GetAdjacentEdges(
        NodeId nodeId,
        CpgQueryDirection direction,
        IReadOnlySet<NLCPGEdgeKind> allowedKinds)
    {
        var edges = direction switch
        {
            CpgQueryDirection.Incoming => _graph.GetIncomingEdges(nodeId),
            CpgQueryDirection.Outgoing => _graph.GetOutgoingEdges(nodeId),
            CpgQueryDirection.Bidirectional => _graph.GetIncomingEdges(nodeId)
                .Concat(_graph.GetOutgoingEdges(nodeId)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
        return edges
            .Where(edge => allowedKinds.Contains(edge.Kind))
            .OrderBy(edge => edge.Kind)
            .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceNodeId)
            .ThenBy(edge => edge.TargetNodeId);
    }

    private static NodeId GetNextNodeId(NodeId currentNodeId, NLCPGEdge edge, CpgQueryDirection direction)
    {
        return direction switch
        {
            CpgQueryDirection.Incoming => edge.SourceNodeId,
            CpgQueryDirection.Outgoing => edge.TargetNodeId,
            CpgQueryDirection.Bidirectional => edge.SourceNodeId == currentNodeId
                ? edge.TargetNodeId
                : edge.SourceNodeId,
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    private static CpgRelationPath CreatePath(QueryState state)
    {
        var nodeIds = new List<NodeId>();
        var edges = new List<NLCPGEdge>();
        for (var current = state; current is not null; current = current.Parent)
        {
            nodeIds.Add(current.NodeId);
            if (current.Edge.HasValue)
            {
                edges.Add(current.Edge.Value);
            }
        }

        nodeIds.Reverse();
        edges.Reverse();
        return new CpgRelationPath(state.SourceNodeId, state.NodeId, nodeIds, edges);
    }

    private static CpgRelationQueryResult CreateUnavailableResult(NLCPGCapability requiredCapabilities)
    {
        return new CpgRelationQueryResult(
            Array.Empty<NLCPGNode>(),
            Array.Empty<NLCPGEdge>(),
            Array.Empty<CpgRelationPath>(),
            CpgQueryStatus.Unavailable,
            null,
            Array.Empty<CpgShardUnavailableResult>(),
            requiredCapabilities,
            WasCacheHit: false,
            VisitedNodeCount: 0,
            VisitedEdgeCount: 0,
            LoadedShardBytes: 0);
    }

    private static void Validate(CpgRelationQuery query)
    {
        if (query.Direction == CpgQueryDirection.Bidirectional &&
            !CpgRelationProfiles.AllowsBidirectional(query.Profile))
        {
            throw new ArgumentException("The selected relation profile does not permit bidirectional traversal.", nameof(query));
        }

        if (query.Budget.MaxHops < 0 || query.Budget.MaxPaths <= 0 ||
            query.Budget.MaxDefinitions <= 0 || query.Budget.MaxVisitedNodes <= 0 ||
            query.Budget.MaxVisitedEdges <= 0 || query.Budget.MaxCachedStates < 0 ||
            query.Budget.MaxCallerFanout <= 0 || query.Budget.MaxCallDepth < 0 ||
            query.Budget.MaxLoadedShardBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Query budgets must be non-negative with positive result and visit limits.");
        }
    }

    private sealed record QueryState(
        NodeId NodeId,
        NodeId SourceNodeId,
        QueryState? Parent,
        NLCPGEdge? Edge,
        int Hops,
        int RemainingCallDepth,
        string CallStack);

    private static string GetCallSiteFrame(NLCPGEdge edge)
    {
        if (edge.CallSiteContext is { } callSiteContext)
        {
            return callSiteContext.ToContextId().Value;
        }

        return edge.ContextId is { } contextId && !string.IsNullOrEmpty(contextId.Value)
            ? contextId.Value
            : $"edge:{edge.SourceNodeId}>{edge.TargetNodeId}";
    }

    private static bool ContainsCallSiteFrame(string callStack, string frame)
    {
        return callStack.Split('\u001f', StringSplitOptions.RemoveEmptyEntries)
            .Contains(frame, StringComparer.Ordinal);
    }

    private readonly record struct QueryKey(
        string GraphSnapshotVersion,
        CpgRelationProfile Profile,
        CpgQueryDirection Direction,
        string Source,
        string Target,
        NLCPGTraversalBudget Budget,
        NLCPGCapability RequiredCapabilities,
        CpgQueryPurpose Purpose)
    {
        public static QueryKey Create(
            NLCPGGraph graph,
            CpgRelationQuery query,
            NLCPGCapability requiredCapabilities)
        {
            return new QueryKey(
                graph.GraphSnapshotVersion,
                query.Profile,
                query.Direction,
                query.Source.CacheKey,
                query.Target?.CacheKey ?? string.Empty,
                query.Budget,
                requiredCapabilities,
                query.Purpose);
        }
    }
}
