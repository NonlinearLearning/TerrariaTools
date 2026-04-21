using Domain.Marking;
using Domain.Propagation;

namespace Logic.Marking;

/// <summary>
/// 规则目标候选准备器。
/// </summary>
public sealed class RuleTargetMarkingPreparer : IRuleTargetMarkingPreparer
{
    private readonly IRuleTargetCandidateBuilder ruleTargetCandidateBuilder;

    public RuleTargetMarkingPreparer(IRuleTargetCandidateBuilder ruleTargetCandidateBuilder)
    {
        this.ruleTargetCandidateBuilder = ruleTargetCandidateBuilder;
    }

    public IReadOnlyCollection<ChangeCandidate> Prepare(RuleTarget ruleTarget)
    {
        RuleExecutionResult result = ruleTargetCandidateBuilder.Build(new RuleTargetCandidateBuildInput
        {
            RuleSetName = "marking-default",
            RuleTarget = ruleTarget,
            ScenarioTags = [ScenarioTag.PlanDrivenRewrite],
        });

        return result.Candidates;
    }
}
