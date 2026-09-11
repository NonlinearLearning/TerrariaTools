using NLISSN.Core.Performance;

namespace NLISSN.Application.Performance;

public sealed class NullPerformanceEventSink : IPerformanceEventSink
{
  public static NullPerformanceEventSink Instance { get; } = new();

  private NullPerformanceEventSink()
  {
  }

  public void Record(PerformanceEvent performanceEvent)
  {
  }
}
