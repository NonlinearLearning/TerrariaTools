namespace NLISSN.Core.Decision;

/// <summary>
/// The only decision representation that is allowed to cross into Rewrite.
/// Instances are created by PlanValidator after the complete relation graph
/// and every selected candidate have been checked.
/// </summary>
public sealed class ExecutablePlan
{
  internal ExecutablePlan(
    DecisionPlan decisionPlan,
    IReadOnlyList<RuleDecision> decisions)
  {
    DecisionPlan = decisionPlan ?? throw new ArgumentNullException(nameof(decisionPlan));
    Decisions = (decisions ?? throw new ArgumentNullException(nameof(decisions))).ToArray();
  }

  public DecisionPlan DecisionPlan { get; }

  public IReadOnlyList<RuleDecision> Decisions { get; }

  public bool IsEmpty => Decisions.Count == 0;
}
