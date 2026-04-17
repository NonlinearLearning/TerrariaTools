using Domain.Decision;
using Domain.Execution;
using Domain.Marking;
using Domain.Propagation;

namespace Application.Contracts.Workflow;

/// <summary>
/// 运行改写工作流请求。
/// </summary>
public sealed class RunRewriteWorkflowRequest
{
    public Guid WorkspaceContextId { get; set; }

    public Guid RuleTargetId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public CandidateKind CandidateKind { get; set; }

    public CandidateReason PrimaryReason { get; set; }

    public IReadOnlyCollection<CandidateReason> AdditionalReasons { get; set; } = Array.Empty<CandidateReason>();

    public IReadOnlyCollection<ScenarioTag> ScenarioTags { get; set; } = Array.Empty<ScenarioTag>();

    public string BoundaryName { get; set; } = "DefaultBoundary";

    public SliceDirection SliceDirection { get; set; } = SliceDirection.Bidirectional;

    public int MaxDepth { get; set; } = 1;

    public bool IncludeExternalReferences { get; set; }

    public IReadOnlyCollection<string> PropagationTargets { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ProtectionRules { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ConflictTargets { get; set; } = Array.Empty<string>();

    public ConfidenceLevel ConfidenceLevel { get; set; } = ConfidenceLevel.Medium;

    public bool ForceReject { get; set; }

    public string DocumentPath { get; set; } = string.Empty;

    public string? MemberSignature { get; set; }

    public string? AnchorText { get; set; }

    public PlanAction PlanAction { get; set; }

    public bool SimulateFailure { get; set; }
}
