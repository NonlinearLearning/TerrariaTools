using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public interface IDecisionBehaviorArbiter
{
  RuleDecision Resolve(
    DecisionContext context,
    DecisionCandidate candidate,
    IReadOnlyList<RuleDecision> proposals);
}
