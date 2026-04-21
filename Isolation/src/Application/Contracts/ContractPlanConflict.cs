namespace Application.Contracts;

public enum ContractPlanConflict
{
    None = 0,
    DuplicateTarget = 1,
    OverlappingRange = 2,
    ParentCoverage = 3,
    MutuallyExclusiveAction = 4,
}
