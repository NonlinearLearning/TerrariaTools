using Xunit;

namespace Deletion.Tests.Architecture;

public sealed class DeletionLayoutArchitectureTests
{
  [Fact]
  public void ProductionProjects_UseTheDeletionProjectLayout()
  {
    Assert.True(File.Exists(ProjectPath("src", "Deletion.Core", "Deletion.Core.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Deletion.Rules", "Deletion.Rules.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Deletion.Application", "Deletion.Application.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Deletion.Cli", "Deletion.Cli.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "Deletion.Logging", "Deletion.Logging.csproj")));
  }

  [Fact]
  public void CompatibilityLauncher_ReferencesOnlyDeletionCli()
  {
    var projectText = File.ReadAllText(ProjectPath(
      "src",
      "RoslynPrototype",
      "RoslynPrototype.csproj"));

    Assert.Contains(
      "ProjectReference Include=\"..\\Deletion.Cli\\Deletion.Cli.csproj\"",
      projectText,
      StringComparison.Ordinal);
    Assert.DoesNotContain(@"..\Host\Host.csproj", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain(@"..\Application\Application.csproj", projectText, StringComparison.Ordinal);
    Assert.DoesNotContain(@"..\Deletion.Rules\Deletion.Rules.csproj", projectText, StringComparison.Ordinal);
  }

  [Fact]
  public void ProductionProjectReferences_MatchTheTargetDependencyGraph()
  {
    AssertProjectReferences(
      new[] { "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj" },
      "src", "Deletion.Core", "Deletion.Core.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\Deletion.Core\\Deletion.Core.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "Deletion.Rules", "Deletion.Rules.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\Deletion.Core\\Deletion.Core.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "Deletion.Application", "Deletion.Application.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\Deletion.Application\\Deletion.Application.csproj",
        "..\\Deletion.Rules\\Deletion.Rules.csproj",
        "..\\Deletion.Core\\Deletion.Core.csproj",
        "..\\Deletion.Logging\\Deletion.Logging.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "Deletion.Cli", "Deletion.Cli.csproj");
    AssertProjectReferences(Array.Empty<string>(), "src", "Deletion.Logging", "Deletion.Logging.csproj");
  }

  private static void AssertProjectReferences(
    IReadOnlyList<string> expected,
    params string[] projectParts)
  {
    var references = File.ReadLines(ProjectPath(projectParts[0], projectParts[1], projectParts[2]))
      .Where(line => line.Contains("<ProjectReference", StringComparison.Ordinal))
      .Select(line => line.Split('"')[1])
      .ToArray();

    Assert.Equal(expected, references);
  }

  private static string ProjectPath(
    string first,
    string second,
    string third,
    [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
  {
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
    {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, first, second, third);
  }
}
