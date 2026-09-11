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
        ElapsedMs = child.Cpg?.BuildElapsedMs
      })
      .Where(measurement => measurement.ElapsedMs.HasValue)
      .Select(measurement => new
      {
        measurement.ItemId,
        ElapsedMs = measurement.ElapsedMs!.Value
      })
      .ToArray();
    var summary = new PerformanceAggregateSummary(
      children.Length,
      children.Count(child => child.Status == PerformanceStatus.Completed),
      childMeasurements.Length == 0 ? null : childMeasurements.Sum(value => value.ElapsedMs),
      childMeasurements.Length == 0 ? null : childMeasurements.Max(value => value.ElapsedMs),
      null,
      null,
      childMeasurements
        .OrderByDescending(value => value.ElapsedMs)
        .ThenBy(value => value.ItemId, StringComparer.Ordinal)
        .Select(value => value.ItemId)
        .FirstOrDefault(),
      childMeasurements.Length == 0
        ? null
        : childMeasurements.Max(value => value.ElapsedMs));
    var stage = new PerformanceStageSample(
      "Directory.Analyze",
      null,
      itemId,
      wallElapsedMs,
      summary.SumWallElapsedMs,
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
