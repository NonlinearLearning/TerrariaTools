namespace NLISSN.Core.Performance;

public interface IPerformanceEventSink
{
  void Record(PerformanceEvent performanceEvent);
}

public static class PerformanceEventSinkExtensions
{
  public static bool TryRecord(
    this IPerformanceEventSink? sink,
    PerformanceEvent performanceEvent)
  {
    if (sink is null)
    {
      return false;
    }

    try
    {
      sink.Record(performanceEvent);
      return true;
    }
    catch
    {
      // Diagnostics are fail-open and must never change analysis semantics.
      return false;
    }
  }
}
