using System.Diagnostics;
using System.Globalization;
using TerrariaTools.Dome.Adapters.Runtime.Process;
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

    [Fact]
    public async Task ProcessRunner_CancellationTerminatesChildProcess()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var pidFile = Path.Combine(tempRoot, "pid.txt");
            var runner = new TerrariaRuntimeProcessRunner();
            using var cts = new CancellationTokenSource();

            var command = $"-NoProfile -Command \"$PID | Set-Content -Path '{pidFile}' -NoNewline; Start-Sleep -Seconds 30\"";
            var runTask = runner.RunAsync("powershell.exe", command, tempRoot, null, null, cts.Token);

            await WaitForFileAsync(pidFile, TimeSpan.FromSeconds(5));
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile), CultureInfo.InvariantCulture);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
            await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(5));
        });
    }

    private sealed class FakeTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter
    {
        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
        }
    }

    private static async Task WithTempRootAsync(Func<string, Task> action)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            await action(tempRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var start = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (start.Elapsed > timeout)
            {
                throw new TimeoutException($"Timed out waiting for file '{path}'.");
            }

            await Task.Delay(50);
        }
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var start = Stopwatch.StartNew();
        while (IsProcessRunning(processId))
        {
            if (start.Elapsed > timeout)
            {
                throw new TimeoutException($"Timed out waiting for process {processId} to exit.");
            }

            await Task.Delay(50);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

