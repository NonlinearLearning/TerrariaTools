using Domain.Analysis;
using Domain.Marking;
using Domain.Rules;

namespace Logic.Marking;

/// <summary>
/// 表示规则目标构造输入。
/// </summary>
public sealed class RuleTargetBuildInput
{
    /// <summary>
    /// 获取或初始化快照标识。
    /// </summary>
    public Guid SnapshotId { get; init; }

    /// <summary>
    /// 获取或初始化规则编码。
    /// </summary>
    public RuleCode RuleCode { get; init; } = RuleCode.Create("unknown");

    /// <summary>
    /// 获取或初始化最小节点。
    /// </summary>
    public MinimumNode Node { get; init; } = new(
        string.Empty,
        string.Empty,
        CpgType.Unknown,
        new LocationRange(string.Empty, 0, 0, 0, 0));

    /// <summary>
    /// 获取或初始化候选原因。
    /// </summary>
    public CandidateReason CandidateReason { get; init; } = CandidateReason.Unknown;

    /// <summary>
    /// 获取或初始化说明。
    /// </summary>
    public string? Note { get; init; }
}
