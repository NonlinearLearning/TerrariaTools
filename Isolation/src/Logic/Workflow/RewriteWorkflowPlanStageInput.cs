using Domain.Decision;
using Domain.Execution;
using Domain.Workspaces;

namespace Logic.Workflow;

public sealed class RewriteWorkflowPlanStageInput
{
    public Guid RunCorrelationId { get; init; }
    public WorkspaceContext WorkspaceContext { get; init; } = null!;
    public Guid CandidateId { get; init; }
    public RewriteDecision Decision { get; init; } = null!;
    public string TargetName { get; init; } = string.Empty;
    public string DocumentPath { get; init; } = string.Empty;
    public string? MemberSignature { get; init; }
    public string? AnchorText { get; init; }
    public PlanAction PlanAction { get; init; }
}
