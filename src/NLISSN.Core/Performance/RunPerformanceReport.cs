using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

public sealed record PerformanceTerminalSummary(
  long? WallElapsedMs,
  long? AccumulatedElapsedMs,
  PerformanceStatus Status,
  bool IsComplete,
  string? ErrorKind = null);

public sealed record RunPerformanceReport
{
  public RunPerformanceReport(
    string runId,
    string inputKind,
    string? inputIdentity,
    IReadOnlyList<ApplicationPerformanceFacts>? items,
    PerformanceStageSample? rootStage,
    PerformanceTerminalSummary terminalSummary,
    PerformanceStatus terminalStatus,
    PerformanceMode mode = PerformanceMode.Normal,
    int sampleNumber = 1,
    bool isWarmup = false,
    bool comparisonEligible = false,
    IReadOnlyList<string>? comparisonReasons = null,
    DirectoryPerformanceFacts? Directory = null,
    IReadOnlyList<PerformanceStageSample>? stages = null,
    PerformanceResourceFacts? resources = null,
    IReadOnlyList<PerformanceAttachmentReference>? attachments = null,
    PerformanceRunIdentity? identity = null)
  {
    if (string.IsNullOrWhiteSpace(runId))
    {
      throw new ArgumentException("A performance run ID cannot be empty.", nameof(runId));
    }

    if (string.IsNullOrWhiteSpace(inputKind))
    {
      throw new ArgumentException("A performance input kind cannot be empty.", nameof(inputKind));
    }

    ArgumentNullException.ThrowIfNull(terminalSummary);
    if (sampleNumber <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(sampleNumber));
    }

    RunId = runId;
    InputKind = inputKind;
    InputIdentity = inputIdentity;
    Items = new ReadOnlyCollection<ApplicationPerformanceFacts>(
      (items ?? Array.Empty<ApplicationPerformanceFacts>()).ToArray());
    RootStage = rootStage;
    TerminalSummary = terminalSummary;
    TerminalStatus = terminalStatus;
    Mode = mode;
    SampleNumber = sampleNumber;
    IsWarmup = isWarmup;
    ComparisonEligible = comparisonEligible;
    ComparisonReasons = new ReadOnlyCollection<string>(
      (comparisonReasons ?? Array.Empty<string>()).ToArray());
    this.Directory = Directory;
    Stages = new ReadOnlyCollection<PerformanceStageSample>(
      (stages ?? Array.Empty<PerformanceStageSample>()).ToArray());
    Resources = resources;
    Attachments = new ReadOnlyCollection<PerformanceAttachmentReference>(
      (attachments ?? Array.Empty<PerformanceAttachmentReference>()).ToArray());
    Identity = identity;
  }

  public string RunId { get; }

  public string InputKind { get; }

  public string? InputIdentity { get; }

  public IReadOnlyList<ApplicationPerformanceFacts> Items { get; }

  public PerformanceStageSample? RootStage { get; }

  public PerformanceTerminalSummary TerminalSummary { get; }

  public PerformanceStatus TerminalStatus { get; }

  public PerformanceMode Mode { get; }

  public int SampleNumber { get; }

  public bool IsWarmup { get; }

  public bool ComparisonEligible { get; }

  public IReadOnlyList<string> ComparisonReasons { get; }

  public DirectoryPerformanceFacts? Directory { get; }

  public IReadOnlyList<PerformanceStageSample> Stages { get; }

  public PerformanceResourceFacts? Resources { get; }

  public IReadOnlyList<PerformanceAttachmentReference> Attachments { get; }

  public PerformanceRunIdentity? Identity { get; }

  public bool IsComplete => TerminalSummary.IsComplete &&
    TerminalStatus == PerformanceStatus.Completed;

  public RunPerformanceReport WithComparison(
    bool comparisonEligible,
    IReadOnlyList<string>? comparisonReasons = null)
  {
    return new RunPerformanceReport(
      RunId,
      InputKind,
      InputIdentity,
      Items,
      RootStage,
      TerminalSummary,
      TerminalStatus,
      Mode,
      SampleNumber,
      IsWarmup,
      comparisonEligible,
      comparisonReasons,
      Directory,
      Stages,
      Resources,
      Attachments,
      Identity);
  }

  public static RunPerformanceReport Create(
    string runId,
    string inputKind,
    ApplicationPerformanceFacts item,
    PerformanceStatus terminalStatus)
  {
    ArgumentNullException.ThrowIfNull(item);
    return new RunPerformanceReport(
      runId,
      inputKind,
      item.ItemId,
      new[] { item },
      null,
      new PerformanceTerminalSummary(null, null, terminalStatus, terminalStatus == PerformanceStatus.Completed),
      terminalStatus);
  }
}
