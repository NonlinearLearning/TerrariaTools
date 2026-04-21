namespace Application.Contracts.Decision;

/// <summary>
/// 决策结果 DTO。
/// </summary>
public sealed class DecisionResultDto
{
    public Guid CandidateId { get; set; }

    public RewriteDecisionDto Decision { get; set; } = new();

    public bool Approved { get; set; }

    public IReadOnlyCollection<DecisionProtectionDto> Protections { get; set; } =
        Array.Empty<DecisionProtectionDto>();

    public IReadOnlyCollection<DecisionConflictDto> Conflicts { get; set; } =
        Array.Empty<DecisionConflictDto>();
}
