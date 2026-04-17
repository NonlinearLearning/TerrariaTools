using Domain.Analysis;
using Domain.Common;

namespace Domain.Marking;

/// <summary>
/// 表示规则命中目标。
/// </summary>
public sealed class RuleTarget : AggregateRoot<Guid>
{
    private RuleTarget(
        Guid id,
        Guid snapshotId,
        string ruleCode,
        MinimumNode node,
        CandidateReason candidateReason,
        string? note)
        : base(id)
    {
        SnapshotId = snapshotId;
        RuleCode = ruleCode;
        Node = node;
        CandidateReason = candidateReason;
        Note = note?.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 获取快照标识。
    /// </summary>
    public Guid SnapshotId { get; }

    /// <summary>
    /// 获取规则编号。
    /// </summary>
    public string RuleCode { get; }

    /// <summary>
    /// 获取最小节点。
    /// </summary>
    public MinimumNode Node { get; }

    /// <summary>
    /// 获取候选原因。
    /// </summary>
    public CandidateReason CandidateReason { get; private set; }

    /// <summary>
    /// 获取补充说明。
    /// </summary>
    public string? Note { get; private set; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 创建规则命中目标。
    /// </summary>
    public static RuleTarget Create(
        Guid snapshotId,
        string ruleCode,
        MinimumNode node,
        CandidateReason candidateReason,
        string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        ArgumentNullException.ThrowIfNull(node);
        return new RuleTarget(Guid.NewGuid(), snapshotId, ruleCode.Trim(), node, candidateReason, note);
    }

    /// <summary>
    /// 更新候选原因和说明。
    /// </summary>
    public void Revise(CandidateReason candidateReason, string? note)
    {
        CandidateReason = candidateReason;
        Note = note?.Trim();
    }
}
