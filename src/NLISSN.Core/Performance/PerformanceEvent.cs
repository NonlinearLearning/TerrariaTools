using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

/// High-cardinality diagnostic data. It is deliberately separate from terminal facts.
public sealed record PerformanceEvent
{
  public PerformanceEvent(
    string runId,
    string stageId,
    string? itemId,
    long? wallElapsedMs,
    long? accumulatedElapsedMs,
    PerformanceStatus status,
    IReadOnlyDictionary<string, long>? counters = null,
    string? errorKind = null)
  {
    if (string.IsNullOrWhiteSpace(runId))
    {
      throw new ArgumentException("A performance event run ID cannot be empty.", nameof(runId));
    }

    if (string.IsNullOrWhiteSpace(stageId))
    {
      throw new ArgumentException("A performance event stage ID cannot be empty.", nameof(stageId));
    }

    RunId = runId;
    StageId = stageId;
    ItemId = itemId;
    WallElapsedMs = wallElapsedMs;
    AccumulatedElapsedMs = accumulatedElapsedMs;
    Status = status;
    Counters = new ReadOnlyDictionary<string, long>(
      new Dictionary<string, long>(counters ?? new Dictionary<string, long>(), StringComparer.Ordinal));
    ErrorKind = errorKind;
  }

  public string RunId { get; }

  public string StageId { get; }

  public string? ItemId { get; }

  public long? WallElapsedMs { get; }

  public long? AccumulatedElapsedMs { get; }

  public PerformanceStatus Status { get; }

  public IReadOnlyDictionary<string, long> Counters { get; }

  public string? ErrorKind { get; }
}
