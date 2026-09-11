namespace NLISSN.Core.Performance;

public sealed record PerformanceStageSample
{
  public PerformanceStageSample(
    string stageId,
    string? parentStageId,
    string? itemId,
    long? wallElapsedMs,
    long? accumulatedElapsedMs,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null,
    long? allocatedBytesDelta = null,
    long? workingSetBytes = null,
    int? gc0Delta = null,
    int? gc1Delta = null,
    int? gc2Delta = null,
    double? queueWaitMs = null,
    int? peakActive = null,
    int? peakBuffer = null,
    int? inputCount = null,
    int? outputCount = null,
    int? nodeDelta = null,
    int? edgeDelta = null,
    long? artifactBytes = null,
    PerformanceAttributionLevel attribution = PerformanceAttributionLevel.None)
  {
    if (string.IsNullOrWhiteSpace(stageId))
    {
      throw new ArgumentException("A performance stage ID cannot be empty.", nameof(stageId));
    }

    StageId = stageId;
    ParentStageId = parentStageId;
    ItemId = itemId;
    WallElapsedMs = wallElapsedMs;
    AccumulatedElapsedMs = accumulatedElapsedMs;
    Status = status;
    ErrorKind = errorKind;
    AllocatedBytesDelta = allocatedBytesDelta;
    WorkingSetBytes = workingSetBytes;
    Gc0Delta = gc0Delta;
    Gc1Delta = gc1Delta;
    Gc2Delta = gc2Delta;
    QueueWaitMs = queueWaitMs;
    PeakActive = peakActive;
    PeakBuffer = peakBuffer;
    InputCount = inputCount;
    OutputCount = outputCount;
    NodeDelta = nodeDelta;
    EdgeDelta = edgeDelta;
    ArtifactBytes = artifactBytes;
    Attribution = attribution;
  }

  public string StageId { get; init; }

  public string? ParentStageId { get; init; }

  public string? ItemId { get; init; }

  public long? WallElapsedMs { get; init; }

  public long? AccumulatedElapsedMs { get; init; }

  public PerformanceStatus Status { get; init; }

  public string? ErrorKind { get; init; }

  public long? AllocatedBytesDelta { get; init; }

  public long? WorkingSetBytes { get; init; }

  public int? Gc0Delta { get; init; }

  public int? Gc1Delta { get; init; }

  public int? Gc2Delta { get; init; }

  public double? QueueWaitMs { get; init; }

  public int? PeakActive { get; init; }

  public int? PeakBuffer { get; init; }

  public int? InputCount { get; init; }

  public int? OutputCount { get; init; }

  public int? NodeDelta { get; init; }

  public int? EdgeDelta { get; init; }

  public long? ArtifactBytes { get; init; }

  public PerformanceAttributionLevel Attribution { get; init; }
}
