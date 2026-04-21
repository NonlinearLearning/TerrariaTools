namespace Domain.Decision;

/// <summary>
/// 表示已解释完成的决策结果。
/// </summary>
public sealed class RewriteDecisionOutcome
{
    private readonly IReadOnlyCollection<DecisionProtection> protections;
    private readonly IReadOnlyCollection<DecisionConflict> conflicts;

    public RewriteDecisionOutcome(
        Guid candidateId,
        ApprovalReason approvalReason,
        RejectionReason? rejectionReason,
        IReadOnlyCollection<DecisionProtection> protections,
        IReadOnlyCollection<DecisionConflict> conflicts)
    {
        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("候选标识不能为空。");
        }

        this.protections = protections ?? throw new ArgumentNullException(nameof(protections));
        this.conflicts = conflicts ?? throw new ArgumentNullException(nameof(conflicts));
        CandidateId = candidateId;
        ApprovalReason = approvalReason;
        RejectionReason = rejectionReason;
    }

    public Guid CandidateId { get; }

    public ApprovalReason ApprovalReason { get; }

    public RejectionReason? RejectionReason { get; }

    public bool Approved => RejectionReason is null;

    public IReadOnlyCollection<DecisionProtection> Protections => protections;

    public IReadOnlyCollection<DecisionConflict> Conflicts => conflicts;
}
