using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public interface IRulePropagator
{
  IEnumerable<PropagatedMarkRecord> Propagate(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks);
}
