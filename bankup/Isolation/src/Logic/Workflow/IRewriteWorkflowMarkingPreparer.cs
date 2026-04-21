using Domain.Marking;
using Domain.Propagation;

namespace Logic.Workflow;

public interface IRewriteWorkflowMarkingPreparer
{
    IReadOnlyCollection<ChangeCandidate> Prepare(RuleTarget ruleTarget);
}
