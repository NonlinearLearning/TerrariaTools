using System.Text.Json;
using NLISSN.Composition;
using NLISSN.Hosting;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;
using Xunit;

namespace RoslynPrototype.Tests.Workspace;

public sealed class WorkspaceAnalysisPerformanceTests : IDisposable
{
  private readonly string _temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-workspace-performance-{Guid.NewGuid():N}");

  public WorkspaceAnalysisPerformanceTests()
  {
    Directory.CreateDirectory(_temporaryDirectory);
  }

  [Fact]
  public async Task Analyze_WorkspacePerformanceRetainsProjectAndTargetFrameworkLevels()
  {
    var solutionPath = FixturePath("WorkspaceFixture.sln");
    var summaryPath = Path.Combine(_temporaryDirectory, "run", "Performance", "summary.json");
    var configuration = new AnalysisConfiguration(
      solutionPath,
      new RulePolicySettings(null, null, new HashSet<string>(StringComparer.Ordinal), false, false, false, false, false),
      new ExecutionSettings(false, false, 1, null, false, false, true, false, false),
      new ArtifactSettings(
        Path.Combine(_temporaryDirectory, "run"),
        Path.Combine(_temporaryDirectory, "diff"),
        Path.Combine(_temporaryDirectory, "runtime.log"),
        Path.Combine(_temporaryDirectory, "evidence.json"),
        Path.Combine(_temporaryDirectory, "rewrite-plan"),
        null,
        Path.Combine(_temporaryDirectory, "resolved.json"),
        false,
        false,
        false,
        RewritePlanMode.None,
        "legacy",
        "workspace-performance",
        true,
        "normal",
        summaryPath),
      new LoggingSettings("normal", "error", Array.Empty<string>(), Array.Empty<string>(), "normal"),
      new ConfigurationProvenance(2, 2, Array.Empty<string>(), new Dictionary<string, string>()),
      new WorkspaceInputOptions(solutionPath, TargetFramework: "net10.0"));

    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeAsync(configuration);

    Assert.NotNull(outcome.Result);
    using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
    var workspace = document.RootElement.GetProperty("workspace");
    var projects = workspace.GetProperty("projects").EnumerateArray().ToArray();
    Assert.NotEmpty(projects);
    Assert.All(projects, project =>
    {
      Assert.False(string.IsNullOrWhiteSpace(project.GetProperty("projectPath").GetString()));
      Assert.False(string.IsNullOrWhiteSpace(project.GetProperty("projectName").GetString()));
      Assert.Equal("net10.0", project.GetProperty("targetFramework").GetString());
      Assert.NotEmpty(project.GetProperty("directory").GetProperty("items").EnumerateArray());
    });
  }

  public void Dispose()
  {
    if (Directory.Exists(_temporaryDirectory))
    {
      Directory.Delete(_temporaryDirectory, recursive: true);
    }
  }

  private static string FixturePath(params string[] parts)
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(
      current!.FullName,
      "tests",
      "NLISSN.Testing",
      "TestCodeSet",
      "Workspace",
      Path.Combine(parts));
  }
}
