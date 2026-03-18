using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Tests.Testing.TestDoubles;
using TerrariaTools.Testing.TestBuilders;
using TerrariaTools.Testing.TestDoubles;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeBuildExecutorLegacyTests
{
    [Fact]
    public async Task ExecuteAsync_UsesNoRestoreAndMultiProcBuildCommand()
    {
        var runner = new RecordingProcessCompatibilityRunner();
        var executor = new TerrariaRuntimeBuildExecutor(runner);
        var layout = new RuntimeLayoutCompatibilityBuilder()
            .WithWorkspacePath("workspace")
            .WithWorkspaceSolutionPath(Path.Combine("workspace", "TerrariaServer.sln"))
            .WithDependencyEnvironmentPath("dependency-env")
            .Build();
        var progress = new FakeTerrariaRuntimeProgressReporter();

        var result = await executor.ExecuteAsync(layout, progress, CancellationToken.None);

        Assert.True(result.BuildSucceeded);
        Assert.Single(runner.Calls);
        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Equal($"build \"{layout.WorkspaceSolutionPath}\" --no-restore -m", runner.Calls[0].Arguments);
        Assert.Equal(layout.WorkspacePath, runner.Calls[0].WorkingDirectory);
        Assert.Contains("stdout line", progress.Messages);
        Assert.Contains("stderr line", progress.Messages);
    }

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }
}
