namespace Application.Contracts;

public enum ContractAuditConclusion
{
    Unknown = 0,
    ApprovedForExecution = 1,
    ApprovedForMerge = 2,
    ReferenceOnly = 3,
    RequiresManualReview = 4,
}
