using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义传播场景的默认规则组合。
/// </summary>
public sealed class PropagationRulePreset : IPropagationRulePreset
{
    private static readonly RuleCode[] RuleCodes =
    [
        RuleCode.Create("propagation.rule"),
    ];

    /// <inheritdoc />
    public RuleCode ResolveRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null)
    {
        RuleCode resolvedRuleCode = !string.IsNullOrWhiteSpace(requestedRuleCode)
            ? RuleCode.Create(requestedRuleCode)
            : fallbackRuleCode ?? throw new InvalidOperationException("传播规则码不能为空。");
        if (!RuleCodes.Contains(resolvedRuleCode))
        {
            throw new InvalidOperationException(
                $"规则 {resolvedRuleCode.Value} 未在 PropagationRulePreset 的传播规则集中注册。");
        }

        return resolvedRuleCode;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RuleCode> GetRuleCodes()
    {
        return RuleCodes.ToArray();
    }
}
