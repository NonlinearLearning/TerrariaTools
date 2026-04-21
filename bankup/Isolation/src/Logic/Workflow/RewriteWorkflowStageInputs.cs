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

public sealed class RewriteWorkflowExecutionStageInput
{
    public Guid RunCorrelationId { get; init; }
    public WorkspaceContext WorkspaceContext { get; init; } = null!;
    public string SourceCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string? MethodName { get; init; }
    public int? ParameterCount { get; init; }
}

public sealed class RewriteWorkflowEvidenceStageInput
{
    public Guid RunCorrelationId { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public PlanAction PlanAction { get; init; }
    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();
}

public sealed class RewriteWorkflowReportStageInput
{
    public Guid RunCorrelationId { get; init; }
    public Guid WorkspaceContextId { get; init; }
    public Guid DecisionId { get; init; }
    public RewriteDecision Decision { get; init; } = null!;
}

public sealed class RewriteWorkflowEventStageInput
{
    public Guid RunCorrelationId { get; init; }
    public WorkspaceContext WorkspaceContext { get; init; } = null!;
    public Guid WorkspaceContextId { get; init; }
    public Guid? AnalysisSnapshotId { get; init; }
    public Guid RuleTargetId { get; init; }
    public string RuleCode { get; init; } = string.Empty;
    public Guid CandidateId { get; init; }
    public Guid DecisionId { get; init; }
    public RewriteDecision Decision { get; init; } = null!;
    public bool Approved { get; init; }
    public int ApprovalCount { get; init; }
    public int RejectionCount { get; init; }
    public int ProtectionCount { get; init; }
    public int PropagationTraceCount { get; init; }
    public int ReasonCount { get; init; }
    public int MaxDepth { get; init; }
    public int ConflictTargetCount { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public string DocumentPath { get; init; } = string.Empty;
    public PlanAction PlanAction { get; init; }
    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();
}
