namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示成员切片产物。
/// </summary>
public sealed class MemberSliceArtifact
{
    public MemberSliceArtifact(string className, string rootMemberName, RewriteArtifactSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMemberName);
        ArgumentNullException.ThrowIfNull(source);
        ClassName = className.Trim();
        RootMemberName = rootMemberName.Trim();
        Source = source;
    }

    public string ClassName { get; }

    public string RootMemberName { get; }

    public RewriteArtifactSource Source { get; }
}
