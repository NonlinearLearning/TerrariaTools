using Domain.Common.Events;

namespace Domain.Decision.Events;

/// <summary>
/// 表示改写裁决已完成。
/// </summary>
public sealed class DecisionCompletedDomainEvent : DomainEventBase
{
    public DecisionCompletedDomainEvent(
        Guid decisionId,
        Guid correlationId,
        bool approved,
        int approvalCount,
        int rejectionCount,
        int protectionCount)
        : base(
            "DecisionCompleted",
            "RewriteDecision",
            decisionId,
            correlationId,
            null,
            $"裁决已完成：批准 {approvalCount}，拒绝 {rejectionCount}，保护 {protectionCount}。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(rejectionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(protectionCount);
        Approved = approved;
        ApprovalCount = approvalCount;
        RejectionCount = rejectionCount;
        ProtectionCount = protectionCount;
    }

    public bool Approved { get; }

    public int ApprovalCount { get; }

    public int RejectionCount { get; }

    public int ProtectionCount { get; }
}
