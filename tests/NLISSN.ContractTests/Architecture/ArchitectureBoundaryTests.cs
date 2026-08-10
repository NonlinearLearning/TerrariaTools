using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Pipeline;
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
    var stageContextText = File.ReadAllText(ProjectPath("src", "NLISSN.Rule", "RuleExecutionContexts.cs"));
    var sessionPath = ProjectPath("src", "NLISSN.Rule", "AnalysisSession.cs");
    var assemblyInfoPath = ProjectPath("src", "NLISSN.Core", "Properties", "AssemblyInfo.cs");
    Assert.False(File.Exists(ProjectPath("src", "NLISSN.Rule", "RuleContext.cs")));
    Assert.True(File.Exists(sessionPath));
    Assert.DoesNotContain("InternalsVisibleTo(\"NLISSN.Rules\")", File.ReadAllText(assemblyInfoPath), StringComparison.Ordinal);
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
  public void StageContexts_CannotBeCastAcrossRuleStages()
  {
    var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "stage-contexts.cs");
    var compilation = CSharpCompilation.Create(
      "StageContextArchitecture",
      new[] { tree },
      new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
    var session = new AnalysisSession(
      new CpgAnalysisContext(new NLCPGGraph(), compilation.GetSemanticModel(tree), tree.GetRoot()),
      AnalysisLegacyOptionsTestExtensions.CreateSettings(new Dictionary<string, string>()));

    AssertExclusive(session.CreateMarkContext(), typeof(IPropagationRuleContext), typeof(ILiftRuleContext), typeof(IProposeRuleContext));
    AssertExclusive(session.CreatePropagationContext(Array.Empty<NLISSN.Core.Marking.MarkRecord>()), typeof(IMarkRuleContext), typeof(ILiftRuleContext), typeof(IProposeRuleContext));
    AssertExclusive(session.CreateLiftContext(Array.Empty<NLISSN.Core.Marking.MarkRecord>(), Array.Empty<NLISSN.Core.Propagation.PropagatedMarkRecord>()), typeof(IMarkRuleContext), typeof(IPropagationRuleContext), typeof(IProposeRuleContext));
    AssertExclusive(session.CreateProposeContext(), typeof(IMarkRuleContext), typeof(IPropagationRuleContext), typeof(ILiftRuleContext));
    Assert.NotNull(typeof(IPropagationRuleContext).GetMethod("ResolveCallFlow"));
    Assert.Null(typeof(IProposeRuleContext).GetMethod("ResolveCallFlow"));
  }

  [Fact]
  public void ApplicationProject_DoesNotReferenceRules()
  {
    var projectText = File.ReadAllText(ProjectPath("src", "NLISSN.Application", "NLISSN.Application.csproj"));
    Assert.DoesNotContain("NLISSN.Rules", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkspaceProject_StaysBelowApplicationAndOwnsMsBuildWorkspace()
  {
    var workspaceDirectory = ProjectPath("src", "NLISSN.Infrastructure", "Workspace");
    var projectPath = Path.Combine(workspaceDirectory, "NLISSN.Workspace.csproj");
    Assert.True(File.Exists(projectPath));

    var projectText = File.ReadAllText(projectPath);
    Assert.Contains("Microsoft.CodeAnalysis.Workspaces.MSBuild", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("NLISSN.Application", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("NLISSN.Core", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain("NLISSN.Rules", projectText, StringComparison.Ordinal);

    var workspaceSources = Directory.EnumerateFiles(workspaceDirectory, "*.cs", SearchOption.AllDirectories);
    Assert.NotEmpty(workspaceSources);
    Assert.Contains(workspaceSources, sourcePath =>
      File.ReadAllText(sourcePath).Contains("MSBuildWorkspace", StringComparison.Ordinal));
    foreach (var sourceDirectory in new[] { "NLISSN.Application", "NLISSN.Core", "NLISSN.Rules", "NLCPG" })
    {
      var directory = ProjectPath("src", sourceDirectory);
      if (!Directory.Exists(directory))
      {
        continue;
      }

      Assert.DoesNotContain(
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories),
        sourcePath => File.ReadAllText(sourcePath).Contains("MSBuildWorkspace", StringComparison.Ordinal));
    }
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

  private static void AssertExclusive(object context, params Type[] forbiddenInterfaces)
  {
    foreach (var forbiddenInterface in forbiddenInterfaces)
    {
      Assert.False(forbiddenInterface.IsInstanceOfType(context), $"Context unexpectedly exposes {forbiddenInterface.Name}.");
    }
  }
}
