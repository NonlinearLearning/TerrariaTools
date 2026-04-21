using Domain.Marking;
using Domain.Propagation;

namespace Logic.Marking;

/// <summary>
/// 定义规则目标候选准备能力。
/// </summary>
public interface IRuleTargetMarkingPreparer
{
    IReadOnlyCollection<ChangeCandidate> Prepare(RuleTarget ruleTarget);
}
