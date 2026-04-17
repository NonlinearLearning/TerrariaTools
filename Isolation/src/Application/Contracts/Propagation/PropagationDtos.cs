using Application.Contracts.Marking;
using Domain.Propagation;
using Domain.Marking;

namespace Application.Contracts.Propagation;

/// <summary>
/// 切片边界 DTO。
/// </summary>
public sealed class SliceBoundaryDto
{
    public string BoundaryName { get; set; } = string.Empty;

    public SliceDirection Direction { get; set; }

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
/// 引用映射 DTO。
/// </summary>
public sealed class ReferenceMappingDto
{
    public string SourceReference { get; set; } = string.Empty;

    public string TargetReference { get; set; } = string.Empty;
}

/// <summary>
/// 构建传播结果请求。
/// </summary>
public sealed class BuildPropagationRequest
{
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
}

/// <summary>
/// 传播结果 DTO。
/// </summary>
public sealed class PropagationResultDto
{
    public Guid CandidateId { get; set; }

    public ChangeCandidateDto Candidate { get; set; } = new();

    public SliceBoundaryDto? SliceBoundary { get; set; }

    public IReadOnlyCollection<PropagationTraceDto> PropagationTraces { get; set; } =
        Array.Empty<PropagationTraceDto>();
}
