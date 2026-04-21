using Domain.Common;
using Domain.Workspaces;

namespace Domain.Execution;

/// <summary>
/// 表示文件变更。
/// </summary>
public sealed class FileChange
{
    public FileChange(DocumentPath documentPath, string summary, IReadOnlyCollection<string> affectedTargets)
        : this(
            documentPath,
            summary,
            (affectedTargets ?? Array.Empty<string>()).Select(TargetName.Create).ToArray())
    {
    }

    public FileChange(DocumentPath documentPath, string summary, IReadOnlyCollection<TargetName> affectedTargets)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(affectedTargets);
        DocumentPath = documentPath;
        Summary = summary.Trim();
        AffectedTargetValues = affectedTargets.ToArray();
    }

    public DocumentPath DocumentPath { get; }

    public string Summary { get; }

    public IReadOnlyCollection<string> AffectedTargets =>
        AffectedTargetValues.Select(static item => item.Value).ToArray();

    public IReadOnlyCollection<TargetName> AffectedTargetValues { get; }
}
