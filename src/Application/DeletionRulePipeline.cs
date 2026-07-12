using Rules;

namespace RoslynPrototype.Application;

public sealed record DeletionRulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers);
