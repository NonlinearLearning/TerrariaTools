namespace NLISSN.Core.Performance;

public interface IPerformanceStageCollector
{
  void Record(PerformanceStageSample sample);

  IReadOnlyList<PerformanceStageSample> Snapshot();
}
