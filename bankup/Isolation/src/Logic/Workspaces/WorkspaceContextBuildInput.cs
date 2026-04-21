using Domain.Rules;
using Domain.Workspaces;

namespace Logic.Workspaces;

/// <summary>
/// 表示工作区上下文构造输入。
/// </summary>
public sealed class WorkspaceContextBuildInput
{
    /// <summary>
    /// 获取或初始化解决方案路径。
    /// </summary>
    public string SolutionPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化语言版本。
    /// </summary>
    public string LanguageVersion { get; init; } = "latest";

    /// <summary>
    /// 获取或初始化项目描述集合。
    /// </summary>
    public IReadOnlyCollection<ProjectDescriptor> Projects { get; init; } = Array.Empty<ProjectDescriptor>();

    /// <summary>
    /// 获取或初始化文档路径集合。
    /// </summary>
    public IReadOnlyCollection<string> Documents { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 获取或初始化引用描述集合。
    /// </summary>
    public IReadOnlyCollection<ReferenceDescriptor> References { get; init; } = Array.Empty<ReferenceDescriptor>();

    /// <summary>
    /// 获取或初始化运行模式。
    /// </summary>
    public RunMode RunMode { get; init; } = RunMode.FullWorkflow;

    /// <summary>
    /// 获取或初始化工作区规则边界输入集合。
    /// </summary>
    public IReadOnlyCollection<WorkspaceEnabledRuleInput> RuleInputs { get; init; } = Array.Empty<WorkspaceEnabledRuleInput>();

    /// <summary>
    /// 获取或初始化输入描述。
    /// </summary>
    public InputDescriptor? InputDescriptor { get; init; }
}
