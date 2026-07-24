using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder;

public enum RoslynCpgBuilderMode
{
  Partitioned
}

public enum RoslynCpgSyntaxPassMode
{
  Partitioned
}

public sealed record RoslynCpgBuilderOptions(
  RoslynCpgBuilderMode BuildMode,
  int MaxDegreeOfParallelism,
  int LargeFileLineThreshold,
  int LargeFileMethodThreshold,
  int LargeMethodLineSpanThreshold,
  bool EnableReferencedSymbolTypeReuse = true,
  bool EnableOperationBackedSyntaxTypes = true,
  RoslynCpgSyntaxPassMode SyntaxPassMode = RoslynCpgSyntaxPassMode.Partitioned,
  int SyntaxLargeFileLineThreshold = 800,
  IReadOnlyCollection<RoslynCpgCapability>? RequestedCapabilities = null,
  RoslynCpgDataFlowOptions? DataFlowOptions = null,
  RoslynCpgInterproceduralDataFlowOptions? InterproceduralDataFlowOptions = null,
  CpgPersistenceOptions? Persistence = null,
  bool UsePreallocatedNodeIds = false,
  int? OrderedResultReorderAllowance = null,
  int MaxOrderedResultRecordCount = 250_000)
{
  public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

  public int EffectiveOrderedResultReorderAllowance =>
    Math.Max(0, OrderedResultReorderAllowance ?? EffectiveMaxDegreeOfParallelism);

  public int EffectiveMaxOrderedResultRecordCount => Math.Max(1, MaxOrderedResultRecordCount);

  public RoslynCpgDataFlowOptions EffectiveDataFlowOptions =>
    DataFlowOptions ?? RoslynCpgDataFlowOptions.Unbounded;

  public RoslynCpgInterproceduralDataFlowOptions EffectiveInterproceduralDataFlowOptions =>
    InterproceduralDataFlowOptions ?? RoslynCpgInterproceduralDataFlowOptions.Default;

  public static RoslynCpgBuilderOptions CreateDefault()
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      Math.Max(1, Environment.ProcessorCount),
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: true,
      EnableOperationBackedSyntaxTypes: true,
      SyntaxPassMode: RoslynCpgSyntaxPassMode.Partitioned,
      SyntaxLargeFileLineThreshold: 800,
      RequestedCapabilities: null);
  }
}

public sealed record CpgPersistenceOptions(
  string StoreRoot,
  string ProfileHash,
  int SchemaVersion = 1,
  CpgPersistenceDurabilityMode DurabilityMode = CpgPersistenceDurabilityMode.Strict,
  bool StreamingMode = false,
  int StreamingReadCacheCapacity = 8,
  int MaxBoundaryAdjacencyEdgesPerShard = 2048,
  int MaxCatalogBatchRows = 1024,
  int MaxCatalogBatchBytes = 1024 * 1024,
  int MaxPendingShardPublications = 16,
  int MaxConcurrentShardExports = 2,
  int MaxConcurrentShardFileWrites = 2,
  int StoreLockWaitMilliseconds = 30000)
{
  public void Validate()
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(StoreRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(ProfileHash);
    if (SchemaVersion <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(SchemaVersion));
    }

    if (StreamingReadCacheCapacity <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(StreamingReadCacheCapacity));
    }

    if (MaxBoundaryAdjacencyEdgesPerShard <= 0 || MaxCatalogBatchRows <= 0 ||
        MaxCatalogBatchBytes <= 0 || MaxPendingShardPublications <= 0 ||
        MaxConcurrentShardExports <= 0 ||
        MaxConcurrentShardFileWrites <= 0 || StoreLockWaitMilliseconds <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(MaxBoundaryAdjacencyEdgesPerShard));
    }
  }
}

public enum CpgPersistenceDurabilityMode
{
  Strict,
  Throughput,
}

public enum RoslynCpgDataFlowOverflowBehavior
{
  SkipMethod,
  FailBuild,
}

public enum RoslynCpgDataFlowOverflowReason
{
  None,
  DefinitionLimitExceeded,
  FlowNodeLimitExceeded,
  CandidateEdgeLimitExceeded,
}

