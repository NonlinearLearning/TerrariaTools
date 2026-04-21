using Application.Contracts;

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

/// <summary>
/// 编译证据 DTO。
/// </summary>
public sealed class CompilationEvidenceDto
{
    public bool Success { get; set; }

    public int DiagnosticCount { get; set; }

    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 静态推理证据 DTO。
/// </summary>
public sealed class StaticReasoningEvidenceDto
{
    public string SubjectName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 行为证据 DTO。
/// </summary>
public sealed class BehaviorEvidenceDto
{
    public string ScenarioName { get; set; } = string.Empty;

    public bool Passed { get; set; }

    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 风险摘要 DTO。
/// </summary>
public sealed class RiskSummaryDto
{
    public ContractRiskLevel Level { get; set; }

    public string LevelName { get; set; } = string.Empty;

    public bool RequiresManualReview { get; set; }

    public IReadOnlyCollection<string> Items { get; set; } = Array.Empty<string>();
}
