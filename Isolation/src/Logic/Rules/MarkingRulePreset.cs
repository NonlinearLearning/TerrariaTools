using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义 marking 场景的默认规则组合。
/// </summary>
public sealed class MarkingRulePreset : IMarkingRulePreset
{
    private static readonly RuleCode[] RuleCodes =
    [
        RuleCode.Create("marking.rule-target"),
        RuleCode.Create("workflow.rule"),
        RuleCode.Create("workflow.guard"),
    ];

    /// <inheritdoc />
    public RuleCode ResolveRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null)
    {
        RuleCode resolvedRuleCode = !string.IsNullOrWhiteSpace(requestedRuleCode)
            ? RuleCode.Create(requestedRuleCode)
            : fallbackRuleCode ?? throw new InvalidOperationException("marking 规则码不能为空。");
        if (!RuleCodes.Contains(resolvedRuleCode))
        {
            throw new InvalidOperationException(
                $"规则 {resolvedRuleCode.Value} 未在 MarkingRulePreset 的 marking 规则集中注册。");
        }

        return resolvedRuleCode;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RuleCode> GetRuleCodes()
    {
        return RuleCodes.ToArray();
    }
}
