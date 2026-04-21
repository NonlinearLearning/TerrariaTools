using Domain.Decision;

namespace Logic.Workflow;

public sealed class RewriteWorkflowReportStageInput
{
    public Guid RunCorrelationId { get; init; }
    public Guid WorkspaceContextId { get; init; }
    public Guid DecisionId { get; init; }
    public RewriteDecision Decision { get; init; } = null!;
}
