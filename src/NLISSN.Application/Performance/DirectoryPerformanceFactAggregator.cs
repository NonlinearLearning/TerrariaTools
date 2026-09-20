using NLISSN.Core.Performance;

namespace NLISSN.Application;

public static class DirectoryPerformanceFactAggregator
{
  public static DirectoryPerformanceFacts Aggregate(
    string itemId,
    IReadOnlyList<DirectoryFileAnalysisResult> fileResults,
    long? wallElapsedMs = null,
    PerformanceStatus status = PerformanceStatus.Completed)
  {
    if (string.IsNullOrWhiteSpace(itemId))
    {
      throw new ArgumentException("A directory performance item ID cannot be empty.", nameof(itemId));
    }

    ArgumentNullException.ThrowIfNull(fileResults);
    var orderedResults = fileResults
      .OrderBy(file => file.Index)
      .ThenBy(file => file.FilePath, StringComparer.Ordinal)
      .ToArray();
    var children = orderedResults
      .Select(file => file.Result.Performance ??
        new ApplicationPerformanceFacts(
          file.FilePath,
          CpgPerformanceFacts.Unavailable(file.FilePath, "application-performance-unavailable"),
          null,
          null,
          PerformanceStatus.Unavailable,
          "application-performance-unavailable"))
      .ToArray();
    var childMeasurements = children
      .Where(child => child.Status == PerformanceStatus.Completed)
      .Select(child => new
      {
        child.ItemId,
        WallElapsedMs = child.Cpg?.BuildElapsedMs,
        AccumulatedElapsedMs = child.Cpg?.BuildElapsedMs
      })
      .Where(measurement => measurement.WallElapsedMs.HasValue)
      .Select(measurement => new
      {
        measurement.ItemId,
        WallElapsedMs = measurement.WallElapsedMs!.Value,
        AccumulatedElapsedMs = measurement.AccumulatedElapsedMs!.Value
      })
      .ToArray();
    var summary = new PerformanceAggregateSummary(
      children.Length,
      children.Count(child => child.Status == PerformanceStatus.Completed),
      childMeasurements.Length == 0 ? null : childMeasurements.Sum(value => value.WallElapsedMs),
      childMeasurements.Length == 0 ? null : childMeasurements.Max(value => value.WallElapsedMs),
      childMeasurements.Length == 0 ? null : childMeasurements.Sum(value => value.AccumulatedElapsedMs),
      childMeasurements.Length == 0 ? null : childMeasurements.Max(value => value.AccumulatedElapsedMs),
      childMeasurements
        .OrderByDescending(value => value.WallElapsedMs)
        .ThenBy(value => value.ItemId, StringComparer.Ordinal)
        .Select(value => value.ItemId)
        .FirstOrDefault(),
      childMeasurements.Length == 0
        ? null
        : childMeasurements.Max(value => value.WallElapsedMs));
    var stage = new PerformanceStageSample(
      PerformanceStageId.DirectoryRead,
      PerformanceStageId.Run,
      itemId,
      wallElapsedMs,
      summary.SumAccumulatedElapsedMs,
      status,
      attribution: PerformanceAttributionLevel.Stage);
    return new DirectoryPerformanceFacts(
      itemId,
      children,
      summary,
      stage,
      status);
  }
}
