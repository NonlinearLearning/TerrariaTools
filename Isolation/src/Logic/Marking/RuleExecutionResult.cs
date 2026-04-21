using Domain.Marking;
using Domain.Propagation;

namespace Logic.Marking;

/// <summary>
/// 表示规则执行结果。
/// </summary>
public sealed class RuleExecutionResult
{
    /// <summary>
    /// 获取或初始化规则目标。
    /// </summary>
    public RuleTarget RuleTarget { get; init; } = null!;

    /// <summary>
    /// 获取或初始化候选集合。
    /// </summary>
    public IReadOnlyCollection<ChangeCandidate> Candidates { get; init; } = Array.Empty<ChangeCandidate>();
}
