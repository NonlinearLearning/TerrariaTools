namespace Domain.Workspaces;

/// <summary>
/// 描述工作区中的外部引用。
/// </summary>
public sealed record ReferenceDescriptor
{
    public ReferenceDescriptor(string name, string version)
        : this(ReferenceName.Create(name), ReferenceVersion.Create(version))
    {
    }

    public ReferenceDescriptor(ReferenceName name, ReferenceVersion version)
    {
        NameValue = name;
        VersionValue = version;
    }

    public string Name => NameValue.Value;

    public ReferenceName NameValue { get; }

    public string Version => VersionValue.Value;

    public ReferenceVersion VersionValue { get; }
}
