using System.Reflection;
using Rules;

namespace RoslynPrototype.Application;

public sealed record RuleRegistrySet(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers);

public static class RuleRegistry
{
    public static RuleRegistrySet CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
    {
        var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(RuleRegistry).Assembly;
        var ruleTypes = assembly
          .GetTypes()
          .Where(type => type.IsClass && !type.IsAbstract)
          .Where(type => type.Namespace == "Rules")
          .Where(type => !disabledTypeNames.Contains(type.Name))
          .ToList();

        return new RuleRegistrySet(
          Markers: CreateRules<RuleDefinitionMark>(ruleTypes),
          Propagators: CreateRules<RuleDefinitionPropagate>(ruleTypes),
          Lifters: CreateRules<RuleDefinitionLift>(ruleTypes),
          Proposers: CreateRules<RuleDefinitionPropose>(ruleTypes));
    }

    private static IReadOnlyList<TRule> CreateRules<TRule>(IReadOnlyList<Type> ruleTypes)
    {
        return ruleTypes
          .Where(type => typeof(TRule).IsAssignableFrom(type))
          .OrderBy(type => type.Name, StringComparer.Ordinal)
          .Select(type => (TRule)Activator.CreateInstance(type)!)
          .ToList();
    }
}
