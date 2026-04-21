using Domain.Marking;
using Domain.Propagation;
using Logic.Marking;
using Logic.Rules;

namespace Logic.Workflow;

public sealed class RewriteWorkflowMarkingPreparer : IRewriteWorkflowMarkingPreparer
{
    private readonly IRewriteWorkflowRulePreset rewriteWorkflowRulePreset;
    private readonly IRuleTargetCandidateBuilder ruleTargetCandidateBuilder;

    public RewriteWorkflowMarkingPreparer(
        IRewriteWorkflowRulePreset rewriteWorkflowRulePreset,
        IRuleTargetCandidateBuilder ruleTargetCandidateBuilder)
    {
        this.rewriteWorkflowRulePreset = rewriteWorkflowRulePreset;
        this.ruleTargetCandidateBuilder = ruleTargetCandidateBuilder;
    }

    public IReadOnlyCollection<ChangeCandidate> Prepare(RuleTarget ruleTarget)
    {
        ArgumentNullException.ThrowIfNull(ruleTarget);

        rewriteWorkflowRulePreset.ResolveMarkingRuleCode(null, ruleTarget.RuleCode);

        RuleExecutionResult result = ruleTargetCandidateBuilder.Build(new RuleTargetCandidateBuildInput
        {
            RuleSetName = rewriteWorkflowRulePreset.GetMarkingRuleSetName(),
            RuleTarget = ruleTarget,
            ScenarioTags = [ScenarioTag.PlanDrivenRewrite],
        });

        return result.Candidates;
    }
}
