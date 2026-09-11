namespace NLISSN.Core.Performance;

public sealed record PerformanceAttachmentReference(
  string Kind,
  string RunId,
  string? StageId,
  PerformanceMode Mode,
  string? RelativePath,
  bool IsAvailable,
  bool IsComplete,
  string? ErrorKind = null);
