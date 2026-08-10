using System.Text.Json;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class WorkspaceConfigurationTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
      Path.GetTempPath(),
      $"nlissn-workspace-configuration-{Guid.NewGuid():N}");

    public WorkspaceConfigurationTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Load_SolutionInputMapsWorkspaceOptionsAndResolvedProvenance()
    {
        var configurationPath = WriteConfiguration(BuildWorkspaceConfiguration(
          "workspace-config",
          FixturePath("WorkspaceFixture.sln"),
          "  project: App/App.csproj\n" +
          "  targetFramework: net10.0\n" +
          "  configuration: Debug\n" +
          "  platform: AnyCPU\n" +
          "  restore: disabled\n" +
          "  generatedSources: include\n" +
          "  generators: enabled\n"));

        var configuration = YamlConfigurationLoader.Load(configurationPath);

        Assert.NotNull(configuration.Workspace);
        Assert.Equal(Path.GetFullPath(FixturePath("WorkspaceFixture.sln")), configuration.InputPath);
        Assert.Equal(
        Path.GetFullPath(FixturePath("App", "App.csproj")),
        configuration.Workspace.ProjectPath);
        Assert.Equal("net10.0", configuration.Workspace.TargetFramework);
        Assert.Equal(WorkspaceGeneratorMode.Enabled, configuration.Workspace.GeneratorMode);
        Assert.Equal(WorkspaceRestoreMode.Disabled, configuration.Workspace.RestoreMode);
        Assert.Equal("explicit", configuration.Provenance.FieldOrigins["input.targetFramework"]);
        Assert.Equal("explicit", configuration.Provenance.FieldOrigins["input.generators"]);

        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var resolved = JsonDocument.Parse(File.ReadAllText(configuration.Artifacts.ResolvedConfigurationPath));
        Assert.Equal(
          Path.GetFullPath(FixturePath("App", "App.csproj")),
          resolved.RootElement.GetProperty("workspace").GetProperty("projectPath").GetString());
        Assert.Equal("Enabled", resolved.RootElement.GetProperty("workspace").GetProperty("generators").GetString());
        Assert.Equal("explicit", resolved.RootElement.GetProperty("provenance").GetProperty("fieldOrigins").GetProperty("input.project").GetString());
    }

    [Fact]
    public void Load_ProjectInputWithProjectSelectorIsRejected()
    {
        var configurationPath = WriteConfiguration(BuildWorkspaceConfiguration(
          "invalid-project-selector",
          FixturePath("App", "App.csproj"),
          "  project: App.csproj\n"));

        var result = YamlConfigurationLoader.TryLoad(configurationPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NLISSN135");
    }

    [Fact]
    public void Load_OutsideSolutionProjectSelectorIsRejected()
    {
        var configurationPath = WriteConfiguration(BuildWorkspaceConfiguration(
          "outside-project-selector",
          FixturePath("WorkspaceFixture.sln"),
          "  project: ..\\Outside.csproj\n"));

        var result = YamlConfigurationLoader.TryLoad(configurationPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NLISSN136");
    }

    [Fact]
    public void Load_LegacySourceInputDoesNotCreateWorkspaceOptions()
    {
        var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
        File.WriteAllText(sourcePath, "public sealed class Input { }");
        var configurationPath = WriteConfiguration(
          "schemaVersion: 2\n" +
          "runId: legacy-input\n" +
          "input:\n" +
          "  path: Input.cs\n" +
          "analysis: {}\n" +
          "execution:\n" +
          "  maxDegreeOfParallelism: 1\n" +
          "artifacts: {}\n");

        var configuration = YamlConfigurationLoader.Load(configurationPath);

        Assert.Null(configuration.Workspace);
    }

    [Fact]
    public void Load_SourceDocumentWithProjectMapsTargetDocumentAndProjectContext()
    {
        var sourcePath = FixturePath("App", "App.cs");
        var projectPath = FixturePath("App", "App.csproj");
        var configurationPath = WriteConfiguration(BuildWorkspaceConfiguration(
          "workspace-document-config",
          sourcePath,
          $"  project: '{projectPath}'\n" +
          "  targetFramework: net10.0\n" +
          "  configuration: Debug\n" +
          "  platform: AnyCPU\n" +
          "  restore: disabled\n"));

        var configuration = YamlConfigurationLoader.Load(configurationPath);

        Assert.NotNull(configuration.Workspace);
        Assert.Equal(Path.GetFullPath(projectPath), configuration.Workspace.Path);
        Assert.Equal(Path.GetFullPath(projectPath), configuration.Workspace.ProjectPath);
        Assert.Equal(Path.GetFullPath(sourcePath), configuration.Workspace.TargetDocumentPath);
        Assert.Equal(Path.GetFullPath(sourcePath), configuration.InputPath);

        YamlConfigurationLoader.PrepareRunArtifacts(configuration);
        var resolved = JsonDocument.Parse(
          File.ReadAllText(configuration.Artifacts.ResolvedConfigurationPath));
        Assert.Equal(
          Path.GetFullPath(sourcePath),
          resolved.RootElement
            .GetProperty("workspace")
            .GetProperty("targetDocumentPath")
            .GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteConfiguration(string content)
    {
        var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
        File.WriteAllText(configurationPath, content);
        return configurationPath;
    }

    private static string BuildWorkspaceConfiguration(
      string runId,
      string inputPath,
      string additionalInput)
    {
        return "schemaVersion: 2\n" +
          $"runId: {runId}\n" +
          "input:\n" +
          $"  path: '{inputPath}'\n" +
          additionalInput +
          "analysis: {}\n" +
          "execution:\n" +
          "  maxDegreeOfParallelism: 1\n" +
          "artifacts:\n" +
          "  root: artifacts\n";
    }

    private static string FixturePath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "tests", "NLISSN.Testing", "TestCodeSet", "Workspace", Path.Combine(parts));
    }
}
