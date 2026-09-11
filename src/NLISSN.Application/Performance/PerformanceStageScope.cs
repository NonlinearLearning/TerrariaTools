using System.Diagnostics;
using NLISSN.Core.Performance;

namespace NLISSN.Application.Performance;

/// Measures one owned stage without deriving parent time from child samples.
public sealed class PerformanceStageScope : IDisposable
{
  private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
  private readonly string _stageId;
  private readonly string? _parentStageId;
  private readonly string? _itemId;
  private readonly PerformanceAttributionLevel _attribution;
  private PerformanceStageSample? _sample;

  private PerformanceStageScope(
    string stageId,
    string? parentStageId,
    string? itemId,
    PerformanceAttributionLevel attribution)
  {
    if (string.IsNullOrWhiteSpace(stageId))
    {
      throw new ArgumentException("A performance stage ID cannot be empty.", nameof(stageId));
    }

    _stageId = stageId;
    _parentStageId = parentStageId;
    _itemId = itemId;
    _attribution = attribution;
  }

  public PerformanceStageSample? Sample => _sample;

  public static PerformanceStageScope Start(
    string stageId,
    string? parentStageId = null,
    string? itemId = null,
    PerformanceAttributionLevel attribution = PerformanceAttributionLevel.Stage)
  {
    return new PerformanceStageScope(stageId, parentStageId, itemId, attribution);
  }

  public PerformanceStageSample Complete(
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null,
    long? accumulatedElapsedMs = null,
    long? allocatedBytesDelta = null,
    long? workingSetBytes = null,
    int? gc0Delta = null,
    int? gc1Delta = null,
    int? gc2Delta = null,
    double? queueWaitMs = null,
    int? peakActive = null,
    int? peakBuffer = null)
  {
    if (_sample is not null)
    {
      return _sample;
    }

    _stopwatch.Stop();
    _sample = new PerformanceStageSample(
      _stageId,
      _parentStageId,
      _itemId,
      _stopwatch.ElapsedMilliseconds,
      accumulatedElapsedMs,
      status,
      errorKind,
      allocatedBytesDelta,
      workingSetBytes,
      gc0Delta,
      gc1Delta,
      gc2Delta,
      queueWaitMs,
      peakActive,
      peakBuffer,
      attribution: _attribution);
    return _sample;
  }

  public void Dispose()
  {
    Complete();
  }
}
