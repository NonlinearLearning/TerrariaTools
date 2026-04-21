using Application.Contracts;

namespace Application.Contracts.Propagation;

/// <summary>
/// 切片边界 DTO。
/// </summary>
public sealed class SliceBoundaryDto
{
    public string BoundaryName { get; set; } = string.Empty;

    public ContractSliceDirection Direction { get; set; }

    public int MaxDepth { get; set; }

    public bool IncludeExternalReferences { get; set; }
}

/// <summary>
/// 传播轨迹 DTO。
/// </summary>
public sealed class PropagationTraceDto
{
    public string SourceName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int StepOrder { get; set; }
}

/// <summary>
/// 传播事实引用 DTO。
/// </summary>
public sealed class PropagationFactReferenceDto
{
    public string SourceNodeId { get; set; } = string.Empty;

    public string TargetNodeId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;
}

/// <summary>
/// 引用映射 DTO。
/// </summary>
public sealed class ReferenceMappingDto
{
    public string SourceReference { get; set; } = string.Empty;

    public string TargetReference { get; set; } = string.Empty;
}

/// <summary>
/// 闭包根 DTO。
/// </summary>
public sealed class ClosureRootDto
{
    public string ClassName { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;
}

/// <summary>
/// 影子类传播边界 DTO。
/// </summary>
public sealed class ShadowBoundaryDto
{
    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } = Array.Empty<ReferenceMappingDto>();
}

/// <summary>
/// 最小运行闭包传播边界 DTO。
/// </summary>
public sealed class RuntimeClosureBoundaryDto
{
    public ClosureRootDto Root { get; set; } = new();

    public ContractClosureIntegrityStatus IntegrityStatus { get; set; }

    public IReadOnlyCollection<ReferenceMappingDto> ReferenceMappings { get; set; } = Array.Empty<ReferenceMappingDto>();
}

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

/// <summary>
/// 传播结果 DTO。
/// </summary>
public sealed class PropagationResultDto
{
    public Guid RunCorrelationId { get; set; }

    public Guid CandidateId { get; set; }

    public ChangeCandidateDto Candidate { get; set; } = new();

    public SliceBoundaryDto? SliceBoundary { get; set; }

    public IReadOnlyCollection<PropagationTraceDto> PropagationTraces { get; set; } = Array.Empty<PropagationTraceDto>();

    public IReadOnlyCollection<PropagationFactReferenceDto> FactReferences { get; set; } = Array.Empty<PropagationFactReferenceDto>();
}
