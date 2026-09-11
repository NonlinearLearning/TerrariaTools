using System.Collections.ObjectModel;
using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public sealed record PerformanceSampleMeasurement(
  string RunId,
  int SampleNumber,
  long? WallElapsedMs,
  long? AccumulatedElapsedMs);

public sealed record PerformanceMetricAggregate(
  int Count,
  long Sum,
  long Max,
  long Median,
  long P95,
  IReadOnlyList<long> RawValues);

public sealed record PerformanceTopItemAggregate(
  string ItemId,
  string RunId,
  long MaxWallElapsedMs);

public sealed record PerformanceSampleAggregateResult
{
  public PerformanceSampleAggregateResult(
    PerformanceMode mode,
    IReadOnlyList<RunPerformanceReport> rawReports,
    IReadOnlyList<string> warmupRunIds,
    IReadOnlyList<string> rejectedRunIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> rejectionReasons,
    IReadOnlyList<PerformanceSampleMeasurement> rawMeasurements,
    IReadOnlyList<string> measurementRunIds,
    PerformanceMetricAggregate? wall,
    PerformanceMetricAggregate? accumulated,
    PerformanceTopItemAggregate? topItem,
    bool isLowConfidence,
    bool isFormalStatisticsEligible)
  {
    Mode = mode;
    RawReports = new ReadOnlyCollection<RunPerformanceReport>(
      (rawReports ?? Array.Empty<RunPerformanceReport>()).ToArray());
    WarmupRunIds = new ReadOnlyCollection<string>(
      (warmupRunIds ?? Array.Empty<string>()).ToArray());
    RejectedRunIds = new ReadOnlyCollection<string>(
      (rejectedRunIds ?? Array.Empty<string>()).ToArray());
    RejectionReasons = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
      (rejectionReasons ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal))
        .ToDictionary(
          entry => entry.Key,
          entry => (IReadOnlyList<string>)entry.Value.ToArray(),
          StringComparer.Ordinal));
    RawMeasurements = new ReadOnlyCollection<PerformanceSampleMeasurement>(
      (rawMeasurements ?? Array.Empty<PerformanceSampleMeasurement>()).ToArray());
    MeasurementRunIds = new ReadOnlyCollection<string>(
      (measurementRunIds ?? Array.Empty<string>()).ToArray());
    Wall = wall;
    Accumulated = accumulated;
    TopItem = topItem;
    MeasurementCount = MeasurementRunIds.Count;
    IsLowConfidence = isLowConfidence;
    IsFormalStatisticsEligible = isFormalStatisticsEligible;
  }

  public PerformanceMode Mode { get; }

  public IReadOnlyList<RunPerformanceReport> RawReports { get; }

  public IReadOnlyList<string> WarmupRunIds { get; }

  public IReadOnlyList<string> RejectedRunIds { get; }

  public IReadOnlyDictionary<string, IReadOnlyList<string>> RejectionReasons { get; }

  public IReadOnlyList<PerformanceSampleMeasurement> RawMeasurements { get; }

  public IReadOnlyList<string> MeasurementRunIds { get; }

  public int MeasurementCount { get; }

  public PerformanceMetricAggregate? Wall { get; }

  public PerformanceMetricAggregate? Accumulated { get; }

  public PerformanceTopItemAggregate? TopItem { get; }

  public bool IsLowConfidence { get; }

  public bool IsFormalStatisticsEligible { get; }
}

/// Aggregates completed, equivalent samples while retaining every raw report.
public static class PerformanceSampleAggregator
{
  public const int MinimumFormalMeasurementCount = 3;