public sealed record RoslynCpgDataFlowOptions(
  int MaxDefinitionsPerMethod,
  int MaxFlowNodesPerMethod = int.MaxValue,
  int MaxCandidateEdgesPerMethod = int.MaxValue,
  RoslynCpgDataFlowOverflowBehavior OverflowBehavior = RoslynCpgDataFlowOverflowBehavior.SkipMethod)
{
  public static RoslynCpgDataFlowOptions Unbounded { get; } = new(int.MaxValue);
}

public sealed record RoslynCpgInterproceduralDataFlowOptions(
  int MaxCallTargetsPerSite = 1,
  int MaxBoundaryEdgesPerMethod = 10000)
{
  public static RoslynCpgInterproceduralDataFlowOptions Default { get; } = new();
}

public sealed record RoslynCpgMethodDataFlowTelemetry(
  string MethodFullName,
  int DefinitionCount,
  int FlowNodeCount,
  int FixpointIterations,
  int UnreachableNodeCount,
  int GeneratedCandidateCount,
  RoslynCpgDataFlowOverflowReason OverflowReason);

public sealed record RoslynCpgBuildTelemetry(
  RoslynCpgBuilderMode RequestedMode,
  RoslynCpgBuilderMode ExecutedMode,
  bool UsedPartitionedOperationBuild,
  bool UsedPartitionedSyntaxPass,
  int SourceLineCount,
  int PartitionCount,
  int MaxDegreeOfParallelism,
  long OperationBuildElapsedMilliseconds,
  long SyntaxBuildElapsedMilliseconds,
  long DataFlowBuildElapsedMilliseconds,
  long FreezeQueryIndexElapsedMilliseconds,
  RoslynCpgFreezeTelemetry FreezeTelemetry,
  RoslynCpgSyntaxPassTelemetry SyntaxPassTelemetry,
  RoslynCpgMethodDecorationTelemetry MethodDecorationTelemetry,
  RoslynCpgDataFlowPassTelemetry DataFlowPassTelemetry,
  int OperationChildBufferRentCount = 0,
  IReadOnlyCollection<RoslynCpgCapability>? ResolvedCapabilities = null,
  IReadOnlyList<string>? ExecutedPassNames = null,
  IReadOnlyList<string>? SkippedPassNames = null,
  string GraphSnapshotVersion = "legacy-v1",
  RoslynCpgInterproceduralDataFlowTelemetry? InterproceduralDataFlowTelemetry = null,
  int GraphNodeCount = 0,
  int GraphEdgeCount = 0,
  RoslynCpgStreamingFragmentTelemetry? StreamingFragments = null,
  RoslynCpgOperationFragmentTelemetry? OperationFragments = null,
  RoslynCpgPreallocationTelemetry? Preallocation = null,
  CpgPersistenceTelemetry? Persistence = null,
  RoslynCpgOrderedWorkWindowTelemetry? OperationOrderedWindow = null,
  RoslynCpgOrderedWorkWindowTelemetry? CfgSensitiveOrderedWindow = null)
{
  public static RoslynCpgBuildTelemetry CreateDefault()
  {
    return new RoslynCpgBuildTelemetry(
      RoslynCpgBuilderMode.Partitioned,
      RoslynCpgBuilderMode.Partitioned,
      UsedPartitionedOperationBuild: true,
      UsedPartitionedSyntaxPass: true,
      SourceLineCount: 0,
      PartitionCount: 0,
      MaxDegreeOfParallelism: 1,
      OperationBuildElapsedMilliseconds: 0,
      SyntaxBuildElapsedMilliseconds: 0,
      DataFlowBuildElapsedMilliseconds: 0,
      FreezeQueryIndexElapsedMilliseconds: 0,
      FreezeTelemetry: RoslynCpgFreezeTelemetry.CreateDefault(),
      SyntaxPassTelemetry: RoslynCpgSyntaxPassTelemetry.CreateDefault(),
      MethodDecorationTelemetry: RoslynCpgMethodDecorationTelemetry.CreateDefault(),
      DataFlowPassTelemetry: RoslynCpgDataFlowPassTelemetry.CreateDefault(),
      ResolvedCapabilities: Array.Empty<RoslynCpgCapability>(),
      ExecutedPassNames: Array.Empty<string>(),
      SkippedPassNames: Array.Empty<string>(),
      GraphSnapshotVersion: "legacy-v1",
      InterproceduralDataFlowTelemetry: RoslynCpgInterproceduralDataFlowTelemetry.CreateDefault());
  }
}

