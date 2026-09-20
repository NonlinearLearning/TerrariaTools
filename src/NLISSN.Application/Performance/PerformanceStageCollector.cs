using System.Collections.Concurrent;
using NLISSN.Core.Performance;

namespace NLISSN.Application.Performance;

/// Collects stage samples for one analysis run. The host owns publication.
public sealed class PerformanceStageCollector : IPerformanceStageCollector
{
  private readonly ConcurrentQueue<PerformanceStageSample> _samples = new();

  public PerformanceStageScope Start(
    string stageId,
    string? parentStageId = null,
    string? itemId = null,
    PerformanceAttributionLevel attribution = PerformanceAttributionLevel.Stage)
  {
    return PerformanceStageScope.Start(
      stageId,
      parentStageId,
      itemId,
      attribution,
      sample => _samples.Enqueue(sample));
  }

  public void Record(PerformanceStageSample sample)
  {
    ArgumentNullException.ThrowIfNull(sample);
    _samples.Enqueue(sample);
  }

  public IReadOnlyList<PerformanceStageSample> Snapshot()
  {
    return _samples.ToArray();
  }
}
