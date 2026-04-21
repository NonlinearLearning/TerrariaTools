namespace Application.Contracts.Output.Verification;

/// <summary>
/// 静态推理证据 DTO。
/// </summary>
public sealed class StaticReasoningEvidenceDto
{
    public string SubjectName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}
