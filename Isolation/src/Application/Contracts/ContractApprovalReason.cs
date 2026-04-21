namespace Application.Contracts;

public enum ContractApprovalReason
{
    Unknown = 0,
    StaticFactConfirmed = 1,
    PropagationBounded = 2,
    CoveredByParentDeletion = 3,
    ClosureIntegrityVerified = 4,
    ShadowBoundaryStable = 5,
}
