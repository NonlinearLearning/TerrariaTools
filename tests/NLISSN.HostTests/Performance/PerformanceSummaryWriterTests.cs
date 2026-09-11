using System.Text.Json;
using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceSummaryWriterTests
{
  [Fact]
  public void ToDocument_UsesStableWireSchemaAndPreservesNullZeroDistinction()
  {
    var report = CreateReport();

    var document = PerformanceSummaryDocument.FromReport(report);
    var json = JsonSerializer.Serialize(
      document,
      PerformanceSummaryDocument.JsonOptions);
    using var parsed = JsonDocument.Parse(json);

    Assert.Equal(1, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
    Assert.Equal("completed", parsed.RootElement.GetProperty("terminalStatus").GetString());
    Assert.Equal(
      new[] { "a.cs", "b.cs" },
      parsed.RootElement.GetProperty("items")
        .EnumerateArray()
        .Select(item => item.GetProperty("itemId").GetString()));
    var stage = parsed.RootElement.GetProperty("rootStage");
    Assert.True(stage.GetProperty("wallElapsedMs").ValueKind == JsonValueKind.Null);
    Assert.Equal(0, stage.GetProperty("accumulatedElapsedMs").GetInt64());
    Assert.False(parsed.RootElement.TryGetProperty("cpgBuilder", out _));
  }

  [Fact]
  public void WriteAtomic_WritesReadableSummaryAndRemovesTemporaryFile()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"nlissn-performance-writer-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "Performance", "summary.json");
    try
    {
      new PerformanceSummaryWriter().WriteAtomic(path, CreateReport());

      Assert.True(File.Exists(path));
      using var parsed = JsonDocument.Parse(File.ReadAllText(path));
      Assert.Equal("run-1", parsed.RootElement.GetProperty("runId").GetString());
      Assert.False(File.Exists(path + ".tmp"));
    }
    finally
    {
      if (Directory.Exists(directory))
      {
        Directory.Delete(directory, recursive: true);
      }
    }
  }

  private static RunPerformanceReport CreateReport()
  {
    var items = new[]
    {
      CreateItem("b.cs", 20),
      CreateItem("a.cs", 10)
    };
    return new RunPerformanceReport(
      "run-1",
      "file",
      "input-hash",
      items,
      new PerformanceStageSample(
        "Run",
        null,
        null,
        null,
        0),
      new PerformanceTerminalSummary(10, 0, PerformanceStatus.Completed, true),
      PerformanceStatus.Completed);
  }

  private static ApplicationPerformanceFacts CreateItem(string itemId, long elapsedMs)
  {
    return new ApplicationPerformanceFacts(
      itemId,
      new CpgPerformanceFacts(itemId, "source", elapsedMs),
      null,
      null);
  }
}
