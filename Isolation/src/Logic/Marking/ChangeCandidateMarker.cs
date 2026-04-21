using Domain.Propagation;

namespace Logic.Marking;

/// <summary>
/// 基于规则命中目标生成候选。
/// </summary>
public sealed class ChangeCandidateMarker : IChangeCandidateMarker
{

    public RuleExecutionResult Execute(RuleExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RuleSet);
        ArgumentNullException.ThrowIfNull(input.RuleTarget);

        ScenarioTag scenarioTag = input.ScenarioTags.FirstOrDefault();
        if (scenarioTag == ScenarioTag.Unknown)
        {
            scenarioTag = ScenarioTag.PlanDrivenRewrite;
        }

        ChangeCandidate candidate = ChangeCandidate.Create(
            input.RuleTarget.Id,
            input.RuleTarget.RuleCode,
            input.RuleTarget.Node.DisplayName,
            input.CandidateKind,
            input.RuleTarget.CandidateReason,
            scenarioTag);

        foreach (ScenarioTag current in input.ScenarioTags.Skip(1))
        {
            candidate.AddScenarioTag(current);
        }

        return new RuleExecutionResult
        {
            RuleTarget = input.RuleTarget,
            Candidates = [candidate],
        };
    }
}
