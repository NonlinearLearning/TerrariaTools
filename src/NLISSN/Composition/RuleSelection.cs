using NLISSN.Application;

namespace NLISSN.Composition;

/// <summary>
/// Immutable input to the compile-time generated rule catalog composer.
/// </summary>
public sealed record RuleSelection
{
    public RuleSelection(
      IEnumerable<string>? disabledRuleIds = null)
    {
        DisabledRuleIds = (disabledRuleIds ?? Array.Empty<string>())
          .Where(id => !string.IsNullOrWhiteSpace(id))
          .ToArray();
    }

    public IReadOnlyList<string> DisabledRuleIds { get; }
}

/// <summary>
/// A non-fatal issue encountered while interpreting a rule selection.
/// </summary>
public sealed record RuleSelectionWarning(string RuleId, string Message);

/// <summary>
/// The composed pipeline and non-fatal selection diagnostics.
/// </summary>
public sealed record RuleCompositionResult(
  RulePipeline Pipeline,
  IReadOnlyList<RuleSelectionWarning> Warnings);
