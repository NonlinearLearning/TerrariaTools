namespace Domain.Execution;

/// <summary>
/// 表示计划冲突。
/// </summary>
public enum PlanConflict
{
    None = 0,
    DuplicateTarget = 1,
    OverlappingRange = 2,
    ParentCoverage = 3,
    MutuallyExclusiveAction = 4,
}
