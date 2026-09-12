using NLISSN.Core.Pipeline;
using NLISSN.Rules;

namespace NLISSN.Composition;

public static class RuleSelectionAdapter
{
    public static RuleSelection FromLegacySettings(
      IEnumerable<string>? disabledRuleTypes)
    {
        return FromLegacySettings(
          disabledRuleTypes,
          deleteUnreachableMethods: false,
          deleteUnreferencedMethods: false,
          clearUnusedInterfaceImplementations: false,
          privatizeInternalOnlyPublicMethods: false);
    }

    public static RuleSelection FromLegacySettings(
      IEnumerable<string>? disabledRuleTypes,
      bool deleteUnreachableMethods,
      bool deleteUnreferencedMethods,
      bool clearUnusedInterfaceImplementations,
      bool privatizeInternalOnlyPublicMethods)
    {
        var requestedFeatures = new List<RuleFeature>();
        if (deleteUnreachableMethods)
        {
            requestedFeatures.Add(RuleFeature.UnreachableMethodDeletion);
        }

        if (deleteUnreferencedMethods)
        {
            requestedFeatures.Add(RuleFeature.UnreferencedMethodDeletion);
        }

        if (clearUnusedInterfaceImplementations)
        {
            requestedFeatures.Add(RuleFeature.UnusedInterfaceImplementationCleanup);
        }

        if (privatizeInternalOnlyPublicMethods)
        {
            requestedFeatures.Add(RuleFeature.InternalOnlyPublicMethodPrivatization);
        }

        var disabledRuleIds = (disabledRuleTypes ?? Array.Empty<string>())
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .SelectMany(ResolveLegacyRuleName)
          .ToArray();
        return new RuleSelection(requestedFeatures, disabledRuleIds);
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

    private static IEnumerable<RuleCatalogEntry> GetAllDescriptors()
    {
        return GeneratedRuleCatalog.Markers.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName))
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.FullyQualifiedName)));
    }

    private sealed record RuleCatalogEntry(
      string RuleId,
      string TypeName,
      string FullyQualifiedName);
}
