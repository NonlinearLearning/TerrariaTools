using NLISSN.Core.Performance;

namespace NLISSN.Performance;

/// Stable reason codes emitted when a performance sample cannot be compared.
public static class PerformanceComparisonReasonCode
{
  public const string IdentityMissing = "identity-missing";
  public const string InputIdentityMismatch = "input-identity-mismatch";
  public const string RuleProfileMismatch = "rule-profile-mismatch";
  public const string CapabilityMismatch = "capability-mismatch";
  public const string CacheModeMismatch = "cache-mode-mismatch";
  public const string SdkMismatch = "sdk-mismatch";
  public const string RuntimeMismatch = "runtime-mismatch";
  public const string OperatingSystemMismatch = "operating-system-mismatch";
  public const string CpuMismatch = "cpu-mismatch";
  public const string EnvironmentMismatch = "environment-mismatch";
  public const string DirectoryDopMismatch = "directory-dop-mismatch";
  public const string CpgDopMismatch = "cpg-dop-mismatch";
  public const string RuleDopMismatch = "rule-dop-mismatch";
  public const string ModeMismatch = "mode-mismatch";
  public const string DiagnosticsMismatch = "diagnostics-mismatch";
  public const string GraphSnapshotMismatch = "graph-snapshot-mismatch";
  public const string RuleSnapshotMismatch = "rule-snapshot-mismatch";
  public const string ArtifactSnapshotMismatch = "artifact-snapshot-mismatch";
  public const string IdentityFieldMissing = "identity-field-missing";
  public const string TerminalIncomplete = "terminal-incomplete";
  public const string TerminalStatusNotCompleted = "terminal-status-not-completed";

  internal static IReadOnlyList<string> All { get; } =
  [
    IdentityMissing,
    InputIdentityMismatch,
    RuleProfileMismatch,
    CapabilityMismatch,
    CacheModeMismatch,
    SdkMismatch,
    RuntimeMismatch,
    OperatingSystemMismatch,
    CpuMismatch,
    EnvironmentMismatch,
    DirectoryDopMismatch,
    CpgDopMismatch,
    RuleDopMismatch,
    ModeMismatch,
    DiagnosticsMismatch,
    GraphSnapshotMismatch,
    RuleSnapshotMismatch,
    ArtifactSnapshotMismatch,
    IdentityFieldMissing,
    TerminalIncomplete,
    TerminalStatusNotCompleted
  ];
}

/// Immutable comparison identity projection. It keeps the checker independent
/// from builders, semantic models, graphs, and serializers.
public sealed record PerformanceComparisonIdentity(
  string? InputIdentity,
  string? RuleProfileHash,
  string? CapabilityFingerprint,
  string? CacheMode,
  string? Sdk,
  string? Runtime,
  string? OperatingSystem,
  string? Cpu,
  string? EnvironmentFingerprint,
  int? DirectoryDop,
  int? CpgDop,
  int? RuleDop,
  PerformanceMode Mode,
  bool DiagnosticsEnabled,
  string? GraphSnapshot,
  string? RuleSnapshot,
  string? ArtifactSnapshot)
{
  public static PerformanceComparisonIdentity? FromReport(RunPerformanceReport report)
  {
    ArgumentNullException.ThrowIfNull(report);
    return report.Identity is null
      ? null
      : new PerformanceComparisonIdentity(
        report.Identity.InputIdentity,
        report.Identity.RuleProfileHash,
        report.Identity.CapabilityFingerprint,
        report.Identity.CacheMode,
        report.Identity.Sdk,
        report.Identity.Runtime,
        report.Identity.OperatingSystem,
        report.Identity.Cpu,
        report.Identity.EnvironmentFingerprint,
        report.Identity.DirectoryDop,
        report.Identity.CpgDop,
        report.Identity.RuleDop,
        report.Identity.Mode,
        report.Identity.DiagnosticsEnabled,
        report.Identity.GraphSnapshot,
        report.Identity.RuleSnapshot,
        report.Identity.ArtifactSnapshot);
  }
}
