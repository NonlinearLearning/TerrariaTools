using Application.Contracts;

namespace Application.Contracts.Workspaces;

/// <summary>
/// 创建工作区上下文请求。
/// </summary>
public sealed class CreateWorkspaceContextRequest
{
    /// <summary>
    /// 获取或设置解决方案路径。
    /// </summary>
    public string SolutionPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置语言版本。
    /// </summary>
    public string LanguageVersion { get; init; } = "latest";

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

    /// <summary>
    /// 获取或设置运行模式。
    /// </summary>
    public ContractRunMode RunMode { get; init; } = ContractRunMode.FullWorkflow;

    /// <summary>
    /// 获取或设置规则集合。
    /// </summary>
    public RuleSetDto? RuleSet { get; init; }

    /// <summary>
    /// 获取或设置输入描述。
    /// </summary>
    public InputDescriptorDto? InputDescriptor { get; init; }
}
