using System.Collections.ObjectModel;

namespace NLISSN.Infrastructure.Workspace;

public enum WorkspaceReferenceKind
{
    Project,
    Analyzer,
    Framework,
    Package,
    Metadata,
    Unknown
}

public sealed record WorkspaceReferenceSnapshot
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMsBuildMetadata =
      new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public WorkspaceReferenceKind Kind { get; }

    public string Display { get; }

    public string? FilePath { get; }

    public bool Exists { get; }

    public string? ReferencedProjectPath { get; }

    public string? TargetFramework { get; }

    public IReadOnlyDictionary<string, string> MsBuildMetadata { get; }

    public WorkspaceReferenceSnapshot(
      WorkspaceReferenceKind kind,
      string display,
      string? filePath,
      bool exists,
      string? referencedProjectPath = null,
      string? targetFramework = null,
      IReadOnlyDictionary<string, string>? msBuildMetadata = null)
    {
        Kind = kind;
        Display = display;
        FilePath = filePath;
        Exists = exists;
        ReferencedProjectPath = referencedProjectPath;
        TargetFramework = targetFramework;
        MsBuildMetadata = NormalizeMetadata(msBuildMetadata);
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(
      IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyMsBuildMetadata;
        }

        return new ReadOnlyDictionary<string, string>(
          metadata
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
              entry => entry.Key,
              entry => entry.Value,
              StringComparer.OrdinalIgnoreCase));
    }
}
