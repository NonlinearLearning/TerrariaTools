namespace Domain.Workspaces;

/// <summary>
/// 描述工作区中的项目。
/// </summary>
public sealed record ProjectDescriptor
{
    public ProjectDescriptor(string name, string path)
        : this(name, WorkspacePath.Create(path))
    {
    }

    public ProjectDescriptor(string name, WorkspacePath path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        PathValue = path;
    }

    public string Name { get; }

    public string Path => PathValue.Value;

    public WorkspacePath PathValue { get; }
}
