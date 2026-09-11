using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class ExternalDiagnosticAttachmentTests
{
  [Fact]
  public void SuccessfulAttachmentPreservesToolManifestAndRunStageAssociation()
  {
    var started = new DateTimeOffset(2026, 9, 11, 1, 2, 3, TimeSpan.Zero);
    var completed = started.AddSeconds(2);
    var reference = PerformanceDiagnosticAttachment.CreateManifest(
      kind: "trace",
      tool: ExternalDiagnosticTool.DotnetTrace,
      runId: "run-1",
      stageId: "Rule.Propagate",
      mode: PerformanceMode.Profile,
      runArtifactRoot: "C:/runs/run-1",
      artifactPath: "C:/runs/run-1/Performance/Diagnostics/trace.nettrace",
      status: PerformanceStatus.Completed,
      isAvailable: true,
      isComplete: true,
      errorKind: null,
      command: "dotnet-trace collect --process-id 123",
      version: "9.0.0",
      startedAtUtc: started,
      completedAtUtc: completed,
      exitCode: 0);

    var attachment = ExternalDiagnosticAttachment.FromReference(reference);

    Assert.Equal(ExternalDiagnosticTool.DotnetTrace, attachment.Tool);
    Assert.Equal("dotnet-trace collect --process-id 123", attachment.Command);
    Assert.Equal("9.0.0", attachment.Version);
    Assert.Equal(started, attachment.StartedAtUtc);
    Assert.Equal(completed, attachment.CompletedAtUtc);
    Assert.Equal(0, attachment.ExitCode);
    Assert.Equal("run-1", attachment.RunId);
    Assert.Equal("Rule.Propagate", attachment.StageId);
    Assert.Equal("Performance/Diagnostics/trace.nettrace", attachment.RelativePath);
    Assert.True(attachment.IsAvailable);
    Assert.True(attachment.IsComplete);
    Assert.Equal(PerformanceStatus.Completed, attachment.Status);
  }

  [Fact]
  public void FailedAttachmentRetainsFailureManifestWithoutClaimingAvailability()
  {
    var reference = PerformanceDiagnosticAttachment.CreateManifest(
      kind: "gcdump",
      tool: ExternalDiagnosticTool.DotnetGcdump,
      runId: "run-2",
      stageId: "CPG.Build",
      mode: PerformanceMode.Profile,
      runArtifactRoot: "C:/runs/run-2",
      artifactPath: "C:/runs/run-2/Performance/Diagnostics/cpg.gcdump",
      status: PerformanceStatus.Failed,
      isAvailable: false,
      isComplete: false,
      errorKind: "command-exit-7",
      command: "dotnet-gcdump collect --process-id 123",
      version: null,
      startedAtUtc: DateTimeOffset.UtcNow,
      completedAtUtc: DateTimeOffset.UtcNow,
      exitCode: 7);

    var attachment = ExternalDiagnosticAttachment.FromReference(reference);

    Assert.Equal(PerformanceStatus.Failed, attachment.Status);
    Assert.False(attachment.IsAvailable);
    Assert.False(attachment.IsComplete);
    Assert.Equal("command-exit-7", attachment.ErrorKind);
    Assert.Equal(7, attachment.ExitCode);
    Assert.Equal("run-2", attachment.RunId);
    Assert.Equal("CPG.Build", attachment.StageId);
  }

  [Fact]
  public void ManifestRejectsAnAvailableArtifactWithoutAPath()
  {
    Assert.Throws<InvalidDataException>(() => PerformanceDiagnosticAttachment.CreateManifest(
      kind: "counters",
      tool: ExternalDiagnosticTool.DotnetCounters,
      runId: "run-3",
      stageId: "Run",
      mode: PerformanceMode.Profile,
      runArtifactRoot: "C:/runs/run-3",
      artifactPath: null,
      status: PerformanceStatus.Completed,
      isAvailable: true,
      isComplete: true,
      errorKind: null,
      command: "dotnet-counters collect",
      version: null,
      startedAtUtc: null,
      completedAtUtc: null,
      exitCode: 0));
  }
}
