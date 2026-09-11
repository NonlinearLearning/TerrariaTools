using System.Text.Json;
using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.ContractTests.Performance;

public sealed class PerformanceSummarySchemaTests
{
  [Fact]
  public void SampleAggregateSchemaPreservesRawMeasurementsAndFormalMetrics()
  {
    var reports = new[]
    {
      CreateReport("run-1", 10, 100, 1),
      CreateReport("run-2", 20, 200, 2),
      CreateReport("run-3", 30, 300, 3)
    };
    var aggregate = PerformanceSampleAggregator.Aggregate(reports, PerformanceMode.Benchmark);
    var document = PerformanceSummaryDocument.FromReport(reports[0], aggregate);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
      document,
      PerformanceSummaryDocument.JsonOptions));

    var sampleAggregate = json.RootElement.GetProperty("sampleAggregate");
    Assert.Equal("benchmark", sampleAggregate.GetProperty("mode").GetString());
    Assert.Equal(3, sampleAggregate.GetProperty("measurementCount").GetInt32());
    Assert.False(sampleAggregate.GetProperty("isLowConfidence").GetBoolean());
    Assert.True(sampleAggregate.GetProperty("isFormalStatisticsEligible").GetBoolean());
    Assert.Equal(3, sampleAggregate.GetProperty("rawMeasurements").GetArrayLength());
    Assert.Equal(20, sampleAggregate.GetProperty("wall").GetProperty("median").GetInt64());
    Assert.Equal(30, sampleAggregate.GetProperty("wall").GetProperty("p95").GetInt64());
    Assert.Equal(200, sampleAggregate.GetProperty("accumulated").GetProperty("median").GetInt64());
  }

  [Fact]
  public void LowConfidenceSchemaKeepsRawValuesAndOmitsFormalPercentiles()
  {
    var reports = new[]
    {
      CreateReport("run-1", 10, 100, 1),
      CreateReport("run-2", 20, 200, 2)
    };
    var aggregate = PerformanceSampleAggregator.Aggregate(reports, PerformanceMode.Benchmark);
    var document = PerformanceSummaryDocument.FromReport(reports[0], aggregate);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
      document,
      PerformanceSummaryDocument.JsonOptions));

    var sampleAggregate = json.RootElement.GetProperty("sampleAggregate");
    Assert.True(sampleAggregate.GetProperty("isLowConfidence").GetBoolean());
    Assert.Equal(2, sampleAggregate.GetProperty("rawMeasurements").GetArrayLength());
    Assert.Equal(JsonValueKind.Null, sampleAggregate.GetProperty("wall").ValueKind);
    Assert.Equal(JsonValueKind.Null, sampleAggregate.GetProperty("accumulated").ValueKind);
  }

  private static RunPerformanceReport CreateReport(
    string runId,
    long wall,
    long accumulated,
    int sampleNumber)
  {
    return new RunPerformanceReport(
      runId,
      "file",
      "fixture",
      Array.Empty<ApplicationPerformanceFacts>(),
      new PerformanceStageSample(
        PerformanceStageId.Run,
        null,
        null,
        wall,
        accumulated),
      new PerformanceTerminalSummary(wall, accumulated, PerformanceStatus.Completed, true),
      PerformanceStatus.Completed,
      PerformanceMode.Benchmark,
      sampleNumber,
      comparisonEligible: true,
      identity: new PerformanceRunIdentity(
        "fixture",
        "rules",
        "capability",
        "warm",
        "sdk",
        "runtime",
        "os",
        "cpu",
        "environment",
        1,
        1,
        1,
        PerformanceMode.Benchmark,
        false,
        "graph",
        "rule",
        "artifact"));
  }
}