public sealed record RoslynCpgPreallocationTelemetry(
  bool UsedCompatibilityPreflight,
  int StableAnchorCount,
  long ElapsedMilliseconds,
  bool UsedAnchorDiscovery = false);

public sealed record RoslynCpgOperationFragmentTelemetry(
  int CommittedFragmentCount,
  int ReleasedFragmentCount,
  int PeakBufferedFragmentCount,
  bool ReleasedBuilderOperationState)
{
  public static RoslynCpgOperationFragmentTelemetry CreateDefault()
  {
    return new RoslynCpgOperationFragmentTelemetry(
      CommittedFragmentCount: 0,
      ReleasedFragmentCount: 0,
      PeakBufferedFragmentCount: 0,
      ReleasedBuilderOperationState: false);
  }
}

public sealed record RoslynCpgOrderedWorkWindowTelemetry(
  int ActiveWorkerPeak,
  int CompletedButUncommittedPeak,
  int CompletedRecordCountPeak,
  long CommitWaitMilliseconds,
  long WindowBlockedMilliseconds)
{
  public static RoslynCpgOrderedWorkWindowTelemetry CreateDefault()
  {
    return new RoslynCpgOrderedWorkWindowTelemetry(0, 0, 0, 0, 0);
  }
}

public sealed record RoslynCpgStreamingFragmentTelemetry(
  IReadOnlyList<int> PublishedOrders,
  IReadOnlyList<string> PublishedKinds,
  int ReleasedFragmentCount,
  int PeakRetainedFragmentCount)
{
  public static RoslynCpgStreamingFragmentTelemetry CreateDefault()
  {
    return new RoslynCpgStreamingFragmentTelemetry(
      Array.Empty<int>(),
      Array.Empty<string>(),
      ReleasedFragmentCount: 0,
      PeakRetainedFragmentCount: 0);
  }
}

public sealed record CpgPersistenceTelemetry(
  int PrimaryShardCount,
  int BoundaryAdjacencyShardCount,
  long PrimaryShardBytes,
  long BoundaryAdjacencyShardBytes,
  long BoundaryEdgeCount,
  long CatalogRowCount,
  int CatalogBatchCount,
  long CatalogQueueWaitMilliseconds,
  long CatalogCommitMilliseconds,
  long FileWriteMilliseconds,
  int PeakQueueDepth,
  int PeakRouterBuffer,
  int SessionInvalidationCount,
  int PeakConcurrentFileWrites = 0,
  int PeakConcurrentShardExports = 0,
  long StoreLockWaitMilliseconds = 0,
  long SerializationMilliseconds = 0,
  long ValidationMilliseconds = 0,
  long FlushMilliseconds = 0,
  IReadOnlyList<int>? CatalogBatchRows = null,
  int ReusedShardCount = 0,
  int ReuseMissCount = 0,
  int ReuseRejectedCount = 0,
  long ReusedShardBytes = 0,
  long ReadBackMilliseconds = 0,
  long HashMilliseconds = 0,
  long StructuralValidationMilliseconds = 0,
  IReadOnlyList<int>? CatalogBatchPublicationCounts = null,
  IReadOnlyList<int>? CatalogBatchEstimatedMetadataBytes = null,
  int PeakReorderBuffer = 0)
{
  public static CpgPersistenceTelemetry CreateDefault() => new(
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    CatalogBatchRows: Array.Empty<int>(),
    CatalogBatchPublicationCounts: Array.Empty<int>(),
    CatalogBatchEstimatedMetadataBytes: Array.Empty<int>(),
    PeakReorderBuffer: 0);
}

