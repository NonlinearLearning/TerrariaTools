using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeEnvironmentBuilderTests
{
    [Fact]
    public async Task RefreshDependencyEnvironmentAsync_CopiesOnlyNonCsFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "src");
            var outputRoot = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Config"));
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Player.cs"), "namespace Sample; public class Player { }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Config", "settings.json"), "{ }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");

            var request = new TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot);
            var layout = TerrariaRuntimeLayout.Create(request);
            var builder = new TerrariaRuntimeEnvironmentBuilder();
            var progress = new FakeTerrariaRuntimeProgressReporter();

            await builder.RefreshDependencyEnvironmentAsync(layout, progress, CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(layout.DependencyEnvironmentPath, "Player.cs")));
            Assert.True(File.Exists(Path.Combine(layout.DependencyEnvironmentPath, "Config", "settings.json")));
            Assert.True(File.Exists(Path.Combine(layout.DependencyEnvironmentPath, "TerrariaServer.sln")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_PreservesStructureAndOverwritesCsFromRewritten()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "src");
            var outputRoot = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Config"));
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Player.cs"), "namespace Sample; public class Player { public void Run() { int count = 1; } }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Config", "settings.json"), "{ }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

            var request = new TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot);
            var layout = TerrariaRuntimeLayout.Create(request);
            var builder = new TerrariaRuntimeEnvironmentBuilder();
            var progress = new FakeTerrariaRuntimeProgressReporter();
            await builder.RefreshDependencyEnvironmentAsync(layout, progress, CancellationToken.None);

            Directory.CreateDirectory(Path.Combine(layout.ArtifactsPath, "rewritten"));
            await File.WriteAllTextAsync(
                Path.Combine(layout.ArtifactsPath, "rewritten", "Player.cs"),
                "namespace Sample; public class Player { public void Run() { } }");

            await builder.PrepareWorkspaceAsync(layout, progress, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(layout.WorkspacePath, "Config", "settings.json")));
            Assert.Equal(
                "namespace Sample; public class Player { public void Run() { } }",
                await File.ReadAllTextAsync(Path.Combine(layout.WorkspacePath, "Player.cs")));
            var workspaceProject = await File.ReadAllTextAsync(Path.Combine(layout.WorkspacePath, "TerrariaServer.csproj"));
            Assert.Contains("<ImplicitUsings>disable</ImplicitUsings>", workspaceProject, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_DoesNotCopyBuildArtifacts()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceRoot = Path.Combine(tempRoot, "src");
            var outputRoot = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "obj", "Debug", "net40", "en-US"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "bin", "Debug"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Config"));

            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Player.cs"), "namespace Sample; public class Player { }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "obj", "Debug", "net40", "en-US", "TerrariaServer.resources.cs"), "// generated");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "bin", "Debug", "TerrariaServer.exe"), "binary");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Config", "settings.json"), "{ }");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "TerrariaServer.sln"), "solution");

            var request = new TerrariaRuntimeRunRequest(Path.Combine(sourceRoot, "TerrariaServer.sln"), outputRoot);
            var layout = TerrariaRuntimeLayout.Create(request);
            var builder = new TerrariaRuntimeEnvironmentBuilder();
            var progress = new FakeTerrariaRuntimeProgressReporter();

            await builder.RefreshDependencyEnvironmentAsync(layout, progress, CancellationToken.None);
            await builder.PrepareWorkspaceAsync(layout, progress, CancellationToken.None);

            Assert.False(Directory.Exists(Path.Combine(layout.DependencyEnvironmentPath, "obj")));
            Assert.False(Directory.Exists(Path.Combine(layout.DependencyEnvironmentPath, "bin")));
            Assert.False(Directory.Exists(Path.Combine(layout.WorkspacePath, "obj")));
            Assert.False(Directory.Exists(Path.Combine(layout.WorkspacePath, "bin")));
            Assert.True(File.Exists(Path.Combine(layout.WorkspacePath, "Config", "settings.json")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public void Report(string message)
        {
        }
    }
}
