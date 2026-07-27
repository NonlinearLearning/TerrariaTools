using NLCPG.Contracts;
using NLISSN.Rules;

namespace NLISSN.Application;

public sealed record  RulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers,
  bool EnableHelperReturnSlicePilot = false)
{
  // 汇总四个阶段所有规则声明的能力需求，并按试验开关补充额外查询能力。
  public IReadOnlyList<NLCPGCapability> GetRequiredCapabilities()
  {
    var requiredCapabilities = Markers.SelectMany(rule => rule.RequiredCapabilities)
      .Concat(Propagators.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Lifters.SelectMany(rule => rule.RequiredCapabilities))
      .Concat(Proposers.SelectMany(rule => rule.RequiredCapabilities))
      .ToList();
    if (EnableHelperReturnSlicePilot && Propagators.Any(rule => string.Equals(
          rule.GetType().Name,
          "ClassSymbolReferencePropagationRule",
          StringComparison.Ordinal)))
    {
      requiredCapabilities.Add(NLCPGCapability.InterproceduralDataFlow);
    }

    return requiredCapabilities
      .Distinct()
      .OrderBy(capability => capability)
      .ToList();
  }
}
