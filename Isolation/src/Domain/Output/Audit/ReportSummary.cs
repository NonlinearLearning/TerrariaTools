using Domain.Execution;
using Domain.Output.Verification;

namespace Domain.Output.Audit;

/// <summary>
/// 表示报告摘要。
/// </summary>
public sealed class ReportSummary
{
    public ReportSummary(int approvedCount, int rejectedCount, int failureCount, string highlights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(rejectedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(failureCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(highlights);
        ApprovedCount = approvedCount;
        RejectedCount = rejectedCount;
        FailureCount = failureCount;
        Highlights = highlights.Trim();
    }

    public int ApprovedCount { get; }

    public int RejectedCount { get; }

    public int FailureCount { get; }

    public string Highlights { get; }

    public static ReportSummary FromExecutionOutcome(
        RewriteResult rewriteResult,
        VerificationEvidence verificationEvidence)
    {
        ArgumentNullException.ThrowIfNull(rewriteResult);
        ArgumentNullException.ThrowIfNull(verificationEvidence);

        bool approved = rewriteResult.ExecutionFailures.Count == 0 && !verificationEvidence.RiskSummary.RequiresManualReview;
        string highlights = verificationEvidence.RiskSummary.Items.Count == 0
            ? "由运行报告装配器基于真实证据生成。"
            : string.Join(" ", verificationEvidence.RiskSummary.Items);

        return new ReportSummary(
            approved ? 1 : 0,
            approved ? 0 : 1,
            rewriteResult.ExecutionFailures.Count,
            highlights);
    }
}
