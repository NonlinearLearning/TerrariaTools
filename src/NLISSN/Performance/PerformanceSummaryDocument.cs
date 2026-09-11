using System.Text.Json;
using System.Text.Json.Serialization;
using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public sealed record PerformanceSummaryDocument
{
  public int SchemaVersion { get; init; }

  public string RunId { get; init; } = string.Empty;

  public string InputKind { get; init; } = string.Empty;

  public string? InputIdentity { get; init; }

  public string Mode { get; init; } = string.Empty;

  public int SampleNumber { get; init; }

  public bool IsWarmup { get; init; }

  public string TerminalStatus { get; init; } = string.Empty;

  public bool IsComplete { get; init; }

  public bool ComparisonEligible { get; init; }

  public IReadOnlyList<string> ComparisonReasons { get; init; } = Array.Empty<string>();

  public PerformanceSummaryStageDocument? RootStage { get; init; }

  public PerformanceSummaryDirectoryDocument? Directory { get; init; }

  public IReadOnlyList<PerformanceSummaryStageDocument> Stages { get; init; } =
    Array.Empty<PerformanceSummaryStageDocument>();

  public PerformanceSummaryResourceDocument? Resources { get; init; }

  public PerformanceSummaryIdentityDocument? Identity { get; init; }

  public IReadOnlyList<PerformanceSummaryItemDocument> Items { get; init; } =
    Array.Empty<PerformanceSummaryItemDocument>();

  public PerformanceSummaryTerminalDocument TerminalSummary { get; init; } =
    new(null, null, "unknown", false, null);

  public IReadOnlyList<PerformanceAttachmentDocument> Attachments { get; init; } =
    Array.Empty<PerformanceAttachmentDocument>();

  [JsonIgnore]
  public static JsonSerializerOptions JsonOptions { get; } = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = true
  };

  public static PerformanceSummaryDocument FromReport(RunPerformanceReport report)
  {
    ArgumentNullException.ThrowIfNull(report);
    return new PerformanceSummaryDocument
    {
      SchemaVersion = 1,
      RunId = report.RunId,
      InputKind = report.InputKind,
      InputIdentity = report.InputIdentity,
      Mode = report.Mode.ToString().ToLowerInvariant(),
      SampleNumber = report.SampleNumber,
      IsWarmup = report.IsWarmup,
      TerminalStatus = report.TerminalStatus.ToString().ToLowerInvariant(),
      IsComplete = report.IsComplete,
      ComparisonEligible = report.ComparisonEligible,
      ComparisonReasons = report.ComparisonReasons.ToArray(),
      RootStage = report.RootStage is null ? null : ToDocument(report.RootStage),
      Directory = report.Directory is null ? null : ToDocument(report.Directory),
      Stages = report.Stages
        .OrderBy(stage => stage.StageId, StringComparer.Ordinal)
        .ThenBy(stage => stage.ItemId, StringComparer.Ordinal)
        .Select(ToDocument)
        .ToArray(),
      Resources = report.Resources is null ? null : new PerformanceSummaryResourceDocument(
        report.Resources.AllocatedBytes,
        report.Resources.HeapBytes,
        report.Resources.WorkingSetBytes,
        report.Resources.Gen0Collections,
        report.Resources.Gen1Collections,
        report.Resources.Gen2Collections,
        report.Resources.PoolOperationCount,
        report.Resources.PoolQueueWaitMs,
        report.Resources.PoolQueueWaitMaxMs,
        report.Resources.PoolPeakActive,
        report.Resources.PoolPeakBuffer,
        report.Resources.Attribution.ToString().ToLowerInvariant(),
        report.Resources.ErrorKind),
      Identity = report.Identity is null ? null : new PerformanceSummaryIdentityDocument(
        report.Identity.InputIdentity,
        report.Identity.RuleProfileHash,
        report.Identity.CapabilityFingerprint,
        report.Identity.CacheMode,
        report.Identity.Sdk,
        report.Identity.Runtime,
        report.Identity.OperatingSystem,
        report.Identity.Cpu,
        report.Identity.EnvironmentFingerprint,
        report.Identity.DirectoryDop,
        report.Identity.CpgDop,
        report.Identity.RuleDop,
        report.Identity.Mode.ToString().ToLowerInvariant(),
        report.Identity.DiagnosticsEnabled,
        report.Identity.GraphSnapshot,
        report.Identity.RuleSnapshot,
        report.Identity.ArtifactSnapshot),
      Items = report.Items
        .OrderBy(item => item.ItemId, StringComparer.Ordinal)
        .Select(ToDocument)
        .ToArray(),
      TerminalSummary = new PerformanceSummaryTerminalDocument(
        report.TerminalSummary.WallElapsedMs,
        report.TerminalSummary.AccumulatedElapsedMs,
        report.TerminalSummary.Status.ToString().ToLowerInvariant(),
        report.TerminalSummary.IsComplete,
        report.TerminalSummary.ErrorKind)
      ,Attachments = report.Attachments
        .OrderBy(attachment => attachment.Kind, StringComparer.Ordinal)
        .ThenBy(attachment => attachment.RelativePath, StringComparer.Ordinal)
        .Select(attachment => new PerformanceAttachmentDocument(
          attachment.Kind,
          attachment.RelativePath,
          attachment.RunId,
          attachment.StageId,
          attachment.Mode.ToString().ToLowerInvariant(),
          attachment.IsAvailable ? "available" : "unavailable",
          attachment.IsComplete,
          attachment.ErrorKind))
        .ToArray()
    };
  }

  private static PerformanceSummaryItemDocument ToDocument(ApplicationPerformanceFacts item)
  {
    return new PerformanceSummaryItemDocument(
      item.ItemId,
      item.Status.ToString().ToLowerInvariant(),
      item.ErrorKind,
      item.Cpg is null ? null : ToDocument(item.Cpg),
      item.RuleGraph is null ? null : ToDocument(item.RuleGraph),
      item.Rewrite is null ? null : ToDocument(item.Rewrite));
  }

  private static PerformanceSummaryCpgDocument ToDocument(CpgPerformanceFacts facts)
  {
    return new PerformanceSummaryCpgDocument(
      facts.SourceIdentity,
      facts.BuildElapsedMs,
      facts.PassSamples
        .OrderBy(sample => sample.PassId, StringComparer.Ordinal)
        .Select(sample => new PerformanceSummaryPassDocument(
          sample.PassId,
          sample.StageId,
          sample.ElapsedMilliseconds))
        .ToArray(),
      facts.AnchorDiscovery is null ? null : ToDocument(facts.AnchorDiscovery),
      facts.Persistence is null ? null : ToDocument(facts.Persistence),
      facts.CacheCounters
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
      facts.DataFlowMethodSamples
        .OrderBy(sample => sample.MethodName, StringComparer.Ordinal)
        .Select(sample => new PerformanceSummaryDataFlowDocument(
          sample.MethodName,
          sample.FlowNodeCount,
          sample.WordsPerSet,
          sample.DefinitionCount,
          sample.WorklistIterations,
          sample.RawCandidateCount,
          sample.UniqueCandidateCount,
          sample.OverflowReason))
        .ToArray(),
      facts.NodeCount,
      facts.EdgeCount,
      facts.Status.ToString().ToLowerInvariant(),
      facts.ErrorKind);
  }

  private static PerformanceSummaryAnchorDocument ToDocument(CpgAnchorDiscoveryFacts facts)
  {
    return new PerformanceSummaryAnchorDocument(
      facts.AnchorCount,
      facts.ElapsedMilliseconds,
      facts.PassSamples
        .OrderBy(sample => sample.PassId, StringComparer.Ordinal)
        .Select(sample => new PerformanceSummaryPassDocument(
          sample.PassId,
          sample.StageId,
          sample.ElapsedMilliseconds))
        .ToArray());
  }

  private static PerformanceSummaryPersistenceDocument ToDocument(
    CpgPersistencePerformanceFacts facts)
  {
    return new PerformanceSummaryPersistenceDocument(
      facts.RestoreAttempted,
      facts.RestoreHit,
      facts.RestoreElapsedMs,
      facts.CatalogReadMs,
      facts.ShardReadMs,
      facts.RestoredShardCount,
      facts.RestoredShardBytes,
      facts.PersistElapsedMs,
      facts.FileWriteMs,
      facts.CatalogWriteMs,
      facts.RoutingIndexWriteMs,
      facts.PrimaryShardCount,
      facts.PrimaryShardBytes,
      facts.BoundaryAdjacencyShardCount,
      facts.BoundaryAdjacencyShardBytes,
      facts.BoundaryEdgeCount,
      facts.ReusedShardCount,
      facts.ReuseMissCount,
      facts.ReuseRejectedCount,
      facts.ReusedShardBytes,
      facts.PeakConcurrentFileWrites,
      facts.PeakConcurrentShardExports,
      facts.PeakReorderBuffer,
      facts.PeakBufferedBoundaryEdges,
      facts.CatalogBatchCount,
      facts.CatalogRowCount,
      facts.Provenance);
  }

  private static PerformanceSummaryRuleGraphDocument ToDocument(
    RuleGraphPerformanceFacts facts)
  {
    return new PerformanceSummaryRuleGraphDocument(
      facts.NodeSamples
        .OrderBy(sample => sample.NodeId, StringComparer.Ordinal)
        .Select(sample => new PerformanceSummaryRuleNodeDocument(
          sample.NodeId,
          sample.InputCount,
          sample.OutputCount,
          sample.WallElapsedMs,
          sample.Status.ToString().ToLowerInvariant(),
          sample.ErrorKind))
        .ToArray(),
      facts.PeakReadyNodeCount,
      facts.PeakConcurrentNodeCount,
      facts.Status.ToString().ToLowerInvariant(),
      facts.ErrorKind);
  }

  private static PerformanceSummaryRewriteDocument ToDocument(RewritePerformanceFacts facts)
  {
    return new PerformanceSummaryRewriteDocument(
      facts.WallElapsedMs,
      facts.EditCount,
      facts.DiffFileCount,
      facts.Status.ToString().ToLowerInvariant(),
      facts.ErrorKind);
  }

  private static PerformanceSummaryStageDocument ToDocument(PerformanceStageSample sample)
  {
    return new PerformanceSummaryStageDocument(
      sample.StageId,
      sample.ParentStageId,
      sample.ItemId,
      sample.WallElapsedMs,
      sample.AccumulatedElapsedMs,
      sample.Status.ToString().ToLowerInvariant(),
      sample.ErrorKind,
      sample.AllocatedBytesDelta,
      sample.WorkingSetBytes,
      sample.Gc0Delta,
      sample.Gc1Delta,
      sample.Gc2Delta,
      sample.QueueWaitMs,
      sample.PeakActive,
      sample.PeakBuffer,
      sample.InputCount,
      sample.OutputCount,
      sample.NodeDelta,
      sample.EdgeDelta,
      sample.ArtifactBytes,
      sample.Attribution.ToString().ToLowerInvariant());
  }

  private static PerformanceSummaryDirectoryDocument ToDocument(DirectoryPerformanceFacts facts)
  {
    return new PerformanceSummaryDirectoryDocument(
      facts.ItemId,
      facts.StageSummary is null ? null : new PerformanceSummaryAggregateDocument(
        facts.StageSummary.Count,
        facts.StageSummary.CompletedCount,
        facts.StageSummary.SumWallElapsedMs,
        facts.StageSummary.MaxWallElapsedMs,
        facts.StageSummary.SumAccumulatedElapsedMs,
        facts.StageSummary.MaxAccumulatedElapsedMs,
        facts.StageSummary.TopItemId,
        facts.StageSummary.TopItemWallElapsedMs),
      facts.Stage is null ? null : ToDocument(facts.Stage),
      facts.Status.ToString().ToLowerInvariant(),
      facts.GraphSnapshot,
      facts.RuleSnapshot,
      facts.ArtifactSnapshot,
      facts.ErrorKind);
  }
}

