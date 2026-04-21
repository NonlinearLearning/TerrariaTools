using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 定义 RewriteWorkflow 场景的默认规则组合。
/// </summary>
public sealed class RewriteWorkflowRulePreset : IRewriteWorkflowRulePreset
{
    private const string MarkingRuleSetName = "workflow-marking";

    private static readonly RuleCode[] MarkingRuleCodes =
    [
        RuleCode.Create("workflow.rule"),
        RuleCode.Create("workflow.guard"),
    ];

    private static readonly RuleCode[] DecisionProtectionRuleCodes =
    [
        RuleCode.Create("public-contract"),
        RuleCode.Create("decision.protect"),
    ];

    /// <inheritdoc />
    public string GetMarkingRuleSetName()
    {
        return MarkingRuleSetName;
    }

    /// <inheritdoc />
    public RuleCode ResolveMarkingRuleCode(string? requestedRuleCode, RuleCode? fallbackRuleCode = null)
    {
        RuleCode resolvedRuleCode = !string.IsNullOrWhiteSpace(requestedRuleCode)
            ? RuleCode.Create(requestedRuleCode)
            : fallbackRuleCode ?? throw new InvalidOperationException("RewriteWorkflow marking 规则码不能为空。");
        if (!MarkingRuleCodes.Contains(resolvedRuleCode))
        {
            throw new InvalidOperationException(
                $"规则 {resolvedRuleCode.Value} 未在 RewriteWorkflowRulePreset 的 marking 规则集中注册。");
        }

        return resolvedRuleCode;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> NormalizeProtectionRules(IReadOnlyCollection<string> protectionRules)
    {
        ArgumentNullException.ThrowIfNull(protectionRules);

        List<string> normalizedRules = new();
        foreach (string protectionRule in protectionRules)
        {
            RuleCode resolvedRuleCode = RuleCode.Create(protectionRule);
            if (!DecisionProtectionRuleCodes.Contains(resolvedRuleCode))
            {
                throw new InvalidOperationException(
                    $"规则 {resolvedRuleCode.Value} 未在 RewriteWorkflowRulePreset 的决策保护规则集中注册。");
            }

            normalizedRules.Add(resolvedRuleCode.Value);
        }

        return normalizedRules;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RuleCode> GetMarkingRuleCodes()
    {
        return MarkingRuleCodes.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RuleCode> GetDecisionProtectionRuleCodes()
    {
        return DecisionProtectionRuleCodes.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<RuleCode> GetDefaultRuleCodes()
    {
        return MarkingRuleCodes
            .Concat(DecisionProtectionRuleCodes)
            .Distinct()
            .ToArray();
    }
}
