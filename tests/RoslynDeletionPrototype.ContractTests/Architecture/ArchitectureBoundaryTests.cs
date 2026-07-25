using Xunit;

namespace RoslynPrototype.Tests;

public sealed class ArchitectureBoundaryTests
{
  [Fact]
  public void RoslynPrototypeProject_DoesNotSourceLinkApplicationOrRules()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "RoslynPrototype", "RoslynPrototype.csproj"));

    Assert.DoesNotContain(@"Compile Include=""..\Application\", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain(@"Compile Include=""..\Rules\", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain(
      @"ProjectReference Include=""..\Application\Application.csproj""",
      projectText,
      StringComparison.Ordinal);
    Assert.DoesNotContain(
      @"ProjectReference Include=""..\Rules\Rules.csproj""",
      projectText,
      StringComparison.Ordinal);
    Assert.Contains(@"ProjectReference Include=""..\Host\Host.csproj""", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void BoundaryProjects_ExistAsStandaloneProjects()
  {
    Assert.True(File.Exists(ProjectPath("src", "Application", "Application.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Host", "Host.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Logging", "Logging.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Rules", "Rules.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "RoslynPrototype", "RoslynPrototype.Core.csproj")));
  }

  [Fact]
  public void LoggingProject_HasNoProjectOrPackageDependencies()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "Logging", "Logging.csproj"));

    Assert.DoesNotContain("<ProjectReference", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("<PackageReference", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void RoslynPrototypeProject_DoesNotCompileHostSurface()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "RoslynPrototype", "RoslynPrototype.csproj"));

    Assert.DoesNotContain("DeletionCommandHost", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionResultFormatter", projectText, StringComparison.Ordinal);
    Assert.False(File.Exists(ProjectPath("src", "RoslynPrototype", "Host", "DeletionCommandHost.cs")));
    Assert.False(File.Exists(ProjectPath("src", "RoslynPrototype", "Host", "DeletionResultFormatter.cs")));
    Assert.False(File.Exists(ProjectPath("src", "RoslynPrototype", "Host", "RuleRegistry.cs")));
  }

  [Fact]
  public void CoreProject_DoesNotGrantFriendAccessToRules()
  {
    var coreRoot = ProjectPath("src", "RoslynPrototype");
    var sourceFiles = Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
      .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Host{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
      .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}samples{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    foreach (var sourceFile in sourceFiles)
    {
      var sourceText = File.ReadAllText(sourceFile);

      Assert.DoesNotContain("InternalsVisibleTo", sourceText, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void ApplicationProject_DoesNotCompileHostOrIoSurface()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "Application", "Application.csproj"));

    Assert.DoesNotContain(@"ProjectReference Include=""..\Rules\Rules.csproj""", projectText, StringComparison.Ordinal);
    var pipelineContractText = File.ReadAllText(ProjectPath("src", "Application", "DeletionRulePipeline.cs"));

    Assert.DoesNotContain("RuleImplementationAssemblyMarker", pipelineContractText, StringComparison.Ordinal);
    Assert.DoesNotContain("CreateDefaultRules", pipelineContractText, StringComparison.Ordinal);
    var serviceText = File.ReadAllText(ProjectPath("src", "Application", "DeletionApplicationService.cs"));

    Assert.DoesNotContain("public DeletionRulePipeline Pipeline", serviceText, StringComparison.Ordinal);
    Assert.DoesNotContain("Pipeline =>", serviceText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionCommandHost", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionDirectoryAnalysisService", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionDiffPathResolver", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionApplicationServiceCompatibilityExtensions", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("DeletionResultFormatter", projectText, StringComparison.Ordinal);
    Assert.False(File.Exists(ProjectPath("src", "Application", "DeletionCommandHost.cs")));
    Assert.False(File.Exists(ProjectPath("src", "Application", "DeletionDirectoryAnalysisService.cs")));
    Assert.False(File.Exists(ProjectPath("src", "Application", "DeletionDiffPathResolver.cs")));
    Assert.False(File.Exists(ProjectPath("src", "Application", "DeletionApplicationServiceCompatibilityExtensions.cs")));
    Assert.False(File.Exists(ProjectPath("src", "Application", "DeletionResultFormatter.cs")));
  }

  [Fact]
  public void RuleContext_DoesNotExposeFullGraphOrAnalysisContext()
  {
    var contextText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "RuleServices",
      "RuleContext.cs"));

    Assert.DoesNotContain("public CpgAnalysisContext AnalysisContext", contextText, StringComparison.Ordinal);
    Assert.DoesNotContain("public RoslynCpgGraph Graph", contextText, StringComparison.Ordinal);
  }

  [Fact]
  public void RuleContext_ExposesServiceCenterSurfaces()
  {
    var contextType = typeof(Rules.RuleContext);

    Assert.NotNull(contextType.GetProperty("Options"));
    Assert.NotNull(contextType.GetProperty("Analysis"));
    Assert.NotNull(contextType.GetProperty("GraphBinding"));
    Assert.NotNull(contextType.GetProperty("StructureViews"));
  }

  [Fact]
  public void MarkingLayer_DoesNotDependOnPropagation()
  {
    var markingRoot = ProjectPath("src", "RoslynPrototype", "Marking");
    var sourceFiles = Directory.EnumerateFiles(markingRoot, "*.cs", SearchOption.AllDirectories);

    foreach (var sourceFile in sourceFiles)
    {
      var sourceText = File.ReadAllText(sourceFile);

      Assert.DoesNotContain("using RoslynPrototype.Propagation;", sourceText, StringComparison.Ordinal);
      Assert.DoesNotContain("PropagatedMarkRecord", sourceText, StringComparison.Ordinal);
      Assert.DoesNotContain("BindPropagatedMarkRecord", sourceText, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void StageEngines_UseCentralizedRuleStageGroupKey()
  {
    var markingEngineText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "Marking",
      "MarkingEngine.cs"));
    var propagationEngineText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "Propagation",
      "PropagationEngine.cs"));
    var liftingEngineText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "Lifting",
      "MarkLiftingEngine.cs"));
    var decisionModelText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "Decision",
      "DecisionModel.cs"));
    var groupKeyText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "RuleServices",
      "RuleStageGroupKey.cs"));

    Assert.Contains("internal static class RuleStageGroupKey", groupKeyText, StringComparison.Ordinal);
    Assert.DoesNotContain("GetGroupKey(MarkRecord", markingEngineText, StringComparison.Ordinal);
    Assert.DoesNotContain(
      "GetGroupKey(PropagatedMarkRecord",
      propagationEngineText,
      StringComparison.Ordinal);
    Assert.DoesNotContain(
      "GetPropagatedGroupKey(PropagatedMarkRecord",
      liftingEngineText,
      StringComparison.Ordinal);
    Assert.DoesNotContain(
      "GetPropagatedGroupKey(PropagatedMarkRecord",
      decisionModelText,
      StringComparison.Ordinal);
  }

  [Fact]
  public void RuleServices_CoreFiles_DoNotReferenceConcreteStageEngines()
  {
    var sourceFiles = new[]
    {
      ProjectPath("src", "RoslynPrototype", "RuleServices", "RuleContext.cs"),
      ProjectPath("src", "RoslynPrototype", "RuleServices", "RuleDefinition.cs"),
      ProjectPath("src", "RoslynPrototype", "RuleServices", "RuleServices.cs"),
      ProjectPath("src", "RoslynPrototype", "RuleServices", "RuleStageGroupKey.cs")
    };
    var forbiddenTokens = new[]
    {
      "MarkingEngine",
      "PropagationEngine",
      "MarkLiftingEngine",
      "RuleDecisionEngine",
      "DeleteDecisionFactory",
      "DecisionCpgFactory",
      "PrototypeRewriter"
    };

    foreach (var sourceFile in sourceFiles)
    {
      var sourceText = File.ReadAllText(sourceFile);
      foreach (var forbiddenToken in forbiddenTokens)
      {
        Assert.DoesNotContain(forbiddenToken, sourceText, StringComparison.Ordinal);
      }
    }
  }

  [Fact]
  public void RuleIdentity_DoesNotUseLegacyMetadataFiles()
  {
    Assert.Empty(Directory.GetFiles(
      ProjectPath("src", "Rules", "RuleIds"),
      "*.cs",
      SearchOption.AllDirectories));
    Assert.Empty(Directory.GetFiles(
      ProjectPath("src", "Rules", "RuleMetadata"),
      "*.cs",
      SearchOption.AllDirectories));
  }

  private static string ProjectPath(params string[] parts)
  {
    return Path.Combine(GetRepositoryRoot(), Path.Combine(parts));
  }

  private static string GetRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "global.json")))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
  }
}
