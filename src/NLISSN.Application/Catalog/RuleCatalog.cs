namespace NLISSN.Rules;

/// 校验规则集配置的唯一性，并保留调用方给出的稳定规则集顺序。
public static class RuleCatalog
{
    public static void ValidateRules(IEnumerable<IRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var ruleOwners = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId))
            {
                throw new InvalidOperationException(
                    $"Rule '{rule.GetType().FullName}' has a blank RuleId.");
            }

            if (!ruleOwners.TryAdd(rule.RuleId, rule.GetType()))
            {
                throw new InvalidOperationException(
                    $"Duplicate RuleId '{rule.RuleId}' in rules " +
                    $"'{ruleOwners[rule.RuleId].FullName}' and '{rule.GetType().FullName}'.");
            }
        }
    }
}
