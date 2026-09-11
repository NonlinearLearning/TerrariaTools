using NLISSN.Core.Performance;
using NLISSN.Core.Rewrite;

namespace NLISSN.Application;

/// Carries the business result and the terminal performance report as separate
/// values so performance consumers never need to inspect mutable analysis state.
public sealed record AnalysisRunOutcome(
  PrototypeAnalysisResult Result,
  RunPerformanceReport Performance)
{
  public AnalysisRunOutcome WithMode(PerformanceMode mode)
  {
    if (Performance.Mode == mode)
    {
      return this;
    }

    return this with
    {
      Performance = new RunPerformanceReport(
        Performance.RunId,
        Performance.InputKind,
        Performance.InputIdentity,
        Performance.Items,
        Performance.RootStage,
        Performance.TerminalSummary,
        Performance.TerminalStatus,
        mode,
        Performance.SampleNumber,
        Performance.IsWarmup,
        Performance.ComparisonEligible,
        Performance.ComparisonReasons,
        Performance.Directory,
        Performance.Stages,
        Performance.Resources,
        Performance.Attachments,
        Performance.Identity)
    };
  }

  public AnalysisRunOutcome WithRuntimeFacts(
    PerformanceStageSample rootStage,
    IReadOnlyList<PerformanceStageSample> stages,
    PerformanceResourceFacts? resources)
  {
    ArgumentNullException.ThrowIfNull(rootStage);
    ArgumentNullException.ThrowIfNull(stages);
    return this with
    {
      Performance = new RunPerformanceReport(
        Performance.RunId,
        Performance.InputKind,
        Performance.InputIdentity,
        Performance.Items,
        rootStage,
        Performance.TerminalSummary with
        {
          WallElapsedMs = rootStage.WallElapsedMs,
          AccumulatedElapsedMs = rootStage.AccumulatedElapsedMs
        },
        Performance.TerminalStatus,
        Performance.Mode,
        Performance.SampleNumber,
        Performance.IsWarmup,
        Performance.ComparisonEligible,
        Performance.ComparisonReasons,
        Performance.Directory,
        stages,
        resources,
        Performance.Attachments,
        Performance.Identity)
    };
  }

  public static AnalysisRunOutcome FromItem(
    string runId,
    string inputKind,
    PrototypeAnalysisResult result,
    PerformanceStatus terminalStatus = PerformanceStatus.Completed,
    PerformanceMode mode = PerformanceMode.Normal,
    string? inputIdentity = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(runId);
    ArgumentException.ThrowIfNullOrWhiteSpace(inputKind);
    ArgumentNullException.ThrowIfNull(result);

    var itemId = result.Performance?.ItemId ?? inputIdentity ?? inputKind;
    var item = result.Performance ?? new ApplicationPerformanceFacts(
      itemId,
      CpgPerformanceFacts.Unavailable(itemId, "application-performance-unavailable"),
      null,
      null,
      terminalStatus,
      terminalStatus == PerformanceStatus.Completed
        ? "application-performance-unavailable"
        : "analysis-terminal-status");
    var report = new RunPerformanceReport(
      runId,
      inputKind,
      inputIdentity,
      new[] { item },
      null,
      new PerformanceTerminalSummary(
        null,
        null,
        terminalStatus,
        terminalStatus == PerformanceStatus.Completed,
        terminalStatus == PerformanceStatus.Completed
          ? null
          : "analysis-terminal-status"),
      terminalStatus,
      mode);
    return new AnalysisRunOutcome(result, report);
  }

  public static AnalysisRunOutcome FromDirectory(
    string runId,
    string inputKind,
    PrototypeAnalysisResult result,
    DirectoryPerformanceFacts? directory,
    PerformanceMode mode = PerformanceMode.Normal,
    string? inputIdentity = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(runId);
    ArgumentException.ThrowIfNullOrWhiteSpace(inputKind);
    ArgumentNullException.ThrowIfNull(result);

    var facts = directory ?? new DirectoryPerformanceFacts(
      inputKind,
      status: PerformanceStatus.Unavailable,
      errorKind: "directory-performance-unavailable");
    var stage = facts.Stage;
    var terminalStatus = facts.Status;
    var terminal = new PerformanceTerminalSummary(
      stage?.WallElapsedMs,
      facts.StageSummary?.SumAccumulatedElapsedMs ?? stage?.AccumulatedElapsedMs,
      terminalStatus,
      terminalStatus == PerformanceStatus.Completed,
      facts.ErrorKind);
    var report = new RunPerformanceReport(
      runId,
      inputKind,
      inputIdentity,
      facts.Children,
      stage,
      terminal,
      terminalStatus,
      mode,
      Directory: facts);
    return new AnalysisRunOutcome(result, report);
  }
}
