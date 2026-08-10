using Xunit;

namespace NLISSN.Tests.Architecture;

public sealed class  LayoutArchitectureTests
{
  [Fact]
  public void ProductionProjects_UseTheNlissnProjectLayout()
  {
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Infrastructure", "Concurrency", "NL.Concurrency.csproj")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Infrastructure", "Testing", "NLISSN.TestComponents.csproj")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Infrastructure", "Workspace", "NLISSN.Workspace.csproj")));
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
      Array.Empty<string>(),
      "src", "NLISSN.Infrastructure", "Workspace", "NLISSN.Workspace.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\Logging\\NLISSN.Logging.csproj",
        "..\\..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\Workspace\\NLISSN.Workspace.csproj"
      },
      "src", "NLISSN.Infrastructure", "Configuration", "NLISSN.Configuration.csproj");
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
         "..\\NLISSN.Infrastructure\\Configuration\\NLISSN.Configuration.csproj",
         "..\\NLISSN.Infrastructure\\Workspace\\NLISSN.Workspace.csproj",
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
      (new[] { "src", "NLISSN.Rule", "AnalysisSession.cs" }, "NLISSN.Core.Pipeline"),
      (new[] { "src", "NLISSN.Rule", "StageRuleContexts.cs" }, "NLISSN.Core.Pipeline"),
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
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "AnalysisConfiguration.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "RulePolicySettings.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "ExecutionSettings.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "ArtifactSettings.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "LoggingSettings.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "ConfigurationProvenance.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "ResolvedConfigurationArtifact.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Configuration", "YamlConfigurationLoader.cs" }, "NLISSN.Infrastructure.Configuration"),
      (new[] { "src", "NLISSN.Infrastructure", "Testing", "TestComponent.cs" }, "NLISSN.Infrastructure.Testing"),
      (new[] { "src", "NLISSN.Infrastructure", "Testing", "TestComponentComposer.cs" }, "NLISSN.Infrastructure.Testing"),
      (new[] { "src", "NLISSN.Infrastructure", "Testing", "TestComponentCombinationGenerator.cs" }, "NLISSN.Infrastructure.Testing"),
      (new[] { "src", "NLISSN.Infrastructure", "Testing", "TestDiffContract.cs" }, "NLISSN.Infrastructure.Testing"),
      (new[] { "src", "NLISSN", "Hosting", "ConfigurationRunHost.cs" }, "NLISSN.Hosting"),
      (new[] { "src", "NLISSN", "Hosting", "CommandHost.cs" }, "NLISSN.Hosting"),
      (new[] { "src", "NLISSN", "Hosting", "DirectoryAnalysisService.cs" }, "NLISSN.Hosting"),
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
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rule", "RuleContext.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleContextServices.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleDefinition.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraph.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraphCompiler.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleGraphExecutor.cs")));
    Assert.False(Directory.Exists(ProjectPath("src", "NLISSN", "Configuration")));
    Assert.False(Directory.Exists(ProjectPath("src", "NLISSN", "Cli")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Infrastructure", "Configuration", "AnalysisOptions.cs")));

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
    Assert.Contains("args.Length != 0", programText, StringComparison.Ordinal);
    Assert.Contains("new ConfigurationRunHost().RunAsync()", programText, StringComparison.Ordinal);
    Assert.DoesNotContain("class ", programText, StringComparison.Ordinal);
  }

  [Fact]
  public void ProductionConfigurationBoundary_ContainsNoLegacyOptionModelOrOptionProperty()
  {
    var sourceFiles = Directory.EnumerateFiles(
      ProjectPath("src"),
      "*.cs",
      SearchOption.AllDirectories);

    foreach (var sourceFile in sourceFiles)
    {
      var source = File.ReadAllText(sourceFile);

      Assert.DoesNotContain("AnalysisOptions", source, StringComparison.Ordinal);
      Assert.DoesNotContain(
        "IReadOnlyDictionary<string, string> Options",
        source,
        StringComparison.Ordinal);
    }
  }

  [Fact]
  public void RuleSources_AreSplitIntoStageOwnedDirectories()
  {
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Mark", "MethodGlobal", "UnreachableMethodMarkRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Mark", "MethodGlobal", "UnreferencedMethodMarkRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Mark", "AtomicExpressions", "AtomicIdentifierNameMarkRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Mark", "Declarations", "DeclarationMarkRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Propagate", "ExpressionFlow", "AssignmentLeftValuePropagationRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Lift", "ControlStructures", "ControlStructureLiftingRule.cs")));
    Assert.True(File.Exists(ProjectPath(
      "src", "NLISSN.Rules", "Propose", "MethodGlobal", "UnreachableMethodProposalRule.cs")));

    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rules", "Mark", "AtomicMarkRules.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rules", "Mark", "DeclarationMarkRules.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rules", "Propagate", "ExpressionFlowPropagationRules.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rules", "Lift", "ControlStructureLiftingRules.cs")));
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rules", "Propose", "DeclarationProposalRules.cs")));
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
