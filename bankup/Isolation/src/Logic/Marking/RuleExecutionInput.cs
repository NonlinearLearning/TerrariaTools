using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;

namespace Logic.Marking;

/// <summary>
/// 表示规则执行输入。
/// </summary>
public sealed class RuleExecutionInput
{
    /// <summary>
    /// 获取或初始化规则集。
    /// </summary>
    public RuleSet RuleSet { get; init; } = null!;

    /// <summary>
    /// 获取或初始化规则目标。
    /// </summary>
    public RuleTarget RuleTarget { get; init; } = null!;

    /// <summary>
    /// 获取或初始化候选种类。
    /// </summary>
    public CandidateKind CandidateKind { get; init; }

    /// <summary>
    /// 获取或初始化场景标签集合。
    /// </summary>
    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; init; } = Array.Empty<ScenarioTag>();
}
