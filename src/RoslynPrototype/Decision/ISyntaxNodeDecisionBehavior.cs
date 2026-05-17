using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public interface ISyntaxNodeDecisionBehavior
{
  int Priority { get; }

  bool CanHandle(DecisionContext context, DecisionCandidate candidate);

  RuleDecision Decide(DecisionContext context, DecisionCandidate candidate);
}