public sealed record RoslynCpgInterproceduralDataFlowTelemetry(
  int BridgeEdgeCount,
  int CutCount,
  IReadOnlyDictionary<string, int> CutCountByReason)
{
  public static RoslynCpgInterproceduralDataFlowTelemetry CreateDefault()
  {
    return new RoslynCpgInterproceduralDataFlowTelemetry(
      BridgeEdgeCount: 0,
      CutCount: 0,
      CutCountByReason: new Dictionary<string, int>(StringComparer.Ordinal));
  }
}

public sealed record RoslynCpgMethodDecorationTelemetry(
  int SyntaxNodeCount,
  int DeclaredSymbolQueryCount)
{
  public static RoslynCpgMethodDecorationTelemetry CreateDefault()
  {
    return new RoslynCpgMethodDecorationTelemetry(
      SyntaxNodeCount: 0,
      DeclaredSymbolQueryCount: 0);
  }
}

public sealed record RoslynCpgSyntaxPassTelemetry(
  long TotalElapsedMilliseconds,
  long TraversalElapsedMilliseconds,
  long CreateSyntaxNodeElapsedMilliseconds,
  long EmitChildTokensElapsedMilliseconds,
  long AddDeclaredSymbolEdgesElapsedMilliseconds,
  long AddReferencedSymbolEdgesElapsedMilliseconds,
  long AddTypeInfoElapsedMilliseconds,
  long ResolveTypeInfoElapsedMilliseconds,
  long AddSyntaxTypeEdgesElapsedMilliseconds,
  long AddTypeReferenceEdgesElapsedMilliseconds,
  int TypeInfoQueryCount,
  int TypeInfoResolvedCount,
  int TypeInfoSymbolReuseCount,
  int DeclaredSymbolQueryCount,
  int DeclaredSymbolResolvedCount,
  int SyntaxNodeCount,
  int SyntaxTokenCount,
  int SyntaxPartitionCount,
  int SyntaxPartitionMaxDegreeOfParallelism,
  int OperationBackedTypeInfoDeferredCount,
  int OperationBackedTypeInfoResolvedCount,
  int OperationBackedTypeInfoFallbackCount,
  long OperationBackedTypeInfoFallbackElapsedMilliseconds,
  long OperationBackedTypeInfoFallbackElapsedTicks,
  int OperationBackedTypeInfoMissingOperationCount,
  int OperationBackedTypeInfoNullOperationTypeCount,
  IReadOnlyDictionary<string, int> OperationBackedTypeInfoFallbackCountBySyntaxKind)
{
  public static RoslynCpgSyntaxPassTelemetry CreateDefault()
  {
    return new RoslynCpgSyntaxPassTelemetry(
      TotalElapsedMilliseconds: 0,
      TraversalElapsedMilliseconds: 0,
      CreateSyntaxNodeElapsedMilliseconds: 0,
      EmitChildTokensElapsedMilliseconds: 0,
      AddDeclaredSymbolEdgesElapsedMilliseconds: 0,
      AddReferencedSymbolEdgesElapsedMilliseconds: 0,
      AddTypeInfoElapsedMilliseconds: 0,
      ResolveTypeInfoElapsedMilliseconds: 0,
      AddSyntaxTypeEdgesElapsedMilliseconds: 0,
      AddTypeReferenceEdgesElapsedMilliseconds: 0,
      TypeInfoQueryCount: 0,
      TypeInfoResolvedCount: 0,
      TypeInfoSymbolReuseCount: 0,
      DeclaredSymbolQueryCount: 0,
      DeclaredSymbolResolvedCount: 0,
      SyntaxNodeCount: 0,
      SyntaxTokenCount: 0,
      SyntaxPartitionCount: 0,
      SyntaxPartitionMaxDegreeOfParallelism: 1,
      OperationBackedTypeInfoDeferredCount: 0,
      OperationBackedTypeInfoResolvedCount: 0,
      OperationBackedTypeInfoFallbackCount: 0,
      OperationBackedTypeInfoFallbackElapsedMilliseconds: 0,
      OperationBackedTypeInfoFallbackElapsedTicks: 0,
      OperationBackedTypeInfoMissingOperationCount: 0,
      OperationBackedTypeInfoNullOperationTypeCount: 0,
      OperationBackedTypeInfoFallbackCountBySyntaxKind: new Dictionary<string, int>(StringComparer.Ordinal));
  }
}

