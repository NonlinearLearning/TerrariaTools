namespace NLCPG.Builder;

public sealed record CpgPersistenceProvenance(
  string SourceHash,
  string ProfileHash,
  int SchemaVersion,
  string CompilerIdentity,
  string CapabilityFingerprint);

public sealed record NLCPGPersistenceMetrics(
  bool RestoreAttempted,
  bool RestoreHit,
  long RestoreElapsedMilliseconds,
  long CatalogReadMilliseconds,
  long ShardReadMilliseconds,
  int RestoredShardCount,
  long RestoredShardBytes,
  long PersistElapsedMilliseconds,
  long FileWriteMilliseconds,
  long CatalogWriteMilliseconds,
  long RoutingIndexWriteMilliseconds,
  int PrimaryShardCount,
  long PrimaryShardBytes,
  int BoundaryAdjacencyShardCount,
  long BoundaryAdjacencyShardBytes,
  long BoundaryEdgeCount,
  int ReusedShardCount,
  int ReuseMissCount,
  int ReuseRejectedCount,
  long ReusedShardBytes,
  int PeakConcurrentFileWrites,
  int PeakConcurrentShardExports,
  int PeakReorderBuffer,
  int PeakBufferedBoundaryEdges,
  int CatalogBatchCount,
  long CatalogRowCount,
  CpgPersistenceProvenance? Provenance = null)
{
    public long RestoreFactsElapsedMilliseconds { get; init; }

    public long RestoreFactsAllocatedBytes { get; init; }

    public long RestoreGraphImportElapsedMilliseconds { get; init; }

    public long RestoreGraphImportAllocatedBytes { get; init; }

    public static NLCPGPersistenceMetrics Empty { get; } = new(
      RestoreAttempted: false,
      RestoreHit: false,
      RestoreElapsedMilliseconds: 0,
      CatalogReadMilliseconds: 0,
      ShardReadMilliseconds: 0,
      RestoredShardCount: 0,
      RestoredShardBytes: 0,
      PersistElapsedMilliseconds: 0,
      FileWriteMilliseconds: 0,
      CatalogWriteMilliseconds: 0,
      RoutingIndexWriteMilliseconds: 0,
      PrimaryShardCount: 0,
      PrimaryShardBytes: 0,
      BoundaryAdjacencyShardCount: 0,
      BoundaryAdjacencyShardBytes: 0,
      BoundaryEdgeCount: 0,
      ReusedShardCount: 0,
      ReuseMissCount: 0,
      ReuseRejectedCount: 0,
      ReusedShardBytes: 0,
      PeakConcurrentFileWrites: 0,
      PeakConcurrentShardExports: 0,
      PeakReorderBuffer: 0,
      PeakBufferedBoundaryEdges: 0,
      CatalogBatchCount: 0,
      CatalogRowCount: 0);

    public int ReuseAttemptCount => ReusedShardCount + ReuseMissCount + ReuseRejectedCount;

    public double ReuseHitRate => ReuseAttemptCount == 0
      ? 0
      : (double)ReusedShardCount / ReuseAttemptCount;
}

public sealed record NLCPGBuildMetrics(
  int OperationNodeCacheHitCount,
  int OperationNodeCacheMissCount,
  int OperationRootCacheHitCount,
  int OperationRootCacheMissCount,
  int OperationInventoryCount,
  int NodeCount,
  int EdgeCount,
  long ElapsedMilliseconds,
  int AnchorDiscoveryAnchorCount = 0,
  long AnchorDiscoveryElapsedMilliseconds = 0,
  IReadOnlyDictionary<string, long>? PassElapsedMilliseconds = null,
  NLCPGPersistenceMetrics? PersistenceMetrics = null,
  IReadOnlyList<NLCPGDataFlowMethodMetrics>? DataFlowMethodMetrics = null,
  CpgBuildInventoryMetrics? BuildInventoryMetrics = null,
  IReadOnlyDictionary<string, long>? AnchorDiscoveryPassElapsedMilliseconds = null)
{
    public static NLCPGBuildMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record NLCPGDataFlowMethodMetrics(
  string MethodName,
  int FlowNodeCount,
  int WordsPerSet,
  int DefinitionCount,
  int WorklistIterations,
  int RawCandidateCount,
  int UniqueCandidateCount,
  NLCPGDataFlowOverflowReason OverflowReason);
