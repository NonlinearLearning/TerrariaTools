namespace Domain.Output.Audit;

/// <summary>
/// 表示审计结论。
/// </summary>
public enum AuditConclusion
{
    Unknown = 0,
    ApprovedForExecution = 1,
    ApprovedForMerge = 2,
    ReferenceOnly = 3,
    RequiresManualReview = 4,
}
