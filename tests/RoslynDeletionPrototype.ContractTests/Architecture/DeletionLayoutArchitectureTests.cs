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
    Assert.DoesNotContain(@"..\Rules\Rules.csproj", projectText, StringComparison.Ordinal);
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
