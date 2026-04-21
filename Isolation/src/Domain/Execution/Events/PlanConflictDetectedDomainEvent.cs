using Domain.Common.Events;

namespace Domain.Execution.Events;

/// <summary>
/// 表示计划冲突已识别。
/// </summary>
public sealed class PlanConflictDetectedDomainEvent : DomainEventBase
{
    public PlanConflictDetectedDomainEvent(
        Guid planId,
        Guid correlationId,
        IReadOnlyCollection<PlanConflict> conflicts)
        : base(
            "PlanConflictDetected",
            "RewriteExecution",
            planId,
            correlationId,
            null,
            $"计划冲突已识别：{string.Join(", ", conflicts)}。")
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        Conflicts = conflicts;
    }

    public IReadOnlyCollection<PlanConflict> Conflicts { get; }
}
