using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

public sealed record CpgPassPerformanceFact(string PassId, long? ElapsedMilliseconds)
{
  public string StageId => PassId.StartsWith("CPG.", StringComparison.Ordinal)
    ? PassId
    : $"CPG.{PassId}";

  public long? WallElapsedMs => ElapsedMilliseconds;
}

public sealed record CpgAnchorDiscoveryFacts(
  int? AnchorCount,
  long? ElapsedMilliseconds,
  IReadOnlyList<CpgPassPerformanceFact>? PassSamples = null)
{
  public IReadOnlyList<CpgPassPerformanceFact> PassSamples { get; init; } =
    new ReadOnlyCollection<CpgPassPerformanceFact>(
      (PassSamples ?? Array.Empty<CpgPassPerformanceFact>()).ToArray());

  public long? WallElapsedMs => ElapsedMilliseconds;
}

public sealed record CpgPersistencePerformanceFacts
{
  public CpgPersistencePerformanceFacts(
    bool restoreAttempted,
    bool restoreHit,
    long? restoreElapsedMs,
    long? catalogReadMs,
    long? shardReadMs,
    int? restoredShardCount,
    long? restoredShardBytes,
    long? persistElapsedMs,
    long? fileWriteMs,
    long? catalogWriteMs,
    long? routingIndexWriteMs,
    int? primaryShardCount,
    long? primaryShardBytes,
    int? boundaryAdjacencyShardCount,
    long? boundaryAdjacencyShardBytes,
    long? boundaryEdgeCount,
    int? reusedShardCount,
    int? reuseMissCount,
    int? reuseRejectedCount,
    long? reusedShardBytes,
    int? peakConcurrentFileWrites,
    int? peakConcurrentShardExports,
    int? peakReorderBuffer,
    int? peakBufferedBoundaryEdges,
    int? catalogBatchCount,
    long? catalogRowCount,
    string? provenance = null)
  {
    RestoreAttempted = restoreAttempted;
    RestoreHit = restoreHit;
    RestoreElapsedMs = restoreElapsedMs;
    CatalogReadMs = catalogReadMs;
    ShardReadMs = shardReadMs;
    RestoredShardCount = restoredShardCount;
    RestoredShardBytes = restoredShardBytes;
    PersistElapsedMs = persistElapsedMs;
    FileWriteMs = fileWriteMs;
    CatalogWriteMs = catalogWriteMs;
    RoutingIndexWriteMs = routingIndexWriteMs;
    PrimaryShardCount = primaryShardCount;
    PrimaryShardBytes = primaryShardBytes;
    BoundaryAdjacencyShardCount = boundaryAdjacencyShardCount;
    BoundaryAdjacencyShardBytes = boundaryAdjacencyShardBytes;
    BoundaryEdgeCount = boundaryEdgeCount;
    ReusedShardCount = reusedShardCount;
    ReuseMissCount = reuseMissCount;
    ReuseRejectedCount = reuseRejectedCount;
    ReusedShardBytes = reusedShardBytes;
    PeakConcurrentFileWrites = peakConcurrentFileWrites;
    PeakConcurrentShardExports = peakConcurrentShardExports;
    PeakReorderBuffer = peakReorderBuffer;
    PeakBufferedBoundaryEdges = peakBufferedBoundaryEdges;
    CatalogBatchCount = catalogBatchCount;
    CatalogRowCount = catalogRowCount;
    Provenance = provenance;
  }

  public bool RestoreAttempted { get; }

  public bool RestoreHit { get; }

  public long? RestoreElapsedMs { get; }

  public long? CatalogReadMs { get; }

  public long? ShardReadMs { get; }

  public int? RestoredShardCount { get; }

  public long? RestoredShardBytes { get; }

  public long? PersistElapsedMs { get; }

  public long? FileWriteMs { get; }

  public long? CatalogWriteMs { get; }

  public long? RoutingIndexWriteMs { get; }

