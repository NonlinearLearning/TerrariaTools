using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public interface IPropagationEngine
{
  IReadOnlyList<PropagatedMarkRecord> Run(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<IDeletionRule> rules);
}
