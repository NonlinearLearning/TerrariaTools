namespace Analysis.Semantic.Flows;

/// <summary>
/// 保存一组方法语义规则，并按方法完整名提供查询。
/// </summary>
public sealed class MethodFlowRuleSet
{
    private readonly Dictionary<string, List<MethodFlowRule>> rulesByMethodFullName =
        new(StringComparer.Ordinal);
    private readonly List<MethodFlowRule> regexRules = new();

    /// <summary>
    /// 获取所有正则匹配规则。
    /// </summary>
    public IReadOnlyList<MethodFlowRule> RegexRules => regexRules;

    /// <summary>
    /// 向规则集追加一条规则。
    /// </summary>
    /// <param name="rule">要追加的规则。</param>
    public void Add(MethodFlowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsRegex)
        {
            regexRules.Add(rule);
            return;
        }

        if (!rulesByMethodFullName.TryGetValue(rule.MethodFullName, out List<MethodFlowRule>? rules))
        {
            rules = new List<MethodFlowRule>();
            rulesByMethodFullName.Add(rule.MethodFullName, rules);
        }

        rules.Add(rule);
    }

    /// <summary>
    /// 获取某个方法对应的全部规则。
    /// </summary>
    /// <param name="methodFullName">方法完整名。</param>
    /// <returns>匹配的规则集合。</returns>
    public IReadOnlyList<MethodFlowRule> GetRules(string methodFullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodFullName);

        return rulesByMethodFullName.TryGetValue(methodFullName, out List<MethodFlowRule>? rules)
            ? rules
            : Array.Empty<MethodFlowRule>();
    }
}
