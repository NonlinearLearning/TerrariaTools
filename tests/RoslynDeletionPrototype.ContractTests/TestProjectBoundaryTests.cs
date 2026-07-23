using System.Xml.Linq;
using Xunit;

namespace RoslynPrototype.ContractTests;

public sealed class TestProjectBoundaryTests
{
  [Fact]
  public void ContractTests_TextAssertionUsageGuardLivesWithArchitectureContracts()
  {
    var misplacedGuardPath = Path.Combine(
      ResolveRepositoryRoot(),
      "tests",
      "RoslynDeletionPrototype.ContractTests",
      "TestInfrastructure",
      "TextAssertionUsageGuardTests.cs");

    Assert.False(File.Exists(misplacedGuardPath));
  }

  [Fact]
  public void UnitTests_TestAssetCatalogTestsDoNotLiveInTestInfrastructure()
  {
    var misplacedTestPath = Path.Combine(
      ResolveRepositoryRoot(),
      "tests",
      "RoslynDeletionPrototype.UnitTests",
      "TestInfrastructure",
      "TestAssetCatalogTests.cs");

    Assert.False(File.Exists(misplacedTestPath));
  }

  [Fact]
  public void PerformanceTests_DeleteClassSampleHelperLivesWithPerformanceSupport()
  {
    var misplacedHelperPath = Path.Combine(
      ResolveRepositoryRoot(),
      "tests",
      "RoslynDeletionPrototype.PerformanceTests",
      "TestInfrastructure",
      "DeleteClassRandomSampleHelper.cs");

    Assert.False(File.Exists(misplacedHelperPath));
  }

  [Fact]
  public void ProjectFiles_KeepSharedAssetsAndTestProjectsSeparated()
  {
    var projectRoot = ResolveRepositoryRoot();
    var testingProject = ReadProject(projectRoot, "RoslynDeletionPrototype.Testing");
    var testProjects = new[]
    {
      ReadProject(projectRoot, "RoslynDeletionPrototype.UnitTests"),
      ReadProject(projectRoot, "RoslynDeletionPrototype.ContractTests"),
      ReadProject(projectRoot, "RoslynDeletionPrototype.HostTests"),
      ReadProject(projectRoot, "RoslynDeletionPrototype.PerformanceTests"),
    };

    Assert.Empty(ProjectReferences(testingProject)
      .Where(reference => reference.Contains("tests", StringComparison.OrdinalIgnoreCase)));
    Assert.DoesNotContain("Microsoft.NET.Test.Sdk", PackageReferences(testingProject));
    Assert.DoesNotContain("xunit", PackageReferences(testingProject), StringComparer.OrdinalIgnoreCase);

    foreach (var project in testProjects)
    {
      Assert.Contains(
        ProjectReferences(project),
        reference => reference.EndsWith(
          "RoslynDeletionPrototype.Testing.csproj",
          StringComparison.OrdinalIgnoreCase));
      Assert.Empty(ProjectReferences(project)
        .Where(reference => reference.Contains("RoslynDeletionPrototype.", StringComparison.OrdinalIgnoreCase) &&
          !reference.EndsWith("RoslynDeletionPrototype.Testing.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    var unitProject = testProjects[0];
    Assert.DoesNotContain(
      ProjectReferences(unitProject),
      reference => reference.EndsWith("Host.csproj", StringComparison.OrdinalIgnoreCase));
  }

  private static IReadOnlyList<string> PackageReferences(XDocument document)
  {
    return document.Descendants("PackageReference")
      .Select(element => element.Attribute("Include")?.Value)
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .Cast<string>()
      .ToArray();
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
    var path = Path.Combine(repositoryRoot, "tests", projectName, $"{projectName}.csproj");
    return XDocument.Load(path);
  }

  private static string ResolveRepositoryRoot()
  {
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
  }
}
