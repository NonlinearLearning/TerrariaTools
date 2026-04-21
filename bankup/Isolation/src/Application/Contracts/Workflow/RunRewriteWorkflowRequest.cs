using Application.Contracts;

namespace Application.Contracts.Workflow;

/// <summary>
/// 运行改写工作流请求。
/// </summary>
public sealed class RunRewriteWorkflowRequest
{
    public Guid RunCorrelationId { get; set; }

    public Guid WorkspaceContextId { get; set; }

    public Guid? AnalysisSnapshotId { get; set; }

    public Guid RuleTargetId { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public ContractCandidateKind CandidateKind { get; set; }

    public ContractCandidateReason PrimaryReason { get; set; }

    public IReadOnlyCollection<ContractCandidateReason> AdditionalReasons { get; set; } = Array.Empty<ContractCandidateReason>();

    public IReadOnlyCollection<ContractScenarioTag> ScenarioTags { get; set; } = Array.Empty<ContractScenarioTag>();

    public string BoundaryName { get; set; } = "DefaultBoundary";

    public ContractSliceDirection SliceDirection { get; set; } = ContractSliceDirection.Bidirectional;

    public int MaxDepth { get; set; } = 1;

    public bool IncludeExternalReferences { get; set; }

    public IReadOnlyCollection<string> PropagationTargets { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ProtectionRules { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ConflictTargets { get; set; } = Array.Empty<string>();

    public ContractConfidenceLevel ConfidenceLevel { get; set; } = ContractConfidenceLevel.Medium;

    public bool ForceReject { get; set; }

    public string DocumentPath { get; set; } = string.Empty;

    public string? MemberSignature { get; set; }

    public string? AnchorText { get; set; }

    public ContractPlanAction PlanAction { get; set; }

    public bool SimulateFailure { get; set; }

    public string SourceCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string? MethodName { get; set; }

    public int? ParameterCount { get; set; }
}