public sealed record PerformanceSummaryItemDocument(
  string ItemId,
  string Status,
  string? ErrorKind,
  PerformanceSummaryCpgDocument? Cpg,
  PerformanceSummaryRuleGraphDocument? RuleGraph,
  PerformanceSummaryRewriteDocument? Rewrite);

public sealed record PerformanceSummaryCpgDocument(
  string? SourceIdentity,
  long? BuildElapsedMs,
  IReadOnlyList<PerformanceSummaryPassDocument> PassSamples,
  PerformanceSummaryAnchorDocument? AnchorDiscovery,
  PerformanceSummaryPersistenceDocument? Persistence,
  IReadOnlyDictionary<string, long> CacheCounters,
  IReadOnlyList<PerformanceSummaryDataFlowDocument> DataFlowMethodSamples,
  int? NodeCount,
  int? EdgeCount,
  string Status,
  string? ErrorKind);

public sealed record PerformanceSummaryPassDocument(
  string PassId,
  string StageId,
  long? ElapsedMilliseconds);

public sealed record PerformanceSummaryAnchorDocument(
  int? AnchorCount,
  long? ElapsedMilliseconds,
  IReadOnlyList<PerformanceSummaryPassDocument> PassSamples);

public sealed record PerformanceSummaryPersistenceDocument(
  bool RestoreAttempted,
  bool RestoreHit,
  long? RestoreElapsedMs,
  long? CatalogReadMs,
  long? ShardReadMs,
  int? RestoredShardCount,
  long? RestoredShardBytes,
  long? PersistElapsedMs,
  long? FileWriteMs,
  long? CatalogWriteMs,
  long? RoutingIndexWriteMs,
  int? PrimaryShardCount,
  long? PrimaryShardBytes,
  int? BoundaryAdjacencyShardCount,
  long? BoundaryAdjacencyShardBytes,
  long? BoundaryEdgeCount,
  int? ReusedShardCount,
  int? ReuseMissCount,
  int? ReuseRejectedCount,
  long? ReusedShardBytes,
  int? PeakConcurrentFileWrites,
  int? PeakConcurrentShardExports,
  int? PeakReorderBuffer,
  int? PeakBufferedBoundaryEdges,
  int? CatalogBatchCount,
  long? CatalogRowCount,
  string? Provenance);

