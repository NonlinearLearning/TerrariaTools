using Application.Contracts;

namespace Application.Contracts.Execution;

/// <summary>
/// 改写计划 DTO。
/// </summary>
public sealed class RewritePlanDto
{
    public Guid Id { get; set; }

    public PlanMetadataDto Metadata { get; set; } = new();

    public IReadOnlyCollection<PlanChangeItemDto> ChangeItems { get; set; } = Array.Empty<PlanChangeItemDto>();

    public IReadOnlyCollection<ContractPlanConflict> Conflicts { get; set; } = Array.Empty<ContractPlanConflict>();
}
