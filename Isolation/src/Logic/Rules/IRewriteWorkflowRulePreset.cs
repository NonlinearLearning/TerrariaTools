using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义 RewriteWorkflow 场景的规则预设。
/// </summary>
public interface IRewriteWorkflowRulePreset
{
    /// <summary>
    /// 获取 marking 阶段规则集名称。
    /// </summary>
    string GetMarkingRuleSetName();

    /// <summary>
    /// 解析 RewriteWorkflow marking 规则码。
    /// </summary>
    RuleCode ResolveMarkingRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null);

    /// <summary>
    /// 规范化 RewriteWorkflow 决策保护规则集合。
    /// </summary>
    IReadOnlyCollection<string> NormalizeProtectionRules(IReadOnlyCollection<string> protectionRules);

    /// <summary>
    /// 获取 RewriteWorkflow marking 默认规则码集合。
    /// </summary>
    IReadOnlyCollection<RuleCode> GetMarkingRuleCodes();

    /// <summary>
    /// 获取 RewriteWorkflow 决策保护规则码集合。
    /// </summary>
    IReadOnlyCollection<RuleCode> GetDecisionProtectionRuleCodes();

    /// <summary>
    /// 获取 RewriteWorkflow 预设全部规则码集合。
    /// </summary>
    IReadOnlyCollection<RuleCode> GetDefaultRuleCodes();
}
