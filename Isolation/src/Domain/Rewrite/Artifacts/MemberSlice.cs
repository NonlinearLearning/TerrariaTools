namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示成员切片结果。
/// </summary>
public sealed class MemberSlice
{
    private readonly List<string> memberNames = new();

    private MemberSlice(MemberSliceArtifact artifact)
    {
        Artifact = artifact;
    }

    public MemberSliceArtifact Artifact { get; }

    /// <summary>
    /// 获取类型名称。
    /// </summary>
    public string ClassName => Artifact.ClassName;

    /// <summary>
    /// 获取根成员名称。
    /// </summary>
    public string RootMemberName => Artifact.RootMemberName;

    /// <summary>
    /// 获取切片源码。
    /// </summary>
    public string SourceCode => Artifact.Source.SourceCode;

    /// <summary>
    /// 获取成员名称集合。
    /// </summary>
    public IReadOnlyCollection<string> MemberNames => memberNames.AsReadOnly();

    /// <summary>
    /// 创建成员切片结果。
    /// </summary>
    public static MemberSlice Create(string className, string rootMemberName, string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMemberName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new MemberSlice(new MemberSliceArtifact(
            className.Trim(),
            rootMemberName.Trim(),
            new RewriteArtifactSource(sourceCode)));
    }

    /// <summary>
    /// 增加成员名称。
    /// </summary>
    public void AddMember(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (memberNames.Contains(memberName, StringComparer.Ordinal))
        {
            return;
        }

        memberNames.Add(memberName.Trim());
    }
}
