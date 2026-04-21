namespace Application.Contracts;

public enum ContractRejectionReason
{
    Unknown = 0,
    ExternalContractDetected = 1,
    ExternalCallerDetected = 2,
    ClosureIntegrityBroken = 3,
    PropagationRiskTooHigh = 4,
    ManualReviewRequired = 5,
}
