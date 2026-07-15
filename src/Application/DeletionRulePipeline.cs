using MinimalRoslynCpg.Contracts;
using Rules;

namespace RoslynPrototype.Application;

public sealed record DeletionRulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers,
  bool EnableHelperReturnSlicePilot = false)
{
  public IReadOnlyList<RoslynCpgCapability> GetRequiredCapabilities()
  {
    var requiredCapabilities = Markers.SelectMany(rule => rule.RequiredCapabilities)
      .Concat(Propagators.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Lifters.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Proposers.SelectMany(rule => rule.RequiredCapabilities))
      .ToList();
    if (EnableHelperReturnSlicePilot && Propagators.Any(rule => string.Equals(
          rule.GetType().Name,
          "DeleteClassSymbolReferencePropagationRule",
          StringComparison.Ordinal)))
    {
      requiredCapabilities.Add(RoslynCpgCapability.InterproceduralDataFlow);
    }

    return requiredCapabilities
      .Distinct()
      .OrderBy(capability => capability)
      .ToList();
  }
}
