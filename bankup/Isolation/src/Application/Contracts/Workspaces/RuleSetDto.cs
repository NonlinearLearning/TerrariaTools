namespace Application.Contracts.Workspaces;

/// <summary>
/// 启用规则 DTO。
/// </summary>
public sealed class EnabledRuleDto
{
    public string RuleCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// 规则集 DTO。
/// </summary>
public sealed class RuleSetDto
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyCollection<EnabledRuleDto> EnabledRules { get; init; } = Array.Empty<EnabledRuleDto>();
}
