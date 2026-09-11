using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceSampleAggregatorTests
{
  [Fact]
  public void Aggregate_ExcludesWarmupAndRejectedRuns_AndKeepsRawValues()
  {
    var baseline = CreateReport("run-0", wall: 999, accumulated: 999, isWarmup: true);
    var first = CreateReport("run-1", wall: 10, accumulated: 100);
    var second = CreateReport("run-2", wall: 20, accumulated: 200);
    var rejected = CreateReport(
      "run-rejected",
      wall: 30,
      accumulated: 300,
      terminalStatus: PerformanceStatus.Failed);

    var result = PerformanceSampleAggregator.Aggregate(
      new[] { baseline, first, second, rejected },
      PerformanceMode.Benchmark);

    Assert.Equal(4, result.RawReports.Count);
    Assert.Single(result.WarmupRunIds);
    Assert.Contains("run-rejected", result.RejectedRunIds);
    Assert.Equal(2, result.MeasurementCount);
    Assert.True(result.IsLowConfidence);
    Assert.Equal(
      new[] { 10L, 20L },
      result.RawMeasurements
        .Where(sample => sample.WallElapsedMs.HasValue)
        .Select(sample => sample.WallElapsedMs!.Value));
    Assert.Null(result.Wall);
    Assert.Null(result.Accumulated);
  }

  [Fact]
  public void Aggregate_WithThreeMeasurements_UsesNearestRankAndKeepsWallSeparateFromAccumulated()
  {
    var reports = new[]
    {
      CreateReport("run-3", wall: 30, accumulated: 300),
      CreateReport("run-1", wall: 10, accumulated: 100, itemWall: ("slow.cs", 15)),
      CreateReport("run-2", wall: 20, accumulated: 200, itemWall: ("slow.cs", 25))
    };

    var result = PerformanceSampleAggregator.Aggregate(reports, PerformanceMode.Benchmark);

    Assert.Equal(3, result.MeasurementCount);
    Assert.False(result.IsLowConfidence);
    Assert.Equal(3, result.Wall!.Count);
    Assert.Equal(60, result.Wall.Sum);
    Assert.Equal(30, result.Wall.Max);
    Assert.Equal(20, result.Wall.Median);
    Assert.Equal(30, result.Wall.P95);
    Assert.Equal(600, result.Accumulated!.Sum);
    Assert.Equal(200, result.Accumulated.Median);
    Assert.Equal(300, result.Accumulated.P95);
    Assert.Equal("slow.cs", result.TopItem!.ItemId);
    Assert.Equal(25, result.TopItem.MaxWallElapsedMs);
    Assert.Equal("run-2", result.TopItem.RunId);
  }

  [Fact]
  public void Aggregate_DoesNotMixModes_AndDiagnosticSamplesRemainOutOfFormalBenchmarkGroup()
  {
    var benchmark = CreateReport("benchmark", wall: 10, accumulated: 100, mode: PerformanceMode.Benchmark);
    var normal = CreateReport("normal", wall: 20, accumulated: 200, mode: PerformanceMode.Normal);
    var diagnostic = CreateReport("diagnostic", wall: 30, accumulated: 300, mode: PerformanceMode.Diagnostic);

    var result = PerformanceSampleAggregator.Aggregate(
      new[] { benchmark, normal, diagnostic },
      PerformanceMode.Benchmark);

    Assert.Equal(3, result.RawReports.Count);
    Assert.Contains("benchmark", result.MeasurementRunIds);
    Assert.Equal(2, result.RejectedRunIds.Count);
    Assert.Contains("normal", result.RejectedRunIds);
    Assert.Contains("diagnostic", result.RejectedRunIds);
    Assert.Equal(1, result.MeasurementCount);
  }

  [Fact]
  public void AggregateByMode_ReturnsIndependentGroups()
  {
    var reports = new[]
    {
      CreateReport("benchmark", wall: 10, accumulated: 100, mode: PerformanceMode.Benchmark),
      CreateReport("normal", wall: 20, accumulated: 200, mode: PerformanceMode.Normal),
      CreateReport("diagnostic", wall: 30, accumulated: 300, mode: PerformanceMode.Diagnostic)
    };

    var groups = PerformanceSampleAggregator.AggregateByMode(reports);

    Assert.Equal(
      new[] { PerformanceMode.Normal, PerformanceMode.Diagnostic, PerformanceMode.Benchmark },
      groups.Select(group => group.Mode));
    Assert.All(groups, group => Assert.Single(group.RawReports));
  }

  private static RunPerformanceReport CreateReport(
    string runId,
    long wall,
    long accumulated,
    PerformanceMode mode = PerformanceMode.Benchmark,
    bool isWarmup = false,
    PerformanceStatus terminalStatus = PerformanceStatus.Completed,
    (string ItemId, long Wall)? itemWall = null)
  {
    var itemId = itemWall?.ItemId ?? "file.cs";
    var item = new ApplicationPerformanceFacts(
      itemId,
      itemWall.HasValue
        ? new CpgPerformanceFacts(itemId, itemId, itemWall.Value.Wall)
        : null,
      null,
      null);
    return new RunPerformanceReport(
      runId,
      "file",
      "input-hash",
      new[] { item },
      new PerformanceStageSample(
        PerformanceStageId.Run,
        null,
        null,
        wall,
        accumulated,
        terminalStatus),
      new PerformanceTerminalSummary(wall, accumulated, terminalStatus, terminalStatus == PerformanceStatus.Completed),
      terminalStatus,
      mode,
      sampleNumber: 1,
      isWarmup: isWarmup,
      comparisonEligible: !isWarmup && terminalStatus == PerformanceStatus.Completed,
      identity: CreateIdentity(mode));
  }

  private static PerformanceRunIdentity CreateIdentity(PerformanceMode mode)
  {
    return new PerformanceRunIdentity(
      "input-hash",
      "rules-1",
      "capability-1",
      "warm",
      "sdk-1",
      "runtime-1",
      "os-1",
      "cpu-1",
      "environment-1",
      1,
      1,
      1,
      mode,
      mode == PerformanceMode.Diagnostic,
      "graph-1",
      "rule-1",
      "artifact-1");
  }
}
