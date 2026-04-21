namespace Domain.Decision;

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
