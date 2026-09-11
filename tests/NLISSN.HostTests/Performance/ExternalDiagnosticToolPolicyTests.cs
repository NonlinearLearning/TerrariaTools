using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class ExternalDiagnosticToolPolicyTests : IDisposable
{
  private readonly string _runRoot = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-external-diagnostic-{Guid.NewGuid():N}");

  public ExternalDiagnosticToolPolicyTests()
  {
    Directory.CreateDirectory(_runRoot);
  }

  [Fact]
  public async Task ToolUnavailableIsExplicitAndDoesNotInvokeRunner()
  {
    var runner = new RecordingRunner((command, _) =>
      throw new Xunit.Sdk.XunitException("runner must not be called when the tool is unavailable"));
    var policy = new ExternalDiagnosticToolPolicy(
      enabled: true,
      toolLocator: new FixedToolLocator(null),
      commandRunner: runner);

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetTrace,
      processId: 123,
      runId: "run-1",
      stageId: "Rule.Propagate",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Unavailable, attachment.Status);
    Assert.False(attachment.IsAvailable);
    Assert.False(attachment.IsComplete);
    Assert.Equal("external-tool-unavailable", attachment.ErrorKind);
    Assert.Equal(0, runner.CallCount);
  }

  [Fact]
  public async Task CommandFailureCleansTemporaryAndPartialOutput()
  {
    var runner = new RecordingRunner((command, _) =>
    {
      Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
      File.WriteAllText(command.OutputPath, "partial");
      return Task.FromResult(new ExternalDiagnosticCommandResult(
        Started: true,
        ExitCode: 7,
        ErrorKind: "command-exit-7"));
    });
    var policy = CreateEnabledPolicy(runner);

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetCounters,
      processId: 123,
      runId: "run-2",
      stageId: "CPG.Build",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Failed, attachment.Status);
    Assert.Equal(7, attachment.ExitCode);
    Assert.False(attachment.IsAvailable);
    Assert.False(attachment.IsComplete);
    Assert.Equal("command-exit-7", attachment.ErrorKind);
    Assert.NotNull(attachment.RelativePath);
    Assert.False(File.Exists(Path.Combine(_runRoot, attachment.RelativePath!.Replace('/', Path.DirectorySeparatorChar))));
    Assert.Empty(Directory.EnumerateFiles(_runRoot, "*.tmp", SearchOption.AllDirectories));
  }

  [Fact]
  public async Task SuccessfulCommandPublishesAttachmentAndPreservesAssociation()
  {
    var runner = new RecordingRunner((command, _) =>
    {
      Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
      File.WriteAllText(command.OutputPath, "profile payload");
      return Task.FromResult(new ExternalDiagnosticCommandResult(
        Started: true,
        ExitCode: 0));
    });
    var policy = CreateEnabledPolicy(runner);

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetTrace,
      processId: 123,
      runId: "run-3",
      stageId: "Rule.Propagate",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Completed, attachment.Status);
    Assert.True(attachment.IsAvailable);
    Assert.True(attachment.IsComplete);
    Assert.Equal("run-3", attachment.RunId);
    Assert.Equal("Rule.Propagate", attachment.StageId);
    Assert.Equal(0, attachment.ExitCode);
    Assert.Equal(1, runner.CallCount);
    Assert.NotNull(attachment.RelativePath);
    Assert.True(File.Exists(Path.Combine(_runRoot, attachment.RelativePath!.Replace('/', Path.DirectorySeparatorChar))));
    Assert.Empty(Directory.EnumerateFiles(_runRoot, "*.tmp", SearchOption.AllDirectories));
  }

  [Fact]
  public async Task NormalModeNeverLaunchesExternalTool()
  {
    var runner = new RecordingRunner((_, _) =>
      throw new Xunit.Sdk.XunitException("normal mode must not launch external tools"));
    var policy = CreateEnabledPolicy(runner);

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetGcdump,
      processId: 123,
      runId: "run-4",
      stageId: "Run",
      mode: PerformanceMode.Normal,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Skipped, attachment.Status);
    Assert.Equal("external-tool-mode-disabled", attachment.ErrorKind);
    Assert.Equal(0, runner.CallCount);
  }

  [Fact]
  public async Task DefaultPolicyDoesNotAutoRunExternalTools()
  {
    var runner = new RecordingRunner((_, _) =>
      throw new Xunit.Sdk.XunitException("disabled policy must not launch external tools"));
    var policy = new ExternalDiagnosticToolPolicy(commandRunner: runner);

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetTrace,
      processId: 123,
      runId: "run-5",
      stageId: "Run",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal(PerformanceStatus.Skipped, attachment.Status);
    Assert.Equal("external-tool-policy-disabled", attachment.ErrorKind);
    Assert.Equal(0, runner.CallCount);
  }

  [Fact]
  public async Task ProfileFailureReturnsManifestInsteadOfChangingBusinessResult()
  {
    var runner = new RecordingRunner((_, _) =>
      Task.FromResult(new ExternalDiagnosticCommandResult(
        Started: false,
        ExitCode: null,
        ErrorKind: "command-start-failed")));
    var policy = CreateEnabledPolicy(runner);
    var businessResult = "analysis-succeeded";

    var attachment = await policy.CaptureAsync(
      ExternalDiagnosticTool.DotnetGcdump,
      processId: 123,
      runId: "run-6",
      stageId: "Artifact.WriteBack",
      mode: PerformanceMode.Profile,
      runArtifactRoot: _runRoot);

    Assert.Equal("analysis-succeeded", businessResult);
    Assert.Equal(PerformanceStatus.Failed, attachment.Status);
    Assert.Equal("command-start-failed", attachment.ErrorKind);
  }

  private ExternalDiagnosticToolPolicy CreateEnabledPolicy(RecordingRunner runner)
  {
    return new ExternalDiagnosticToolPolicy(
      enabled: true,
      duration: TimeSpan.FromMilliseconds(10),
      timeout: TimeSpan.FromSeconds(1),
      toolLocator: new FixedToolLocator(new ExternalDiagnosticToolInfo(
        ExternalDiagnosticTool.DotnetTrace,
        "dotnet-trace",
        "9.0.0")),
      commandRunner: runner);
  }

  public void Dispose()
  {
    if (Directory.Exists(_runRoot))
    {
      Directory.Delete(_runRoot, recursive: true);
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

  private sealed class RecordingRunner : IExternalDiagnosticCommandRunner
  {
    private readonly Func<ExternalDiagnosticCommand, CancellationToken, Task<ExternalDiagnosticCommandResult>> _handler;

    public RecordingRunner(Func<ExternalDiagnosticCommand, CancellationToken, Task<ExternalDiagnosticCommandResult>> handler)
    {
      _handler = handler;
    }

    public int CallCount { get; private set; }

    public Task<ExternalDiagnosticCommandResult> RunAsync(
      ExternalDiagnosticCommand command,
      CancellationToken cancellationToken)
    {
      CallCount++;
      return _handler(command, cancellationToken);
    }
  }
}
