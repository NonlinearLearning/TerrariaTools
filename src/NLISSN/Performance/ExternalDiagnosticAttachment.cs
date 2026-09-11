using System.Text.Json;
using System.Text.Json.Serialization;

namespace NLISSN.Performance;

public enum ExternalDiagnosticAttachmentState
{
    Available,
    Unavailable,
    Failed,
    Skipped
}

public sealed record ExternalDiagnosticAttachment
{
    public ExternalDiagnosticAttachment(
      string tool,
      string? version,
      string? command,
      DateTimeOffset? startedAt,
      DateTimeOffset? completedAt,
      int? exitCode,
      string? relativeArtifactPath,
      ExternalDiagnosticAttachmentState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        if (relativeArtifactPath is not null &&
          (Path.IsPathRooted(relativeArtifactPath) || IsOutsideArtifactRoot(relativeArtifactPath)))
        {
            throw new ArgumentException(
              "Diagnostic attachment paths must be relative to the run artifact root.",
              nameof(relativeArtifactPath));
        }

        Tool = tool;
        Version = version;
        Command = command;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        ExitCode = exitCode;
        RelativeArtifactPath = relativeArtifactPath;
        State = state;
    }

    public string Tool { get; }
    public string? Version { get; }
    public string? Command { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
    public int? ExitCode { get; }
    public string? RelativeArtifactPath { get; }
    public ExternalDiagnosticAttachmentState State { get; }

    private static bool IsOutsideArtifactRoot(string path)
    {
        return path == ".." ||
          path.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
          path.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}

public sealed record PerformanceDiagnosticAttachment
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PerformanceDiagnosticAttachment(
      string runId,
      string stageId,
      string mode,
      IReadOnlyList<ExternalDiagnosticAttachment> attachments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(attachments);

        RunId = runId;
        StageId = stageId;
        Mode = mode;
        Attachments = attachments
          .OrderBy(attachment => attachment.Tool, StringComparer.Ordinal)
          .ToArray();
    }

    public string RunId { get; }
    public string StageId { get; }
    public string Mode { get; }
    public IReadOnlyList<ExternalDiagnosticAttachment> Attachments { get; }

    public static void Write(string path, PerformanceDiagnosticAttachment attachment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(attachment);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
          ?? throw new ArgumentException("The manifest path must have a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(attachment, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static PerformanceDiagnosticAttachment Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var attachment = JsonSerializer.Deserialize<PerformanceDiagnosticAttachment>(
          File.ReadAllText(path),
          JsonOptions);
        return attachment
          ?? throw new InvalidDataException("The diagnostic attachment manifest is empty.");
    }
}
