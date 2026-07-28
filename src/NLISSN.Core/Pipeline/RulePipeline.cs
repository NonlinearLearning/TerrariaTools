using NLCPG.Contracts;
using NLISSN.Rules;

namespace NLISSN.Application;

public sealed record  RulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers,
  bool EnableHelperReturnSlicePilot = false,
  bool EnableRuleGraphExecution = false,
  IReadOnlyList<RuleDefinitionMark>? DisabledMarkers = null,
  IReadOnlyList<RuleDefinitionPropagate>? DisabledPropagators = null,
  IReadOnlyList<RuleDefinitionLift>? DisabledLifters = null,
  IReadOnlyList<RuleDefinitionPropose>? DisabledProposers = null)
{
  public CompiledRuleGraph CompileRuleGraph()
  {
    var declaredNodes = Markers
      .Select(rule => ToNode(rule, RuleKind.Mark))
      .Concat(Propagators.Select(rule => ToNode(rule, RuleKind.Propagate)))
      .Concat(Lifters.Select(rule => ToNode(rule, RuleKind.Lift)))
      .Concat(Proposers.Select(rule => ToNode(rule, RuleKind.Propose)))
      .Concat((DisabledMarkers ?? Array.Empty<RuleDefinitionMark>()).Select(rule => ToNode(rule, RuleKind.Mark)))
      .Concat((DisabledPropagators ?? Array.Empty<RuleDefinitionPropagate>()).Select(rule => ToNode(rule, RuleKind.Propagate)))
      .Concat((DisabledLifters ?? Array.Empty<RuleDefinitionLift>()).Select(rule => ToNode(rule, RuleKind.Lift)))
      .Concat((DisabledProposers ?? Array.Empty<RuleDefinitionPropose>()).Select(rule => ToNode(rule, RuleKind.Propose)))
      .ToList();
    return new RuleGraphCompiler().Compile(declaredNodes);
  }

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

  private static RuleGraphNode ToNode(IRuleDefinition rule, RuleKind kind)
  {
    return new RuleGraphNode(
      RuleNodeId.For(kind, rule.RuleId),
      kind,
      rule.ProducedOutputs,
      RuleGraphDependencyCatalog.GetDependencies(rule, kind, rule.Dependencies));
  }
}
