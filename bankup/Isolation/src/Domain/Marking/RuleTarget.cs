using Domain.Analysis;
using Domain.Common;
using Domain.Marking.Events;
using Domain.Rules;

namespace Domain.Marking;

/// <summary>
/// 表示规则命中目标。
/// </summary>
public sealed class RuleTarget : AggregateRoot<Guid>
{
    private bool isConfirmed;
    private bool isLocked;

    private RuleTarget(
        Guid id,
        Guid snapshotId,
        RuleCode ruleCode,
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
    public RuleCode RuleCode { get; }

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

    public bool IsConfirmed => isConfirmed;

    /// <summary>
    /// 创建规则命中目标。
    /// </summary>
    public static RuleTarget Create(
        Guid snapshotId,
        RuleCode ruleCode,
        MinimumNode node,
        CandidateReason candidateReason,
        string? note)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new RuleTarget(Guid.NewGuid(), snapshotId, ruleCode, node, candidateReason, note);
    }

    /// <summary>
    /// 更新候选原因和说明。
    /// </summary>
    public void Revise(CandidateReason candidateReason, string? note)
    {
        EnsureUnlocked();
        CandidateReason = candidateReason;
        Note = note?.Trim();
    }

    public void Confirm(Guid correlationId)
    {
        isConfirmed = true;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("RuleTargetIdentified", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new RuleTargetIdentifiedDomainEvent(
            Id,
            resolvedCorrelationId,
            RuleCode.Value,
            Node.DisplayName));
    }

    public void AttachNote(string? note)
    {
        EnsureUnlocked();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public void EscalateCandidateReason(CandidateReason nextReason)
    {
        EnsureUnlocked();
        if ((int)nextReason < (int)CandidateReason)
        {
            throw new InvalidOperationException("候选原因只能升级，不能降级。");
        }

        CandidateReason = nextReason;
    }

    public void Lock()
    {
        isLocked = true;
    }

    public void EnsureUnlocked()
    {
        if (isLocked)
        {
            throw new InvalidOperationException("规则命中目标已锁定，不能继续修改。");
        }
    }
}
