namespace TerrariaTools.Dome.Application;

using System.Diagnostics;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

public interface ITerrariaRuntimeProcessRunner
{
    Task<ApplicationAbstractions.TerrariaRuntimeProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken);
}

public interface ITerrariaRuntimeBuildExecutor
{
    Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
        ApplicationAbstractions.TerrariaRuntimeLayout layout,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

public interface ITerrariaRuntimeProgressReporter
{
    void Report(string message);
}

public sealed class ConsoleTerrariaRuntimeProgressReporter : ITerrariaRuntimeProgressReporter, IDomeProgressReporter
{
    public void Report(string message)
    {
        Console.WriteLine(message);
    }
}

public sealed class TerrariaRuntimeProcessRunner : ITerrariaRuntimeProcessRunner
{
    public async Task<ApplicationAbstractions.TerrariaRuntimeProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new List<string>();
        var standardError = new List<string>();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
            {
                return;
            }

            standardOutput.Add(eventArgs.Data);
            onStandardOutput?.Invoke(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
            {
                return;
            }

            standardError.Add(eventArgs.Data);
            onStandardError?.Invoke(eventArgs.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ApplicationAbstractions.TerrariaRuntimeProcessResult(
            process.ExitCode,
            string.Join(Environment.NewLine, standardOutput),
            string.Join(Environment.NewLine, standardError));
    }
}

public sealed class TerrariaRuntimeBuildExecutor(ITerrariaRuntimeProcessRunner processRunner) : ITerrariaRuntimeBuildExecutor
{
    public TerrariaRuntimeBuildExecutor()
        : this(new TerrariaRuntimeProcessRunner())
    {
    }

    public async Task<ApplicationAbstractions.TerrariaRuntimeBuildSummary> ExecuteAsync(
        ApplicationAbstractions.TerrariaRuntimeLayout layout,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var arguments = $"build \"{layout.WorkspaceSolutionPath}\" --no-restore -m";
        progressReporter.Report($"[tr-run] Starting solution build: dotnet {arguments}");

        var processResult = await processRunner.RunAsync(
            "dotnet",
            arguments,
            layout.WorkspacePath,
            progressReporter.Report,
            progressReporter.Report,
            cancellationToken);

        var summary = new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
            processResult.ExitCode == 0,
            processResult.ExitCode,
            $"dotnet {arguments}",
            layout.WorkspacePath,
            layout.DependencyEnvironmentPath,
            layout.WorkspaceSolutionPath,
            processResult.StandardOutput,
            processResult.StandardError);

        progressReporter.Report($"[tr-run] Solution build finished with exit code {summary.BuildExitCode}.");
        return summary;
    }
}
