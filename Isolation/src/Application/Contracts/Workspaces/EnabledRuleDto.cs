namespace Application.Contracts.Workspaces;

/// <summary>
/// 启用规则 DTO。
/// </summary>
public sealed class EnabledRuleDto
{
    public string RuleCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}
