namespace Application.Contracts.Workspaces;

/// <summary>
/// 引用条目 DTO。
/// </summary>
public sealed class ReferenceItemDto
{
    /// <summary>
    /// 获取或设置引用名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置引用版本。
    /// </summary>
    public string Version { get; init; } = string.Empty;
}
