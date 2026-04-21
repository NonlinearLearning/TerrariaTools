using Domain.Common;
using Domain.Decision.Events;
using Domain.Rules;
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
        bool exists = protections.Any(current =>
            current.CandidateId == decisionProtection.CandidateId &&
            current.RuleCode.Equals(decisionProtection.RuleCode));
        if (!exists)
        {
            protections.Add(decisionProtection);
        }
    }

    public void AddConflict(DecisionConflict decisionConflict)
    {
        ArgumentNullException.ThrowIfNull(decisionConflict);
        bool exists = conflicts.Any(current =>
            current.LeftCandidateId == decisionConflict.LeftCandidateId &&
            current.RightCandidateId == decisionConflict.RightCandidateId &&
            string.Equals(current.Description, decisionConflict.Description, StringComparison.Ordinal));
        if (!exists)
        {
            conflicts.Add(decisionConflict);
        }
    }

    public void ReviseConfidence(ConfidenceLevel confidenceLevel)
    {
        ConfidenceLevel = confidenceLevel;
    }

    public bool ApplyOutcome(RewriteDecisionOutcome outcome, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        ResetCandidateResolution(outcome.CandidateId);
        foreach (DecisionProtection decisionProtection in outcome.Protections)
        {
            AddProtection(decisionProtection);
        }

        foreach (DecisionConflict decisionConflict in outcome.Conflicts)
        {
            AddConflict(decisionConflict);
        }

        if (outcome.Approved)
        {
            Approve(outcome.CandidateId, outcome.ApprovalReason);
        }
        else
        {
            Reject(outcome.CandidateId, outcome.RejectionReason!.Value);
        }

        RecordCompletion(correlationId, outcome.Approved);
        return outcome.Approved;
    }

    private void ResetCandidateResolution(Guid candidateId)
    {
        approvals.Remove(candidateId);
        rejections.Remove(candidateId);
        protections.RemoveAll(item => item.CandidateId == candidateId);
        conflicts.RemoveAll(item => item.LeftCandidateId == candidateId || item.RightCandidateId == candidateId);
    }

    private void RecordCompletion(Guid correlationId, bool approved)
    {
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("DecisionCompleted", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new DecisionCompletedDomainEvent(
            Id,
            resolvedCorrelationId,
            approved,
            approvals.Count,
            rejections.Count,
            protections.Count));
    }
}

/// <summary>
/// 表示保护条件。
/// </summary>
public sealed class DecisionProtection
{
    public DecisionProtection(Guid candidateId, RuleCode ruleCode, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        CandidateId = candidateId;
        RuleCode = ruleCode;
        Description = description.Trim();
    }

    public Guid CandidateId { get; }

    public RuleCode RuleCode { get; }

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
