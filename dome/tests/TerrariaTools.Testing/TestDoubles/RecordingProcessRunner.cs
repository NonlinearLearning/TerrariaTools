using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class RecordingProcessRunner : ITerrariaRuntimeProcessRunner
{
    private readonly TerrariaRuntimeProcessResult _result;

    public RecordingProcessRunner(TerrariaRuntimeProcessResult? result = null)
    {
        _result = result ?? new TerrariaRuntimeProcessResult(0, "stdout line", "stderr line");
    }

    public List<(string FileName, string Arguments, string WorkingDirectory)> Calls { get; } = [];

    public Task<TerrariaRuntimeProcessResult> RunAsync(
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
