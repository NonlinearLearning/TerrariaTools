using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Composition;

public static class RulePipelineComposer
{
    public static RuleCompositionResult Compose(RuleSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var enabledFeatures = selection.RequestedFeatures.ToHashSet();
        enabledFeatures.Add(RuleFeature.Core);

        var allRuleIds = GetAllDescriptors()
          .Select(descriptor => descriptor.RuleId)
          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedDisabledIds = selection.DisabledRuleIds
          .Where(id => !string.IsNullOrWhiteSpace(id))
          .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
          .Select(group => group.First())
          .ToArray();
        var disabledIds = requestedDisabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warnings = requestedDisabledIds
          .Where(id => !allRuleIds.Contains(id))
          .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
          .ThenBy(id => id, StringComparer.Ordinal)
          .Select(id => new RuleSelectionWarning(
            id,
            $"Unknown disabled RuleId '{id}' was ignored."))
          .ToArray();

        var markers = ComposeStage(GeneratedRuleCatalog.Markers, enabledFeatures, disabledIds, out var disabledMarkers);
        var propagators = ComposeStage(GeneratedRuleCatalog.Propagators, enabledFeatures, disabledIds, out var disabledPropagators);
        var lifters = ComposeStage(GeneratedRuleCatalog.Lifters, enabledFeatures, disabledIds, out var disabledLifters);
        var proposers = ComposeStage(GeneratedRuleCatalog.Proposers, enabledFeatures, disabledIds, out var disabledProposers);

        var selectedRules = markers.Cast<IRuleDefinition>()
          .Concat(propagators)
          .Concat(lifters)
          .Concat(proposers)
          .Concat(disabledMarkers)
          .Concat(disabledPropagators)
          .Concat(disabledLifters)
          .Concat(disabledProposers)
          .ToArray();
        RuleCatalog.ValidateRules(selectedRules);

        return new RuleCompositionResult(
          new RulePipeline(
            markers,
            propagators,
            lifters,
            proposers,
            disabledMarkers,
            disabledPropagators,
            disabledLifters,
            disabledProposers),
          warnings);
    }

    private static IReadOnlyList<TStage> ComposeStage<TStage>(
      IReadOnlyList<RuleRegistration<TStage>> descriptors,
      IReadOnlySet<RuleFeature> enabledFeatures,
      IReadOnlySet<string> disabledIds,
      out IReadOnlyList<TStage> disabledRules)
      where TStage : class, IRuleDefinition
    {
        var active = new List<TStage>();
        var disabled = new List<TStage>();
        foreach (var descriptor in descriptors
          .Where(descriptor => enabledFeatures.Contains(descriptor.Feature))
          .OrderBy(descriptor => descriptor.TypeName, StringComparer.Ordinal)
          .ThenBy(descriptor => descriptor.FullyQualifiedName, StringComparer.Ordinal))
        {
            var rule = descriptor.Factory();
            if (disabledIds.Contains(descriptor.RuleId))
            {
                disabled.Add(rule);
            }
            else
            {
                active.Add(rule);
            }
        }

        disabledRules = disabled;
        return active;
    }

    private static IEnumerable<RuleCatalogEntry> GetAllDescriptors()
    {
        return GeneratedRuleCatalog.Markers.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.Feature))
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.Feature)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.Feature)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new RuleCatalogEntry(
            descriptor.RuleId,
            descriptor.Feature)));
    }

    private sealed record RuleCatalogEntry(string RuleId, RuleFeature Feature);
}
