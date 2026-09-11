using NLISSN.Core.Pipeline;

namespace NLISSN.Composition;

public sealed record RuleSelection
{
    public RuleSelection(
      IEnumerable<RuleFeature>? requestedFeatures = null,
      IEnumerable<string>? disabledRuleIds = null)
    {
        RequestedFeatures = (requestedFeatures ?? Array.Empty<RuleFeature>()).ToHashSet();
        DisabledRuleIds = (disabledRuleIds ?? Array.Empty<string>())
          .Where(id => !string.IsNullOrWhiteSpace(id))
          .ToArray();
    }

    public IReadOnlySet<RuleFeature> RequestedFeatures { get; }

    public IReadOnlyList<string> DisabledRuleIds { get; }
}

public sealed record RuleSelectionWarning(string RuleId, string Message);

public sealed record RuleCompositionResult(
  NLISSN.Application.RulePipeline Pipeline,
  IReadOnlyList<RuleSelectionWarning> Warnings);
