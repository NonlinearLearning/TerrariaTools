namespace NLISSN.Rules;

/// 校验规则集配置的唯一性，并保留调用方给出的稳定规则集顺序。
public static class RuleCatalog
{
    public static void ValidateRules(IEnumerable<IRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var capabilityOwners = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.CapabilityId))
            {
                throw new InvalidOperationException(
                    $"Rule '{rule.GetType().FullName}' has a blank CapabilityId.");
            }

            if (!capabilityOwners.TryAdd(rule.CapabilityId, rule.GetType()))
            {
                throw new InvalidOperationException(
                    $"Duplicate CapabilityId '{rule.CapabilityId}' in rules " +
                    $"'{capabilityOwners[rule.CapabilityId].FullName}' and '{rule.GetType().FullName}'.");
            }
        }
    }
}
