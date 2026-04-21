namespace Domain.Decision;

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
