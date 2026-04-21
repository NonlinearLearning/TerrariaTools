namespace Logic.Workspaces;

/// <summary>
/// 表示工作区启用规则的边界输入。
/// </summary>
public sealed class WorkspaceEnabledRuleInput
{
    /// <summary>
    /// 获取或初始化规则编码。
    /// </summary>
    public string RuleCode { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化显示名称。
    /// </summary>
    public string? DisplayName { get; init; }
}
