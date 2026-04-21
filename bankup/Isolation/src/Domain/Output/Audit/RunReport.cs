using Domain.Execution;
using Domain.Common;
using Domain.Output.Verification;
using Domain.Output.Audit.Events;

namespace Domain.Output.Audit;

/// <summary>
/// 表示运行报告聚合根。
/// </summary>
public sealed class RunReport : AggregateRoot<Guid>
{
    private bool isFinalized;

    private RunReport(
        Guid id,
        Guid workspaceContextId,
        Guid rewriteDecisionId,
        Guid rewritePlanId,
        Guid rewriteResultId,
        ReportSummary reportSummary,
        AuditConclusion auditConclusion)
        : base(id)
    {
        WorkspaceContextId = workspaceContextId;
        RewriteDecisionId = rewriteDecisionId;
        RewritePlanId = rewritePlanId;
        RewriteResultId = rewriteResultId;
        ReportSummary = reportSummary;
        AuditConclusion = auditConclusion;
        GeneratedAt = DateTimeOffset.UtcNow;
    }

    public Guid WorkspaceContextId { get; }

    public Guid RewriteDecisionId { get; }

    public Guid RewritePlanId { get; }

    public Guid RewriteResultId { get; }

    public ReportSummary ReportSummary { get; private set; }

    public AuditConclusion AuditConclusion { get; private set; }

    public Guid? VerificationEvidenceId { get; private set; }

    public DateTimeOffset GeneratedAt { get; }

    public bool IsFinalized => isFinalized;

    public static RunReport Create(
        Guid workspaceContextId,
        Guid rewriteDecisionId,
        Guid rewritePlanId,
        Guid rewriteResultId,
        ReportSummary reportSummary,
        AuditConclusion auditConclusion)
    {
        ArgumentNullException.ThrowIfNull(reportSummary);
        return new RunReport(
            Guid.NewGuid(),
            workspaceContextId,
            rewriteDecisionId,
            rewritePlanId,
            rewriteResultId,
            reportSummary,
            auditConclusion);
    }

    public static RunReport CreateFromExecutionOutcome(
        Guid workspaceContextId,
        Guid rewriteDecisionId,
        Guid rewritePlanId,
        RewriteResult rewriteResult,
        VerificationEvidence verificationEvidence)
    {
        ArgumentNullException.ThrowIfNull(rewriteResult);
        ArgumentNullException.ThrowIfNull(verificationEvidence);

        RunReport report = Create(
            workspaceContextId,
            rewriteDecisionId,
            rewritePlanId,
            rewriteResult.Id,
            new ReportSummary(0, 0, 0, "待从执行结果计算。"),
            AuditConclusion.ReferenceOnly);
        report.RecalculateFromEvidence(rewriteResult, verificationEvidence);
        return report;
    }

    public void AttachVerificationEvidence(Guid verificationEvidenceId)
    {
        if (verificationEvidenceId == Guid.Empty)
        {
            throw new InvalidOperationException("证据标识不能为空。");
        }

        if (VerificationEvidenceId.HasValue && VerificationEvidenceId.Value != verificationEvidenceId)
        {
            throw new InvalidOperationException("运行报告只允许挂接一次证据。");
        }

        VerificationEvidenceId = verificationEvidenceId;
    }

    public void ReviseSummary(ReportSummary reportSummary, AuditConclusion auditConclusion)
    {
        ArgumentNullException.ThrowIfNull(reportSummary);
        if (isFinalized)
        {
            throw new InvalidOperationException("运行报告完成后不能再修订摘要。");
        }

        ReportSummary = reportSummary;
        AuditConclusion = auditConclusion;
    }

    public void RecalculateFromEvidence(RewriteResult rewriteResult, VerificationEvidence verificationEvidence)
    {
        ArgumentNullException.ThrowIfNull(rewriteResult);
        ArgumentNullException.ThrowIfNull(verificationEvidence);
        if (isFinalized)
        {
            throw new InvalidOperationException("运行报告完成后不能再根据证据重算。");
        }

        if (rewriteResult.Id != RewriteResultId)
        {
            throw new InvalidOperationException("运行报告只能根据同一执行结果重新计算。");
        }

        if (verificationEvidence.RewriteResultId != RewriteResultId)
        {
            throw new InvalidOperationException("验证证据必须指向同一执行结果。");
        }

        ReportSummary = ReportSummary.FromExecutionOutcome(rewriteResult, verificationEvidence);
        AuditConclusion = DetermineAuditConclusion(rewriteResult, verificationEvidence);
        AttachVerificationEvidence(verificationEvidence.Id);
    }

    public void Finalize(Guid correlationId)
    {
        if (!VerificationEvidenceId.HasValue)
        {
            throw new InvalidOperationException("运行报告挂接验证证据后才能完成。");
        }

        isFinalized = true;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("RunReportGenerated", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new RunReportGeneratedDomainEvent(
            Id,
            resolvedCorrelationId,
            AuditConclusion,
            ReportSummary.Highlights));
    }

    private static AuditConclusion DetermineAuditConclusion(
        RewriteResult rewriteResult,
        VerificationEvidence verificationEvidence)
    {
        return rewriteResult.ExecutionFailures.Count == 0 && !verificationEvidence.RiskSummary.RequiresManualReview
            ? AuditConclusion.ApprovedForExecution
            : AuditConclusion.RequiresManualReview;
    }
}

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

/// <summary>
/// 表示审计结论。
/// </summary>
public enum AuditConclusion
{
    Unknown = 0,
    ApprovedForExecution = 1,
    ApprovedForMerge = 2,
    ReferenceOnly = 3,
    RequiresManualReview = 4,
}
