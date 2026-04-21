using Domain.Common.Events;

namespace Domain.Output.Audit.Events;

/// <summary>
/// 表示运行报告已生成。
/// </summary>
public sealed class RunReportGeneratedDomainEvent : DomainEventBase
{
    public RunReportGeneratedDomainEvent(
        Guid runReportId,
        Guid correlationId,
        AuditConclusion auditConclusion,
        string highlights)
        : base(
            "RunReportGenerated",
            "AuditReporting",
            runReportId,
            correlationId,
            null,
            $"运行报告已生成：结论 {auditConclusion}，摘要 {highlights}。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(highlights);
        AuditConclusion = auditConclusion;
        Highlights = highlights.Trim();
    }

    public AuditConclusion AuditConclusion { get; }

    public string Highlights { get; }
}
