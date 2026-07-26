using Xunit;

namespace NLISSN.Tests.Architecture;

public sealed class DeletionLayoutArchitectureTests
{
  [Fact]
  public void ProductionProjects_UseTheNlissnProjectLayout()
  {
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Core", "NLISSN.Core.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Rules", "NLISSN.Rules.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Application", "NLISSN.Application.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN", "NLISSN.csproj")));
    Assert.True(File.Exists(ProjectPath("src", "NLISSN.Logging", "NLISSN.Logging.csproj")));
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
      new[] { "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj" },
      "src", "NLISSN.Core", "NLISSN.Core.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "NLISSN.Rules", "NLISSN.Rules.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "NLISSN.Application", "NLISSN.Application.csproj");
    AssertProjectReferences(
      new[]
      {
        "..\\NLISSN.Application\\NLISSN.Application.csproj",
        "..\\NLISSN.Rules\\NLISSN.Rules.csproj",
        "..\\NLISSN.Core\\NLISSN.Core.csproj",
        "..\\NLISSN.Logging\\NLISSN.Logging.csproj",
        "..\\MinimalRoslynCpg\\MinimalRoslynCpg.csproj"
      },
      "src", "NLISSN", "NLISSN.csproj");
    AssertProjectReferences(Array.Empty<string>(), "src", "NLISSN.Logging", "NLISSN.Logging.csproj");
  }

  private static void AssertProjectReferences(IReadOnlyList<string> expected, params string[] projectParts)
  {
    var references = File.ReadLines(ProjectPath(projectParts[0], projectParts[1], projectParts[2]))
      .Where(line => line.Contains("<ProjectReference", StringComparison.Ordinal))
      .Select(line => line.Split('"')[1])
      .ToArray();

    Assert.Equal(expected, references);
  }

  private static string ProjectPath(string first, string second, string third, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
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
