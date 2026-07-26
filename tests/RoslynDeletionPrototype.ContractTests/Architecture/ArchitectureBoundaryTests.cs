using System.Diagnostics;
using Xunit;

namespace NLISSN.Tests.Architecture;

public sealed class ArchitectureBoundaryTests
{
  [Fact]
  public void LegacyProductionProjectsAndSourceFiles_AreAbsent()
  {
    AssertNoProductionFiles(ProjectPath("src", "Host"));
    AssertNoProductionFiles(ProjectPath("src", "Application"));
    AssertNoProductionFiles(ProjectPath("src", "Rules"));
    AssertNoProductionFiles(ProjectPath("src", "Logging"));
  }

  [Fact]
  public void LoggingProject_HasNoDependencies()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "NLISSN.Logging", "NLISSN.Logging.csproj"));
    Assert.DoesNotContain("<ProjectReference", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("<PackageReference", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void CoreRuleContext_DoesNotExposeTheFullGraph()
  {
    var contextText = File.ReadAllText(ProjectPath("src", "NLISSN.Core", "Pipeline", "RuleContext.cs"));
    Assert.DoesNotContain("public CpgAnalysisContext AnalysisContext", contextText, StringComparison.Ordinal);
    Assert.DoesNotContain("public RoslynCpgGraph Graph", contextText, StringComparison.Ordinal);
  }

  [Fact]
  public void ApplicationProject_DoesNotReferenceRules()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "NLISSN.Application", "NLISSN.Application.csproj"));
    Assert.DoesNotContain("NLISSN.Rules", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void DirectoryAnalysisUseCase_StaysWithinTheApplicationBoundary()
  {
    var useCasePath = ProjectPath("src", "NLISSN.Application", "Analysis", "DirectoryAnalysisUseCase.cs");
    Assert.True(File.Exists(useCasePath), "Directory analysis must have an Application-owned use case.");

    var useCaseText = File.ReadAllText(useCasePath);
    Assert.DoesNotContain("using NLISSN;", useCaseText, StringComparison.Ordinal);
    Assert.DoesNotContain("File.", useCaseText, StringComparison.Ordinal);
    Assert.DoesNotContain("Directory.", useCaseText, StringComparison.Ordinal);
  }

  [Fact]
  public void ProductionNamespaces_UseNlissnNamespaceRoot()
  {
    var productionSources = new[]
      {
        "NLISSN.Core",
        "NLISSN.Rules",
        "NLISSN.Application",
        "NLISSN",
        "NLISSN.Logging"
      }
      .SelectMany(project => Directory.EnumerateFiles(
        ProjectPath("src", project),
        "*.cs",
        SearchOption.AllDirectories));

    foreach (var sourcePath in productionSources)
    {
      var source = File.ReadAllText(sourcePath);
      Assert.DoesNotContain("namespace RoslynPrototype", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Deletion", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Application", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Rules", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Host", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Logging", source, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void MarkStage_HasNoPropagationDependency()
  {
    var markSources = Directory.EnumerateFiles(
      ProjectPath("src", "NLISSN.Rules", "Mark"),
      "*.cs",
      SearchOption.AllDirectories);

    foreach (var sourcePath in markSources)
    {
      Assert.DoesNotContain(
        "Propagation",
        File.ReadAllText(sourcePath),
        StringComparison.Ordinal);
    }
  }

  private static string ProjectPath(params string[] parts)
  {
    var sourceFile = new StackTrace(true).GetFrames()?
      .Select(frame => frame.GetFileName())
      .First(path => !string.IsNullOrWhiteSpace(path));
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile!)!);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, Path.Combine(parts));
  }

  private static void AssertNoProductionFiles(string path)
  {
    if (!Directory.Exists(path))
    {
      return;
    }

    Assert.Empty(Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories));
    Assert.Empty(Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories));
  }
}
