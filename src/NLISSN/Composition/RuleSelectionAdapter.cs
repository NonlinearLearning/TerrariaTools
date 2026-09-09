using NLISSN.Rules;

namespace NLISSN.Composition;

/// <summary>
/// Translates the legacy configuration shape into the RuleSelection contract.
/// </summary>
public static class RuleSelectionAdapter
{
    public static RuleSelection FromLegacySettings(
      IEnumerable<string>? disabledRuleTypes)
    {
        var disabledRuleIds = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .SelectMany(ResolveLegacyRuleName)
          .ToArray();
        return new RuleSelection(disabledRuleIds);
    }

    private static IEnumerable<string> ResolveLegacyRuleName(string value)
    {
        var matches = GetAllDescriptors()
          .Where(descriptor =>
            string.Equals(descriptor.RuleId, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(descriptor.TypeName, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(descriptor.FullyQualifiedName, value, StringComparison.OrdinalIgnoreCase))
          .Select(descriptor => descriptor.RuleId)
          .Distinct(StringComparer.Ordinal)
          .OrderBy(ruleId => ruleId, StringComparer.Ordinal)
          .ToArray();
        return matches.Length == 0 ? new[] { value } : matches;
    }

    private static IEnumerable<RuleCatalogDescriptor> GetAllDescriptors()
    {
        return GeneratedRuleCatalog.Markers.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName))
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)));
    }

    private sealed record RuleCatalogDescriptor(
      string RuleId,
      string TypeName,
      string FullyQualifiedName);
}
