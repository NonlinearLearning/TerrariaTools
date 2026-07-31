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
  public void RuleContexts_ExposeOnlyStageCapabilitiesToRules()
  {
    var contextText = File.ReadAllText(ProjectPath("src", "NLISSN.Rule", "RuleContext.cs"));
    var stageContextText = File.ReadAllText(ProjectPath("src", "NLISSN.Rule", "RuleExecutionContexts.cs"));
    Assert.DoesNotContain("public CpgAnalysisContext AnalysisContext", contextText, StringComparison.Ordinal);
    Assert.DoesNotContain("public NLCPGGraph Graph", contextText, StringComparison.Ordinal);
    Assert.DoesNotContain("public sealed class RuleContext :", contextText, StringComparison.Ordinal);
    Assert.Contains("private sealed class MarkRuleContext : IMarkRuleContext", contextText, StringComparison.Ordinal);
    Assert.Contains("private sealed class PropagationRuleContext : IPropagationRuleContext", contextText, StringComparison.Ordinal);
    Assert.Contains("private sealed class LiftRuleContext : ILiftRuleContext", contextText, StringComparison.Ordinal);
    Assert.Contains("private sealed class ProposeRuleContext : IProposeRuleContext", contextText, StringComparison.Ordinal);
    Assert.Contains("public interface IMarkRuleContext", stageContextText, StringComparison.Ordinal);
    Assert.Contains("public interface IPropagationRuleContext", stageContextText, StringComparison.Ordinal);
    Assert.Contains("public interface ILiftRuleContext", stageContextText, StringComparison.Ordinal);
    Assert.Contains("public interface IProposeRuleContext", stageContextText, StringComparison.Ordinal);

    AssertRuleDefinitionUsesStageContext("NLISSN.Core", "Marking", "RuleDefinitionMark.cs", "IMarkRuleContext");
    AssertRuleDefinitionUsesStageContext("NLISSN.Core", "Propagation", "RuleDefinitionPropagate.cs", "IPropagationRuleContext");
    AssertRuleDefinitionUsesStageContext("NLISSN.Core", "Lifting", "RuleDefinitionLift.cs", "ILiftRuleContext");
    AssertRuleDefinitionUsesStageContext("NLISSN.Core", "Decision", "RuleDefinitionPropose.cs", "IProposeRuleContext");
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
        "NLISSN"
      }
      .SelectMany(project => Directory.EnumerateFiles(
        ProjectPath("src", project),
        "*.cs",
        SearchOption.AllDirectories));

    foreach (var sourcePath in productionSources)
    {
      var source = File.ReadAllText(sourcePath);
      Assert.DoesNotContain("namespace RoslynPrototype", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Application", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Rules", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Host", source, StringComparison.Ordinal);
      Assert.DoesNotContain("namespace Logging", source, StringComparison.Ordinal);
      Assert.DoesNotMatch("(?m)^namespace (?!NLISSN(?:[.;]))", source);
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

  private static void AssertRuleDefinitionUsesStageContext(
    string project,
    string directory,
    string fileName,
    string stageContextName)
  {
    var definitionText = File.ReadAllText(ProjectPath("src", project, directory, fileName));
    Assert.Contains(stageContextName, definitionText, StringComparison.Ordinal);
    Assert.DoesNotMatch(@"\bRuleContext\s+context\b", definitionText);
  }
}
