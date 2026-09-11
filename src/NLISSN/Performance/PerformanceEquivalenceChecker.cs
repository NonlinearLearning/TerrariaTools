using NLISSN.Core.Performance;

namespace NLISSN.Performance;

public sealed record PerformanceEquivalenceResult(
  bool IsEligible,
  IReadOnlyList<string> ReasonCodes)
{
  public RunPerformanceReport Apply(RunPerformanceReport report)
  {
    ArgumentNullException.ThrowIfNull(report);
    return report.WithComparison(IsEligible, ReasonCodes);
  }
}

/// Compares immutable terminal reports without re-running any analysis.
public static class PerformanceEquivalenceChecker
{
  public static PerformanceEquivalenceResult Compare(
    RunPerformanceReport baseline,
    RunPerformanceReport candidate)
  {
    ArgumentNullException.ThrowIfNull(baseline);
    ArgumentNullException.ThrowIfNull(candidate);

    var reasons = new SortedSet<string>(StringComparer.Ordinal);
    CompareTerminal(baseline, reasons);
    CompareTerminal(candidate, reasons);
    CompareIdentity(baseline, candidate, reasons);

    return new PerformanceEquivalenceResult(
      reasons.Count == 0,
      reasons.ToArray());
  }

  private static void CompareTerminal(
    RunPerformanceReport report,
    ISet<string> reasons)
  {
    if (!report.IsComplete || !report.TerminalSummary.IsComplete)
    {
      reasons.Add(PerformanceComparisonReasonCode.TerminalIncomplete);
    }

    if (report.TerminalStatus != PerformanceStatus.Completed ||
        report.TerminalSummary.Status != PerformanceStatus.Completed)
    {
      reasons.Add(PerformanceComparisonReasonCode.TerminalStatusNotCompleted);
    }
  }

  private static void CompareIdentity(
    RunPerformanceReport baseline,
    RunPerformanceReport candidate,
    ISet<string> reasons)
  {
    var left = PerformanceComparisonIdentity.FromReport(baseline);
    var right = PerformanceComparisonIdentity.FromReport(candidate);
    if (left is null || right is null)
    {
      reasons.Add(PerformanceComparisonReasonCode.IdentityMissing);
      return;
    }

    CompareRequired(left.InputIdentity, right.InputIdentity, PerformanceComparisonReasonCode.InputIdentityMismatch, reasons);
    CompareRequired(left.RuleProfileHash, right.RuleProfileHash, PerformanceComparisonReasonCode.RuleProfileMismatch, reasons);
    CompareRequired(left.CapabilityFingerprint, right.CapabilityFingerprint, PerformanceComparisonReasonCode.CapabilityMismatch, reasons);
    CompareRequired(left.CacheMode, right.CacheMode, PerformanceComparisonReasonCode.CacheModeMismatch, reasons);
    CompareRequired(left.Sdk, right.Sdk, PerformanceComparisonReasonCode.SdkMismatch, reasons);
    CompareRequired(left.Runtime, right.Runtime, PerformanceComparisonReasonCode.RuntimeMismatch, reasons);
    CompareRequired(left.OperatingSystem, right.OperatingSystem, PerformanceComparisonReasonCode.OperatingSystemMismatch, reasons);
    CompareRequired(left.Cpu, right.Cpu, PerformanceComparisonReasonCode.CpuMismatch, reasons);
    CompareRequired(left.EnvironmentFingerprint, right.EnvironmentFingerprint, PerformanceComparisonReasonCode.EnvironmentMismatch, reasons);
    CompareRequired(left.DirectoryDop, right.DirectoryDop, PerformanceComparisonReasonCode.DirectoryDopMismatch, reasons);
    CompareRequired(left.CpgDop, right.CpgDop, PerformanceComparisonReasonCode.CpgDopMismatch, reasons);
    CompareRequired(left.RuleDop, right.RuleDop, PerformanceComparisonReasonCode.RuleDopMismatch, reasons);

    if (left.Mode != right.Mode)
    {
      reasons.Add(PerformanceComparisonReasonCode.ModeMismatch);
    }

    if (left.DiagnosticsEnabled != right.DiagnosticsEnabled)
    {
      reasons.Add(PerformanceComparisonReasonCode.DiagnosticsMismatch);
    }

    CompareRequired(left.GraphSnapshot, right.GraphSnapshot, PerformanceComparisonReasonCode.GraphSnapshotMismatch, reasons);
    CompareRequired(left.RuleSnapshot, right.RuleSnapshot, PerformanceComparisonReasonCode.RuleSnapshotMismatch, reasons);
    CompareRequired(left.ArtifactSnapshot, right.ArtifactSnapshot, PerformanceComparisonReasonCode.ArtifactSnapshotMismatch, reasons);
  }

  private static void CompareRequired<T>(
    T? left,
    T? right,
    string mismatchReason,
    ISet<string> reasons)
    where T : struct
  {
    if (!left.HasValue || !right.HasValue)
    {
      reasons.Add(PerformanceComparisonReasonCode.IdentityFieldMissing);
      return;
    }

    if (!EqualityComparer<T>.Default.Equals(left.Value, right.Value))
    {
      reasons.Add(mismatchReason);
    }
  }

  private static void CompareRequired(
    string? left,
    string? right,
    string mismatchReason,
    ISet<string> reasons)
  {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
    {
      reasons.Add(PerformanceComparisonReasonCode.IdentityFieldMissing);
      return;
    }

    if (!string.Equals(left, right, StringComparison.Ordinal))
    {
      reasons.Add(mismatchReason);
    }
  }
}
