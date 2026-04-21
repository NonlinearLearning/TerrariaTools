using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Logic.Workflow.Events;

namespace Logic.Workflow;

/// <summary>
/// 表示工作流报告阶段输出。
/// </summary>
public sealed class RewriteWorkflowReportStageResult
{
    public RewritePlan Plan { get; init; } = null!;

    public RewriteResult Result { get; init; } = null!;

    public VerificationEvidence Evidence { get; init; } = null!;

    public RunReport Report { get; init; } = null!;

    public RewriteWorkflowArtifacts ToArtifacts(IReadOnlyCollection<DomainEventEnvelope>? domainEvents = null)
    {
        return new RewriteWorkflowArtifacts
        {
            Plan = Plan,
            Result = Result,
            Evidence = Evidence,
            Report = Report,
            DomainEvents = domainEvents ?? Array.Empty<DomainEventEnvelope>(),
        };
    }
}
