namespace Application.Contracts.Decision;

/// <summary>
/// 冲突项 DTO。
/// </summary>
public sealed class DecisionConflictDto
{
    public Guid LeftCandidateId { get; set; }

    public Guid RightCandidateId { get; set; }

    public string Description { get; set; } = string.Empty;
}
