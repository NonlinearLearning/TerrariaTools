namespace NLISSN.Rules;

/// 校验规则集配置的唯一性，并保留调用方给出的稳定规则集顺序。
public static class RuleCatalog
{
    // 校验 RuleSet Id 与 CapabilityId 没有冲突，并保留调用方给出的规则集顺序。
    public static IReadOnlyList<IRuleSet> Create(IEnumerable<IRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);

        var configuredRuleSets = ruleSets
            .Where(ruleSet => ruleSet is not null)
            .ToList();
        var ruleSetIds = new HashSet<string>(StringComparer.Ordinal);
        var capabilityOwners = new Dictionary<string, (string RuleSetId, Type Type)>(StringComparer.Ordinal);

        foreach (var ruleSet in configuredRuleSets)
        {
            if (string.IsNullOrWhiteSpace(ruleSet.Id))
            {
                throw new InvalidOperationException("RuleSet Id must not be blank.");
            }

            if (!ruleSetIds.Add(ruleSet.Id))
            {
                throw new InvalidOperationException($"Duplicate RuleSet Id '{ruleSet.Id}'.");
            }

            foreach (var rule in EnumerateRules(ruleSet))
            {
                if (string.IsNullOrWhiteSpace(rule.CapabilityId))
                {
                    throw new InvalidOperationException(
                        $"Rule '{rule.GetType().FullName}' in RuleSet '{ruleSet.Id}' has a blank CapabilityId.");
                }

                if (!capabilityOwners.TryAdd(
                        rule.CapabilityId,
                        (ruleSet.Id, rule.GetType())))
                {
                    var existing = capabilityOwners[rule.CapabilityId];
                    throw new InvalidOperationException(
                        $"Duplicate CapabilityId '{rule.CapabilityId}' in RuleSets " +
                        $"'{existing.RuleSetId}' ({existing.Type.FullName}) and " +
                        $"'{ruleSet.Id}' ({rule.GetType().FullName}).");
                }
            }
        }

        return configuredRuleSets;
    }

    private static IEnumerable<IRuleDefinition> EnumerateRules(IRuleSet ruleSet)
    {
        return ruleSet.Markers
            .Cast<IRuleDefinition>()
            .Concat(ruleSet.Propagators)
            .Concat(ruleSet.Lifters)
            .Concat(ruleSet.Proposers);
    }
}
