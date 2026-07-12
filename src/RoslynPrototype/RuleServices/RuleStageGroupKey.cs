using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

internal static class RuleStageGroupKey
{
  internal static string Get(MarkRecord mark)
  {
    return mark.GroupKey ?? mark.RuleId;
  }

  internal static string Get(PropagatedMarkRecord propagatedMark)
  {
    return propagatedMark.GroupKey ??
      propagatedMark.Mark.GroupKey ??
      propagatedMark.SourceMark.GroupKey ??
      propagatedMark.RuleId;
  }

  internal static string Get(LiftedMarkRecord liftedMark)
  {
    return liftedMark.GroupKey ?? liftedMark.RuleId;
  }

  internal static string Get(DecisionUnit unit)
  {
    return unit.GroupKey ?? unit.RuleId;
  }
}
