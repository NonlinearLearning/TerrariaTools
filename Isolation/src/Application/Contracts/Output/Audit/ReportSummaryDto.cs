using Application.Contracts;

namespace Application.Contracts.Output.Audit;

/// <summary>
/// 报告摘要 DTO。
/// </summary>
public sealed class ReportSummaryDto
{
    public int ApprovedCount { get; set; }

    public int RejectedCount { get; set; }

    public int FailureCount { get; set; }

    public string Highlights { get; set; } = string.Empty;
}
