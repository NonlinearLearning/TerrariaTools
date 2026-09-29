using NLCPG.Contracts;
using NLCPG.Model;
using NLCPG.Persistence;
using System.Diagnostics;

namespace NLCPG.Analysis;

/// Executes the same typed relation protocol over bounded, on-demand shard loading.
public sealed class CpgShardRelationQueryService : ICpgRelationQueryService
{
    private readonly CpgShardQueryResolver _resolver;
    private readonly NLCPGCapability _availableCapabilities;

    public CpgShardRelationQueryService(
        CpgShardQueryResolver resolver,
        NLCPGCapability availableCapabilities)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _availableCapabilities = availableCapabilities;
    }

    public CpgRelationQueryResult Query(CpgRelationQuery query)
    {
        throw new InvalidOperationException("A shard-backed relation query must use QueryAsync.");
    }

    public async Task<CpgRelationQueryResult> QueryAsync(
        CpgRelationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);
        var requiredCapabilities = CpgRelationProfiles.GetRequiredCapabilities(query.Profile) |
            query.RequiredCapabilities;
        if ((_availableCapabilities & requiredCapabilities) != requiredCapabilities)
        {
            return Empty(CpgQueryStatus.Unavailable, requiredCapabilities, Array.Empty<CpgShardUnavailableResult>(), 0);
        }

        if (query.Source.NodeIds is null || query.Source.NodeIds.Count == 0)
        {
            return Empty(
                CpgQueryStatus.Unavailable,
                requiredCapabilities,
                new[] { new CpgShardUnavailableResult(NodeId.Empty, "sourceNodeIdRequired") },
                0);
        }

        var loaded = new Dictionary<string, CpgFrozenShard>(StringComparer.Ordinal);
        var unavailable = new List<CpgShardUnavailableResult>();
        var visited = new HashSet<NodeId>(query.Source.NodeIds);
        var frontier = query.Source.NodeIds.OrderBy(nodeId => nodeId).ToArray();
        long loadedBytes = 0;
        string? truncationReason = null;
        var allowedKinds = CpgRelationProfiles.GetAllowedEdgeKinds(query.Profile);
        var shardLoadStopwatch = Stopwatch.StartNew();
        for (var hop = 0; hop <= query.Budget.MaxHops && frontier.Length > 0; hop += 1)
        {
            var next = new SortedSet<NodeId>();
            foreach (var nodeId in frontier)
            {
                var resolved = await _resolver.FindResolvedByNodeAsync(nodeId, cancellationToken);
                if (resolved.Count == 0)
                {
                    unavailable.Add(new CpgShardUnavailableResult(nodeId, "anchorUnavailable"));
                    continue;
                }

                foreach (var shard in resolved)
                {
                    if (!loaded.ContainsKey(shard.Location.ShardId) &&
                        loadedBytes + shard.Location.ByteLength > query.Budget.MaxLoadedShardBytes)
                    {
                        truncationReason ??= "maxLoadedShardBytes";
                        continue;
                    }

                    if (loaded.TryAdd(shard.Location.ShardId, shard.Shard))
                    {
                        loadedBytes += shard.Location.ByteLength;
                    }

                    if (hop == query.Budget.MaxHops)
                    {
                        continue;
                    }

                    foreach (var neighbor in GetNeighbors(shard.Shard, nodeId, query.Direction, allowedKinds))
                    {
                        if (visited.Count >= query.Budget.MaxVisitedNodes)
                        {
                            truncationReason ??= "maxVisitedNodes";
                            break;
                        }

                        if (visited.Add(neighbor))
                        {
                            next.Add(neighbor);
                        }
                    }

                    if (truncationReason is not null)
                    {
                        break;
                    }
                }

                if (truncationReason is not null)
                {
                    break;
                }
            }

            if (truncationReason is not null)
            {
                break;
            }

            frontier = next.ToArray();
        }
        shardLoadStopwatch.Stop();

        var graph = BuildGraph(loaded.Values);
        var result = new CpgRelationQueryService(graph, _availableCapabilities).Query(query);
        var metrics = (result.Metrics ?? new CpgRelationQueryMetrics(0, 0, 0, 0)) with
        {
            ShardLoadElapsedMilliseconds = shardLoadStopwatch.ElapsedMilliseconds,
        };
        if (truncationReason is not null)
        {
            return result with
            {
                Status = CpgQueryStatus.Truncated,
                TruncationReason = truncationReason,
                UnavailableShards = unavailable,
                LoadedShardBytes = loadedBytes,
                Metrics = metrics,
            };
        }

        if (unavailable.Count > 0)
        {
            return result with
            {
                Status = CpgQueryStatus.Unavailable,
                UnavailableShards = unavailable,
                LoadedShardBytes = loadedBytes,
                Metrics = metrics,
            };
        }

        return result with { LoadedShardBytes = loadedBytes, Metrics = metrics };
    }

    private static NLCPGGraph BuildGraph(IEnumerable<CpgFrozenShard> shards)
    {
        var nodes = new Dictionary<NodeId, NLCPGNode>();
        var edges = new HashSet<NLCPGEdge>();
        var stringInterner = new StringInterner();
        var shardList = shards.ToArray();

        // 归并段与 ReadGraph(IEnumerable<CpgFrozenShard>) 共用同一份实现（原为逐字重复的循环）。
        // 分片序仍由本方法传入，故节点的插入序与改前逐位一致。
        CpgFrozenShardGraphReader.AccumulateShardGraphs(shardList, nodes, edges, stringInterner);

        foreach (var boundary in shardList.SelectMany(CpgFrozenShardGraphReader.ReadBoundaryEdges))
        {
            if (nodes.ContainsKey(boundary.SourceNodeId) && nodes.ContainsKey(boundary.TargetNodeId))
            {
                edges.Add(boundary);
            }
        }

        return NLCPGGraph.CreateFrozen(nodes.Values, edges, stringInterner);
    }

    private static IEnumerable<NodeId> GetNeighbors(
        CpgFrozenShard shard,
        NodeId nodeId,
        CpgQueryDirection direction,
        IReadOnlySet<NLCPGEdgeKind> allowedKinds)
    {
        var graph = CpgFrozenShardGraphReader.ReadGraph(shard);
        var edges = direction switch
        {
            CpgQueryDirection.Incoming => graph.GetIncomingEdges(nodeId),
            CpgQueryDirection.Outgoing => graph.GetOutgoingEdges(nodeId),
            CpgQueryDirection.Bidirectional => graph.GetIncomingEdges(nodeId).Concat(graph.GetOutgoingEdges(nodeId)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
        return edges
            .Concat(CpgFrozenShardGraphReader.ReadBoundaryEdges(shard))
            .Where(edge => allowedKinds.Contains(edge.Kind))
            .Where(edge => direction != CpgQueryDirection.Incoming || edge.TargetNodeId == nodeId)
            .Where(edge => direction != CpgQueryDirection.Outgoing || edge.SourceNodeId == nodeId)
            .Select(edge => edge.SourceNodeId == nodeId ? edge.TargetNodeId : edge.SourceNodeId);
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

    private static CpgRelationQueryResult Empty(
        CpgQueryStatus status,
        NLCPGCapability capabilities,
        IReadOnlyList<CpgShardUnavailableResult> unavailable,
        long loadedShardBytes)
    {
        return new CpgRelationQueryResult(
            Array.Empty<NLCPGNode>(),
            Array.Empty<NLCPGEdge>(),
            Array.Empty<CpgRelationPath>(),
            status,
            null,
            unavailable,
            capabilities,
            WasCacheHit: false,
            VisitedNodeCount: 0,
            VisitedEdgeCount: 0,
            loadedShardBytes);
    }
}
