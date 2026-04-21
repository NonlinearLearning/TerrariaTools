namespace Domain.Execution;

/// <summary>
/// 表示计划原因。
/// </summary>
public enum PlanReason
{
    Unknown = 0,
    CandidateApproved = 1,
    LinkedActionDetected = 2,
    ParentCoverageResolved = 3,
    ClosureBoundaryRequired = 4,
    ShadowBoundaryRequired = 5,
}
