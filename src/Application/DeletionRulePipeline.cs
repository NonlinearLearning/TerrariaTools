using Rules;
using MinimalRoslynCpg.Contracts;

namespace RoslynPrototype.Application;

public sealed record DeletionRulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers)
{
  public IReadOnlyList<RoslynCpgCapability> GetRequiredCapabilities()
  {
    return Markers.SelectMany(rule => rule.RequiredCapabilities)
      .Concat(Propagators.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Lifters.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Proposers.SelectMany(rule => rule.RequiredCapabilities))
      .Distinct()
      .OrderBy(capability => capability)
      .ToList();
  }
}
