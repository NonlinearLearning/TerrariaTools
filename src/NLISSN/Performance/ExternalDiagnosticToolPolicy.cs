using NLISSN.Core.Performance;

namespace NLISSN.Performance;

/// Controls the explicit, fail-open boundary for external diagnostic tools.
/// The default policy is disabled, so normal and benchmark runs never start one.
public sealed class ExternalDiagnosticToolPolicy
{
  private readonly bool _enabled;
  private readonly TimeSpan _duration;
  private readonly TimeSpan _timeout;
  private readonly IExternalDiagnosticToolLocator _toolLocator;
  private readonly IExternalDiagnosticCommandRunner _commandRunner;

  public ExternalDiagnosticToolPolicy(
    bool enabled = false,
    TimeSpan? duration = null,
    TimeSpan? timeout = null,
    IExternalDiagnosticToolLocator? toolLocator = null,
    IExternalDiagnosticCommandRunner? commandRunner = null)
  {
    _enabled = enabled;
    _duration = duration ?? TimeSpan.FromSeconds(5);
    _timeout = timeout ?? TimeSpan.FromSeconds(30);
    if (_duration <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(duration));
    }

    if (_timeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    _toolLocator = toolLocator ?? new PathExternalDiagnosticToolLocator();
    _commandRunner = commandRunner ?? new ProcessExternalDiagnosticCommandRunner();
  }

  public async Task<ExternalDiagnosticAttachment> CaptureAsync(
    ExternalDiagnosticTool tool,
    int processId,
    string runId,
    string? stageId,
    PerformanceMode mode,
    string runArtifactRoot,
    CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(runArtifactRoot);
    if (processId <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(processId));
    }

    var relativePath = CreateRelativePath(tool);
    var finalPath = Path.Combine(
      Path.GetFullPath(runArtifactRoot),
      relativePath.Replace('/', Path.DirectorySeparatorChar));
    var temporaryPath = finalPath + ".tmp";

    if (!_enabled)
    {
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Skipped,
        "external-tool-policy-disabled",
        command: null,
        version: null,
        startedAtUtc: null,
        completedAtUtc: null,
        exitCode: null);
    }

    if (mode != PerformanceMode.Profile)
    {
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Skipped,
        "external-tool-mode-disabled",
        command: null,
        version: null,
        startedAtUtc: null,
        completedAtUtc: null,
        exitCode: null);
    }

    var toolInfo = _toolLocator.Locate(tool);
    if (toolInfo is null)
    {
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Unavailable,
        "external-tool-unavailable",
        command: null,
        version: null,
        startedAtUtc: null,
        completedAtUtc: null,
        exitCode: null);
    }

    var command = CreateCommand(tool, toolInfo, processId, temporaryPath);
    var startedAtUtc = DateTimeOffset.UtcNow;
    ExternalDiagnosticCommandResult result;
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
      DeleteIfPresent(temporaryPath);
      DeleteIfPresent(finalPath);
      result = await _commandRunner.RunAsync(command, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      DeleteIfPresent(temporaryPath);
      DeleteIfPresent(finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Failed,
        "command-cancelled",
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        DateTimeOffset.UtcNow,
        exitCode: null);
    }
    catch (Exception exception)
    {
      DeleteIfPresent(temporaryPath);
      DeleteIfPresent(finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Failed,
        exception.GetType().Name,
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        DateTimeOffset.UtcNow,
        exitCode: null);
    }

    var completedAtUtc = DateTimeOffset.UtcNow;
    var errorKind = ResolveErrorKind(result);
    if (!result.Started || result.TimedOut || result.ExitCode != 0)
    {
      DeleteIfPresent(temporaryPath);
      DeleteIfPresent(finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Failed,
        errorKind,
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        completedAtUtc,
        result.ExitCode);
    }

    if (!File.Exists(temporaryPath))
    {
      DeleteIfPresent(finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Failed,
        "output-missing",
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        completedAtUtc,
        result.ExitCode);
    }

    try
    {
      File.Move(temporaryPath, finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Completed,
        errorKind: null,
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        completedAtUtc,
        result.ExitCode);
    }
    catch (Exception exception)
    {
      DeleteIfPresent(temporaryPath);
      DeleteIfPresent(finalPath);
      return CreateManifest(
        tool,
        runId,
        stageId,
        mode,
        runArtifactRoot,
        finalPath,
        PerformanceStatus.Failed,
        exception.GetType().Name,
        command.DisplayCommand,
        toolInfo.Version,
        startedAtUtc,
        completedAtUtc,
        result.ExitCode);
    }
  }

  private ExternalDiagnosticCommand CreateCommand(
    ExternalDiagnosticTool tool,
    ExternalDiagnosticToolInfo toolInfo,
    int processId,
    string outputPath)
  {
    var duration = _duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
    var arguments = tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => new[]
      {
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--duration", duration,
        "--output", outputPath
      },
      ExternalDiagnosticTool.DotnetCounters => new[]
      {
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--duration", duration,
        "--format", "csv",
        "--output", outputPath
      },
      ExternalDiagnosticTool.DotnetGcdump => new[]
      {
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--output", outputPath
      },
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    return new ExternalDiagnosticCommand(
      toolInfo.ExecutablePath,
      arguments,
      outputPath,
      _timeout);
  }

  private static string CreateRelativePath(ExternalDiagnosticTool tool)
  {
    return $"Performance/Diagnostics/{PathExternalDiagnosticToolLocator.GetAttachmentKind(tool)}-{Guid.NewGuid():N}{PathExternalDiagnosticToolLocator.GetFileExtension(tool)}";
  }

  private static string ResolveErrorKind(ExternalDiagnosticCommandResult result)
  {
    if (!string.IsNullOrWhiteSpace(result.ErrorKind))
    {
      return result.ErrorKind;
    }

    if (result.TimedOut)
    {
      return "command-timeout";
    }

    if (!result.Started)
    {
      return "command-start-failed";
    }

    return result.ExitCode.HasValue
      ? $"command-exit-{result.ExitCode.Value}"
      : "command-failed";
  }

  private static ExternalDiagnosticAttachment CreateManifest(
    ExternalDiagnosticTool tool,
    string runId,
    string? stageId,
    PerformanceMode mode,
    string runArtifactRoot,
    string artifactPath,
    PerformanceStatus status,
    string? errorKind,
    string? command,
    string? version,
    DateTimeOffset? startedAtUtc,
    DateTimeOffset? completedAtUtc,
    int? exitCode)
  {
    var reference = PerformanceDiagnosticAttachment.CreateManifest(
      PathExternalDiagnosticToolLocator.GetAttachmentKind(tool),
      tool,
      runId,
      stageId,
      mode,
      runArtifactRoot,
      artifactPath,
      status,
      isAvailable: status == PerformanceStatus.Completed,
      isComplete: status == PerformanceStatus.Completed,
      errorKind: status == PerformanceStatus.Completed ? null : errorKind,
      command,
      version,
      startedAtUtc,
      completedAtUtc,
      exitCode);
    return ExternalDiagnosticAttachment.Create(tool, reference);
  }

  private static void DeleteIfPresent(string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }
  }
}
