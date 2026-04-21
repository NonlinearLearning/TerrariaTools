using Application.Contracts;

namespace Application.Contracts.Propagation;

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
