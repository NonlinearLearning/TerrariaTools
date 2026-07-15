namespace MinimalRoslynCpg.Analysis.FlowSummaries;

/// <summary>
/// Holds built-in conservative summaries. The first overlay intentionally has no BCL assumptions.
/// </summary>
public static class RoslynCpgDefaultFlowSummaries
{
  public static IReadOnlyList<RoslynCpgFlowSummary> All { get; } = Array.Empty<RoslynCpgFlowSummary>();

  public static bool TryGet(string stableKey, out RoslynCpgFlowSummary? summary)
  {
    summary = All.FirstOrDefault(candidate => string.Equals(
      candidate.StableKey,
      stableKey,
      StringComparison.Ordinal));
    return summary is not null;
  }
}
