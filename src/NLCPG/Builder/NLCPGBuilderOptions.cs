using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;

namespace NLCPG.Builder;

public enum NLCPGBuilderMode
{
    Partitioned
}

public enum NLCPGSyntaxPassMode
{
    Partitioned
}

public sealed record NLCPGBuilderOptions(
  NLCPGBuilderMode BuildMode,
  int MaxDegreeOfParallelism,
  int LargeFileLineThreshold,
  int LargeFileMethodThreshold,
  int LargeMethodLineSpanThreshold,
  bool EnableReferencedSymbolTypeReuse = true,
  bool EnableOperationBackedSyntaxTypes = true,
  NLCPGSyntaxPassMode SyntaxPassMode = NLCPGSyntaxPassMode.Partitioned,
  int SyntaxLargeFileLineThreshold = 800,
  IReadOnlyCollection<NLCPGCapability>? RequestedCapabilities = null,
  NLCPGDataFlowOptions? DataFlowOptions = null,
  NLCPGInterproceduralDataFlowOptions? InterproceduralDataFlowOptions = null,
  ICallFlowResolver? CallFlowResolver = null,
  NLCPGFlowSummaryOptions? FlowSummaryOptions = null,
  CpgPersistenceOptions? Persistence = null,
  bool UsePreallocatedNodeIds = false,
  int? OrderedResultReorderAllowance = null,
  int MaxOrderedResultRecordCount = 250_000)
{
    public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

    public int EffectiveOrderedResultReorderAllowance =>
      Math.Max(0, OrderedResultReorderAllowance ?? EffectiveMaxDegreeOfParallelism);

    public int EffectiveMaxOrderedResultRecordCount => Math.Max(1, MaxOrderedResultRecordCount);

    public NLCPGDataFlowOptions EffectiveDataFlowOptions =>
      DataFlowOptions ?? NLCPGDataFlowOptions.Unbounded;

    public NLCPGInterproceduralDataFlowOptions EffectiveInterproceduralDataFlowOptions =>
      InterproceduralDataFlowOptions ?? NLCPGInterproceduralDataFlowOptions.Default;

    public NLCPGFlowSummaryOptions EffectiveFlowSummaryOptions =>
      FlowSummaryOptions ?? NLCPGFlowSummaryOptions.Default;

    // 返回当前仓库推荐的默认构图参数。
    public static NLCPGBuilderOptions CreateDefault()
    {
        return new NLCPGBuilderOptions(
          NLCPGBuilderMode.Partitioned,
          Math.Max(1, Environment.ProcessorCount),
          LargeFileLineThreshold: 800,
          LargeFileMethodThreshold: 8,
          LargeMethodLineSpanThreshold: 80,
          EnableReferencedSymbolTypeReuse: true,
          EnableOperationBackedSyntaxTypes: true,
          SyntaxPassMode: NLCPGSyntaxPassMode.Partitioned,
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
  int StoreLockWaitMilliseconds = 30000,
  bool UseMinimalRoutingCatalog = true)
{
    // 校验持久化配置的必填项和并发阈值是否合法。
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

public enum NLCPGDataFlowOverflowBehavior
{
    SkipMethod,
    FailBuild,
}

public enum NLCPGDataFlowOverflowReason
{
    None,
    DefinitionLimitExceeded,
    FlowNodeLimitExceeded,
    CandidateEdgeLimitExceeded,
}

public sealed record NLCPGDataFlowOptions(
  int MaxDefinitionsPerMethod,
  int MaxFlowNodesPerMethod = int.MaxValue,
  int MaxCandidateEdgesPerMethod = int.MaxValue,
  NLCPGDataFlowOverflowBehavior OverflowBehavior = NLCPGDataFlowOverflowBehavior.SkipMethod)
{
    public static NLCPGDataFlowOptions Unbounded { get; } = new(int.MaxValue);
}

public sealed record NLCPGInterproceduralDataFlowOptions(
  int MaxCallTargetsPerSite = 1,
  int MaxBoundaryEdgesPerMethod = 10000)
{
    public static NLCPGInterproceduralDataFlowOptions Default { get; } = new();
}

public sealed record NLCPGFlowSummaryOptions(
  int MaxMappingsPerCallSite = 16,
  int MaxMappingsPerMethod = 64,
  int MaxMappingsPerBuild = 4096)
{
    public static NLCPGFlowSummaryOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxMappingsPerCallSite <= 0 || MaxMappingsPerMethod <= 0 || MaxMappingsPerBuild <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMappingsPerCallSite));
        }
    }
}

public sealed record NLCPGFlowSummaryMetrics(
  int ResolvedMappings,
  int UnknownCalls,
  int SignatureMismatches,
  int BlockedMappings,
  int RejectedEndpoints,
  int TruncatedMappings)
{
    public static NLCPGFlowSummaryMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0);
}
