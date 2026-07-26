using Deletion.Application;
using Deletion.Rules;

namespace Deletion.Cli;

public static class RuleRegistry
{
  public static IReadOnlyList<MinimalRoslynCpg.Contracts.RoslynCpgCapability> GetRequiredCapabilities(
    DeletionRulePipeline pipeline)
  {
    return pipeline.GetRequiredCapabilities();
  }

  public static DeletionRulePipeline CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
  {
    return CreateRules(DefaultRuleSets.Create(), disabledRuleTypes);
  }

  public static DeletionRulePipeline CreateRules(
    IEnumerable<IRuleSet> ruleSets,
    IEnumerable<string>? disabledRuleTypes = null)
  {
    var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var configuredRuleSets = RuleCatalog.Create(ruleSets);

    return new DeletionRulePipeline(
      Markers: CreateRules(configuredRuleSets.SelectMany(ruleSet => ruleSet.Markers), disabledTypeNames),
      Propagators: CreateRules(configuredRuleSets.SelectMany(ruleSet => ruleSet.Propagators), disabledTypeNames),
      Lifters: CreateRules(configuredRuleSets.SelectMany(ruleSet => ruleSet.Lifters), disabledTypeNames),
      Proposers: CreateRules(configuredRuleSets.SelectMany(ruleSet => ruleSet.Proposers), disabledTypeNames));
  }

  private static IReadOnlyList<TRule> CreateRules<TRule>(
    IEnumerable<TRule> rules,
    IReadOnlySet<string> disabledTypeNames)
    where TRule : class
  {
    return rules
      .Where(rule => !disabledTypeNames.Contains(rule.GetType().Name))
      .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
      .ToList();
  }
}
