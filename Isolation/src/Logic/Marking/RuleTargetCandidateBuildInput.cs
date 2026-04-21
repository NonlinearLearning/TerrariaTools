using Domain.Marking;
using Domain.Propagation;

namespace Logic.Marking;

/// <summary>
/// 表示规则目标候选构造输入。
/// </summary>
public sealed class RuleTargetCandidateBuildInput
{
    /// <summary>
    /// 获取或初始化规则集名称。
    /// </summary>
    public string RuleSetName { get; init; } = "marking-default";

    /// <summary>
    /// 获取或初始化规则目标。
    /// </summary>
    public RuleTarget RuleTarget { get; init; } = null!;

    /// <summary>
    /// 获取或初始化场景标签集合。
    /// </summary>
    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; init; } = Array.Empty<ScenarioTag>();
}
