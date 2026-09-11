using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

public sealed record RuleGraphNodePerformanceFact(
  string NodeId,
  int? InputCount,
  int? OutputCount,
  long? WallElapsedMs,
  PerformanceStatus Status = PerformanceStatus.Completed,
  string? ErrorKind = null);

public sealed record RuleGraphPerformanceFacts
{
  public RuleGraphPerformanceFacts(
    IReadOnlyList<RuleGraphNodePerformanceFact>? nodeSamples = null,
    int? peakReadyNodeCount = null,
    int? peakConcurrentNodeCount = null,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    NodeSamples = new ReadOnlyCollection<RuleGraphNodePerformanceFact>(
      (nodeSamples ?? Array.Empty<RuleGraphNodePerformanceFact>()).ToArray());
    PeakReadyNodeCount = peakReadyNodeCount;
    PeakConcurrentNodeCount = peakConcurrentNodeCount;
    Status = status;
    ErrorKind = errorKind;
  }

  public IReadOnlyList<RuleGraphNodePerformanceFact> NodeSamples { get; }

  public int? PeakReadyNodeCount { get; }

  public int? PeakConcurrentNodeCount { get; }

  public PerformanceStatus Status { get; }

  public string? ErrorKind { get; }
}

public sealed record RewritePerformanceFacts(
  long? WallElapsedMs,
  int? EditCount,
  int? DiffFileCount,
  PerformanceStatus Status = PerformanceStatus.Completed,
  string? ErrorKind = null);

public sealed record ApplicationPerformanceFacts
{
  public ApplicationPerformanceFacts(
    string itemId,
    CpgPerformanceFacts? cpg,
    RuleGraphPerformanceFacts? ruleGraph,
    RewritePerformanceFacts? rewrite,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    if (string.IsNullOrWhiteSpace(itemId))
    {
      throw new ArgumentException("An application performance item ID cannot be empty.", nameof(itemId));
    }

    ItemId = itemId;
    Cpg = cpg;
    RuleGraph = ruleGraph;
    Rewrite = rewrite;
    Status = status;
    ErrorKind = errorKind;
  }

  public string ItemId { get; }

  public CpgPerformanceFacts? Cpg { get; }

  public RuleGraphPerformanceFacts? RuleGraph { get; }

  public RewritePerformanceFacts? Rewrite { get; }

  public IReadOnlyList<ApplicationPerformanceFacts>? Children => null;

  public PerformanceStatus Status { get; }

  public string? ErrorKind { get; }
}
