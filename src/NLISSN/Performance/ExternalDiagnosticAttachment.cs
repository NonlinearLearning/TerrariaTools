using System.ComponentModel;
using System.Diagnostics;
using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public enum ExternalDiagnosticTool
{
  DotnetTrace,
  DotnetCounters,
  DotnetGcdump
}

internal static class ExternalDiagnosticToolNames
{
  public static string ToManifestName(ExternalDiagnosticTool tool)
  {
    return tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => "dotnet-trace",
      ExternalDiagnosticTool.DotnetCounters => "dotnet-counters",
      ExternalDiagnosticTool.DotnetGcdump => "dotnet-gcdump",
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
  }

  public static bool TryParse(string? value, out ExternalDiagnosticTool tool)
  {
    tool = default;
    if (value is null)
    {
      return false;
    }

    foreach (var candidate in Enum.GetValues<ExternalDiagnosticTool>())
    {
      if (string.Equals(value, ToManifestName(candidate), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
      {
        tool = candidate;
        return true;
      }
    }

    return false;
  }
}

public sealed record ExternalDiagnosticToolInfo
{
  public ExternalDiagnosticToolInfo(
    ExternalDiagnosticTool tool,
    string executablePath,
    string? version = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
    Tool = tool;
    ExecutablePath = executablePath;
    Version = version;
  }

  public ExternalDiagnosticTool Tool { get; }

  public string ExecutablePath { get; }

  public string? Version { get; }
}

public sealed record ExternalDiagnosticCommand
{
  public ExternalDiagnosticCommand(
    string fileName,
    IReadOnlyList<string> arguments,
    string outputPath,
    TimeSpan timeout)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
    ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
    ArgumentNullException.ThrowIfNull(arguments);
    if (timeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    FileName = fileName;
    Arguments = arguments;
    OutputPath = outputPath;
    Timeout = timeout;
  }

  public string FileName { get; }

  public IReadOnlyList<string> Arguments { get; }

  public string OutputPath { get; }

  public TimeSpan Timeout { get; }

  public string DisplayCommand =>
    string.Join(
      " ",
      new[] { FileName }.Concat(Arguments).Select(QuoteArgument));

  private static string QuoteArgument(string value)
  {
    if (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('"'))
    {
      return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    return value;
  }
}

public sealed record ExternalDiagnosticCommandResult(
  bool Started,
  int? ExitCode,
  bool TimedOut = false,
  string? ErrorKind = null);

public interface IExternalDiagnosticToolLocator
{
  ExternalDiagnosticToolInfo? Locate(ExternalDiagnosticTool tool);
}

public interface IExternalDiagnosticCommandRunner
{
  Task<ExternalDiagnosticCommandResult> RunAsync(
    ExternalDiagnosticCommand command,
    CancellationToken cancellationToken);
}

/// A manifest for one externally-produced diagnostic artifact.
/// The binary payload is deliberately outside this object and outside summary JSON.
public sealed record ExternalDiagnosticAttachment
{
  private ExternalDiagnosticAttachment(
    ExternalDiagnosticTool tool,
    PerformanceAttachmentReference reference)
  {
    Tool = tool;
    Reference = reference;
  }

  public ExternalDiagnosticTool Tool { get; }

  public PerformanceAttachmentReference Reference { get; }

  public string Kind => Reference.Kind;

  public string RunId => Reference.RunId;

  public string? StageId => Reference.StageId;

  public PerformanceMode Mode => Reference.Mode;

  public string? RelativePath => Reference.RelativePath;

  public bool IsAvailable => Reference.IsAvailable;

  public bool IsComplete => Reference.IsComplete;

  public PerformanceStatus Status => Reference.Status;

  public string? ErrorKind => Reference.ErrorKind;

  public string? Command => Reference.Command;

  public string? Version => Reference.Version;

  public DateTimeOffset? StartedAtUtc => Reference.StartedAtUtc;

  public DateTimeOffset? CompletedAtUtc => Reference.CompletedAtUtc;

  public int? ExitCode => Reference.ExitCode;

  public static ExternalDiagnosticAttachment FromReference(
    PerformanceAttachmentReference reference)
  {
    ArgumentNullException.ThrowIfNull(reference);
    if (!ExternalDiagnosticToolNames.TryParse(reference.Tool, out var tool))
    {
      throw new ArgumentException(
        "The attachment reference does not identify a supported external diagnostic tool.",
        nameof(reference));
    }

    return new ExternalDiagnosticAttachment(tool, reference);
  }

  public static ExternalDiagnosticAttachment Create(
    ExternalDiagnosticTool tool,
    PerformanceAttachmentReference reference)
  {
    ArgumentNullException.ThrowIfNull(reference);
    return new ExternalDiagnosticAttachment(tool, reference);
  }
}

internal sealed class PathExternalDiagnosticToolLocator : IExternalDiagnosticToolLocator
{
  public ExternalDiagnosticToolInfo? Locate(ExternalDiagnosticTool tool)
  {
    var executable = GetExecutableName(tool);
    var path = FindOnPath(executable);
    return path is null ? null : new ExternalDiagnosticToolInfo(tool, path);
  }

  internal static string GetExecutableName(ExternalDiagnosticTool tool)
  {
    return tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => "dotnet-trace",
      ExternalDiagnosticTool.DotnetCounters => "dotnet-counters",
      ExternalDiagnosticTool.DotnetGcdump => "dotnet-gcdump",
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
  }

  internal static string GetAttachmentKind(ExternalDiagnosticTool tool)
  {
    return tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => "trace",
      ExternalDiagnosticTool.DotnetCounters => "counters",
      ExternalDiagnosticTool.DotnetGcdump => "gcdump",
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
  }

  internal static string GetFileExtension(ExternalDiagnosticTool tool)
  {
    return tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => ".nettrace",
      ExternalDiagnosticTool.DotnetCounters => ".csv",
      ExternalDiagnosticTool.DotnetGcdump => ".gcdump",
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
  }

  private static string? FindOnPath(string executable)
  {
    var pathValue = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(pathValue))
    {
      return null;
    }

    var executableNames = OperatingSystem.IsWindows()
      ? new[] { executable, executable + ".exe", executable + ".cmd" }
      : new[] { executable };
    foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      foreach (var executableName in executableNames)
      {
        var candidate = Path.Combine(directory, executableName);
        if (File.Exists(candidate))
        {
          return candidate;
        }
      }
    }

    return null;
  }
}

internal sealed class ProcessExternalDiagnosticCommandRunner : IExternalDiagnosticCommandRunner
{
  public async Task<ExternalDiagnosticCommandResult> RunAsync(
    ExternalDiagnosticCommand command,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = command.FileName,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };
    foreach (var argument in command.Arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = new Process { StartInfo = startInfo };
    try
    {
      if (!process.Start())
      {
        return new ExternalDiagnosticCommandResult(
          Started: false,
          ExitCode: null,
          ErrorKind: "command-start-failed");
      }
    }
    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
    {
      return new ExternalDiagnosticCommandResult(
        Started: false,
        ExitCode: null,
        ErrorKind: exception.GetType().Name);
    }

    var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(command.Timeout);
    try
    {
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
      await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
      return new ExternalDiagnosticCommandResult(
        Started: true,
        ExitCode: process.ExitCode);
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      TryKill(process);
      return new ExternalDiagnosticCommandResult(
        Started: true,
        ExitCode: null,
        TimedOut: !cancellationToken.IsCancellationRequested,
        ErrorKind: cancellationToken.IsCancellationRequested
          ? "command-cancelled"
          : "command-timeout");
    }
    catch (Exception exception)
    {
      TryKill(process);
      return new ExternalDiagnosticCommandResult(
        Started: true,
        ExitCode: null,
        ErrorKind: exception.GetType().Name);
    }
  }

  private static void TryKill(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
    }
  }
}
