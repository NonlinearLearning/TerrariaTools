using Application.Contracts;

namespace Application.Contracts.Output.Verification;

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
