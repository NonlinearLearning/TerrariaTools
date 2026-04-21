using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义 marking 场景的稳定规则预设。
/// </summary>
public interface IMarkingRulePreset
{
    /// <summary>
    /// 解析 marking 规则码。
    /// </summary>
    RuleCode ResolveRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null);

    /// <summary>
    /// 获取 marking 场景的稳定规则码。
    /// </summary>
    IReadOnlyCollection<RuleCode> GetRuleCodes();
}
