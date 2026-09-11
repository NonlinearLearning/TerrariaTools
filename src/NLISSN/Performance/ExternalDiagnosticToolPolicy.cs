using System.Diagnostics;

namespace NLISSN.Performance;

public sealed record ExternalDiagnosticToolPolicy(
    string Tool,
    string Executable,
    string Arguments,
    string DisplayCommand,
    string? Version,
    TimeSpan Timeout,
    string OutputDirectory,
    string ArtifactRoot,
    string ArtifactPath)
{
    private static readonly IReadOnlyList<string> RequiredToolNames = Array.AsReadOnly(new[]
    {
        "dotnet-trace",
        "dotnet-counters",
        "dotnet-gcdump"
    });

    public static IReadOnlyList<string> RequiredTools => RequiredToolNames;

    public static IReadOnlyList<ExternalDiagnosticToolPolicy> CreateDefaultProfilePolicies(
      int processId,
      string outputDirectory,
      TimeSpan? timeout = null)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var artifactRoot = Directory.GetParent(fullOutputDirectory)?.FullName
          ?? throw new ArgumentException(
            "The output directory must have a run artifact parent.",
            nameof(outputDirectory));
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);

        return new[]
        {
            CreateTracePolicy(processId, fullOutputDirectory, artifactRoot, effectiveTimeout),
            CreateCountersPolicy(processId, fullOutputDirectory, artifactRoot, effectiveTimeout),
            CreateGcDumpPolicy(processId, fullOutputDirectory, artifactRoot, effectiveTimeout)
        };
    }

    public static bool IsAvailable(string tool)
    {
        return TryGetVersion(tool) is not null;
    }

    public static string? TryGetVersion(string tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (process is null || !process.WaitForExit(5000))
            {
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output)
              ? process.StandardError.ReadToEnd().Trim()
              : output;
        }
        catch (Exception exception) when (
          exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static ExternalDiagnosticToolPolicy CreateTracePolicy(
      int processId,
      string outputDirectory,
      string artifactRoot,
      TimeSpan timeout)
    {
        var path = Path.Combine(outputDirectory, "trace.nettrace");
        var arguments = $"collect --process-id {processId} --duration 00:00:02 --output {Quote(path)}";
        return Create(
          "dotnet-trace",
          arguments,
          "dotnet-trace",
          timeout,
          outputDirectory,
          artifactRoot,
          path);
    }

    private static ExternalDiagnosticToolPolicy CreateCountersPolicy(
      int processId,
      string outputDirectory,
      string artifactRoot,
      TimeSpan timeout)
    {
        var path = Path.Combine(outputDirectory, "counters.csv");
        var arguments =
          $"collect --process-id {processId} --duration 00:00:02 --format csv --output {Quote(path)}";
        return Create(
          "dotnet-counters",
          arguments,
          "dotnet-counters",
          timeout,
          outputDirectory,
          artifactRoot,
          path);
    }

    private static ExternalDiagnosticToolPolicy CreateGcDumpPolicy(
      int processId,
      string outputDirectory,
      string artifactRoot,
      TimeSpan timeout)
    {
        var path = Path.Combine(outputDirectory, "heap.gcdump");
        var arguments = $"collect --process-id {processId} --output {Quote(path)}";
        return Create(
          "dotnet-gcdump",
          arguments,
          "dotnet-gcdump",
          timeout,
          outputDirectory,
          artifactRoot,
          path);
    }

    private static ExternalDiagnosticToolPolicy Create(
      string tool,
      string arguments,
      string displayName,
      TimeSpan timeout,
      string outputDirectory,
      string artifactRoot,
      string artifactPath)
    {
        return new ExternalDiagnosticToolPolicy(
          tool,
          tool,
          arguments,
          $"{displayName} {arguments}",
          TryGetVersion(tool),
          timeout,
          outputDirectory,
          artifactRoot,
          artifactPath);
    }

    private static string Quote(string path)
    {
        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
