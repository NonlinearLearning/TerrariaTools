using Xunit;

namespace NLISSN.Tests.Architecture;

public sealed class  LayoutArchitectureTests
{
  [Fact]
  public void ProductionProjects_UseTheNlissnProjectLayout()
  {
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Infrastructure", "Concurrency", "NL.Concurrency.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Core", "NLISSN.Core.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Rules", "NLISSN.Rules.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Application", "NLISSN.Application.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN", "NLISSN.csproj")));
  }

  [Fact]
  public void NlissnProject_IsTheDirectExecutableEntryPoint()
  {
    var legacyDirectory = Path.GetDirectoryName(ProjectPath("src", "RoslynPrototype", "placeholder"))!;
    Assert.False(Directory.Exists(legacyDirectory));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN", "Program.cs")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN", "AGENTS.md")));

    var projectText = File.ReadAllText(ProjectPath("src", "NLISSN", "NLISSN.csproj"));

    Assert.Contains("<OutputType>Exe</OutputType>", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void ProductionProjectReferences_MatchTheTargetDependencyGraph()
  {
    AssertProjectReferences(
      Array.Empty<string>(),
      "src", "NLISSN.Infrastructure", "Concurrency", "NL.Concurrency.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Infrastructure\\Caching\\NL.Caching.csproj",
        "..\\NLISSN.Infrastructure\\Concurrency\\NL.Concurrency.csproj",
        "..\\NLCPG\\NLCPG.csproj"
      },
      "src", "NLISSN.Core", "NLISSN.Core.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Infrastructure\\Concurrency\\NL.Concurrency.csproj",
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\NLCPG\\NLCPG.csproj"
      },
      "src", "NLISSN.Rules", "NLISSN.Rules.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Infrastructure\\Concurrency\\NL.Concurrency.csproj",
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\NLCPG\\NLCPG.csproj",
      },
      "src", "NLISSN.Application", "NLISSN.Application.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Infrastructure\\Concurrency\\NL.Concurrency.csproj",
        "..\\NLISSN.Application\\NLISSN.Application.csproj",
        "..\\NLISSN.Infrastructure\\Logging\\NLISSN.Logging.csproj",
        "..\\NLISSN.Rules\\NLISSN.Rules.csproj",
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\NLCPG\\NLCPG.csproj"
      },
      "src", "NLISSN", "NLISSN.csproj");
  }

  [Fact]
  public void ProductionSources_UseTheirOwningDirectoriesAndNamespaces()
  {
    var expectedSources = new[]
    {
      (new[] { "src", "NLISSN.Application", "ExecutionRuntime.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "RuleContext.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "IRuleDefinition.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Core", "Marking", "RuleDefinitionMark.cs" }, "NLISSN.Core.Marking"),
      (new[] { "src", "NLISSN.Core", "Propagation", "RuleDefinitionPropagate.cs" }, "NLISSN.Core.Propagation"),
      (new[] { "src", "NLISSN.Core", "Lifting", "RuleDefinitionLift.cs" }, "NLISSN.Core.Lifting"),
      (new[] { "src", "NLISSN.Core", "Decision", "RuleDefinitionPropose.cs" }, "NLISSN.Core.Decision"),
      (new[] { "src", "NLISSN.Rule", "RuleGraph.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "RuleGraphCompiler.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "RuleGraphExecutor.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "RuleStructureContract.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "RuleStructureContractGraphCompiler.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Application", "Analysis", "RulePipeline.cs" }, "NLISSN.Application"),
      (new[] { "src", "NLISSN", "Cli", "Parsing", "ApplicationOptions.cs" }, "NLISSN.Cli.Parsing"),
      (new[] { "src", "NLISSN", "Cli", "Hosting", "CliRunner.cs" }, "NLISSN.Cli.Hosting"),
      (new[] { "src", "NLISSN", "Cli", "Hosting", "CommandHost.cs" }, "NLISSN.Cli.Hosting"),
      (new[] { "src", "NLISSN", "Cli", "Hosting", "DirectoryAnalysisService.cs" }, "NLISSN.Cli.Hosting"),
      (new[] { "src", "NLISSN", "Artifacts", "DiffPathResolver.cs" }, "NLISSN.Artifacts"),
      (new[] { "src", "NLISSN", "Artifacts", "RewritePlanArtifactService.cs" }, "NLISSN.Artifacts"),
      (new[] { "src", "NLISSN", "Artifacts", "RewritePlanReplayService.cs" }, "NLISSN.Artifacts"),
      (new[] { "src", "NLISSN", "Composition", "RuleRegistry.cs" }, "NLISSN.Composition"),
      (new[] { "src", "NLISSN", "Telemetry", "RuntimeMeasurementLog.cs" }, "NLISSN.Telemetry")
    };

    foreach (var (pathParts, expectedNamespace) in expectedSources)
    {
      var sourcePath = RepositoryPath(pathParts);

      Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");
      Assert.Contains(
        $"namespace {expectedNamespace};",
        File.ReadAllText(sourcePath),
        StringComparison.Ordinal);
    }

    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RulePipeline.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleContext.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleContextServices.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleDefinition.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraph.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraphCompiler.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraphExecutor.cs")));

    var coreProjectText = File.ReadAllText(ProjectPath("src", "NLISSN.Core", "NLISSN.Core.csproj"));
    Assert.Contains("<Compile Include=\"..\\NLISSN.Application\\ExecutionRuntime.cs\"", coreProjectText, StringComparison.Ordinal);
    Assert.Contains("<Compile Include=\"..\\NLISSN.Rule\\*.cs\"", coreProjectText, StringComparison.Ordinal);

    var applicationProjectText = File.ReadAllText(ProjectPath("src", "NLISSN.Application", "NLISSN.Application.csproj"));
    Assert.Contains("<Compile Remove=\"ExecutionRuntime.cs\"", applicationProjectText, StringComparison.Ordinal);
    Assert.Equal(
      new[] { "Program.cs" },
      Directory.EnumerateFiles(ProjectPath("src", "NLISSN"), "*.cs", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(fileName => fileName, StringComparer.Ordinal));

    var programText = File.ReadAllText(ProjectPath("src", "NLISSN", "Program.cs"));
    Assert.Contains("CliRunner.RunAsync(args)", programText, StringComparison.Ordinal);
    Assert.DoesNotContain("class ", programText, StringComparison.Ordinal);
  }

  private static void AssertProjectReferences(IReadOnlyList<string> expected, params string[] projectParts)
  {
    var references = File.ReadLines(ProjectPath(projectParts))
      .Where(line => line.Contains("<ProjectReference", StringComparison.Ordinal))
      .Select(line => line.Split('"')[1])
      .ToArray();

    Assert.Equal(expected, references);
  }

  private static string RepositoryPath(params string[] parts)
  {
    var sourceFile = GetSourceFilePath();
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, Path.Combine(parts));
  }

  private static string ProjectPath(params string[] parts)
  {
    var sourceFile = GetSourceFilePath();
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, Path.Combine(parts));
  }

  private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") => sourceFile;
}
