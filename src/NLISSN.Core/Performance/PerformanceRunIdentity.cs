namespace NLISSN.Core.Performance;

/// Immutable inputs required before two terminal reports may be compared.
public sealed record PerformanceRunIdentity(
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
  string? GraphSnapshot = null,
  string? RuleSnapshot = null,
  string? ArtifactSnapshot = null);
