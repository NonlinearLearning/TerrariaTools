using NLISSN.Core.Performance;
using NLISSN.Core.Pipeline;

namespace NLISSN.Application.Performance;

public static class RuleGraphPerformanceFactMapper
{
  public static RuleGraphPerformanceFacts? Map(
    IReadOnlyList<RuleGraphNodeTelemetry>? telemetry,
    RuleGraphExecutionMetrics? metrics)
  {
    if (telemetry is null && metrics is null)
    {
      return null;
    }

    var nodes = (telemetry ?? Array.Empty<RuleGraphNodeTelemetry>())
      .OrderBy(node => node.NodeId.Value, StringComparer.Ordinal)
      .Select(node => new RuleGraphNodePerformanceFact(
        node.NodeId.Value,
        node.InputCount,
        node.OutputCount,
        node.ElapsedMilliseconds,
        node.Status == RuleGraphNodeStatus.Disabled
          ? PerformanceStatus.Skipped
          : PerformanceStatus.Completed))
      .ToArray();
    return new RuleGraphPerformanceFacts(
      nodes,
      metrics?.PeakReadyNodeCount,
      metrics?.PeakConcurrentNodeCount,
      telemetry is null ? PerformanceStatus.Unavailable : PerformanceStatus.Completed);
  }
}
