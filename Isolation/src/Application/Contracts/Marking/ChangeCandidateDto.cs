using Domain.Marking;

namespace Application.Contracts.Marking;

/// <summary>
/// 变更候选 DTO。
/// </summary>
public sealed class ChangeCandidateDto
{
    public Guid Id { get; set; }

    public Guid RuleTargetId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public CandidateKind CandidateKind { get; set; }

    public IReadOnlyCollection<CandidateReason> Reasons { get; set; } = Array.Empty<CandidateReason>();

    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; set; } = Array.Empty<ScenarioTag>();

    public bool IsCoveredByParentAction { get; set; }

    public Guid? CoveredByCandidateId { get; set; }
}
