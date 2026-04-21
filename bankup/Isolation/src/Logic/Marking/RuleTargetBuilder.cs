using Domain.Marking;

namespace Logic.Marking;

/// <summary>
/// 规则目标构造器。
/// </summary>
public sealed class RuleTargetBuilder : IRuleTargetBuilder
{

    public RuleTarget Build(RuleTargetBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return RuleTarget.Create(
            input.SnapshotId,
            input.RuleCode,
            input.Node,
            input.CandidateReason,
            input.Note);
    }
}
