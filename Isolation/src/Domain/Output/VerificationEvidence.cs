using Domain.Common;

namespace Domain.Output;

/// <summary>
/// 表示证据聚合根。
/// </summary>
public sealed class VerificationEvidence : AggregateRoot<Guid>
{
    private readonly List<CompilationEvidence> compilationEvidence = new();
    private readonly List<StaticReasoningEvidence> staticReasoningEvidence = new();
    private readonly List<BehaviorEvidence> behaviorEvidence = new();

    private VerificationEvidence(Guid id, Guid rewriteResultId)
        : base(id)
    {
        RewriteResultId = rewriteResultId;
        RiskSummary = new RiskSummary("未评估", false, Array.Empty<string>());
    }

    public Guid RewriteResultId { get; }

    public RiskSummary RiskSummary { get; private set; }

    public IReadOnlyCollection<CompilationEvidence> CompilationEvidence => compilationEvidence.AsReadOnly();

    public IReadOnlyCollection<StaticReasoningEvidence> StaticReasoningEvidence => staticReasoningEvidence.AsReadOnly();

    public IReadOnlyCollection<BehaviorEvidence> BehaviorEvidence => behaviorEvidence.AsReadOnly();

    public static VerificationEvidence Create(Guid rewriteResultId)
    {
        return new VerificationEvidence(Guid.NewGuid(), rewriteResultId);
    }

    public void AddCompilationEvidence(CompilationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        compilationEvidence.Add(evidence);
    }

    public void AddStaticReasoningEvidence(StaticReasoningEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        staticReasoningEvidence.Add(evidence);
    }

    public void AddBehaviorEvidence(BehaviorEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        behaviorEvidence.Add(evidence);
    }

    public void UpdateRiskSummary(RiskSummary riskSummary)
    {
        ArgumentNullException.ThrowIfNull(riskSummary);
        RiskSummary = riskSummary;
    }
}

/// <summary>
/// 表示运行报告聚合根。
/// </summary>
public sealed class RunReport : AggregateRoot<Guid>
{
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

    public void AttachVerificationEvidence(Guid verificationEvidenceId)
    {
        VerificationEvidenceId = verificationEvidenceId;
    }

    public void ReviseSummary(ReportSummary reportSummary, AuditConclusion auditConclusion)
    {
        ArgumentNullException.ThrowIfNull(reportSummary);
        ReportSummary = reportSummary;
        AuditConclusion = auditConclusion;
    }
}

/// <summary>
/// 表示编译证据。
/// </summary>
public sealed class CompilationEvidence
{
    public CompilationEvidence(bool success, int diagnosticCount, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentOutOfRangeException.ThrowIfNegative(diagnosticCount);
        Success = success;
        DiagnosticCount = diagnosticCount;
        Summary = summary.Trim();
    }

    public bool Success { get; }

    public int DiagnosticCount { get; }

    public string Summary { get; }
}

/// <summary>
/// 表示静态推理证据。
/// </summary>
public sealed class StaticReasoningEvidence
{
    public StaticReasoningEvidence(string subjectName, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        SubjectName = subjectName.Trim();
        Summary = summary.Trim();
    }

    public string SubjectName { get; }

    public string Summary { get; }
}

/// <summary>
/// 表示行为证据。
/// </summary>
public sealed class BehaviorEvidence
{
    public BehaviorEvidence(string scenarioName, bool passed, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ScenarioName = scenarioName.Trim();
        Passed = passed;
        Summary = summary.Trim();
    }

    public string ScenarioName { get; }

    public bool Passed { get; }

    public string Summary { get; }
}

/// <summary>
/// 表示风险摘要。
/// </summary>
public sealed class RiskSummary
{
    public RiskSummary(string levelName, bool requiresManualReview, IReadOnlyCollection<string> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelName);
        LevelName = levelName.Trim();
        RequiresManualReview = requiresManualReview;
        Items = items;
    }

    public string LevelName { get; }

    public bool RequiresManualReview { get; }

    public IReadOnlyCollection<string> Items { get; }
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
