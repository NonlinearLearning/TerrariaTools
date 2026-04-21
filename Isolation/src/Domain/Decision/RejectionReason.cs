namespace Domain.Decision;

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