public sealed record PerformanceSummaryDataFlowDocument(
  string MethodName,
  int? FlowNodeCount,
  int? WordsPerSet,
  int? DefinitionCount,
  int? WorklistIterations,
  int? RawCandidateCount,
  int? UniqueCandidateCount,
  string? OverflowReason);

public sealed record PerformanceSummaryRuleGraphDocument(
  IReadOnlyList<PerformanceSummaryRuleNodeDocument> NodeSamples,
  int? PeakReadyNodeCount,
  int? PeakConcurrentNodeCount,
  string Status,
  string? ErrorKind);

public sealed record PerformanceSummaryRuleNodeDocument(
  string NodeId,
  int? InputCount,
  int? OutputCount,
  long? WallElapsedMs,
  string Status,
  string? ErrorKind);

public sealed record PerformanceSummaryRewriteDocument(
  long? WallElapsedMs,
  int? EditCount,
  int? DiffFileCount,
  string Status,
  string? ErrorKind);

public sealed record PerformanceSummaryStageDocument(
  string StageId,
  string? ParentStageId,
  string? ItemId,
  long? WallElapsedMs,
  long? AccumulatedElapsedMs,
  string Status,
  string? ErrorKind,
  long? AllocatedBytesDelta,
  long? WorkingSetBytes,
  int? Gc0Delta,
  int? Gc1Delta,
  int? Gc2Delta,
  double? QueueWaitMs,
  int? PeakActive,
  int? PeakBuffer,
  int? InputCount,
  int? OutputCount,
  int? NodeDelta,
  int? EdgeDelta,
  long? ArtifactBytes,
  string Attribution);

