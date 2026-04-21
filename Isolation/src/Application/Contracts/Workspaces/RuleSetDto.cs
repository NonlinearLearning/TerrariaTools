namespace Application.Contracts.Workspaces;

/// <summary>
/// 规则集 DTO。
/// </summary>
public sealed class RuleSetDto
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyCollection<EnabledRuleDto> EnabledRules { get; init; } =
        Array.Empty<EnabledRuleDto>();
}
