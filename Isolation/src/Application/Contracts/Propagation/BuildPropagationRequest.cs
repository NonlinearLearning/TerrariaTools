using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 构建传播结果请求。
/// </summary>
public sealed class BuildPropagationRequest
{
    public Guid RunCorrelationId { get; set; }

    public Guid? WorkspaceContextId { get; set; }

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

    public ChangeCandidateDto? Candidate { get; set; }
}
