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

  /// <summary>
  /// 目录运行的文件条目**只能**序列化一份：挂在 <c>directory.items</c> 下。
  /// </summary>
  /// <remarks>
  /// 回归锁：模型层刻意让 <c>report.Items</c> 与 <c>report.Directory.Children</c>
  /// 指向同一批 facts（identity 指纹与样本聚合都读 <c>report.Items</c>），
  /// 但线上格式若两处都写，每个 item 就会在 JSON 里出现两次——
  /// 967 文件的真实运行据此写出 3.97 GB 摘要（两个 items 数组各占约一半）。
  /// </remarks>
  [Fact]
  public void DirectorySchemaSerializesFileFactsOnceUnderDirectoryItems()
  {
    var child = new ApplicationPerformanceFacts("a.cs", null, null, null);
    var directory = new DirectoryPerformanceFacts("directory", new[] { child });
    var report = CreateReport("directory-run", 10, 10, 1, directory: directory);

    var document = PerformanceSummaryDocument.FromReport(report);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
      document,
      PerformanceSummaryDocument.JsonOptions));

    var root = json.RootElement;
    Assert.Equal(
      new[] { "a.cs" },
      root.GetProperty("directory").GetProperty("items")
        .EnumerateArray()
        .Select(item => item.GetProperty("itemId").GetString()));
    Assert.Empty(root.GetProperty("items").EnumerateArray());
    // 整份 JSON 里该文件只出现一次（directory 自身的 itemId 是 "directory"）。
    Assert.Equal(
      1,
      JsonSerializer.Serialize(document, PerformanceSummaryDocument.JsonOptions)
        .Split("\"a.cs\"", StringSplitOptions.None).Length - 1);
  }

  [Fact]
  public void WorkspaceSchemaRetainsProjectsAndFileFactsInStableOrder()
  {
    var firstProject = new WorkspaceProjectPerformanceFacts(
      "z-project|net10.0",
      @"C:\workspace\z-project\z-project.csproj",
      "ZProject",
      "net10.0",
      new DirectoryPerformanceFacts(
        "z-project|net10.0",
        new[] { new ApplicationPerformanceFacts("z.cs", null, null, null) }));
    var secondProject = new WorkspaceProjectPerformanceFacts(
      "a-project|net10.0",
      @"C:\workspace\a-project\a-project.csproj",
      "AProject",
      "net10.0",
      new DirectoryPerformanceFacts(
        "a-project|net10.0",
        new[] { new ApplicationPerformanceFacts("a.cs", null, null, null) }));
    var workspace = new WorkspacePerformanceFacts(
      "workspace",
      new[] { firstProject, secondProject },
      new PerformanceAggregateSummary(2, 2, 20, 12, null, null, "a-project|net10.0", 12),
      new PerformanceStageSample(PerformanceStageId.WorkspaceLoad, PerformanceStageId.Run, "workspace", 8, null));
    var report = CreateReport("workspace-run", 20, 20, 1, workspace);

    var document = PerformanceSummaryDocument.FromReport(report);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
      document,
      PerformanceSummaryDocument.JsonOptions));

    var projects = json.RootElement.GetProperty("workspace")
      .GetProperty("projects")
      .EnumerateArray()
      .ToArray();
    Assert.Equal(2, projects.Length);
    Assert.Equal("AProject", projects[0].GetProperty("projectName").GetString());
    Assert.Equal("a-project|net10.0", projects[0].GetProperty("projectId").GetString());
    Assert.Equal("net10.0", projects[0].GetProperty("targetFramework").GetString());
    Assert.Equal("a.cs", projects[0].GetProperty("directory").GetProperty("items")[0]
      .GetProperty("itemId").GetString());
    Assert.Equal("ZProject", projects[1].GetProperty("projectName").GetString());
  }

  private static RunPerformanceReport CreateReport(
    string runId,
    long wall,
    long accumulated,
    int sampleNumber,
    WorkspacePerformanceFacts? workspace = null,
    DirectoryPerformanceFacts? directory = null)
  {
    return new RunPerformanceReport(
      runId,
      "file",
      "fixture",
      directory?.Children ?? Array.Empty<ApplicationPerformanceFacts>(),
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
        "artifact"),
       Directory: directory,
       workspace: workspace);
  }
}
