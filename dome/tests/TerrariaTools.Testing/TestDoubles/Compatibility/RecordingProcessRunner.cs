using TerrariaTools.Dome.Adapters.Runtime.Process;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class RecordingProcessCompatibilityRunner : ITerrariaRuntimeProcessRunner
{
    private readonly ApplicationAbstractions.TerrariaRuntimeProcessResult _result;

    public RecordingProcessCompatibilityRunner(ApplicationAbstractions.TerrariaRuntimeProcessResult? result = null)
    {
        _result = result ?? new ApplicationAbstractions.TerrariaRuntimeProcessResult(0, "stdout line", "stderr line");
    }

    public List<(string FileName, string Arguments, string WorkingDirectory)> Calls { get; } = [];

    public Task<ApplicationAbstractions.TerrariaRuntimeProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onStandardOutput,
        Action<string>? onStandardError,
        CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments, workingDirectory));
        if (!string.IsNullOrEmpty(_result.StandardOutput))
        {
            onStandardOutput?.Invoke(_result.StandardOutput);
        }

        if (!string.IsNullOrEmpty(_result.StandardError))
        {
            onStandardError?.Invoke(_result.StandardError);
        }

        return Task.FromResult(_result);
    }
}



