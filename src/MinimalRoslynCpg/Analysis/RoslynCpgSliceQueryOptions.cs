using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// Defines the bounded edge set and traversal limits for a backwards CPG slice.
/// </summary>
public sealed record RoslynCpgSliceQueryOptions(
    IReadOnlySet<RoslynCpgEdgeKind> AllowedEdgeKinds,
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxCallDepth = 0,
    int MaxVisitedNodes = int.MaxValue,
    int MaxVisitedEdges = int.MaxValue,
    int MaxCachedStates = 4096,
    int MaxCallerFanout = int.MaxValue);

/// <summary>
/// Defines every traversal limit that participates in query semantics and caching.
/// </summary>
public sealed record RoslynCpgTraversalBudget(
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxVisitedNodes,
    int MaxVisitedEdges);

/// <summary>
/// Reports deterministic traversal work and cache activity for one query execution.
/// </summary>
public sealed record RoslynCpgQueryTelemetry(
    long VisitedNodeCount,
    long VisitedEdgeCount,
    long MaterializedPathCount,
    bool WasTruncated,
    string? TruncationReason,
    long CacheHitCount,
    long CacheMissCount,
    int MaxObservedCallDepth = 0,
    long InterproceduralBridgeExpansionCount = 0,
    long CacheCapacityBypassCount = 0);

/// <summary>
/// Represents one stable source-to-sink path found by a CPG slice query.
/// </summary>
public sealed record RoslynCpgSlicePath(
    NodeId SourceNodeId,
    NodeId SinkNodeId,
    IReadOnlyList<NodeId> NodeIds);

public sealed record CpgShardUnavailableResult(NodeId NodeId, string Reason);

/// <summary>
/// Contains the bounded, deterministic result of a CPG slice query.
/// </summary>
public sealed record RoslynCpgSliceResult(
    IReadOnlyList<RoslynCpgSlicePath> Paths,
    bool WasTruncated,
    string? TruncationReason,
    long VisitedNodeCount,
    long VisitedEdgeCount,
    RoslynCpgQueryTelemetry? Telemetry = null,
    IReadOnlyList<CpgShardUnavailableResult>? UnavailableShards = null,
    CpgShardQueryTelemetry? ShardTelemetry = null);
