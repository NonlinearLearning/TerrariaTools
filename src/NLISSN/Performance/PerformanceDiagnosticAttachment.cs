using NLISSN.Core.Performance;

namespace NLISSN.Performance;

/// Creates and validates references to diagnostic artifacts without putting the
/// artifact payload in the terminal performance summary.
public static class PerformanceDiagnosticAttachment
{
  public static PerformanceAttachmentReference Create(
    string kind,
    string runId,
    string? stageId,
    PerformanceMode mode,
    string runArtifactRoot,
    string artifactPath)
  {
    ValidateIdentity(kind, runId, stageId);
    ArgumentException.ThrowIfNullOrWhiteSpace(runArtifactRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

    var fullRoot = NormalizeRoot(runArtifactRoot);
    var fullArtifactPath = Path.GetFullPath(artifactPath);
    EnsureWithinRoot(fullRoot, fullArtifactPath);

    var relativePath = NormalizeRelativePath(Path.GetRelativePath(fullRoot, fullArtifactPath));
    var isAvailable = File.Exists(fullArtifactPath);
    return new PerformanceAttachmentReference(
      kind,
      runId,
      stageId,
      mode,
      relativePath,
      isAvailable,
      isAvailable,
      isAvailable ? null : "attachment-missing");
  }

  public static PerformanceAttachmentReference CreateUnavailable(
    string kind,
    string runId,
    string? stageId,
    PerformanceMode mode,
    string? relativePath = null,
    string errorKind = "attachment-unavailable")
  {
    ValidateIdentity(kind, runId, stageId);
    ArgumentException.ThrowIfNullOrWhiteSpace(errorKind);

    return new PerformanceAttachmentReference(
      kind,
      runId,
      stageId,
      mode,
      relativePath is null ? null : NormalizeRelativePath(relativePath),
      IsAvailable: false,
      IsComplete: false,
      ErrorKind: errorKind);
  }

  public static void ValidateForSummary(
    PerformanceAttachmentReference attachment,
    string reportRunId,
    string runArtifactRoot)
  {
    ArgumentNullException.ThrowIfNull(attachment);
    ArgumentException.ThrowIfNullOrWhiteSpace(reportRunId);
    ArgumentException.ThrowIfNullOrWhiteSpace(runArtifactRoot);

    if (!string.Equals(attachment.RunId, reportRunId, StringComparison.Ordinal))
    {
      throw new InvalidDataException(
        $"Attachment run ID '{attachment.RunId}' does not match report run ID '{reportRunId}'.");
    }

    var fullRoot = NormalizeRoot(runArtifactRoot);
    if (attachment.RelativePath is null)
    {
      if (attachment.IsAvailable || attachment.IsComplete)
      {
        throw new InvalidDataException(
          "An available or complete attachment must have a relative path.");
      }

      return;
    }

    var relativePath = NormalizeRelativePath(attachment.RelativePath);
    var fullArtifactPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
    EnsureWithinRoot(fullRoot, fullArtifactPath);

    if (attachment.IsComplete && !attachment.IsAvailable)
    {
      throw new InvalidDataException(
        "An attachment cannot be complete when it is unavailable.");
    }

    if (attachment.IsAvailable && !File.Exists(fullArtifactPath))
    {
      throw new InvalidDataException(
        $"Attachment '{attachment.RelativePath}' is marked available but does not exist.");
    }
  }

  public static string ResolveRunArtifactRoot(string summaryPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(summaryPath);

    var fullSummaryPath = Path.GetFullPath(summaryPath);
    var summaryDirectory = Path.GetDirectoryName(fullSummaryPath)
      ?? throw new ArgumentException("Summary path must have a parent directory.", nameof(summaryPath));
    var directoryInfo = new DirectoryInfo(summaryDirectory);
    return string.Equals(directoryInfo.Name, "Performance", StringComparison.OrdinalIgnoreCase) &&
      directoryInfo.Parent is not null
      ? directoryInfo.Parent.FullName
      : directoryInfo.FullName;
  }

  public static string NormalizeRelativePath(string relativePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

    if (Path.IsPathRooted(relativePath))
    {
      throw new ArgumentException("Attachment paths must be relative to the run artifact root.", nameof(relativePath));
    }

    var normalized = relativePath.Replace('\\', '/');
    var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
    {
      throw new ArgumentException(
        "Attachment paths cannot contain empty, current-directory, or parent-directory segments.",
        nameof(relativePath));
    }

    return string.Join('/', segments);
  }

  private static void ValidateIdentity(string kind, string runId, string? stageId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(kind);
    ArgumentException.ThrowIfNullOrWhiteSpace(runId);
    if (stageId is not null)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
    }
  }

  private static string NormalizeRoot(string root)
  {
    var fullRoot = Path.GetFullPath(root);
    return fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
  }

  private static void EnsureWithinRoot(string fullRoot, string fullPath)
  {
    var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
    var comparison = OperatingSystem.IsWindows()
      ? StringComparison.OrdinalIgnoreCase
      : StringComparison.Ordinal;
    if (!fullPath.StartsWith(rootWithSeparator, comparison))
    {
      throw new ArgumentException(
        "Attachment paths must remain inside the run artifact root.",
        nameof(fullPath));
    }
  }
}