  public static PerformanceSampleAggregateResult Aggregate(
    IReadOnlyList<RunPerformanceReport> reports,
    PerformanceMode mode)
  {
    ArgumentNullException.ThrowIfNull(reports);

    var rawReports = reports
      .OrderBy(report => report.SampleNumber)
      .ThenBy(report => report.RunId, StringComparer.Ordinal)
      .ToArray();
    var warmupRunIds = rawReports
      .Where(report => report.IsWarmup)
      .Select(report => report.RunId)
      .OrderBy(runId => runId, StringComparer.Ordinal)
      .ToArray();
    var rejectedRunIds = new SortedSet<string>(StringComparer.Ordinal);
    var rejectionReasons = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    var eligibleReports = new List<RunPerformanceReport>();

    var baseline = rawReports
      .Where(report => !report.IsWarmup)
      .Where(report => report.Mode == mode)
      .Where(report => report.ComparisonEligible)
      .Where(report => report.ComparisonReasons.Count == 0)
      .Where(report => report.IsComplete)
      .OrderBy(report => report.SampleNumber)
      .ThenBy(report => report.RunId, StringComparer.Ordinal)
      .FirstOrDefault();

    foreach (var report in rawReports)
    {
      if (report.IsWarmup)
      {
        continue;
      }

      var reasons = GetRejectionReasons(report, mode, baseline);
      if (reasons.Count > 0)
      {
        rejectedRunIds.Add(report.RunId);
        rejectionReasons[report.RunId] = reasons;
        continue;
      }

      eligibleReports.Add(report);
    }

    var measurements = eligibleReports
      .Select(report => new PerformanceSampleMeasurement(
        report.RunId,
        report.SampleNumber,
        report.TerminalSummary.WallElapsedMs,
        report.TerminalSummary.AccumulatedElapsedMs))
      .OrderBy(sample => sample.SampleNumber)
      .ThenBy(sample => sample.RunId, StringComparer.Ordinal)
      .ToArray();
    var measurementRunIds = measurements
      .Select(sample => sample.RunId)
      .ToArray();
    var formalStatisticsEligible = mode is PerformanceMode.Normal or PerformanceMode.Benchmark;
    var wallValues = measurements
      .Where(sample => sample.WallElapsedMs.HasValue)
      .Select(sample => sample.WallElapsedMs!.Value)
      .ToArray();
    var accumulatedValues = measurements
      .Where(sample => sample.AccumulatedElapsedMs.HasValue)
      .Select(sample => sample.AccumulatedElapsedMs!.Value)
      .ToArray();
    var hasEnoughMeasurements = measurements.Length >= MinimumFormalMeasurementCount;
    var wall = formalStatisticsEligible && hasEnoughMeasurements
      ? CreateMetricAggregate(wallValues)
      : null;
    var accumulated = formalStatisticsEligible && hasEnoughMeasurements
      ? CreateMetricAggregate(accumulatedValues)
      : null;

    return new PerformanceSampleAggregateResult(
      mode,
      rawReports,
      warmupRunIds,
      rejectedRunIds.ToArray(),
      rejectionReasons,
      measurements,
      measurementRunIds,
      wall,
      accumulated,
      formalStatisticsEligible ? FindTopItem(eligibleReports) : null,
      !formalStatisticsEligible || measurements.Length < MinimumFormalMeasurementCount,
      formalStatisticsEligible);
  }

  public static IReadOnlyList<PerformanceSampleAggregateResult> AggregateByMode(
    IReadOnlyList<RunPerformanceReport> reports)
  {
    ArgumentNullException.ThrowIfNull(reports);
    return reports
      .Select(report => report.Mode)
      .Distinct()
      .OrderBy(mode => mode)
      .Select(mode => Aggregate(
        reports.Where(report => report.Mode == mode).ToArray(),
        mode))
      .ToArray();
  }

  private static IReadOnlyList<string> GetRejectionReasons(
    RunPerformanceReport report,
    PerformanceMode mode,
    RunPerformanceReport? baseline)
  {
    var reasons = new SortedSet<string>(StringComparer.Ordinal);
    if (report.Mode != mode)
    {
      reasons.Add(PerformanceComparisonReasonCode.ModeMismatch);
    }

    if (report.ComparisonReasons.Count > 0)
    {
      foreach (var reason in report.ComparisonReasons)
      {
        reasons.Add(reason);
      }
    }

    if (!report.ComparisonEligible)
    {
      reasons.Add(PerformanceComparisonReasonCode.ComparisonIneligible);
    }

    if (!report.IsComplete)
    {
      reasons.Add(PerformanceComparisonReasonCode.TerminalIncomplete);
      reasons.Add(PerformanceComparisonReasonCode.TerminalStatusNotCompleted);
    }

    if (!report.TerminalSummary.WallElapsedMs.HasValue)
    {
      reasons.Add(PerformanceComparisonReasonCode.WallElapsedMissing);
    }

    if (report.Mode == mode && baseline is not null && report.IsComplete)
    {
      foreach (var reason in PerformanceEquivalenceChecker.Compare(baseline, report).ReasonCodes)
      {
        reasons.Add(reason);
      }
    }
    else if (report.Mode == mode && baseline is null)
    {
      reasons.Add(PerformanceComparisonReasonCode.IdentityMissing);
    }

    return reasons.ToArray();
  }

  private static PerformanceMetricAggregate? CreateMetricAggregate(
    IReadOnlyList<long> values)
  {
    if (values.Count < MinimumFormalMeasurementCount)
    {
      return null;
    }

    var ordered = values.OrderBy(value => value).ToArray();
    return new PerformanceMetricAggregate(
      ordered.Length,
      checked(ordered.Sum()),
      ordered[^1],
      PercentileNearestRank(ordered, 0.50),
      PercentileNearestRank(ordered, 0.95),
      ordered);
  }

  private static long PercentileNearestRank(
    IReadOnlyList<long> orderedValues,
    double percentile)
  {
    var rank = Math.Max(1, (int)Math.Ceiling(percentile * orderedValues.Count));
    return orderedValues[rank - 1];
  }

  private static PerformanceTopItemAggregate? FindTopItem(
    IReadOnlyList<RunPerformanceReport> reports)
  {
    return reports
      .SelectMany(report => report.Items
        .Where(item => item.Cpg?.BuildElapsedMs is not null)
        .Select(item => new PerformanceTopItemAggregate(
          item.ItemId,
          report.RunId,
          item.Cpg!.BuildElapsedMs!.Value)))
      .OrderByDescending(item => item.MaxWallElapsedMs)
      .ThenBy(item => item.ItemId, StringComparer.Ordinal)
      .ThenBy(item => item.RunId, StringComparer.Ordinal)
      .FirstOrDefault();
  }
}