public sealed record RoslynCpgDataFlowPassTelemetry(
  long TotalElapsedMilliseconds,
  long EnumerateMethodBlocksElapsedMilliseconds,
  long EnumerateOrderedOperationsElapsedMilliseconds,
  long CfgSensitiveElapsedMilliseconds,
  long ValueSourceEdgeElapsedMilliseconds,
  long ReturnFlowEdgeElapsedMilliseconds,
  long TerminalFlowEdgeElapsedMilliseconds,
  long CallArgumentAndReturnElapsedMilliseconds,
  long BuildFlowNeighborsElapsedMilliseconds,
  long FixpointElapsedMilliseconds,
  long ReachingDefinitionEdgeElapsedMilliseconds,
  int MethodBlockCount,
  int OrderedOperationCount,
  long PrepareFlowNodesElapsedMilliseconds,
  long CollectUsedFactsElapsedMilliseconds,
  long CreateDefinitionFactsElapsedMilliseconds,
  long InitializeCfgSensitiveStateElapsedMilliseconds,
  int FlowNodeCount,
  int UsedFactCount,
  int DefinitionFactCount,
  int UsedFactPartitionCount,
  int UsedFactPartitionMaxDegreeOfParallelism,
  int CfgSensitivePartitionCount,
  int CfgSensitivePartitionMaxDegreeOfParallelism,
  long CfgSensitiveCandidateGenerationElapsedMilliseconds = 0,
  long CfgSensitiveCandidateCommitElapsedMilliseconds = 0,
  int PeakBufferedCandidateBatchCount = 0,
  int CandidateEdgeCount = 0,
  int FrozenOperationNodeCount = 0,
  int MethodOperationNodeProjectionCount = 0,
  int UsedFactRecordCount = 0,
  long PrepareFlowNodesElapsedTicks = 0,
  long CollectUsedFactsElapsedTicks = 0,
  int SkippedMethodCount = 0,
  IReadOnlyList<RoslynCpgMethodDataFlowTelemetry>? MethodTelemetry = null,
  int ReleasedCfgSensitivePlanCount = 0)
{
  public static RoslynCpgDataFlowPassTelemetry CreateDefault()
  {
    return new RoslynCpgDataFlowPassTelemetry(
      TotalElapsedMilliseconds: 0,
      EnumerateMethodBlocksElapsedMilliseconds: 0,
      EnumerateOrderedOperationsElapsedMilliseconds: 0,
      CfgSensitiveElapsedMilliseconds: 0,
      ValueSourceEdgeElapsedMilliseconds: 0,
      ReturnFlowEdgeElapsedMilliseconds: 0,
      TerminalFlowEdgeElapsedMilliseconds: 0,
      CallArgumentAndReturnElapsedMilliseconds: 0,
      BuildFlowNeighborsElapsedMilliseconds: 0,
      FixpointElapsedMilliseconds: 0,
      ReachingDefinitionEdgeElapsedMilliseconds: 0,
      MethodBlockCount: 0,
      OrderedOperationCount: 0,
      PrepareFlowNodesElapsedMilliseconds: 0,
      CollectUsedFactsElapsedMilliseconds: 0,
      CreateDefinitionFactsElapsedMilliseconds: 0,
      InitializeCfgSensitiveStateElapsedMilliseconds: 0,
      FlowNodeCount: 0,
      UsedFactCount: 0,
      DefinitionFactCount: 0,
      UsedFactPartitionCount: 0,
      UsedFactPartitionMaxDegreeOfParallelism: 1,
      CfgSensitivePartitionCount: 0,
      CfgSensitivePartitionMaxDegreeOfParallelism: 1,
      MethodTelemetry: Array.Empty<RoslynCpgMethodDataFlowTelemetry>());
  }
}
