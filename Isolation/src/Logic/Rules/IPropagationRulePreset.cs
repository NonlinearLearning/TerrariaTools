using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义传播场景的稳定规则预设。
/// </summary>
public interface IPropagationRulePreset
{
    /// <summary>
    /// 解析传播规则码。
    /// </summary>
    RuleCode ResolveRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null);

    /// <summary>
    /// 获取传播场景的稳定规则码。
    /// </summary>
    IReadOnlyCollection<RuleCode> GetRuleCodes();
}
