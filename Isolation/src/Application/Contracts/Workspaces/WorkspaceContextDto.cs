namespace Application.Contracts.Workspaces;

/// <summary>
/// 工作区上下文 DTO。
/// </summary>
public sealed class WorkspaceContextDto
{
    /// <summary>
    /// 获取或设置工作区标识。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 获取或设置解决方案路径。
    /// </summary>
    public string SolutionPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置语言版本。
    /// </summary>
    public string LanguageVersion { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置项目列表。
    /// </summary>
    public IReadOnlyCollection<ProjectItemDto> Projects { get; init; } = Array.Empty<ProjectItemDto>();

    /// <summary>
    /// 获取或设置文档路径列表。
    /// </summary>
    public IReadOnlyCollection<string> Documents { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或设置引用列表。
    /// </summary>
    public IReadOnlyCollection<ReferenceItemDto> References { get; init; } = Array.Empty<ReferenceItemDto>();
}
