using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 计划项 DTO。
/// </summary>
public sealed class PlanChangeItemDto
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public PlanTargetDto PlanTarget { get; set; } = new();

    public ContractPlanAction PlanAction { get; set; }

    public int Order { get; set; }

    public IReadOnlyCollection<ContractPlanReason> Reasons { get; set; } = Array.Empty<ContractPlanReason>();
}
