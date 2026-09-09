using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Composition;

/// <summary>
/// Composes the generated rules for the current rule-catalog framework.
/// </summary>
public static class RulePipelineComposer
{
    public static RuleCompositionResult Compose(RuleSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

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

        var markers = ComposeStage(
          GeneratedRuleCatalog.Markers,
          disabledIds,
          out var disabledMarkers);
        var propagators = ComposeStage(
          GeneratedRuleCatalog.Propagators,
          disabledIds,
          out var disabledPropagators);
        var lifters = ComposeStage(
          GeneratedRuleCatalog.Lifters,
          disabledIds,
          out var disabledLifters);
        var proposers = ComposeStage(
          GeneratedRuleCatalog.Proposers,
          disabledIds,
          out var disabledProposers);

        var allSelectedRules = markers.Cast<IRuleDefinition>()
          .Concat(propagators)
          .Concat(lifters)
          .Concat(proposers)
          .Concat(disabledMarkers)
          .Concat(disabledPropagators)
          .Concat(disabledLifters)
          .Concat(disabledProposers)
          .ToArray();
        RuleCatalog.ValidateRules(allSelectedRules);

        return new RuleCompositionResult(
          new RulePipeline(
            Markers: markers,
            Propagators: propagators,
            Lifters: lifters,
            Proposers: proposers,
            DisabledMarkers: disabledMarkers,
            DisabledPropagators: disabledPropagators,
            DisabledLifters: disabledLifters,
            DisabledProposers: disabledProposers),
          warnings);
    }

    private static IReadOnlyList<TStage> ComposeStage<TStage>(
      IEnumerable<RuleRegistration<TStage>> descriptors,
      IReadOnlySet<string> disabledIds,
      out IReadOnlyList<TStage> disabledRules)
      where TStage : class, IRuleDefinition
    {
        var active = new List<TStage>();
        var disabled = new List<TStage>();
        foreach (var descriptor in descriptors
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

    private static IEnumerable<RuleCatalogDescriptor> GetAllDescriptors()
    {
        return GeneratedRuleCatalog.Markers.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId))
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new RuleCatalogDescriptor(
            descriptor.RuleId)));
    }

    private sealed record RuleCatalogDescriptor(string RuleId);
}
