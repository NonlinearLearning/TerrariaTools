using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Analysis;

/// <summary>
/// Defines the bounded edge set and traversal limits for a backwards CPG slice.
/// </summary>
public sealed record RoslynCpgSliceQueryOptions(
    IReadOnlySet<RoslynCpgEdgeKind> AllowedEdgeKinds,
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxCallDepth = 0);

/// <summary>
/// Represents one stable source-to-sink path found by a CPG slice query.
/// </summary>
public sealed record RoslynCpgSlicePath(
    string SourceNodeId,
    string SinkNodeId,
    IReadOnlyList<string> NodeIds);

/// <summary>
/// Contains the bounded, deterministic result of a CPG slice query.
/// </summary>
public sealed record RoslynCpgSliceResult(
    IReadOnlyList<RoslynCpgSlicePath> Paths,
    bool WasTruncated,
    string? TruncationReason,
    long VisitedNodeCount,
    long VisitedEdgeCount);
