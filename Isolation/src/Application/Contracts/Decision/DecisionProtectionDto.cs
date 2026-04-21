namespace Application.Contracts.Decision;

/// <summary>
/// 保护项 DTO。
/// </summary>
public sealed class DecisionProtectionDto
{
    public Guid CandidateId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
