using Application.Contracts;
using Application.Contracts.Output.Audit;
using Application.Contracts.Output.Verification;
using Domain.Output.Audit;
using Domain.Output.Verification;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static VerificationEvidenceDto Map(VerificationEvidence verificationEvidence)
    {
        return new VerificationEvidenceDto
        {
            Id = verificationEvidence.Id,
            RewriteResultId = verificationEvidence.RewriteResultId,
            RiskSummary = Map(verificationEvidence.RiskSummary),
            CompilationEvidence = verificationEvidence.CompilationEvidence.Select(Map).ToArray(),
            StaticReasoningEvidence = verificationEvidence.StaticReasoningEvidence.Select(Map).ToArray(),
            BehaviorEvidence = verificationEvidence.BehaviorEvidence.Select(Map).ToArray(),
        };
    }

    public static RunReportDto Map(RunReport runReport)
    {
        return new RunReportDto
        {
            Id = runReport.Id,
            WorkspaceContextId = runReport.WorkspaceContextId,
            RewriteDecisionId = runReport.RewriteDecisionId,
            RewritePlanId = runReport.RewritePlanId,
            RewriteResultId = runReport.RewriteResultId,
            VerificationEvidenceId = runReport.VerificationEvidenceId,
            ReportSummary = Map(runReport.ReportSummary),
            AuditConclusion = Map(runReport.AuditConclusion),
        };
    }

    public static CompilationEvidenceDto Map(CompilationEvidence compilationEvidence)
    {
        return new CompilationEvidenceDto
        {
            Success = compilationEvidence.Success,
            DiagnosticCount = compilationEvidence.DiagnosticCount,
            Summary = compilationEvidence.Summary,
        };
    }

    public static StaticReasoningEvidenceDto Map(StaticReasoningEvidence staticReasoningEvidence)
    {
        return new StaticReasoningEvidenceDto
        {
            SubjectName = staticReasoningEvidence.SubjectName,
            Summary = staticReasoningEvidence.Summary,
        };
    }

    public static BehaviorEvidenceDto Map(BehaviorEvidence behaviorEvidence)
    {
        return new BehaviorEvidenceDto
        {
            ScenarioName = behaviorEvidence.ScenarioName,
            Passed = behaviorEvidence.Passed,
            Summary = behaviorEvidence.Summary,
        };
    }

    public static RiskSummaryDto Map(RiskSummary riskSummary)
    {
        return new RiskSummaryDto
        {
            Level = Map(riskSummary.Level),
            LevelName = riskSummary.LevelName,
            RequiresManualReview = riskSummary.RequiresManualReview,
            Items = riskSummary.Items.ToArray(),
        };
    }

    public static ReportSummaryDto Map(ReportSummary reportSummary)
    {
        return new ReportSummaryDto
        {
            ApprovedCount = reportSummary.ApprovedCount,
            RejectedCount = reportSummary.RejectedCount,
            FailureCount = reportSummary.FailureCount,
            Highlights = reportSummary.Highlights,
        };
    }

    public static ContractRiskLevel Map(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => ContractRiskLevel.Low,
            RiskLevel.Medium => ContractRiskLevel.Medium,
            RiskLevel.High => ContractRiskLevel.High,
            _ => ContractRiskLevel.Unknown,
        };
    }

    public static ContractAuditConclusion Map(AuditConclusion auditConclusion)
    {
        return auditConclusion switch
        {
            AuditConclusion.ApprovedForExecution => ContractAuditConclusion.ApprovedForExecution,
            AuditConclusion.ApprovedForMerge => ContractAuditConclusion.ApprovedForMerge,
            AuditConclusion.ReferenceOnly => ContractAuditConclusion.ReferenceOnly,
            AuditConclusion.RequiresManualReview => ContractAuditConclusion.RequiresManualReview,
            _ => ContractAuditConclusion.Unknown,
        };
    }
}
