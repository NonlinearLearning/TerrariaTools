using System.Text.Json;
using NLISSN.Core.Pipeline;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Hosting;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceArtifactEndToEndTests : IDisposable
{
  private readonly string _root = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-performance-artifact-{Guid.NewGuid():N}");

  public PerformanceArtifactEndToEndTests()
  {
    Directory.CreateDirectory(_root);
  }

  [Fact]
  public async Task EnabledPerformancePublishesTerminalSummaryAfterBusinessResultCompletes()
  {
    var sourcePath = WriteSource("enabled.cs");
    var summaryPath = Path.Combine(_root, "run", "Performance", "summary.json");

    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(
      CreateConfiguration(sourcePath, summaryPath, writePerformance: true));

    Assert.NotNull(outcome.Result);
    Assert.True(File.Exists(summaryPath));
    using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
    Assert.Equal("run-1", document.RootElement.GetProperty("runId").GetString());
    Assert.Equal("completed", document.RootElement.GetProperty("terminalStatus").GetString());
    Assert.Equal("file", document.RootElement.GetProperty("inputKind").GetString());
  }

  [Fact]
  public async Task DisabledPerformanceDoesNotCreateSummary()
  {
    var sourcePath = WriteSource("disabled.cs");
    var summaryPath = Path.Combine(_root, "disabled", "Performance", "summary.json");

    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(
      CreateConfiguration(sourcePath, summaryPath, writePerformance: false));

    Assert.NotNull(outcome.Result);
    Assert.False(File.Exists(summaryPath));
  }

  [Fact]
  public async Task PerformanceWriterFailureDoesNotChangeBusinessOutcome()
  {
    var sourcePath = WriteSource("writer-failure.cs");
    var parentFile = Path.Combine(_root, "occupied");
    File.WriteAllText(parentFile, "occupied");
    var summaryPath = Path.Combine(parentFile, "Performance", "summary.json");

    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(
      CreateConfiguration(sourcePath, summaryPath, writePerformance: true));

    Assert.NotNull(outcome.Result);
    Assert.Equal("completed", outcome.Performance.TerminalStatus.ToString().ToLowerInvariant());
    Assert.False(File.Exists(summaryPath));
  }

  public void Dispose()
  {
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  private string WriteSource(string fileName)
  {
    var path = Path.Combine(_root, fileName);
    File.WriteAllText(path, "public sealed class Input { public int Value => 1; }");
    return path;
  }

  private AnalysisConfiguration CreateConfiguration(
    string sourcePath,
    string summaryPath,
    bool writePerformance)
  {
    return new AnalysisConfiguration(
      sourcePath,
      new RulePolicySettings(null, null, new HashSet<string>(StringComparer.Ordinal), false, false, false, false, false),
      new ExecutionSettings(false, false, 1, null, false, false, true, false, false),
      new ArtifactSettings(
        Path.Combine(_root, "run"),
        Path.Combine(_root, "diff"),
        Path.Combine(_root, "runtime.log"),
        Path.Combine(_root, "evidence.json"),
        Path.Combine(_root, "rewrite-plan"),
        null,
        Path.Combine(_root, "resolved.json"),
        false,
        false,
        false,
        RewritePlanMode.None,
        "legacy",
        "run-1",
        writePerformance,
        "normal",
        summaryPath),
      new LoggingSettings("normal", "error", Array.Empty<string>(), Array.Empty<string>(), "normal"),
      new ConfigurationProvenance(2, 2, Array.Empty<string>(), new Dictionary<string, string>()));
  }
}
