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
    var identity = document.RootElement.GetProperty("identity");
    Assert.Equal("normal", identity.GetProperty("mode").GetString());
    Assert.False(string.IsNullOrWhiteSpace(identity.GetProperty("sourceManifestHash").GetString()));
    Assert.False(string.IsNullOrWhiteSpace(identity.GetProperty("configurationFingerprint").GetString()));
    Assert.Equal(1, identity.GetProperty("directoryDop").GetInt32());
    Assert.Equal(1, identity.GetProperty("cpgDop").GetInt32());
    Assert.Equal(1, identity.GetProperty("ruleDop").GetInt32());
    var stages = document.RootElement.GetProperty("stages").EnumerateArray().ToArray();
    Assert.Equal("completed", stages.Single(stage => stage.GetProperty("stageId").GetString() == "CPG.Build").GetProperty("status").GetString());
    Assert.Contains(stages, stage => stage.GetProperty("stageId").GetString() == "Artifact.Rewrite");
    Assert.Empty(document.RootElement.GetProperty("attachments").EnumerateArray());
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
  public async Task DiagnosticPerformancePublishesDiagnosticAttachmentAndCompletedCpgStage()
  {
    var sourcePath = WriteSource("diagnostic.cs");
    File.WriteAllText(sourcePath, "public sealed class Input { public int Value(int x) { return x + 1; } }");
    var summaryPath = Path.Combine(_root, "diagnostic-run", "Performance", "summary.json");

    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(
      CreateConfiguration(sourcePath, summaryPath, writePerformance: true, performanceMode: "diagnostic"));

    Assert.NotNull(outcome.Result);
    using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
    var identity = document.RootElement.GetProperty("identity");
    Assert.True(identity.GetProperty("diagnosticsEnabled").GetBoolean());

    var cpgStage = document.RootElement.GetProperty("stages")
      .EnumerateArray()
      .Single(stage => stage.GetProperty("stageId").GetString() == "CPG.Build");
    Assert.Equal("completed", cpgStage.GetProperty("status").GetString());

    var attachment = document.RootElement.GetProperty("attachments")
      .EnumerateArray()
      .Single(item => item.GetProperty("kind").GetString() == "diagnostic-events");
    Assert.Equal("completed", attachment.GetProperty("status").GetString());
    Assert.True(attachment.GetProperty("isComplete").GetBoolean());
    Assert.True(File.Exists(Path.Combine(
      Path.GetDirectoryName(summaryPath)!,
      "..",
      attachment.GetProperty("relativePath").GetString()!.Replace('/', Path.DirectorySeparatorChar))));
    var diagnosticPath = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(summaryPath)!,
      "..",
      attachment.GetProperty("relativePath").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
    using var diagnostics = JsonDocument.Parse(File.ReadAllText(diagnosticPath));
    Assert.NotEmpty(diagnostics.RootElement.GetProperty("events").EnumerateArray().ToArray());
    Assert.NotEmpty(diagnostics.RootElement.GetProperty("partitions").EnumerateArray().ToArray());
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
    Assert.False(string.IsNullOrWhiteSpace(outcome.Performance.PublicationErrorKind));
    Assert.False(File.Exists(summaryPath));
  }

  [Fact]
  public async Task BusinessFailurePublishesFailedTerminalPerformanceSummary()
  {
    var sourcePath = WriteSource("failed.cs");
    var occupiedEvidencePath = Path.Combine(_root, "occupied-evidence");
    File.WriteAllText(occupiedEvidencePath, "occupied");
    var summaryPath = Path.Combine(_root, "failed-run", "Performance", "summary.json");

    await Assert.ThrowsAsync<IOException>(() => new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(
      CreateConfiguration(
        sourcePath,
        summaryPath,
        writePerformance: true,
        writeEvidence: true,
        evidencePath: Path.Combine(occupiedEvidencePath, "evidence.json"))));

    Assert.True(File.Exists(summaryPath));
    using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
    Assert.Equal("failed", document.RootElement.GetProperty("terminalStatus").GetString());
    Assert.False(document.RootElement.GetProperty("isComplete").GetBoolean());
    Assert.False(string.IsNullOrWhiteSpace(
      document.RootElement.GetProperty("terminalSummary").GetProperty("errorKind").GetString()));
    var evidenceStage = document.RootElement.GetProperty("stages")
      .EnumerateArray()
      .Single(stage => stage.GetProperty("stageId").GetString() == "Artifact.Evidence");
    Assert.Equal("failed", evidenceStage.GetProperty("status").GetString());
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
    bool writePerformance,
    string performanceMode = "normal",
    bool writeEvidence = false,
    string? evidencePath = null)
  {
    return new AnalysisConfiguration(
      sourcePath,
      new RulePolicySettings(null, null, new HashSet<string>(StringComparer.Ordinal), false, false, false, false, false),
      new ExecutionSettings(false, false, 1, 1, 1, 1, 1, 1, false, false, true, false, false),
      new ArtifactSettings(
        Path.Combine(_root, "run"),
        Path.Combine(_root, "diff"),
        Path.Combine(_root, "runtime.log"),
        evidencePath ?? Path.Combine(_root, "evidence.json"),
        Path.Combine(_root, "rewrite-plan"),
        null,
        Path.Combine(_root, "resolved.json"),
        false,
        false,
        writeEvidence,
        RewritePlanMode.None,
        "legacy",
        "run-1",
        writePerformance,
        performanceMode,
        summaryPath),
      new LoggingSettings("normal", "error", Array.Empty<string>(), Array.Empty<string>(), "normal"),
      new ConfigurationProvenance(2, 2, Array.Empty<string>(), new Dictionary<string, string>()));
  }
}
