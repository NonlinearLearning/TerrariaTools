using Rules;

namespace RoslynPrototype.Application;

public static class RuleRegistry
{
  public static DeletionRulePipeline CreateDefaultRules(IEnumerable<string>? disabledRuleTypes = null)
  {
    var disabledTypeNames = (disabledRuleTypes ?? Array.Empty<string>())
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var assembly = typeof(RuleImplementationAssemblyMarker).Assembly;
    var ruleTypes = assembly
      .GetTypes()
      .Where(type => type.IsClass && !type.IsAbstract)
      .Where(type => type.Namespace == "Rules")
      .Where(type => !disabledTypeNames.Contains(type.Name))
      .ToList();

    return new DeletionRulePipeline(
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
