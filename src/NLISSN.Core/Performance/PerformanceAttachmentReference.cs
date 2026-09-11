namespace NLISSN.Core.Performance;

public sealed record PerformanceAttachmentReference(
  string Kind,
  string RunId,
  string? StageId,
  PerformanceMode Mode,
  string? RelativePath,
  bool IsAvailable,
  bool IsComplete,
  string? ErrorKind = null,
  PerformanceStatus Status = PerformanceStatus.Unknown,
  string? Tool = null,
  string? Command = null,
  string? Version = null,
  DateTimeOffset? StartedAtUtc = null,
  DateTimeOffset? CompletedAtUtc = null,
  int? ExitCode = null);
