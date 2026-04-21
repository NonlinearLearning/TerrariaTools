using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 变更候选 DTO。
/// </summary>
public sealed class ChangeCandidateDto
{
    public Guid Id { get; set; }

    public Guid RuleTargetId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public ContractCandidateKind CandidateKind { get; set; }

    public IReadOnlyCollection<ContractCandidateReason> Reasons { get; set; } = Array.Empty<ContractCandidateReason>();

    public IReadOnlyCollection<ContractScenarioTag> ScenarioTags { get; set; } = Array.Empty<ContractScenarioTag>();

    public bool IsCoveredByParentAction { get; set; }

    public Guid? CoveredByCandidateId { get; set; }
}
