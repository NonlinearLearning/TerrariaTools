using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MinimalRoslynCpgProjectBoundaryTests
{
  [Fact]
  public void CpgProjects_WhenSeparated_KeepThePlannedDependencyDirection()
  {
    var repositoryRoot = GetRepositoryRoot();
    var coreProject = ReadProject(repositoryRoot, "MinimalRoslynCpg.Core");
    var persistenceProject = ReadProject(repositoryRoot, "MinimalRoslynCpg.Persistence");
    var queryProject = ReadProject(repositoryRoot, "MinimalRoslynCpg.Query");
    var cliProject = XDocument.Load(Path.Combine(
      repositoryRoot,
      "src",
      "MinimalRoslynCpg",
      "MinimalRoslynCpg.csproj"));

    Assert.Empty(ProjectReferences(coreProject));
    Assert.Equal(
      new[] { "..\\MinimalRoslynCpg.Core\\MinimalRoslynCpg.Core.csproj" },
      ProjectReferences(persistenceProject));
    Assert.Equal(
      new[]
      {
        "..\\MinimalRoslynCpg.Core\\MinimalRoslynCpg.Core.csproj",
        "..\\MinimalRoslynCpg.Persistence\\MinimalRoslynCpg.Persistence.csproj",
      },
      ProjectReferences(queryProject));
    Assert.Equal(
      new[]
      {
        "..\\MinimalRoslynCpg.Core\\MinimalRoslynCpg.Core.csproj",
        "..\\MinimalRoslynCpg.Persistence\\MinimalRoslynCpg.Persistence.csproj",
        "..\\MinimalRoslynCpg.Query\\MinimalRoslynCpg.Query.csproj",
      },
      ProjectReferences(cliProject));
  }

  [Fact]
  public void DirectoryBuildProps_WhenUsedFromAWorktree_UsesLocalBuildOutput()
  {
    var projectText = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "Directory.Build.props"));

    Assert.DoesNotContain("D:\\ProjectItem\\SourceCode\\Net\\NL\\Build", projectText, StringComparison.Ordinal);
    Assert.Contains("$(MSBuildThisFileDirectory)Build", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void CoreAssembly_WhenLoaded_ContainsTheGraphModelAndContracts()
  {
    var assembly = Assembly.Load("MinimalRoslynCpg.Core");

    Assert.NotNull(assembly.GetType("MinimalRoslynCpg.Model.RoslynCpgGraph"));
    Assert.NotNull(assembly.GetType("MinimalRoslynCpg.Contracts.RoslynCpgNodeKind"));
  }

  private static IReadOnlyList<string> ProjectReferences(XDocument document)
  {
    return document.Descendants("ProjectReference")
      .Select(element => element.Attribute("Include")?.Value)
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .Cast<string>()
      .ToArray();
  }

  private static XDocument ReadProject(string repositoryRoot, string projectName)
  {
    return XDocument.Load(Path.Combine(repositoryRoot, "src", projectName, $"{projectName}.csproj"));
  }

  private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "")
  {
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
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
