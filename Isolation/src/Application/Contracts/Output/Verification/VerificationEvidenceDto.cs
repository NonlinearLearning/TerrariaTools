namespace Application.Contracts.Output.Verification;

/// <summary>
/// 证据 DTO。
/// </summary>
public sealed class VerificationEvidenceDto
{
    public Guid Id { get; set; }

    public Guid RewriteResultId { get; set; }

    public RiskSummaryDto RiskSummary { get; set; } = new();

    public IReadOnlyCollection<CompilationEvidenceDto> CompilationEvidence { get; set; } =
        Array.Empty<CompilationEvidenceDto>();

    public IReadOnlyCollection<StaticReasoningEvidenceDto> StaticReasoningEvidence { get; set; } =
        Array.Empty<StaticReasoningEvidenceDto>();

    public IReadOnlyCollection<BehaviorEvidenceDto> BehaviorEvidence { get; set; } =
        Array.Empty<BehaviorEvidenceDto>();
}
