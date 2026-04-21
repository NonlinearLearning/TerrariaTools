using Domain.Common.Events;

namespace Domain.Execution.Events;

/// <summary>
/// 表示改写计划已编译。
/// </summary>
public sealed class RewritePlanCompiledDomainEvent : DomainEventBase
{
    public RewritePlanCompiledDomainEvent(
        Guid planId,
        Guid correlationId,
        string planName,
        int changeItemCount)
        : base(
            "RewritePlanCompiled",
            "RewriteExecution",
            planId,
            correlationId,
            null,
            $"改写计划已编译：{planName}，计划项 {changeItemCount} 个。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planName);
        ArgumentOutOfRangeException.ThrowIfNegative(changeItemCount);
        PlanName = planName.Trim();
        ChangeItemCount = changeItemCount;
    }

    public string PlanName { get; }

    public int ChangeItemCount { get; }
}
