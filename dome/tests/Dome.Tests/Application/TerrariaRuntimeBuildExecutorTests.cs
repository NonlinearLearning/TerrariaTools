using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeBuildExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesNoRestoreAndMultiProcBuildCommand()
    {
        var runner = new FakeTerrariaRuntimeProcessRunner();
        var executor = new TerrariaRuntimeBuildExecutor(runner);
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var solutionPath = Path.Combine(tempRoot, "TerrariaServer.sln");
            await File.WriteAllTextAsync(solutionPath, "solution");
            var layout = TerrariaRuntimeLayout.Create(new TerrariaRuntimeRunRequest(solutionPath, Path.Combine(tempRoot, "out")));
            Directory.CreateDirectory(layout.WorkspacePath);
            await File.WriteAllTextAsync(layout.WorkspaceSolutionPath, "solution");
            var progress = new FakeTerrariaRuntimeProgressReporter();

            var result = await executor.ExecuteAsync(layout, progress, CancellationToken.None);

            Assert.True(result.BuildSucceeded);
            Assert.Equal("dotnet", runner.FileName);
            Assert.Equal($"build \"{layout.WorkspaceSolutionPath}\" --no-restore -m", runner.Arguments);
            Assert.Equal(layout.WorkspacePath, runner.WorkingDirectory);
            Assert.Contains("stdout line", progress.Messages);
            Assert.Contains("stderr line", progress.Messages);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeTerrariaRuntimeProcessRunner : ITerrariaRuntimeProcessRunner
    {
        public string? FileName { get; private set; }
        public string? Arguments { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public Task<TerrariaRuntimeProcessResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            Action<string>? onStandardOutput,
            Action<string>? onStandardError,
            CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            onStandardOutput?.Invoke("stdout line");
            onStandardError?.Invoke("stderr line");
            return Task.FromResult(new TerrariaRuntimeProcessResult(0, "stdout line", "stderr line"));
        }
    }

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public List<string> Messages { get; } = new();

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }
}
