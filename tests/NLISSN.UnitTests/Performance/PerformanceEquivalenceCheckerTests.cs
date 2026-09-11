using NLISSN.Core.Performance;
using NLISSN.Performance;
using Xunit;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceEquivalenceCheckerTests
{
  [Fact]
  public void MatchingIdentitySnapshotsAndTerminalFactsAreEligible()
  {
    var baseline = CreateReport("run-baseline");
    var candidate = CreateReport("run-candidate");

    var result = PerformanceEquivalenceChecker.Compare(baseline, candidate);

    Assert.True(result.IsEligible);
    Assert.Empty(result.ReasonCodes);
    Assert.True(result.Apply(candidate).ComparisonEligible);
    Assert.Empty(result.Apply(candidate).ComparisonReasons);
  }

  [Fact]
  public void IdentityMismatchProducesStableReasonCodeAndKeepsRawFacts()
  {
    var baseline = CreateReport("run-baseline");
    var candidate = CreateReport(
      "run-candidate",
      CreateIdentity(cacheMode: "cold"));

    var result = PerformanceEquivalenceChecker.Compare(baseline, candidate);
    var applied = result.Apply(candidate);

    Assert.False(result.IsEligible);
    Assert.Contains(PerformanceComparisonReasonCode.CacheModeMismatch, result.ReasonCodes);
    Assert.False(applied.ComparisonEligible);
    Assert.Contains(PerformanceComparisonReasonCode.CacheModeMismatch, applied.ComparisonReasons);
    Assert.Same(candidate.Items.Single(), applied.Items.Single());
  }

  [Fact]
  public void MissingIdentitySnapshotOrTerminalFactsFailsClosed()
  {
    var baseline = CreateReport("run-baseline");
    var candidate = CreateReportWithoutIdentity(
      "run-candidate",
      terminalSummary: new PerformanceTerminalSummary(
        null,
        null,
        PerformanceStatus.Unknown,
        IsComplete: false),
      terminalStatus: PerformanceStatus.Unknown);

    var result = PerformanceEquivalenceChecker.Compare(baseline, candidate);

    Assert.False(result.IsEligible);
    Assert.Contains(PerformanceComparisonReasonCode.IdentityMissing, result.ReasonCodes);
    Assert.Contains(PerformanceComparisonReasonCode.TerminalIncomplete, result.ReasonCodes);
    Assert.Contains(PerformanceComparisonReasonCode.TerminalStatusNotCompleted, result.ReasonCodes);
  }

  [Theory]
  [InlineData(PerformanceMode.Normal)]
  [InlineData(PerformanceMode.Diagnostic)]
  [InlineData(PerformanceMode.Profile)]
  [InlineData(PerformanceMode.Benchmark)]
  public void DifferentModeOrDiagnosticsFlagIsNotComparable(PerformanceMode mode)
  {
    var baseline = CreateReport("run-baseline");
    var candidate = CreateReport(
      "run-candidate",
      CreateIdentity(
        mode: mode,
        diagnosticsEnabled: mode == PerformanceMode.Diagnostic));

    var result = PerformanceEquivalenceChecker.Compare(baseline, candidate);

    if (mode == PerformanceMode.Normal)
    {
      Assert.True(result.IsEligible);
      return;
    }

    Assert.False(result.IsEligible);
    Assert.Contains(PerformanceComparisonReasonCode.ModeMismatch, result.ReasonCodes);
  }

  [Fact]
  public void SnapshotMismatchAndFailedTerminalAreRejectedIndependently()
  {
    var baseline = CreateReport("run-baseline");
    var candidate = CreateReport(
      "run-candidate",
      CreateIdentity(graphSnapshot: "different-graph"),
      new PerformanceTerminalSummary(
        10,
        20,
        PerformanceStatus.Failed,
        IsComplete: false,
        ErrorKind: "analysis-failed"),
      PerformanceStatus.Failed);

    var result = PerformanceEquivalenceChecker.Compare(baseline, candidate);

    Assert.False(result.IsEligible);
    Assert.Contains(PerformanceComparisonReasonCode.GraphSnapshotMismatch, result.ReasonCodes);
    Assert.Contains(PerformanceComparisonReasonCode.TerminalIncomplete, result.ReasonCodes);
    Assert.Contains(PerformanceComparisonReasonCode.TerminalStatusNotCompleted, result.ReasonCodes);
  }

  private static RunPerformanceReport CreateReport(
    string runId,
    PerformanceRunIdentity? identity = null,
    PerformanceTerminalSummary? terminalSummary = null,
    PerformanceStatus terminalStatus = PerformanceStatus.Completed)
  {
    return new RunPerformanceReport(
      runId,
      "file",
      "input-hash",
      new[]
      {
        new ApplicationPerformanceFacts(
          "file.cs",
          CpgPerformanceFacts.Unavailable("file.cs"),
          null,
          null)
      },
      null,
      terminalSummary ?? new PerformanceTerminalSummary(10, 20, PerformanceStatus.Completed, true),
      terminalStatus,
      PerformanceMode.Normal,
      identity: identity ?? CreateIdentity());
  }

  private static RunPerformanceReport CreateReportWithoutIdentity(
    string runId,
    PerformanceTerminalSummary terminalSummary,
    PerformanceStatus terminalStatus)
  {
    return new RunPerformanceReport(
      runId,
      "file",
      "input-hash",
      Array.Empty<ApplicationPerformanceFacts>(),
      null,
      terminalSummary,
      terminalStatus,
      PerformanceMode.Normal,
      identity: null);
  }

  private static PerformanceRunIdentity CreateIdentity(
    string cacheMode = "warm",
    PerformanceMode mode = PerformanceMode.Normal,
    bool diagnosticsEnabled = false,
    string graphSnapshot = "graph-1")
  {
    return new PerformanceRunIdentity(
      "input-hash",
      "rules-1",
      "capability-1",
      cacheMode,
      "sdk-1",
      "runtime-1",
      "os-1",
      "cpu-1",
      "environment-1",
      1,
      12,
      1,
      mode,
      diagnosticsEnabled,
      graphSnapshot,
      "rule-1",
      "artifact-1");
  }
}
