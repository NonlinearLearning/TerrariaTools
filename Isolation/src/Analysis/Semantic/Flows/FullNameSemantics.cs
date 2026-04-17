using Analysis.Core;
using Analysis.Semantic;
using System.Text.RegularExpressions;

namespace Analysis.Semantic.Flows;

/// <summary>
/// 基于方法完整名匹配语义规则。
///
/// 这个类型对应 Joern `FullNameSemantics.scala`：
/// - 精确匹配方法完整名；
/// - 支持正则规则；
/// - 同名规则自动合并。
/// </summary>
public sealed class FullNameSemantics : ISemantics
{
    private readonly MethodFlowRuleSet ruleSet;

    /// <summary>
    /// 使用规则集初始化完整名语义。
    /// </summary>
    /// <param name="ruleSet">规则集。</param>
    public FullNameSemantics(MethodFlowRuleSet ruleSet)
    {
        this.ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
    }

    /// <summary>
    /// 创建空语义。
    /// </summary>
    /// <returns>空完整名语义。</returns>
    public static FullNameSemantics Empty()
    {
        return new FullNameSemantics(new MethodFlowRuleSet());
    }

    /// <summary>
    /// 从规则集合创建完整名语义。
    /// </summary>
    /// <param name="rules">规则集合。</param>
    /// <returns>完整名语义。</returns>
    public static FullNameSemantics FromRules(IEnumerable<MethodFlowRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        MethodFlowRuleSet ruleSet = new();
        foreach (MethodFlowRule rule in rules)
        {
            ruleSet.Add(rule);
        }

        return new FullNameSemantics(ruleSet);
    }

    /// <inheritdoc />
    public IReadOnlyList<MethodFlowRule> ForMethod(CpgNode methodNode)
    {
        ArgumentNullException.ThrowIfNull(methodNode);

        string fullName = methodNode.PropertyAsString("FullName");
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Array.Empty<MethodFlowRule>();
        }

        List<MethodFlowRule> exactRules = ruleSet.GetRules(fullName).ToList();
        exactRules.AddRange(ruleSet.RegexRules.Where(rule => Regex.IsMatch(fullName, rule.MethodFullName)));
        return exactRules;
    }
}
