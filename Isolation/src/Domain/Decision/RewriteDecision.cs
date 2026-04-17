using Domain.Common;

namespace Domain.Decision;

/// <summary>
/// 表示改写决策聚合根。
/// </summary>
public sealed class RewriteDecision : AggregateRoot<Guid>
{
    private readonly Dictionary<Guid, ApprovalReason> approvals = new();
    private readonly Dictionary<Guid, RejectionReason> rejections = new();
    private readonly List<DecisionProtection> protections = new();
    private readonly List<DecisionConflict> conflicts = new();

    private RewriteDecision(Guid id, string decisionName, ConfidenceLevel confidenceLevel)
        : base(id)
    {
        DecisionName = decisionName;
        ConfidenceLevel = confidenceLevel;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string DecisionName { get; }

    public ConfidenceLevel ConfidenceLevel { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyDictionary<Guid, ApprovalReason> Approvals => approvals;

    public IReadOnlyDictionary<Guid, RejectionReason> Rejections => rejections;

    public IReadOnlyCollection<DecisionProtection> Protections => protections.AsReadOnly();

    public IReadOnlyCollection<DecisionConflict> Conflicts => conflicts.AsReadOnly();

    public static RewriteDecision Create(string decisionName, ConfidenceLevel confidenceLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionName);
        return new RewriteDecision(Guid.NewGuid(), decisionName.Trim(), confidenceLevel);
    }

    public void Approve(Guid candidateId, ApprovalReason approvalReason)
    {
        approvals[candidateId] = approvalReason;
        rejections.Remove(candidateId);
    }

    public void Reject(Guid candidateId, RejectionReason rejectionReason)
    {
        rejections[candidateId] = rejectionReason;
        approvals.Remove(candidateId);
    }

    public void AddProtection(DecisionProtection decisionProtection)
    {
        ArgumentNullException.ThrowIfNull(decisionProtection);
        protections.Add(decisionProtection);
    }

    public void AddConflict(DecisionConflict decisionConflict)
    {
        ArgumentNullException.ThrowIfNull(decisionConflict);
        conflicts.Add(decisionConflict);
    }

    public void ReviseConfidence(ConfidenceLevel confidenceLevel)
    {
        ConfidenceLevel = confidenceLevel;
    }
}

/// <summary>
/// 表示保护条件。
/// </summary>
public sealed class DecisionProtection
{
    public DecisionProtection(Guid candidateId, string ruleCode, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        CandidateId = candidateId;
        RuleCode = ruleCode.Trim();
        Description = description.Trim();
    }

    public Guid CandidateId { get; }

    public string RuleCode { get; }

    public string Description { get; }
}

/// <summary>
/// 表示决策冲突。
/// </summary>
public sealed class DecisionConflict
{
    public DecisionConflict(Guid leftCandidateId, Guid rightCandidateId, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        LeftCandidateId = leftCandidateId;
        RightCandidateId = rightCandidateId;
        Description = description.Trim();
    }

    public Guid LeftCandidateId { get; }

    public Guid RightCandidateId { get; }

    public string Description { get; }
}

/// <summary>
/// 表示批准原因。
/// </summary>
public enum ApprovalReason
{
    Unknown = 0,
    StaticFactConfirmed = 1,
    PropagationBounded = 2,
    CoveredByParentDeletion = 3,
    ClosureIntegrityVerified = 4,
    ShadowBoundaryStable = 5,
}

/// <summary>
/// 表示拒绝原因。
/// </summary>
public enum RejectionReason
{
    Unknown = 0,
    ExternalContractDetected = 1,
    ExternalCallerDetected = 2,
    ClosureIntegrityBroken = 3,
    PropagationRiskTooHigh = 4,
    ManualReviewRequired = 5,
}

/// <summary>
/// 表示置信等级。
/// </summary>
public enum ConfidenceLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
