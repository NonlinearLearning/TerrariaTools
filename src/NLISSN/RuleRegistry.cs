using NLISSN.Application;
using NLISSN.Rules;

namespace NLISSN;

/// 从默认规则集或调用方提供的规则集构建确定性的规则管道。
public static class RuleRegistry
{
    // 汇总整条规则管道声明的 CPG 能力需求，供调用方决定要构哪些图能力。
    public static IReadOnlyList<NLCPG.Contracts.NLCPGCapability> GetRequiredCapabilities(RulePipeline pipeline)
    {
        return pipeline.GetRequiredCapabilities();
    }

    // 基于默认规则集创建稳定排序后的规则管道，并按名称排除禁用规则。
    public static RulePipeline CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
    {
        return CreateRules(DefaultRuleSets.Create(), disabledRuleTypes);
    }

    // 把调用方给出的规则集展平成四个阶段的规则列表，并应用禁用名单过滤。
    public static RulePipeline CreateRules(IEnumerable<IRuleSet> ruleSets, IEnumerable<string>? disabledRuleTypes = null)
    {
        var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configuredRuleSets = RuleCatalog.Create(ruleSets);
        var markers = configuredRuleSets.SelectMany(ruleSet => ruleSet.Markers).ToList();
        var propagators = configuredRuleSets.SelectMany(ruleSet => ruleSet.Propagators).ToList();
        var lifters = configuredRuleSets.SelectMany(ruleSet => ruleSet.Lifters).ToList();
        var proposers = configuredRuleSets.SelectMany(ruleSet => ruleSet.Proposers).ToList();

        return new RulePipeline(
          Markers: CreateRules(markers, disabledTypeNames),
          Propagators: CreateRules(propagators, disabledTypeNames),
          Lifters: CreateRules(lifters, disabledTypeNames),
          Proposers: CreateRules(proposers, disabledTypeNames),
          EnableRuleGraphExecution: true,
          DisabledMarkers: FindDisabled(markers, disabledTypeNames),
          DisabledPropagators: FindDisabled(propagators, disabledTypeNames),
          DisabledLifters: FindDisabled(lifters, disabledTypeNames),
          DisabledProposers: FindDisabled(proposers, disabledTypeNames));
    }

    private static IReadOnlyList<TRule> CreateRules<TRule>(IEnumerable<TRule> rules, IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => !disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }

    private static IReadOnlyList<TRule> FindDisabled<TRule>(
      IEnumerable<TRule> rules,
      IReadOnlySet<string> disabledTypeNames)
      where TRule : class
    {
        return rules
          .Where(rule => disabledTypeNames.Contains(rule.GetType().Name))
          .OrderBy(rule => rule.GetType().Name, StringComparer.Ordinal)
          .ToList();
    }
}
