using Domain.Decision;

namespace Logic.Decision;

/// <summary>
/// 表示决策构造结果。
/// </summary>
public sealed class RewriteDecisionResolution
{
    /// <summary>
    /// 获取或初始化候选标识。
    /// </summary>
    public Guid CandidateId { get; init; }

    /// <summary>
    /// 获取或初始化改写决策。
    /// </summary>
    public RewriteDecision Decision { get; init; } = null!;

    /// <summary>
    /// 获取或初始化是否批准。
    /// </summary>
    public bool Approved { get; init; }
}
