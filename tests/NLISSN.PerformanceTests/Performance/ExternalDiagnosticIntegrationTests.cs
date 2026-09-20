using System.Diagnostics;
using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class ExternalDiagnosticIntegrationTests : IDisposable
{
  private readonly string _runRoot = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-external-diagnostic-integration-{Guid.NewGuid():N}");

  public ExternalDiagnosticIntegrationTests()
  {
    Directory.CreateDirectory(_runRoot);
  }

  [Fact]
  public async Task InstalledToolProducesManifestWithSharedRunAndStage()
  {
    if (!string.Equals(
      Environment.GetEnvironmentVariable("NLISSN_RUN_EXTERNAL_DIAGNOSTIC_TESTS"),
      "1",
      StringComparison.Ordinal))
    {
      return;
    }

    foreach (var tool in Enum.GetValues<ExternalDiagnosticTool>())
    {
      var executable = GetToolExecutable(tool);
      if (!TryLocate(executable, out var executablePath))
      {
        continue;
      }

      var policy = new ExternalDiagnosticToolPolicy(
        enabled: true,
        duration: TimeSpan.FromMilliseconds(200),
        timeout: TimeSpan.FromSeconds(20),
        toolLocator: new FixedToolLocator(new ExternalDiagnosticToolInfo(
          tool,
          executablePath,
          ReadVersion(executablePath))));

      var attachment = await policy.CaptureAsync(
        tool,
        Environment.ProcessId,
        runId: "integration-run",
        stageId: "Run",
        mode: PerformanceMode.Profile,
        runArtifactRoot: _runRoot);

      Assert.Equal(tool, attachment.Tool);
      Assert.Equal("integration-run", attachment.RunId);
      Assert.Equal("Run", attachment.StageId);
      Assert.Equal(PerformanceMode.Profile, attachment.Mode);
      Assert.NotNull(attachment.Command);
      Assert.NotNull(attachment.StartedAtUtc);
      Assert.NotNull(attachment.CompletedAtUtc);
      Assert.True(attachment.CompletedAtUtc >= attachment.StartedAtUtc);

      if (attachment.Status == PerformanceStatus.Completed)
      {
        Assert.True(attachment.IsAvailable);
        Assert.True(attachment.IsComplete);
        Assert.Equal(0, attachment.ExitCode);
        Assert.NotNull(attachment.RelativePath);
        Assert.True(File.Exists(ResolveAttachmentPath(attachment.RelativePath!)));
      }
      else
      {
        Assert.Equal(PerformanceStatus.Failed, attachment.Status);
        Assert.False(attachment.IsAvailable);
        Assert.False(attachment.IsComplete);
        Assert.NotNull(attachment.ErrorKind);
      }
    }
  }

  [Fact]
  public async Task MissingToolIsUnavailableAndDoesNotClaimAProfileArtifact()
  {
    var policy = new ExternalDiagnosticToolPolicy(
      enabled: true,
      toolLocator: new FixedToolLocator(null));

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetTrace,
      Environment.ProcessId,
      runId: "missing-tool-run",
      stageId: "Run",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Unavailable, attachment.Status);
    Assert.False(attachment.IsAvailable);
    Assert.False(attachment.IsComplete);
    Assert.Equal("external-tool-unavailable", attachment.ErrorKind);
    Assert.Null(attachment.Command);
    Assert.Null(attachment.StartedAtUtc);
    Assert.Null(attachment.CompletedAtUtc);
  }

  [Fact]
  public async Task NormalModeDoesNotDependOnInstalledProfileTools()
  {
    var policy = new ExternalDiagnosticToolPolicy(
      enabled: true,
      toolLocator: new ThrowingLocator());

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetGcdump,
      Environment.ProcessId,
      runId: "normal-run",
      stageId: "Run",
      mode: PerformanceMode.Normal,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Skipped, attachment.Status);
    Assert.Equal("external-tool-mode-disabled", attachment.ErrorKind);
  }

  public void Dispose()
  {
    if (Directory.Exists(_runRoot))
    {
      Directory.Delete(_runRoot, recursive: true);
    }
  }

  private string ResolveAttachmentPath(string relativePath)
  {
    return Path.Combine(_runRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
  }

  private static string GetToolExecutable(ExternalDiagnosticTool tool)
  {
    return tool switch
    {
      ExternalDiagnosticTool.DotnetTrace => "dotnet-trace",
      ExternalDiagnosticTool.DotnetCounters => "dotnet-counters",
      ExternalDiagnosticTool.DotnetGcdump => "dotnet-gcdump",
      _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
  }

  private static bool TryLocate(string executable, out string path)
  {
    var pathValue = Environment.GetEnvironmentVariable("PATH");
    if (!string.IsNullOrWhiteSpace(pathValue))
    {
      var names = OperatingSystem.IsWindows()
        ? new[] { executable, executable + ".exe", executable + ".cmd" }
        : new[] { executable };
      foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
      {
        foreach (var name in names)
        {
          var candidate = Path.Combine(directory, name);
          if (File.Exists(candidate))
          {
            path = candidate;
            return true;
          }
        }
      }
    }

    path = string.Empty;
    return false;
  }

  private static string? ReadVersion(string executablePath)
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = executablePath,
        ArgumentList = { "--version" },
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
      });
      if (process is null)
      {
        return null;
      }

      process.WaitForExit(5000);
      var output = process.StandardOutput.ReadToEnd().Trim();
      return string.IsNullOrWhiteSpace(output) ? null : output;
    }
    catch (Exception)
    {
      return null;
    }
  }

  private sealed class FixedToolLocator : IExternalDiagnosticToolLocator
  {
    private readonly ExternalDiagnosticToolInfo? _tool;

    public FixedToolLocator(ExternalDiagnosticToolInfo? tool)
    {
      _tool = tool;
    }

    public ExternalDiagnosticToolInfo? Locate(ExternalDiagnosticTool tool)
    {
      return _tool is null
        ? null
        : new ExternalDiagnosticToolInfo(tool, _tool.ExecutablePath, _tool.Version);
    }
  }

  private sealed class ThrowingLocator : IExternalDiagnosticToolLocator
  {
    public ExternalDiagnosticToolInfo? Locate(ExternalDiagnosticTool tool)
    {
      throw new InvalidOperationException("normal mode must not locate tools");
    }
  }
}