public sealed record PerformanceSummaryTerminalDocument(
  long? WallElapsedMs,
  long? AccumulatedElapsedMs,
  string Status,
  bool IsComplete,
  string? ErrorKind);

public sealed record PerformanceSummaryResourceDocument(
  long? AllocatedBytes,
  long? HeapBytes,
  long? WorkingSetBytes,
  int? Gen0Collections,
  int? Gen1Collections,
  int? Gen2Collections,
  int? PoolOperationCount,
  double? PoolQueueWaitMs,
  double? PoolQueueWaitMaxMs,
  int? PoolPeakActive,
  int? PoolPeakBuffer,
  string Attribution,
  string? ErrorKind);

public sealed record PerformanceSummaryIdentityDocument(
  string? InputIdentity,
  string? RuleProfileHash,
  string? CapabilityFingerprint,
  string? CacheMode,
  string? Sdk,
  string? Runtime,
  string? OperatingSystem,
  string? Cpu,
  string? EnvironmentFingerprint,
  int? DirectoryDop,
  int? CpgDop,
  int? RuleDop,
  string Mode,
  bool DiagnosticsEnabled,
  string? GraphSnapshot,
  string? RuleSnapshot,
  string? ArtifactSnapshot);

public sealed record PerformanceSummaryAggregateDocument(
  int Count,
  int CompletedCount,
  long? SumWallElapsedMs,
  long? MaxWallElapsedMs,
  long? SumAccumulatedElapsedMs,
  long? MaxAccumulatedElapsedMs,
  string? TopItemId,
  long? TopItemWallElapsedMs);

public sealed record PerformanceSummaryDirectoryDocument(
  string ItemId,
  PerformanceSummaryAggregateDocument? StageSummary,
  PerformanceSummaryStageDocument? Stage,
  string Status,
  string? GraphSnapshot,
  string? RuleSnapshot,
  string? ArtifactSnapshot,
  string? ErrorKind);

public sealed record PerformanceAttachmentDocument(
  string Kind,
  string? RelativePath,
  string RunId,
  string? StageId,
  string Mode,
  string Status,
  bool IsComplete,
  string? ErrorKind);