  public int? PrimaryShardCount { get; }

  public long? PrimaryShardBytes { get; }

  public int? BoundaryAdjacencyShardCount { get; }

  public long? BoundaryAdjacencyShardBytes { get; }

  public long? BoundaryEdgeCount { get; }

  public int? ReusedShardCount { get; }

  public int? ReuseMissCount { get; }

  public int? ReuseRejectedCount { get; }

  public long? ReusedShardBytes { get; }

  public int? PeakConcurrentFileWrites { get; }

  public int? PeakConcurrentShardExports { get; }

  public int? PeakReorderBuffer { get; }

  public int? PeakBufferedBoundaryEdges { get; }

  public int? CatalogBatchCount { get; }

  public long? CatalogRowCount { get; }

  public string? Provenance { get; }
}

public sealed record CpgDataFlowMethodPerformanceFact(
  string MethodName,
  int? FlowNodeCount,
  int? WordsPerSet,
  int? DefinitionCount,
  int? WorklistIterations,
  int? RawCandidateCount,
  int? UniqueCandidateCount,
  string? OverflowReason);

public sealed record CpgPerformanceFacts
{
  public CpgPerformanceFacts(
    string itemId,
    string? sourceIdentity,
    long? buildElapsedMs,
    IReadOnlyList<CpgPassPerformanceFact>? passSamples = null,
    CpgAnchorDiscoveryFacts? anchorDiscovery = null,
    CpgPersistencePerformanceFacts? persistence = null,
    IReadOnlyDictionary<string, long>? cacheCounters = null,
    IReadOnlyList<CpgDataFlowMethodPerformanceFact>? dataFlowMethodSamples = null,
    int? nodeCount = null,
    int? edgeCount = null,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    if (string.IsNullOrWhiteSpace(itemId))
    {
      throw new ArgumentException("A CPG performance item ID cannot be empty.", nameof(itemId));
    }

    ItemId = itemId;
    SourceIdentity = sourceIdentity;
    BuildElapsedMs = buildElapsedMs;
    PassSamples = new ReadOnlyCollection<CpgPassPerformanceFact>(
      (passSamples ?? Array.Empty<CpgPassPerformanceFact>()).ToArray());
    AnchorDiscovery = anchorDiscovery;
    Persistence = persistence;
    CacheCounters = new ReadOnlyDictionary<string, long>(
      new Dictionary<string, long>(
        cacheCounters ?? new Dictionary<string, long>(),
        StringComparer.Ordinal));
    DataFlowMethodSamples = new ReadOnlyCollection<CpgDataFlowMethodPerformanceFact>(
      (dataFlowMethodSamples ?? Array.Empty<CpgDataFlowMethodPerformanceFact>()).ToArray());
    NodeCount = nodeCount;
    EdgeCount = edgeCount;
    Status = status;
    ErrorKind = errorKind;
  }

  public string ItemId { get; }

  public string? SourceIdentity { get; }

  public long? BuildElapsedMs { get; }

  public IReadOnlyList<CpgPassPerformanceFact> PassSamples { get; }

  public CpgAnchorDiscoveryFacts? AnchorDiscovery { get; }

  public CpgPersistencePerformanceFacts? Persistence { get; }

  public IReadOnlyDictionary<string, long> CacheCounters { get; }

  public IReadOnlyList<CpgDataFlowMethodPerformanceFact> DataFlowMethodSamples { get; }

  public int? NodeCount { get; }

  public int? EdgeCount { get; }

  public PerformanceStatus Status { get; }

  public string? ErrorKind { get; }

  public bool IsAvailable => Status == PerformanceStatus.Completed;

  public static CpgPerformanceFacts Unavailable(string itemId, string? errorKind = null)
  {
    return new CpgPerformanceFacts(
      itemId,
      null,
      null,
      status: PerformanceStatus.Unavailable,
      errorKind: errorKind);
  }
}
