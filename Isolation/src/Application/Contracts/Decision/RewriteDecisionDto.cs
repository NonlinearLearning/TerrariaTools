using Application.Contracts;

namespace Application.Contracts.Decision;

/// <summary>
/// 改写决策 DTO。
/// </summary>
public sealed class RewriteDecisionDto
{
    public Guid Id { get; set; }

    public string DecisionName { get; set; } = string.Empty;

    public ContractConfidenceLevel ConfidenceLevel { get; set; }

    public IReadOnlyDictionary<Guid, ContractApprovalReason> Approvals { get; set; } =
        new Dictionary<Guid, ContractApprovalReason>();

    public IReadOnlyDictionary<Guid, ContractRejectionReason> Rejections { get; set; } =
        new Dictionary<Guid, ContractRejectionReason>();

    public IReadOnlyCollection<DecisionProtectionDto> Protections { get; set; } =
        Array.Empty<DecisionProtectionDto>();

    public IReadOnlyCollection<DecisionConflictDto> Conflicts { get; set; } =
        Array.Empty<DecisionConflictDto>();
}
