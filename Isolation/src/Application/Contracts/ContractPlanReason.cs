namespace Application.Contracts;

public enum ContractPlanReason
{
    Unknown = 0,
    CandidateApproved = 1,
    LinkedActionDetected = 2,
    ParentCoverageResolved = 3,
    ClosureBoundaryRequired = 4,
    ShadowBoundaryRequired = 5,
}
