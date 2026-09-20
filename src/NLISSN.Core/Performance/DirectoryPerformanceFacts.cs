using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

public sealed record PerformanceAggregateSummary
{
  public PerformanceAggregateSummary(
    int count,
    int completedCount,
    long? sumWallElapsedMs,
    long? maxWallElapsedMs,
    long? sumAccumulatedElapsedMs,
    long? maxAccumulatedElapsedMs,
    string? topItemId,
    long? topItemWallElapsedMs)
  {
    Count = count;
    CompletedCount = completedCount;
    SumWallElapsedMs = sumWallElapsedMs;
    MaxWallElapsedMs = maxWallElapsedMs;
    SumAccumulatedElapsedMs = sumAccumulatedElapsedMs;
    MaxAccumulatedElapsedMs = maxAccumulatedElapsedMs;
    TopItemId = topItemId;
    TopItemWallElapsedMs = topItemWallElapsedMs;
  }

  public int Count { get; }

  public int CompletedCount { get; }

  public long? SumWallElapsedMs { get; }

  public long? MaxWallElapsedMs { get; }

  public long? SumAccumulatedElapsedMs { get; }

  public long? MaxAccumulatedElapsedMs { get; }

  public string? TopItemId { get; }

  public long? TopItemWallElapsedMs { get; }
}

public sealed record DirectoryPerformanceFacts
{
  public DirectoryPerformanceFacts(
    string itemId,
    IReadOnlyList<ApplicationPerformanceFacts>? children = null,
    PerformanceAggregateSummary? stageSummary = null,
    PerformanceStageSample? stage = null,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? graphSnapshot = null,
    string? ruleSnapshot = null,
    string? artifactSnapshot = null,
    string? errorKind = null)
  {
    if (string.IsNullOrWhiteSpace(itemId))
    {
      throw new ArgumentException("A directory performance item ID cannot be empty.", nameof(itemId));
    }

    ItemId = itemId;
    Children = new ReadOnlyCollection<ApplicationPerformanceFacts>(
      (children ?? Array.Empty<ApplicationPerformanceFacts>()).ToArray());
    StageSummary = stageSummary;
    Stage = stage;
    Status = status;
    GraphSnapshot = graphSnapshot;
    RuleSnapshot = ruleSnapshot;
    ArtifactSnapshot = artifactSnapshot;
    ErrorKind = errorKind;
  }

  public string ItemId { get; }

  public IReadOnlyList<ApplicationPerformanceFacts> Children { get; }

  public PerformanceAggregateSummary? StageSummary { get; }

  public PerformanceStageSample? Stage { get; }

  public PerformanceStatus Status { get; }

  public string? GraphSnapshot { get; }

  public string? RuleSnapshot { get; }

  public string? ArtifactSnapshot { get; }

  public string? ErrorKind { get; }

  public DirectoryPerformanceFacts WithStage(PerformanceStageSample stage)
  {
    ArgumentNullException.ThrowIfNull(stage);
    return new DirectoryPerformanceFacts(
      ItemId,
      Children,
      StageSummary,
      stage,
      Status,
      GraphSnapshot,
      RuleSnapshot,
      ArtifactSnapshot,
      ErrorKind);
  }
}
