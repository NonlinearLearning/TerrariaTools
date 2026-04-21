namespace Application.Contracts.Workspaces;

/// <summary>
/// 项目条目 DTO。
/// </summary>
public sealed class ProjectItemDto
{
    /// <summary>
    /// 获取或设置项目名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置项目路径。
    /// </summary>
    public string Path { get; init; } = string.Empty;
}
